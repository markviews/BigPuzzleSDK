// BigPuzzleSDK: shows the real game model on each SDK component in the editor (button, door,
// lights, ...), placed exactly where the mod will put its copy in the game.
//
// The previews are hidden, never-saved objects (HideFlags.HideAndDontSave) that are NOT children
// of the puzzle: they just follow their component around. They use in-memory meshes, so they
// can never end up in a scene, prefab or .puzzlepack. The exporter also removes them before
// building, as a second safety net.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

namespace BigPuzzleSDK.GameAssets {

[InitializeOnLoad]
public static class GamePreviews {
    internal const string Prefix = "__BigWalkPreview";
    private const double Interval = 0.3;

    // SDK components that copy a vanilla object (PuzzleLight picks its template by its 'type')
    private static readonly HashSet<string> Components = new HashSet<string> {
        "PuzzleButton", "PuzzleSwitch", "PuzzleBox", "PuzzleDoor", "PuzzleGourdPlinth", "PuzzleGourdVice",
        "PuzzleLiveSignal", "PuzzlePegBoard", "PuzzlePegTile", "PuzzleStartSwitch", "PuzzleValidator",
        "PuzzleDisplay", "PuzzlePokeButton", "PuzzleProgressBar", "PuzzleSimPressSwitch", "PuzzleLight", "PuzzleTimer", "PuzzleConfetti",
        "PuzzleGlass", "PuzzlePillow", "PuzzleTelescope",
        // Batch scan (low-priority components)
        "PuzzleVanillaItem", "PuzzleCountInput", "PuzzleCountValidator", "PuzzleIntercom", "PuzzleIntercomSpeaker",
        "PuzzleSoundPeg", "PuzzleSoundPegAnswer", "PuzzleNoItemsGate", "PuzzleCirclePeg",
        "PuzzleCirclePegBoard", "PuzzleTimerSpawner", "PuzzleTimerGoal",
        "PuzzleFireworkLauncher", "PuzzleButtonNHold",
    };

    // One preview: a hidden, unparented object that follows its SDK component around
    private sealed class Preview {
        public GameObject root; public string key; public Vector3 worldScale; public bool followsScale; public Renderer placeholder;
        // PuzzleGlass: one glass copy per renderer, each following its own object (null for the others)
        public List<(Transform copy, Transform target)> followers;
        public List<Renderer> hidden;
        public List<Material> ownMaterials;   // made for this preview only (the wire's tiling)
    }

    // Components whose copy follows the creator's scale; all others keep the game's own size
    internal static readonly HashSet<string> Scalable = new HashSet<string> { "PuzzleTimer", "PuzzleProgressBar", "PuzzleGlass" };
    private static readonly Dictionary<MonoBehaviour, Preview> previews = new Dictionary<MonoBehaviour, Preview>();

    private static double nextTick;
    private static Material vertexColor, glass, screen, plain, redTint, cannonball;
    private static bool cannonballHasSkin;
    private static bool warnedShader;

    // Previews are always on once the game models are imported
    public static bool Enabled => true;

    static GamePreviews() {
        EditorApplication.update += Tick;
        SceneView.beforeSceneGui += _ => Follow();
        AssemblyReloadEvents.beforeAssemblyReload += RemoveAll;
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) RemoveAll(); };
        EditorApplication.quitting += RemoveAll;
        EditorSceneManager.sceneClosing += (scene, removing) => RemoveAll();
        PrefabStage.prefabStageClosing += _ => RemoveAll();
        BigWalkGameModels.Changed += () => {   // rebuilt with the new meshes and sheets on the next tick
            RemoveAll();
            foreach (Material m in tileMaterials.Values) if (m != null) UnityEngine.Object.DestroyImmediate(m);
            tileMaterials.Clear();
            foreach (Material m in tintMaterials.Values) if (m != null) UnityEngine.Object.DestroyImmediate(m);
            tintMaterials.Clear();
            pickMeshes.Clear();
        };
        Selection.selectionChanged += RedirectSelection;
        SceneView.duringSceneGui += HandleClicks;
    }

    // ---- Keeping previews up to date ----
    private static void Tick() {
        Follow();
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            foreach (MonoBehaviour c in previews.Keys) KeepUnscaled(c);   // every frame, so a scale drag snaps straight back
        if (EditorApplication.timeSinceStartup < nextTick) return;
        nextTick = EditorApplication.timeSinceStartup + Interval;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (MonoBehaviour c in SdkComponents()) KeepUnscaled(c);   // also when previews are off
        if (!Enabled) { if (previews.Count > 0) RemoveAll(); return; }   // (PuzzleGlass needs no game models; the others check in Refresh)

        var seen = new HashSet<MonoBehaviour>();
        foreach (MonoBehaviour c in SdkComponents()) {
            seen.Add(c);
            try { Refresh(c); }
            catch (Exception ex) { Debug.LogWarning($"[PuzzleSDK] Preview for '{c.name}' failed: {ex.Message}"); }
        }
        // Components that were deleted, disabled or unloaded
        var gone = new List<MonoBehaviour>();
        foreach (var kv in previews) if (kv.Key == null || !seen.Contains(kv.Key)) gone.Add(kv.Key);
        foreach (MonoBehaviour c in gone) Remove(c);
    }

    // Most components always use the game's own size in-game, so their object's scale is kept
    // at 1,1,1 to avoid a placeholder that looks bigger or smaller than what players will see.
    // Only the timer and the progress bar can be scaled.
    private static readonly HashSet<int> toldAboutScale = new HashSet<int>();

    private static void KeepUnscaled(MonoBehaviour c) {
        if (c == null || Scalable.Contains(c.GetType().Name)) return;
        Transform t = c.transform;
        if (t.localScale == Vector3.one) return;
        t.localScale = Vector3.one;
        EditorUtility.SetDirty(t);
        if (toldAboutScale.Add(c.GetInstanceID()))
            Debug.Log($"[PuzzleSDK] '{c.name}': {c.GetType().Name} always uses the game's own size, so its scale stays at 1,1,1. " +
                      "(Only PuzzleTimer, PuzzleProgressBar and PuzzleGlass can be scaled.)", c);
    }

    // Moves every preview to where its component is now
    private static void Follow() {
        foreach (var kv in previews) {
            MonoBehaviour c = kv.Key;
            Preview p = kv.Value;
            if (c == null || p.root == null) continue;
            if (p.followers != null) {
                foreach (var f in p.followers) {
                    if (f.copy == null || f.target == null) continue;
                    f.copy.SetPositionAndRotation(f.target.position, f.target.rotation);
                    f.copy.localScale = f.target.lossyScale;
                }
                continue;
            }
            Transform t = c.transform;
            p.root.transform.SetPositionAndRotation(t.position, t.rotation);
            p.root.transform.localScale = p.followsScale ? Vector3.Scale(p.worldScale, t.lossyScale) : p.worldScale;
        }
    }

    private static IEnumerable<MonoBehaviour> SdkComponents() {
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null) {
            // In prefab mode only the prefab being edited is shown
            if (stage.prefabContentsRoot != null)
                foreach (MonoBehaviour c in stage.prefabContentsRoot.GetComponentsInChildren<MonoBehaviour>(false))
                    if (IsSdkComponent(c)) yield return c;
            yield break;
        }
        foreach (MonoBehaviour c in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (IsSdkComponent(c) && c.isActiveAndEnabled) yield return c;
    }

    internal static bool IsSdkComponent(MonoBehaviour c) {
        if (c == null) return false;
        Type t = c.GetType();
        return t.Namespace == "BigPuzzleSDK" && Components.Contains(t.Name);
    }

    // A PuzzleHider on the object or above it that hides it in 4-player lobbies (the editor shows
    // the 4-player layout)
    private static bool HiddenByHider(Component c) {
        for (Transform t = c.transform; t != null; t = t.parent)
            foreach (MonoBehaviour mb in t.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetType().Name == "PuzzleHider") {
                    SerializedProperty show4 = new SerializedObject(mb).FindProperty("show4");
                    if (show4 != null && !show4.boolValue) return true;
                }
        return false;
    }

    // Which vanilla template a component copies
    internal static string LabelFor(MonoBehaviour c) {
        string type = c.GetType().Name;
        if (type == "PuzzleTelescope") {   // the plain telescope when Invisible Ink View is off
            SerializedProperty lv = new SerializedObject(c).FindProperty("invisibleInkView");
            return lv != null && !lv.boolValue ? "PuzzleTelescope Normal" : type;
        }
        if (type == "PuzzleButtonNHold") {   // 2, 3 or 4 dots: the buttons in its group (same puzzle, same Group)
            string g = new SerializedObject(c).FindProperty("group")?.stringValue ?? "";
            Component root = c.GetComponentInParent(c.GetType().Assembly.GetType("BigPuzzleSDK.PuzzleSDK"), true);
            int count = 0;
            if (root != null)
                foreach (Component other in root.GetComponentsInChildren(c.GetType(), false))   // switched-off ones don't count
                    if (!HiddenByHider(other) && (new SerializedObject(other).FindProperty("group")?.stringValue ?? "") == g) count++;
            return "PuzzleButtonNHold " + (count >= 4 ? 4 : count == 3 ? 3 : 2);
        }
        if (type == "PuzzlePegBoard") {   // Size and Start Marker pick the board model
            var boardSo = new SerializedObject(c);
            SerializedProperty size = boardSo.FindProperty("size");
            SerializedProperty marker = boardSo.FindProperty("startMarker");
            string[] sizes = { "1", "2", "3", "4", "5", "6", "12" };
            int sizeIndex = size != null && size.propertyType == SerializedPropertyType.Enum ? size.enumValueIndex : 4;
            return "PuzzlePegBoard " + sizes[Mathf.Clamp(sizeIndex, 0, sizes.Length - 1)] + (marker != null && marker.boolValue ? " Marker" : "");
        }
        if (type == "PuzzleNoItemsGate") {   // the blindfold catwalk's gate when Blind Mask is on
            SerializedProperty bm = new SerializedObject(c).FindProperty("blindMask");
            return bm != null && bm.boolValue ? "PuzzleNoItemsGate BlindMask" : type;
        }
        if (type == "PuzzleVanillaItem") {   // Item: which of the game's items
            SerializedProperty it = new SerializedObject(c).FindProperty("itemType");
            string itemName = it != null && it.propertyType == SerializedPropertyType.Enum && it.enumValueIndex >= 0 && it.enumValueIndex < it.enumNames.Length ? it.enumNames[it.enumValueIndex] : "Blindfold";
            if (itemName == "CowbellBig") itemName = "Cowbell";   // (the big cowbell's template is just "Cowbell")
            if (itemName == "Buoy" || itemName == "BuoyRed") {   // the buoy light on its pedestal (Include Base off: the buoy alone)
                SerializedProperty bas = new SerializedObject(c).FindProperty("includeBase");
                return (itemName == "BuoyRed" ? "PuzzleBuoyLight Red" : "PuzzleBuoyLight") + (bas != null && !bas.boolValue ? " NoBase" : "");
            }
            return "PuzzleVanillaItem " + itemName;
        }
        if (type == "PuzzleDisplay") {   // Size: 1, 2, 3 or 5 flaps
            SerializedProperty sz = new SerializedObject(c).FindProperty("size");
            string[] frames = { "PuzzleDisplay", "PuzzleDisplay Two", "PuzzleDisplay Three", "PuzzleDisplay Five" };
            return sz != null && sz.propertyType == SerializedPropertyType.Enum && sz.enumValueIndex > 0 && sz.enumValueIndex < frames.Length ? frames[sz.enumValueIndex] : type;
        }
        if (type == "PuzzleProgressBar") {   // Style: Straight or Spiral
            SerializedProperty st = new SerializedObject(c).FindProperty("style");
            return st != null && st.propertyType == SerializedPropertyType.Enum && st.enumValueIndex == 1 ? "PuzzleProgressBar Spiral" : type;
        }
        if (type != "PuzzleLight") return type;
        var so = new SerializedObject(c);
        SerializedProperty p = so.FindProperty("type");
        if (p == null || p.propertyType != SerializedPropertyType.Enum) return "PuzzleLight TableRed";
        int i = p.enumValueIndex;
        return "PuzzleLight " + (i >= 0 && i < p.enumNames.Length ? p.enumNames[i] : "TableRed");
    }

    private static void Refresh(MonoBehaviour c) {
        if (c.GetType().Name == "PuzzleGlass") { RefreshGlass(c); return; }
        if (!BigWalkGameModels.Ready) { Remove(c); return; }
        string label = LabelFor(c);
        string key = label;
        TileImage tileImage = null;
        if (c.GetType().Name == "PuzzlePegTile") {
            tileImage = TileImageOf(c);
            key += "|" + tileImage.Key;
        }
        Color tint = Color.white;
        if (c.GetType().Name == "PuzzleVanillaItem") {   // Kettle and Walkie Talkie: the chosen colour
            tint = ItemTint(c, label);
            key += "|" + ColorUtility.ToHtmlStringRGB(tint);
            if (label.EndsWith("CannonBall") && BigWalkGameModels.GetTexture("Cannonball_BaseColor") != null) key += "|skin";   // rebuilt once the skin has loaded
        }
        if (previews.TryGetValue(c, out Preview existing)) {
            if (existing.key == key && existing.root != null) return;
            Remove(c);
        }
        Template template = PreviewTable.Find(label);
        if (template == null || template.parts.Length == 0) return;   // nothing to show: the placeholder stays
        Build(c, template, key, tileImage, tint);
    }

    // White keeps the game's own colour: a yellow kettle, a blue walkie talkie
    private static Color ItemTint(MonoBehaviour c, string label) {
        Color own = label.EndsWith("Kettle") ? new Color(1f, 0.786f, 0.089f) : label.EndsWith("WalkieTalkie") ? new Color(0.251f, 0.6f, 0.878f)
            : label.EndsWith("Backpack") ? new Color(0f, 0.663f, 0.588f)
            : label.EndsWith("Belt") ? new Color(0.29f, 0.082f, 0.22f) : label.EndsWith("FlareGun") ? new Color(0.745f, 0.278f, 0.193f) : Color.white;
        SerializedProperty p = new SerializedObject(c).FindProperty("itemColor");
        if (p == null || p.propertyType != SerializedPropertyType.Color) return own;
        Color chosen = p.colorValue;
        return Mathf.Abs(chosen.r - 1f) < 0.002f && Mathf.Abs(chosen.g - 1f) < 0.002f && Mathf.Abs(chosen.b - 1f) < 0.002f ? own : new Color(chosen.r, chosen.g, chosen.b, 1f);
    }

    private static readonly Dictionary<Color, Material> tintMaterials = new Dictionary<Color, Material>();
    private static Material TintMaterial(Color tint) {
        MaterialFor(Kind.VertexColor);
        if (tintMaterials.TryGetValue(tint, out Material known) && known != null) return known;
        Material m = Make(vertexColor.shader, tint, true, false);
        tintMaterials[tint] = m;
        return m;
    }

    // The picture a peg tile is set to show (its imageSet, image and customSheet fields)
    internal sealed class TileImage {
        public string set = "A";
        public int image = 1;
        public Texture2D custom;
        public string Key => set + "|" + image + "|" + (custom != null ? custom.GetInstanceID() : 0);
    }

    // The game's image set names, by PuzzleImageSet number
    private static readonly string[] GameSetNames = { "A", "B", "C", "D", "Scout", "Circle", "Pontiff", "Camo", "LargeA", "Custom" };

    internal static TileImage TileImageOf(MonoBehaviour c) {
        var result = new TileImage();
        var so = new SerializedObject(c);
        SerializedProperty set = so.FindProperty("imageSet");
        // By number, so PuzzlePegSet (Shapes = 0 ...) and PuzzleImageSet (A = 0 ...) both work
        if (set != null && set.propertyType == SerializedPropertyType.Enum && set.intValue >= 0 && set.intValue < GameSetNames.Length)
            result.set = GameSetNames[set.intValue];
        SerializedProperty image = so.FindProperty("image");
        if (image != null && image.propertyType == SerializedPropertyType.Integer) result.image = image.intValue;
        SerializedProperty custom = so.FindProperty("customSheet");
        if (custom != null && custom.propertyType == SerializedPropertyType.ObjectReference) result.custom = custom.objectReferenceValue as Texture2D;
        return result;
    }

    private static void Build(MonoBehaviour owner, Template template, string key, TileImage tileImage, Color tint) {
        var root = new GameObject(Prefix + " " + key) { hideFlags = HideFlags.HideAndDontSave };
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null) SceneManager.MoveGameObjectToScene(root, stage.scene);
        else if (owner.gameObject.scene.IsValid()) SceneManager.MoveGameObjectToScene(root, owner.gameObject.scene);
        root.layer = owner.gameObject.layer;

        int built = 0;
        foreach (Part part in template.parts) {
            if (part.mesh == "#text") {
                MakeText(root.transform, part, owner.gameObject.layer);
                continue;
            }
            Mesh mesh = part.mesh == "#cube" ? BuiltinCube() : part.mesh == "#sphere" ? BuiltinSphere() : BigWalkGameModels.Get(part.mesh, part.vertices);
            if (mesh == null) continue;
            GameObject go = MakePart(root.transform, part, owner.gameObject.layer);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var mats = new Material[mesh.subMeshCount];
            for (int s = 0; s < mats.Length; s++) {
                Kind kind = s < part.kinds.Length ? part.kinds[s] : Kind.VertexColor;
                mats[s] = kind == Kind.PegTileFace ? TileMaterial(tileImage) : kind == Kind.Tinted ? TintMaterial(tint) : MaterialFor(kind);
            }
            renderer.sharedMaterials = mats;
            built++;
        }

        var preview = new Preview {
            root = root, key = key,
            worldScale = new Vector3(template.worldScale.x, template.worldScale.y, template.worldScale.z),
            followsScale = Scalable.Contains(owner.GetType().Name),
        };
        // Hide the creator's placeholder shape while the real model shows (editor only, not saved)
        if (built > 0) {
            Renderer placeholder = owner.GetComponent<Renderer>();
            if (placeholder != null && !placeholder.forceRenderingOff) {
                placeholder.forceRenderingOff = true;
                preview.placeholder = placeholder;
            }
        }
        previews[owner] = preview;
        Follow();
    }

    // ---- PuzzleGlass: the creator's own meshes, drawn see-through (no game model needed) ----
    private static void RefreshGlass(MonoBehaviour c) {
        var filters = new List<MeshFilter>();
        foreach (MeshFilter mf in c.GetComponentsInChildren<MeshFilter>(false))
            if (mf.sharedMesh != null && mf.GetComponent<Renderer>() != null) filters.Add(mf);
        var so = new SerializedObject(c);
        SerializedProperty wireProp = so.FindProperty("wire");
        bool wire = wireProp != null && wireProp.boolValue;
        const float tiles = 6f;   // wire squares per metre, the same as the mod
        string key = "PuzzleGlass" + (wire ? "|wire " + tiles : "");
        foreach (MeshFilter mf in filters) {
            key += "|" + mf.GetInstanceID() + ":" + mf.sharedMesh.GetInstanceID() + ":" + mf.sharedMesh.subMeshCount;
            if (wire) key += ":" + mf.transform.lossyScale.ToString("F3");   // the wire's tiling follows the size
        }
        if (previews.TryGetValue(c, out Preview existing)) {
            if (existing.key == key && existing.root != null) return;
            Remove(c);
        }
        if (filters.Count == 0) return;

        var root = new GameObject(Prefix + " PuzzleGlass") { hideFlags = HideFlags.HideAndDontSave };
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null) SceneManager.MoveGameObjectToScene(root, stage.scene);
        else if (c.gameObject.scene.IsValid()) SceneManager.MoveGameObjectToScene(root, c.gameObject.scene);
        var preview = new Preview { root = root, key = key, worldScale = Vector3.one,
            followers = new List<(Transform, Transform)>(), hidden = new List<Renderer>(), ownMaterials = new List<Material>() };
        Material glassLook = MaterialFor(Kind.Glass);
        foreach (MeshFilter mf in filters) {
            var go = new GameObject(Prefix + " glass " + mf.name) { hideFlags = HideFlags.HideAndDontSave };
            go.layer = mf.gameObject.layer;
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mats = new Material[Mathf.Max(1, mf.sharedMesh.subMeshCount)];
            for (int i = 0; i < mats.Length; i++) mats[i] = glassLook;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            preview.followers.Add((go.transform, mf.transform));
            if (wire) AddWirePreview(go.transform, mf, tiles, preview.ownMaterials);
            // The creator's own material is replaced in-game: hide it here (editor only, not saved)
            Renderer own = mf.GetComponent<Renderer>();
            if (!own.forceRenderingOff) { own.forceRenderingOff = true; preview.hidden.Add(own); }
        }
        previews[c] = preview;
        Follow();
    }

    // Same as the mod: a copy of the mesh squashed to half its thickness, inside the pane
    private static void AddWirePreview(Transform copy, MeshFilter mf, float tiles, List<Material> made) {
        Mesh mesh = mf.sharedMesh;
        Bounds b = mesh.bounds;
        Vector3 world = Vector3.Scale(b.size, mf.transform.lossyScale);
        world = new Vector3(Mathf.Abs(world.x), Mathf.Abs(world.y), Mathf.Abs(world.z));
        int thin = world.x <= world.y && world.x <= world.z ? 0 : (world.y <= world.z ? 1 : 2);
        Vector3 squash = Vector3.one;
        squash[thin] = 0.5f;

        var go = new GameObject(Prefix + " wire") { hideFlags = HideFlags.HideAndDontSave };
        go.layer = copy.gameObject.layer;
        go.transform.SetParent(copy, false);
        go.transform.localScale = squash;
        go.transform.localPosition = b.center - Vector3.Scale(b.center, squash);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        float u = thin == 0 ? world.z : world.x;
        float v = thin == 1 ? world.z : world.y;
        MaterialFor(Kind.VertexColor);
        Material m = Make(vertexColor.shader, Color.white, false, true);
        m.SetFloat("_UseTexture", 1f);
        m.SetTexture("_MainTex", WireTexture());
        m.SetTextureScale("_MainTex", new Vector2(u * tiles, v * tiles));
        m.renderQueue = 2999;   // before the glass
        made.Add(m);
        var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
        for (int i = 0; i < mats.Length; i++) mats[i] = m;
        go.AddComponent<MeshRenderer>().sharedMaterials = mats;
    }

    // One wire square: black lines on the edges, see-through inside
    private static Texture2D wireTexture;
    private static Texture2D WireTexture() {
        if (wireTexture != null) return wireTexture;
        const int size = 64, line = 3;
        wireTexture = new Texture2D(size, size, TextureFormat.RGBA32, true) {
            name = "BigPuzzleSDK wire", hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear,
        };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++) {
                bool edge = x < line || x >= size - line || y < line || y >= size - line;
                pixels[y * size + x] = edge ? new Color(0.02f, 0.02f, 0.02f, 1f) : new Color(0f, 0f, 0f, 0f);
            }
        wireTexture.SetPixels(pixels);
        wireTexture.Apply(true, true);
        return wireTexture;
    }

    private static GameObject MakePart(Transform root, Part part, int layer) {
        var go = new GameObject(Prefix + " " + part.mesh) { hideFlags = HideFlags.HideAndDontSave };
        go.layer = layer;
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(part.position.x, part.position.y, part.position.z);
        go.transform.localRotation = new Quaternion(part.rotation.x, part.rotation.y, part.rotation.z, part.rotation.w);
        go.transform.localScale = new Vector3(part.scale.x, part.scale.y, part.scale.z);
        return go;
    }

    private static Mesh cube;
    private static Mesh sphere;
    internal static Mesh BuiltinSphere() {
        if (sphere == null) sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        return sphere;
    }

    internal static Mesh BuiltinCube() {
        if (cube == null) cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        return cube;
    }

    // The timer's face, as the game shows it: a dot-matrix of dim grey dots with the lit time on
    // top. The game draws the dots with its own dot-matrix font; here they are a tiled dot texture.
    private static void MakeText(Transform root, Part part, int layer) {
        // Dot grid: 23 x 8 square cells filling the black face (0.831 x 0.332) with a small border
        GameObject dots = MakePart(root, part, layer);
        dots.name = Prefix + " timer dots";
        dots.transform.localScale = Vector3.Scale(dots.transform.localScale, new Vector3(0.80f, 0.278f, 1f));
        dots.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        dots.AddComponent<MeshRenderer>().sharedMaterial = DotMaterial();

        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        if (font == null) {
            if (!warnedFont) Debug.LogWarning("[PuzzleSDK] Timer preview: no TextMesh Pro font found (Window > TextMeshPro > Import TMP Essential Resources).");
            warnedFont = true;
            return;
        }
        GameObject go = MakePart(root, part, layer);
        go.transform.localPosition += go.transform.localRotation * new Vector3(0f, 0f, -0.0005f);   // a hair in front of the dots
        var text = go.AddComponent<TextMeshPro>();
        text.font = font;
        text.text = "00:00";
        text.fontSize = 3f;
        text.color = new Color(0.8302f, 0.2693f, 0.1063f, 1f);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.rectTransform.sizeDelta = new Vector2(0.81f, 0.32f);   // the game's text box on the timer face
        foreach (Transform child in go.transform) child.gameObject.hideFlags = HideFlags.HideAndDontSave;   // TMP sub-meshes
    }
    private static bool warnedFont;

    private static Material dotMaterial;
    internal static Material DotMaterial() {
        if (dotMaterial != null) return dotMaterial;
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) {
            name = "BigPuzzleSDK timer dot", hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear,
        };
        var pixels = new Color[size * size];
        var grey = new Color(0.3f, 0.3f, 0.3f, 1f);
        var black = new Color(0.02f, 0.02f, 0.02f, 1f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++) {
                float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                pixels[y * size + x] = Color.Lerp(grey, black, Mathf.Clamp01((d - 0.38f) / 0.06f));   // soft-edged circle
            }
        tex.SetPixels(pixels);
        tex.Apply(true, true);
        MaterialFor(Kind.VertexColor);
        dotMaterial = Make(vertexColor.shader, Color.white, false, false);
        dotMaterial.SetFloat("_UseTexture", 1f);
        dotMaterial.SetTexture("_MainTex", tex);
        dotMaterial.SetTextureScale("_MainTex", new Vector2(23f, 8f));
        return dotMaterial;
    }

    // ---- Removing ----
    private static void Remove(MonoBehaviour c) {
        if (!previews.TryGetValue(c, out Preview p)) return;
        if (p.root != null) UnityEngine.Object.DestroyImmediate(p.root);
        if (p.placeholder != null) p.placeholder.forceRenderingOff = false;
        if (p.hidden != null) foreach (Renderer r in p.hidden) if (r != null) r.forceRenderingOff = false;
        if (p.ownMaterials != null) foreach (Material m in p.ownMaterials) if (m != null) UnityEngine.Object.DestroyImmediate(m);
        previews.Remove(c);
    }

    // Removes every preview (also called by the exporter before it builds a pack)
    public static void RemoveAll() {
        foreach (MonoBehaviour c in new List<MonoBehaviour>(previews.Keys)) Remove(c);
        previews.Clear();
        // Any left over from before a script reload
        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go != null && go.name.StartsWith(Prefix) && go.transform.parent == null) UnityEngine.Object.DestroyImmediate(go);
        nextTick = 0;
    }

    // Removes previews from a copy of a puzzle (the exporter's temporary copy). Previews are
    // never inside a puzzle, so this is only a safety net.
    public static void StripFrom(GameObject root) {
        var doomed = new List<GameObject>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith(Prefix)) doomed.Add(t.gameObject);
        foreach (GameObject go in doomed) if (go != null) UnityEngine.Object.DestroyImmediate(go);
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = false;
    }

    // Clicking a preview in the Scene view selects the SDK object it belongs to
    private static void RedirectSelection() {
        GameObject picked = Selection.activeGameObject;
        if (picked == null || !picked.name.StartsWith(Prefix)) return;
        GameObject root = picked.transform.root.gameObject;
        foreach (var kv in previews)
            if (kv.Value.root == root && kv.Key != null) { Selection.activeGameObject = kv.Key.gameObject; return; }
    }

    // ---- Clicking a preview in the Scene view selects its SDK object ----
    // The previews are hidden objects, which Unity's own click-to-select never picks, so clicks
    // are watched here: a click (no drag) on a preview selects the SDK object it belongs to,
    // unless one of the scene's own objects is in front of it. Shift/Ctrl add or remove.
    private static Vector2 downAt;
    private static bool downPending;
    private static readonly Dictionary<Mesh, (Vector3[] v, int[] t)> pickMeshes = new Dictionary<Mesh, (Vector3[], int[])>();

    private static void HandleClicks(SceneView view) {
        Event e = Event.current;
        if (e.button != 0 || e.alt) return;
        if (e.rawType == EventType.MouseDown) {
            downAt = e.mousePosition;
            downPending = GUIUtility.hotControl == 0;   // not on a move/rotate/scale handle
            return;
        }
        if (e.rawType != EventType.MouseUp || !downPending) return;
        downPending = false;
        if ((e.mousePosition - downAt).sqrMagnitude > 16f) return;   // a drag, not a click
        if (previews.Count == 0) return;

        GameObject owner = PreviewUnder(e.mousePosition, out float distance);
        if (owner == null) return;

        // Something of the scene's own in front of the preview wins
        GameObject normal = HandleUtility.PickGameObject(e.mousePosition, false);
        if (normal != null && normal != owner) {
            Renderer r = normal.GetComponent<Renderer>();
            if (r != null && r.bounds.IntersectRay(HandleUtility.GUIPointToWorldRay(e.mousePosition), out float d) && d < distance) return;
        }

        if (e.shift || EditorGUI.actionKey) {
            var list = new List<UnityEngine.Object>(Selection.objects);
            if (list.Contains(owner)) list.Remove(owner); else list.Add(owner);
            Selection.objects = list.ToArray();
        } else {
            Selection.activeGameObject = owner;
        }
        e.Use();
    }

    // The SDK object whose preview is under this Scene view point, and how far away it is
    private static GameObject PreviewUnder(Vector2 position, out float best) {
        Ray ray = HandleUtility.GUIPointToWorldRay(position);
        GameObject owner = null;
        best = float.MaxValue;
        foreach (var kv in previews) {
            MonoBehaviour c = kv.Key;
            if (c == null || kv.Value.root == null) continue;
            foreach (MeshFilter mf in kv.Value.root.GetComponentsInChildren<MeshFilter>(true)) {
                if (mf.sharedMesh == null) continue;
                Renderer r = mf.GetComponent<Renderer>();
                if (r == null || !r.bounds.IntersectRay(ray, out float boundsHit) || boundsHit > best) continue;
                if (!mf.sharedMesh.isReadable) {   // a creator's mesh that can't be read: its bounds will do
                    if (boundsHit < best) { best = boundsHit; owner = c.gameObject; }
                    continue;
                }
                if (HitMesh(ray, mf, out float hit) && hit < best) { best = hit; owner = c.gameObject; }
            }
        }
        return owner;
    }

    // Ray against a mesh's triangles; distance in world units
    private static bool HitMesh(Ray ray, MeshFilter mf, out float distance) {
        distance = float.MaxValue;
        Mesh mesh = mf.sharedMesh;
        if (!pickMeshes.TryGetValue(mesh, out var data)) {
            if (!mesh.isReadable) return false;
            data = (mesh.vertices, mesh.triangles);
            pickMeshes[mesh] = data;
        }
        Matrix4x4 toLocal = mf.transform.worldToLocalMatrix;
        Vector3 o = toLocal.MultiplyPoint3x4(ray.origin);
        Vector3 dir = toLocal.MultiplyVector(ray.direction);
        Vector3[] v = data.v; int[] t = data.t;
        float bestT = float.MaxValue;
        for (int i = 0; i + 2 < t.Length; i += 3) {
            Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) continue;          // parallel; both sides count
            float inv = 1f / det;
            Vector3 s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) continue;
            Vector3 q = Vector3.Cross(s, e1);
            float w = Vector3.Dot(dir, q) * inv;
            if (w < 0f || u + w > 1f) continue;
            float tt = Vector3.Dot(e2, q) * inv;
            if (tt > 0f && tt < bestT) bestT = tt;
        }
        if (bestT == float.MaxValue) return false;
        Vector3 world = mf.transform.localToWorldMatrix.MultiplyPoint3x4(o + dir * bestT);
        distance = Vector3.Distance(ray.origin, world);
        return true;
    }

    // ---- Materials (in memory only) ----
    internal static Material MaterialFor(Kind kind) {
        if (vertexColor == null) {
            Shader shader = Shader.Find("Hidden/BigPuzzleSDK/GamePreview");
            if (shader == null) {
                if (!warnedShader) Debug.LogWarning("[PuzzleSDK] The preview shader (GamePreview.shader) is missing; previews use a plain material.");
                warnedShader = true;
                shader = Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/InternalErrorShader");
            }
            vertexColor = Make(shader, Color.white, true, false);
            glass = Make(shader, new Color(0.865f, 0.898f, 0.961f, 0.22f), false, true);
            screen = Make(shader, new Color(0.035f, 0.035f, 0.04f, 1f), false, false);
            plain = Make(shader, new Color(0.62f, 0.62f, 0.6f, 1f), false, false);
            redTint = Make(shader, new Color(1f, 0.22f, 0.2f, 1f), true, false);   // the red buoy: vertex colours, pushed to red
        }
        if (kind == Kind.Cannonball) {
            Texture2D skin = BigWalkGameModels.GetTexture("Cannonball_BaseColor");
            if (cannonball == null || (skin != null && cannonball.GetTexture("_MainTex") != skin) || (skin == null && cannonballHasSkin)) {
                cannonball = Make(vertexColor.shader, skin != null ? Color.white : new Color(0.3f, 0.32f, 0.36f, 1f), false, false);
                if (skin != null) { cannonball.SetFloat("_UseTexture", 1f); cannonball.SetTexture("_MainTex", skin); }
                cannonballHasSkin = skin != null;
            }
            return cannonball;
        }
        switch (kind) {
            case Kind.Glass: return glass;
            case Kind.Plain: return plain;
            case Kind.Screen: return screen;
            case Kind.RedTint: return redTint;
            default: return vertexColor;
        }
    }

    // A peg tile's picture: one cell of an image sheet (shared by every tile showing that picture)
    private static readonly Dictionary<string, Material> tileMaterials = new Dictionary<string, Material>();

    internal static Material TileMaterial(TileImage image) {
        MaterialFor(Kind.VertexColor);   // makes sure the shared materials exist
        if (image == null || !PreviewTable.CellFor(image.set, image.image, out TileCell cell)) return vertexColor;   // blank face
        Texture2D sheet = cell.texture == null ? image.custom : BigWalkGameModels.GetTexture(cell.texture);
        if (sheet == null) return vertexColor;
        string key = sheet.GetInstanceID() + "|" + cell.x + "," + cell.y + "|" + cell.columns + "x" + cell.rows;
        if (tileMaterials.TryGetValue(key, out Material known) && known != null) return known;
        Material m = Make(vertexColor.shader, Color.white, true, false);   // the tile face's own light colour
        m.SetFloat("_UseTexture", 2f);   // the sheet is a mask: printed as dark ink
        m.SetTexture("_MainTex", sheet);
        // The face's UVs cover 0..1; show only this cell (cells count from the top-left)
        m.SetTextureScale("_MainTex", new Vector2(1f / cell.columns, 1f / cell.rows));
        m.SetTextureOffset("_MainTex", new Vector2((float)cell.x / cell.columns, 1f - (cell.y + 1f) / cell.rows));
        tileMaterials[key] = m;
        return m;
    }

    private static Material Make(Shader shader, Color color, bool useVertexColor, bool transparent) {
        var m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        m.SetColor("_Color", color);
        m.SetFloat("_VertexColor", useVertexColor ? 1f : 0f);
        if (transparent) {
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);   // glass is seen from both sides in-game, so it is here too
            m.renderQueue = 3000;
        }
        return m;
    }
}

} // namespace BigPuzzleSDK.GameAssets
