using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Creator tool (Ctrl+F3 with DevKeys on): writes everything the editor-side asset ripper will
// need about each vanilla template to BepInEx/BigPuzzleSDK_TemplateDump.txt: the template's
// scene and path, its child hierarchy with local transforms, and for every renderer its mesh
// (name, readable, vertex count, static-batched or not) and materials (shader, textures, colours).
// Read-only: it changes nothing in the game. Only the templates for the current lobby size are
// loaded, so run it once in a 2-, 3- and 4-player lobby to get every variant.
internal static class TemplateDump {
    // Component, then the exact paths (any lobby size) or, prefixed with "name:", an object name to search for
    internal static readonly (string label, string[] paths)[] Targets = {
        ("PuzzleButton",      new[] { GameRefs.Button.TemplatePath }),
        ("PuzzleSwitch",      new[] { GameRefs.Switch.ExactPath }),
        ("PuzzleBox",         new[] { GameRefs.Box.ExactPath }),
        ("PuzzleConfetti",    new[] { GameRefs.Confetti.ExactPath }),
        ("PuzzleDoor",        new[] { GameRefs.Door.ExactPath }),
        ("PuzzleGourdPlinth", new[] { GameRefs.GourdPlinth.ExactPath }),
        ("PuzzleGourdVice",   new[] { GameRefs.GourdVice.ExactPath }),
        ("PuzzleLiveSignal",  new[] { GameRefs.LiveSignal.ExactPath }),
        ("PuzzlePegBoard",    new[] { GameRefs.PegBoard.ExactPath }),
        ("PuzzlePegTile",     new[] { GameRefs.PegTile.ExactPath }),
        ("PuzzleStartSwitch", new[] { GameRefs.StartSwitch.ExactPath }),
        ("PuzzleValidator",   new[] { GameRefs.Validator.ExactPath }),
        ("PuzzleDisplay",     new[] {
            GameRefs.Display.ExactPath,
            GameRefs.Display.ExactPath3,
            GameRefs.Display.ExactPath2 }),
        ("PuzzleTimer",       new[] {
            GameRefs.Timer.ExactPath,
            GameRefs.Timer.ExactPath3,
            GameRefs.Timer.ExactPath2 }),
        ("PuzzlePokeButton",  new[] {
            GameRefs.PokeButton.ExactPath,
            GameRefs.PokeButton.ExactPath3,
            GameRefs.PokeButton.ExactPath2 }),
        ("PuzzleProgressBar", new[] {
            GameRefs.ProgressBar.ExactPath,
            GameRefs.ProgressBar.ExactPath3,
            GameRefs.ProgressBar.ExactPath2 }),
        ("PuzzleSimPressSwitch", new[] {
            GameRefs.SimPressSwitch.ExactPath,
            GameRefs.SimPressSwitch.ExactPath3,
            GameRefs.SimPressSwitch.ExactPath2 }),
        ("PuzzleLight TableRed",        new[] { GameRefs.Light.ExactPath(PuzzleLightType.TableRed) }),
        ("PuzzleLight Pendant",         new[] { GameRefs.Light.ExactPath(PuzzleLightType.Pendant) }),
        ("PuzzleLight Spotlight_Black", new[] { GameRefs.Light.ExactPath(PuzzleLightType.Spotlight_Black) }),
        ("PuzzleLight Spotlight_Red",   new[] { "name:" + GameRefs.Light.TemplateName(PuzzleLightType.Spotlight_Red) + "|" + GameRefs.Light.PreferredPath(PuzzleLightType.Spotlight_Red) }),
        ("PuzzleLight Tube",            new[] { "name:" + GameRefs.Light.TemplateName(PuzzleLightType.Tube) + "|" + GameRefs.Light.PreferredPath(PuzzleLightType.Tube) }),
        ("PuzzleLight Circle",          new[] { "name:" + GameRefs.Light.TemplateName(PuzzleLightType.Circle) + "|" + GameRefs.Light.PreferredPath(PuzzleLightType.Circle) }),
        ("PuzzleLight Ring",            new[] { "name:" + GameRefs.Light.TemplateName(PuzzleLightType.Ring) + "|" + GameRefs.Light.PreferredPath(PuzzleLightType.Ring) }),
        ("PuzzleTelescope",             new[] { GameRefs.Telescope.ExactPath, GameRefs.ScanExtras.TelescopeLobbyVision }),
        ("PuzzlePillow",                new[] { GameRefs.Pillow.ExactPath, GameRefs.ScanExtras.PillowByName }),
    };

    // ---- Batch scan from a list file ----
    // BepInEx/config/BigPuzzleSDK_ScanTargets.txt, one target per line:
    //     Label = exact/path/from/UnityExplorer ; another/path/for/other/lobby/sizes ; name:ObjectName|preferred/path/part
    // Lines starting with # are ignored. When the file lists anything, Ctrl+F3 scans only those
    // (re-read on every press, so no rebuild is needed) and also writes every game script's settings.
    private static string TargetsFile => Path.Combine(Paths.ConfigPath, "BigPuzzleSDK_ScanTargets.txt");
    private static bool detailed;   // also write the game scripts' settings

    private static (string label, string[] paths)[] FileTargets() {
        var list = new List<(string, string[])>();
        try {
            if (!File.Exists(TargetsFile)) {
                File.WriteAllText(TargetsFile,
                    "# BigPuzzleSDK scan targets (Ctrl+F3 with DevKeys on). One per line:\n" +
                    "#   Label = exact/path/from/UnityExplorer ; path/in/another/lobby/size ; name:ObjectName|preferred/path/part\n" +
                    "# Example:\n" +
                    "#   PuzzleFogMachine = LandmarksPlayerCountAny/SomeChallenge/Positioner/FogMachine\n");
                return list.ToArray();
            }
            foreach (string raw in File.ReadAllLines(TargetsFile)) {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) { Plugin.log.LogWarning($"[Puzzle] scan targets: no '=' in \"{line}\", skipped"); continue; }
                string label = line.Substring(0, eq).Trim();
                var paths = new List<string>();
                foreach (string part in line.Substring(eq + 1).Split(';')) {
                    string path = part.Trim().Trim('/');
                    if (path.Length > 0) paths.Add(path);
                }
                if (paths.Count > 0) list.Add((label, paths.ToArray()));
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[Puzzle] could not read {TargetsFile}: {ex.GetType().Name}: {ex.Message}");
        }
        return list.ToArray();
    }

    internal static void Run() {
        var fromFile = FileTargets();
        detailed = fromFile.Length > 0;
        var targets = detailed ? fromFile : Targets;
        var sb = new StringBuilder();
        var meshes = new SortedDictionary<string, string>();      // unique meshes, for the summary
        var textures = new SortedDictionary<string, string>();    // unique textures
        var shaders = new SortedSet<string>();
        int found = 0, missing = 0;

        sb.AppendLine("BigPuzzleSDK template dump  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Game " + Application.version + "  Unity " + Application.unityVersion);
        sb.Append("Loaded scenes:");
        for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++) {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
            sb.Append($"  {scene.name} (index {scene.buildIndex}, {scene.path})");
        }
        sb.AppendLine().AppendLine();

        foreach (var (label, paths) in targets) {
            GameObject root = null;
            foreach (string p in paths) {
                root = p.StartsWith("name:") ? SearchByName(p.Substring(5)) : Templates.Find(p);
                if (root != null) break;
            }
            sb.AppendLine("==================== " + label);
            if (root == null) {
                sb.AppendLine("  NOT LOADED in this lobby").AppendLine();
                missing++;
                continue;
            }
            found++;
            sb.AppendLine($"  scene: {root.scene.name}   path: {Templates.FullPath(root.transform)}");
            sb.AppendLine($"  activeInHierarchy: {root.activeInHierarchy}   world scale: {V(root.transform.lossyScale)}");
            Matrix4x4 w = root.transform.localToWorldMatrix;
            sb.AppendLine($"  world matrix: {w.m00:R} {w.m01:R} {w.m02:R} {w.m03:R} {w.m10:R} {w.m11:R} {w.m12:R} {w.m13:R} {w.m20:R} {w.m21:R} {w.m22:R} {w.m23:R}");
            DumpObject(root.transform, 1, sb, meshes, textures, shaders);
            sb.AppendLine();
        }

        if (!detailed) DumpPegTileImages(sb, textures);

        sb.AppendLine("==================== UNIQUE MESHES (name | vertices | readable | notes)");
        foreach (var kv in meshes) sb.AppendLine("  " + kv.Key + " | " + kv.Value);
        sb.AppendLine().AppendLine("==================== UNIQUE TEXTURES (name | size | format | readable)");
        foreach (var kv in textures) sb.AppendLine("  " + kv.Key + " | " + kv.Value);
        sb.AppendLine().AppendLine("==================== SHADERS");
        foreach (string s in shaders) sb.AppendLine("  " + s);

        string file = Path.Combine(Paths.BepInExRootPath, detailed
            ? "BigPuzzleSDK_ScanDump_" + DateTime.Now.ToString("HHmmss") + ".txt"
            : "BigPuzzleSDK_TemplateDump.txt");
        try {
            File.WriteAllText(file, sb.ToString());
            Plugin.log.LogInfo($"[Puzzle] Ctrl+F3: dumped {found} template(s) ({missing} not loaded in this lobby), {meshes.Count} meshes, {textures.Count} textures to {file}");
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[Puzzle] Ctrl+F3: could not write {file}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Which atlas cell every peg tile image uses, so the editor preview can show the right picture
    private static void DumpPegTileImages(StringBuilder sb, SortedDictionary<string, string> textures) {
        sb.AppendLine("==================== PEG TILE IMAGES");
        try {
            PegTileDataSet data = null;
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PegTileRenderer>())) {
                PegTileRenderer r = obj.TryCast<PegTileRenderer>();
                if (r != null && r.pegTileDataSet != null && r.gameObject.scene.IsValid()) { data = r.pegTileDataSet; break; }
            }
            if (data == null) { sb.AppendLine("  no PegTileRenderer loaded").AppendLine(); return; }
            sb.AppendLine($"  data set: {data.name}");
            var materials = data.materialPerTextureIndex;
            var sizes = data.sizePerTextureIndex;
            for (int i = 0; i < materials.Length; i++) {
                Material m = materials[i];
                Texture tex = m != null && m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                string grid = i < sizes.Length ? $"{sizes[i].x}x{sizes[i].y}" : "?";
                sb.AppendLine($"  sheet {i}: material '{(m != null ? m.name : "none")}' texture '{(tex != null ? tex.name : "none")}' {(tex != null ? tex.width + "x" + tex.height : "")} grid {grid}");
                if (tex != null) textures[tex.name] = $"{tex.width}x{tex.height} | peg tile sheet {i}";
            }
            var entries = data.indexDatas;
            foreach (PuzzleImageSet set in Enum.GetValues(typeof(PuzzleImageSet))) {
                if (set == PuzzleImageSet.Custom) continue;
                for (int image = 0; image <= 16; image++) {
                    PropGroup group = PuzzleDisplay.GroupFor(set, image);
                    string where = "none";
                    for (int e = 0; e < entries.Length; e++) {
                        if (entries[e].propGroup != group) continue;
                        where = $"sheet {entries[e].textureIndex} cell {entries[e].xIndex},{entries[e].yIndex}";
                        break;
                    }
                    sb.AppendLine($"  {set} {image}: group {group} -> {where}");
                }
            }
        } catch (Exception ex) {
            sb.AppendLine($"  failed: {ex.GetType().Name}: {ex.Message}");
        }
        sb.AppendLine();
    }

    // "Name|preferred/path/part": an object with that name (or "Name (n)"), preferring one whose path contains the part
    internal static GameObject SearchByName(string spec) {
        string[] parts = spec.Split('|');
        string wanted = parts[0];
        string preferred = parts.Length > 1 ? parts[1] : null;
        GameObject first = null;
        foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Transform>())) {
            Transform t = obj.TryCast<Transform>();
            if (t == null || !t.gameObject.scene.IsValid()) continue;
            if (t.name != wanted && !t.name.StartsWith(wanted + " (")) continue;
            if (Templates.FullPath(t).Contains("BigPuzzleSDK") || Templates.FullPath(t).Contains("Vanilla")) continue;
            if (first == null) first = t.gameObject;
            if (preferred != null && Templates.FullPath(t).Contains(preferred)) return t.gameObject;
        }
        return first;
    }

    private static void DumpObject(Transform t, int depth, StringBuilder sb, SortedDictionary<string, string> meshes,
                                   SortedDictionary<string, string> textures, SortedSet<string> shaders) {
        string pad = new string(' ', depth * 2);
        GameObject go = t.gameObject;
        sb.AppendLine($"{pad}- {go.name}{(go.activeSelf ? "" : "  [inactive]")}   pos {V(t.localPosition)}  rot {V(t.localEulerAngles)}  scale {V(t.localScale)}  layer {go.layer}{(go.isStatic ? "  static" : "")}");

        var names = new List<string>();
        foreach (Component c in go.GetComponents(Il2CppType.Of<Component>())) {
            if (c == null) continue;
            string n = c.GetIl2CppType().Name;
            if (n == "Transform") continue;
            Behaviour b = c.TryCast<Behaviour>();
            names.Add(b != null && !b.enabled ? n + "(off)" : n);
        }
        if (names.Count > 0) sb.AppendLine($"{pad}    components: {string.Join(", ", names)}");
        if (detailed) DumpScripts(go, pad + "    ", sb);

        try {
            MeshFilter mf = go.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            SkinnedMeshRenderer skinned = go.GetComponent<SkinnedMeshRenderer>();
            if (mesh == null && skinned != null) mesh = skinned.sharedMesh;
            Renderer r = go.GetComponent<Renderer>();
            if (mesh != null) {
                string notes = "";
                if (mesh.name.StartsWith("Combined Mesh")) notes += " STATIC-BATCHED";
                bool readable = false;
                try { readable = mesh.isReadable; } catch (Exception) { }
                sb.AppendLine($"{pad}    mesh: '{mesh.name}'  verts {mesh.vertexCount}  submeshes {mesh.subMeshCount}  readable {readable}  bounds {V(mesh.bounds.center)}/{V(mesh.bounds.size)}{notes}");
                if (r != null) {
                    try {
                        if (r.isPartOfStaticBatch) {
                            MeshRenderer mr = r.TryCast<MeshRenderer>();
                            string first = mr != null ? mr.subMeshStartIndex.ToString() : "?";
                            sb.AppendLine($"{pad}    PART OF STATIC BATCH (first submesh {first}, materials {r.sharedMaterials.Length})");
                        }
                    } catch (Exception) { }
                }
                meshes[mesh.name] = $"{mesh.vertexCount} | {readable} |{notes}";
            }
            if (r != null) {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) {
                    Material m = mats[i];
                    if (m == null) { sb.AppendLine($"{pad}    material[{i}]: none"); continue; }
                    string shader = m.shader != null ? m.shader.name : "?";
                    shaders.Add(shader);
                    sb.AppendLine($"{pad}    material[{i}]: '{m.name}'  shader '{shader}'{(r.enabled ? "" : "  (renderer off)")}");
                    DumpMaterial(m, pad + "      ", sb, textures);
                }
            }
        } catch (Exception ex) {
            sb.AppendLine($"{pad}    (could not read renderer: {ex.GetType().Name}: {ex.Message})");
        }

        for (int i = 0; i < t.childCount; i++)
            DumpObject(t.GetChild(i), depth + 1, sb, meshes, textures, shaders);
    }

    // ---- The game scripts' settings (batch scan only) ----
    // Only properties that stand for a stored field (the interop has a NativeFieldInfoPtr_<name>
    // for each), so no game code runs: reading them is safe.
    private static readonly System.Reflection.Assembly GameAssembly = typeof(PeckSwitch).Assembly;
    private const int MaxItems = 32;

    internal static void DumpScripts(GameObject go, string pad, StringBuilder sb) {
        foreach (Component c in go.GetComponents(Il2CppType.Of<Component>())) {
            if (c == null) continue;
            Type type = null;
            try { type = GameAssembly.GetType(c.GetIl2CppType().FullName, false); } catch (Exception) { }
            if (type == null) continue;   // Unity's own components (renderers, colliders...) are covered above
            object script;
            try { script = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod("TryCast").MakeGenericMethod(type).Invoke(c, null); }
            catch (Exception) { continue; }
            if (script == null) continue;
            sb.AppendLine($"{pad}[{type.Name}]");
            DumpFields(script, type, pad + "  ", sb, 0);
        }
    }

    private static void DumpFields(object obj, Type type, string pad, StringBuilder sb, int depth) {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
        for (Type t = type; t != null && t.Assembly == GameAssembly; t = t.BaseType) {
            foreach (var prop in t.GetProperties(flags)) {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                if (t.GetField("NativeFieldInfoPtr_" + prop.Name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static) == null) continue;
                string value;
                int at = sb.Length;   // a list or nested block writes its own lines: name it above them
                try { value = Describe(prop.GetValue(obj), pad, sb, depth); }
                catch (Exception ex) { value = "? " + (ex.InnerException ?? ex).GetType().Name; }
                if (value != null) sb.AppendLine($"{pad}{prop.Name} = {value}");
                else sb.Insert(at, $"{pad}{prop.Name}:{Environment.NewLine}");
            }
        }
    }

    // A value on one line; a game class or struct (e.g. a list of outcomes) is written out below
    // (returns null when it wrote the lines itself)
    private static string Describe(object v, string pad, StringBuilder sb, int depth) {
        if (v == null) return "null";
        switch (v) {
            case string str: return "\"" + str + "\"";
            case bool or int or uint or short or ushort or long or ulong or byte or sbyte: return v.ToString();
            case float f: return f.ToString("0.###");
            case double d: return d.ToString("0.###");
            case Enum e: return e.ToString();
            case Vector2 v2: return $"({v2.x:0.###}, {v2.y:0.###})";
            case Vector3 v3: return V(v3);
            case Vector4 v4: return V4(v4);
            case Quaternion q: return V(q.eulerAngles) + " (euler)";
            case Color col: return $"color({col.r:0.###}, {col.g:0.###}, {col.b:0.###}, {col.a:0.###})";
            case LayerMask lm: return "layers " + lm.value;
        }
        if (v is UnityEngine.Object uo) {
            if (uo == null) return "null (destroyed)";
            Component comp = uo.TryCast<Component>();
            if (comp != null) return $"{comp.GetIl2CppType().Name} on '{Templates.FullPath(comp.transform)}'";
            GameObject g = uo.TryCast<GameObject>();
            if (g != null) return $"GameObject '{Templates.FullPath(g.transform)}'";
            // A game settings asset (ScriptableObject, e.g. a music set): write out what's in it
            Type assetType = null;
            try { assetType = GameAssembly.GetType(uo.GetIl2CppType().FullName, false); } catch (Exception) { }
            if (assetType != null && depth < 3 && uo.TryCast<ScriptableObject>() != null) {
                object asset = null;
                try { asset = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod("TryCast").MakeGenericMethod(assetType).Invoke(uo, null); } catch (Exception) { }
                if (asset != null) {
                    sb.AppendLine($"{pad}  {{{assetType.Name} '{uo.name}'}}");
                    DumpFields(asset, assetType, pad + "    ", sb, depth + 1);
                    return null;
                }
            }
            return $"{uo.GetIl2CppType().Name} '{uo.name}'";
        }
        // Arrays and lists: the count and the first few items
        Type vt = v.GetType();
        var countProp = vt.GetProperty("Count") ?? vt.GetProperty("Length");
        var indexer = vt.GetProperty("Item");
        if (countProp != null && indexer != null && indexer.GetIndexParameters().Length == 1) {
            int count;
            try { count = Convert.ToInt32(countProp.GetValue(v)); } catch (Exception) { return vt.Name; }
            if (count == 0) return "[] (0)";
            if (depth >= 3) return $"[{count} items]";
            sb.AppendLine($"{pad}  ({count} items)");
            for (int i = 0; i < count && i < MaxItems; i++) {
                object item;
                try { item = indexer.GetValue(v, new object[] { i }); } catch (Exception ex) { sb.AppendLine($"{pad}  [{i}] ? {ex.GetType().Name}"); continue; }
                string line = Describe(item, pad + "    ", sb, depth + 1);
                if (line != null) sb.AppendLine($"{pad}  [{i}] {line}");
            }
            if (count > MaxItems) sb.AppendLine($"{pad}  ... {count - MaxItems} more");
            return null;
        }
        // A game class or struct stored inside the script (a list entry, a settings block)
        if (vt.Assembly == GameAssembly && depth < 3) {
            sb.AppendLine($"{pad}  {{{vt.Name}}}");
            DumpFields(v, vt, pad + "    ", sb, depth + 1);
            return null;
        }
        return vt.Name;
    }

    private static void DumpMaterial(Material m, string pad, StringBuilder sb, SortedDictionary<string, string> textures) {
        Shader shader = m.shader;
        if (shader == null) return;
        int count = 0;
        try { count = shader.GetPropertyCount(); } catch (Exception) { return; }
        for (int i = 0; i < count; i++) {
            try {
                string prop = shader.GetPropertyName(i);
                switch (shader.GetPropertyType(i)) {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        Color c = m.GetColor(prop);
                        sb.AppendLine($"{pad}{prop} = color({c.r:0.###}, {c.g:0.###}, {c.b:0.###}, {c.a:0.###})");
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        sb.AppendLine($"{pad}{prop} = vector{V4(m.GetVector(prop))}");
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        sb.AppendLine($"{pad}{prop} = {m.GetFloat(prop):0.###}");
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        Texture tex = m.GetTexture(prop);
                        if (tex == null) { sb.AppendLine($"{pad}{prop} = none"); break; }
                        string format = "?";
                        Texture2D t2 = tex.TryCast<Texture2D>();
                        if (t2 != null) format = t2.format.ToString();
                        bool readable = false;
                        try { readable = tex.isReadable; } catch (Exception) { }
                        Vector2 scale = m.GetTextureScale(prop), offset = m.GetTextureOffset(prop);
                        sb.AppendLine($"{pad}{prop} = texture '{tex.name}' {tex.width}x{tex.height} {format}  tiling {scale.x:0.###},{scale.y:0.###} offset {offset.x:0.###},{offset.y:0.###}");
                        textures[tex.name] = $"{tex.width}x{tex.height} | {format} | {readable}";
                        break;
                }
            } catch (Exception) { }
        }
        try {
            var keywords = m.shaderKeywords;
            if (keywords != null && keywords.Length > 0) sb.AppendLine($"{pad}keywords: {string.Join(" ", keywords)}");
        } catch (Exception) { }
    }

    private static string V(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
    private static string V4(Vector4 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###}, {v.w:0.###})";
}

} // namespace BigPuzzleSDK
