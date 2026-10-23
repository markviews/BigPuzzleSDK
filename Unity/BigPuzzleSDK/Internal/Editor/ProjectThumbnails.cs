// BigPuzzleSDK: Project window thumbnails for prefabs that contain SDK components, drawn with the
// game's real models (the same previews as in the Scene view) instead of the placeholder shapes.
//
// The thumbnails are drawn over Unity's own icon and kept in memory only; nothing is saved.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TMPro;

namespace BigPuzzleSDK.GameAssets {

[InitializeOnLoad]
public static class ProjectThumbnails {
    private const int Size = 128;
    private const int PerFrame = 2;   // thumbnails rendered per editor update, so the window never stalls

    private sealed class Entry { public string hash; public Texture2D texture; public bool hasSdk; }
    private static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
    private static readonly List<string> queue = new List<string>();
    private static PreviewRenderUtility renderer;

    static ProjectThumbnails() {
        EditorApplication.projectWindowItemOnGUI += OnItem;
        EditorApplication.update += Process;
        BigWalkGameModels.Changed += ClearAll;
        AssemblyReloadEvents.beforeAssemblyReload += () => { ClearAll(); if (renderer != null) { renderer.Cleanup(); renderer = null; } };
    }

    // Thumbnails are redrawn when the prefab or the preview shader changes
    private static string HashFor(string path) {
        string hash = AssetDatabase.GetAssetDependencyHash(path).ToString();
        Shader shader = Shader.Find("Hidden/BigPuzzleSDK/GamePreview");
        string shaderPath = shader != null ? AssetDatabase.GetAssetPath(shader) : null;
        if (!string.IsNullOrEmpty(shaderPath)) hash += "|" + AssetDatabase.GetAssetDependencyHash(shaderPath);
        return hash;
    }

    private static void ClearAll() {
        foreach (Entry e in cache.Values) if (e.texture != null) UnityEngine.Object.DestroyImmediate(e.texture);
        cache.Clear();
        queue.Clear();
        EditorApplication.RepaintProjectWindow();
    }

    private static void OnItem(string guid, Rect rect) {
        if (Event.current.type != EventType.Repaint || !GamePreviews.Enabled || !BigWalkGameModels.Ready) return;
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return;

        string hash = HashFor(path);
        if (!cache.TryGetValue(guid, out Entry entry) || entry.hash != hash) {
            if (!queue.Contains(guid)) queue.Add(guid);
            return;   // Unity's own icon until ours is ready
        }
        if (!entry.hasSdk || entry.texture == null) return;

        // The icon part of the item: the square top of a grid cell, or the small icon in list view
        Rect icon = rect.height > rect.width * 0.9f || rect.height > 24f
            ? new Rect(rect.x, rect.y, rect.width, rect.width)
            : new Rect(rect.x + 3f, rect.y, rect.height, rect.height);
        EditorGUI.DrawRect(icon, EditorGUIUtility.isProSkin ? new Color(0.2f, 0.2f, 0.2f) : new Color(0.76f, 0.76f, 0.76f));
        GUI.DrawTexture(icon, entry.texture, ScaleMode.ScaleToFit);
    }

    private static void Process() {
        if (queue.Count == 0) return;
        int done = 0;
        while (queue.Count > 0 && done < PerFrame) {
            string guid = queue[0];
            queue.RemoveAt(0);
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var entry = new Entry { hash = HashFor(path) };
            try {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) entry.texture = Render(prefab, out entry.hasSdk);
            } catch (Exception ex) {
                CleanTemporary();
                Debug.LogWarning($"[PuzzleSDK] Could not draw the thumbnail of {path}: {ex.Message}");
            }
            if (cache.TryGetValue(guid, out Entry old) && old.texture != null) UnityEngine.Object.DestroyImmediate(old.texture);
            cache[guid] = entry;
            done++;
        }
        EditorApplication.RepaintProjectWindow();
    }

    // ---- Drawing one prefab ----
    private struct Draw { public Mesh mesh; public Matrix4x4 matrix; public Material[] materials; }

    // Templates seen better from another side than the front (e.g. a ceiling light facing down)
    private static readonly Dictionary<string, Vector3> ViewFrom = new Dictionary<string, Vector3> {
        { "PuzzleLight Circle", new Vector3(0.5f, -0.6f, 0.62f) },
    };

    private static readonly List<GameObject> temporary = new List<GameObject>();

    private static Texture2D Render(GameObject prefab, out bool hasSdk) {
        var draws = new List<Draw>();
        var sdkObjects = new HashSet<GameObject>();
        hasSdk = false;
        Vector3 dir = new Vector3(0.55f, 0.45f, 0.7f);   // the game objects face +Z
        bool onlyGlass = true;   // every SDK component here is a PuzzleGlass

        foreach (MonoBehaviour c in prefab.GetComponentsInChildren<MonoBehaviour>(true)) {
            if (!GamePreviews.IsSdkComponent(c)) continue;
            hasSdk = true;
            if (c.GetType().Name != "PuzzleGlass") onlyGlass = false;
            string label = GamePreviews.LabelFor(c);
            Template template = PreviewTable.Find(label);
            if (template == null || template.parts.Length == 0) continue;
            sdkObjects.Add(c.gameObject);   // its placeholder shape is replaced by the game model
            if (ViewFrom.TryGetValue(label, out Vector3 view)) dir = view;
            GamePreviews.TileImage tile = c.GetType().Name == "PuzzlePegTile" ? GamePreviews.TileImageOf(c) : null;

            Transform t = c.transform;
            var world = new Vector3(template.worldScale.x, template.worldScale.y, template.worldScale.z);
            Vector3 scale = GamePreviews.Scalable.Contains(c.GetType().Name) ? Vector3.Scale(world, t.lossyScale) : world;
            Matrix4x4 root = Matrix4x4.TRS(t.position, t.rotation, scale);
            foreach (Part part in template.parts) {
                Matrix4x4 partMatrix = root * Matrix4x4.TRS(
                    new Vector3(part.position.x, part.position.y, part.position.z),
                    new Quaternion(part.rotation.x, part.rotation.y, part.rotation.z, part.rotation.w),
                    new Vector3(part.scale.x, part.scale.y, part.scale.z));
                if (part.mesh == "#text") { AddTimerFace(draws, partMatrix); continue; }
                Mesh mesh = part.mesh == "#cube" ? GamePreviews.BuiltinCube() : part.mesh == "#sphere" ? GamePreviews.BuiltinSphere() : part.mesh.StartsWith("#") ? null : BigWalkGameModels.Get(part.mesh, part.vertices);
                if (mesh == null) continue;
                var mats = new Material[mesh.subMeshCount];
                for (int s = 0; s < mats.Length; s++) {
                    Kind kind = s < part.kinds.Length ? part.kinds[s] : Kind.VertexColor;
                    mats[s] = kind == Kind.PegTileFace ? GamePreviews.TileMaterial(tile) : GamePreviews.MaterialFor(kind);
                }
                draws.Add(new Draw { mesh = mesh, matrix = partMatrix, materials = mats });
            }
        }
        if (!hasSdk) return null;

        // The creator's own meshes (except the placeholders the game models replace)
        foreach (MeshRenderer r in prefab.GetComponentsInChildren<MeshRenderer>(true)) {
            if (sdkObjects.Contains(r.gameObject) || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            MeshFilter f = r.GetComponent<MeshFilter>();
            if (f == null || f.sharedMesh == null) continue;
            draws.Add(new Draw { mesh = f.sharedMesh, matrix = r.transform.localToWorldMatrix, materials = r.sharedMaterials });
        }
        if (draws.Count == 0) { CleanTemporary(); return null; }

        // Frame everything, seen from the front (+Z), a little from the side and above
        Bounds bounds = BoundsOf(draws);
        // A flat glass pane (a Plane lies flat at 0, 0, 0) is shown standing up like a window
        // in the thumbnail only; the scene and the prefab are not changed
        if (onlyGlass && bounds.size.y < 0.25f * Mathf.Max(bounds.size.x, bounds.size.z)) {
            Matrix4x4 standUp = Matrix4x4.TRS(bounds.center, Quaternion.Euler(90f, 0f, 0f), Vector3.one) * Matrix4x4.TRS(-bounds.center, Quaternion.identity, Vector3.one);
            for (int i = 0; i < draws.Count; i++) { Draw d = draws[i]; d.matrix = standUp * d.matrix; draws[i] = d; }
            bounds = BoundsOf(draws);
        }
        // Glass is looked at square on (a little from the side and above), from the pane's wide side
        if (onlyGlass) dir = bounds.size.x < bounds.size.z ? new Vector3(0.92f, 0.25f, 0.3f) : new Vector3(0.3f, 0.25f, 0.92f);
        float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);

        if (renderer == null) renderer = new PreviewRenderUtility();
        Camera cam = renderer.camera;
        cam.fieldOfView = 30f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.32f, 0.32f, 0.32f, 1f);   // dark models stay visible
        dir = dir.normalized;
        float distance = radius / Mathf.Sin(15f * Mathf.Deg2Rad);
        cam.transform.position = bounds.center + dir * distance;
        cam.transform.rotation = Quaternion.LookRotation(-dir);
        cam.nearClipPlane = Mathf.Max(0.001f, distance - radius * 2f);
        cam.farClipPlane = distance + radius * 2f;

        renderer.BeginStaticPreview(new Rect(0, 0, Size, Size));
        foreach (Draw d in draws)
            for (int s = 0; s < d.mesh.subMeshCount; s++) {
                Material m = d.materials != null && d.materials.Length > 0 ? d.materials[Mathf.Min(s, d.materials.Length - 1)] : null;
                if (m != null) renderer.DrawMesh(d.mesh, d.matrix, m, s);
            }
        renderer.Render(true);
        Texture2D texture = renderer.EndStaticPreview();
        CleanTemporary();
        texture.hideFlags = HideFlags.HideAndDontSave;
        return texture;
    }

    private static Bounds BoundsOf(List<Draw> draws) {
        Bounds bounds = default;
        bool first = true;
        foreach (Draw d in draws) {
            Bounds b = TransformBounds(d.mesh.bounds, d.matrix);
            if (first) { bounds = b; first = false; } else bounds.Encapsulate(b);
        }
        return bounds;
    }

    // The timer's dot grid and "00:00", as in the Scene view preview
    private static void AddTimerFace(List<Draw> draws, Matrix4x4 face) {
        draws.Add(new Draw {
            mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx"),
            matrix = face * Matrix4x4.Scale(new Vector3(0.80f, 0.278f, 1f)),
            materials = new[] { GamePreviews.DotMaterial() },
        });
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        if (font == null) return;
        var go = new GameObject("BigPuzzleSDK thumbnail text") { hideFlags = HideFlags.HideAndDontSave };
        temporary.Add(go);
        var text = go.AddComponent<TextMeshPro>();
        text.font = font;
        text.text = "00:00";
        text.fontSize = 3f;
        text.color = new Color(0.8302f, 0.2693f, 0.1063f, 1f);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = new Vector2(0.81f, 0.32f);
        text.ForceMeshUpdate();
        if (text.mesh == null) return;
        draws.Add(new Draw {
            mesh = text.mesh,
            matrix = face * Matrix4x4.Translate(new Vector3(0f, 0f, -0.0005f)),
            materials = new[] { text.fontSharedMaterial },
        });
    }

    private static void CleanTemporary() {
        foreach (GameObject go in temporary) if (go != null) UnityEngine.Object.DestroyImmediate(go);
        temporary.Clear();
    }

    private static Bounds TransformBounds(Bounds b, Matrix4x4 m) {
        Vector3 c = m.MultiplyPoint3x4(b.center);
        Vector3 e = b.extents;
        Vector3 ax = m.MultiplyVector(new Vector3(e.x, 0, 0)), ay = m.MultiplyVector(new Vector3(0, e.y, 0)), az = m.MultiplyVector(new Vector3(0, 0, e.z));
        var size = new Vector3(
            Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
            Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
            Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
        return new Bounds(c, size * 2f);
    }
}

} // namespace BigPuzzleSDK.GameAssets
