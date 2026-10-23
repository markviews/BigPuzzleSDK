// BigPuzzleSDK: Tools > Big Puzzle SDK > Fix Material Shaders. Switches materials that use shaders the game
// doesn't have (a store pack's own shaders, Built-in ones) to URP/Lit, keeping their texture and colour.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BigPuzzleSDK {

public class FixMaterialShadersWindow : EditorWindow {
    private class Group {
        public string shader;
        public bool convert;
        public readonly List<Material> materials = new List<Material>();
    }

    private readonly List<Group> groups = new List<Group>();
    private Vector2 scroll;
    private string status = "";

    [MenuItem("Tools/Big Puzzle SDK/Fix Material Shaders", false, 3)]
    public static void Open() {
        var window = GetWindow<FixMaterialShadersWindow>(true, "Fix Material Shaders");
        window.minSize = new Vector2(460, 320);
        window.Scan();
    }

    private void OnGUI() {
        EditorGUILayout.HelpBox(
            "Switches materials to URP/Lit, keeping their texture and colour, and makes them matte. Use it on shaders the game doesn't have (the mod log says " +
            "\"The game has no shader named ...\"). It changes the material assets themselves; Edit > Undo works.\n\n" +
            "Which materials: the selected materials, the selected objects, or every material in the selected folders. With nothing selected: every puzzle in the open scene.",
            MessageType.Info);
        if (GUILayout.Button("Scan again")) Scan();

        EditorGUILayout.Space();
        if (groups.Count == 0) EditorGUILayout.LabelField("No materials found.");
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (Group g in groups) {
            EditorGUILayout.BeginHorizontal();
            g.convert = EditorGUILayout.ToggleLeft(g.shader + "   (" + g.materials.Count + (g.materials.Count == 1 ? " material)" : " materials)"), g.convert);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        bool any = false;
        foreach (Group g in groups) if (g.convert) any = true;
        using (new EditorGUI.DisabledScope(!any))
            if (GUILayout.Button("Convert ticked shaders to URP/Lit", GUILayout.Height(28))) Convert();
        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
    }

    // ---- Finding materials ----
    private void Scan() {
        groups.Clear();
        var set = new HashSet<Material>();
        foreach (Object o in Selection.objects) {
            if (o is Material m) set.Add(m);
            else if (o is GameObject go) AddFrom(go, set);
        }
        foreach (string guid in Selection.assetGUIDs) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!AssetDatabase.IsValidFolder(path)) continue;
            foreach (string found in AssetDatabase.FindAssets("t:Material", new[] { path })) {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(found));
                if (mat != null) set.Add(mat);
            }
        }
        if (set.Count == 0)
            foreach (PuzzleSDK puzzle in Object.FindObjectsByType<PuzzleSDK>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                AddFrom(puzzle.gameObject, set);

        var byShader = new Dictionary<string, Group>();
        foreach (Material m in set) {
            string name = m.shader != null ? m.shader.name : "(missing shader)";
            if (!byShader.TryGetValue(name, out Group g)) {
                g = new Group { shader = name, convert = ShouldFix(m.shader) };
                byShader[name] = g;
                groups.Add(g);
            }
            g.materials.Add(m);
        }
        groups.Sort((a, b) => string.Compare(a.shader, b.shader, System.StringComparison.OrdinalIgnoreCase));
        status = "";
        Repaint();
    }

    private static void AddFrom(GameObject go, HashSet<Material> set) {
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            foreach (Material m in r.sharedMaterials)
                if (m != null) set.Add(m);
    }

    // Ticked by default: shaders that are broken here, or that belong to the Built-in pipeline or a store pack.
    // Everything else (URP, Shader Graph, and any shader the game may have) starts unticked.
    private static bool ShouldFix(Shader s) {
        if (s == null || !s.isSupported) return true;
        string n = s.name;
        return n.StartsWith("Synty/") || n == "Standard" || n.StartsWith("Standard (") || n.StartsWith("Legacy Shaders/")
            || n.StartsWith("Mobile/") || n.StartsWith("Nature/") || n == "Autodesk Interactive";
    }

    // ---- Converting ----
    private void Convert() {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) { status = "Universal Render Pipeline/Lit wasn't found. Is the Universal Render Pipeline package installed?"; return; }

        int done = 0, seeThrough = 0, readOnly = 0;
        foreach (Group g in groups) {
            if (!g.convert) continue;
            foreach (Material m in g.materials) {
                if (m == null) continue;
                string path = AssetDatabase.GetAssetPath(m);
                if (path.StartsWith("Packages/")) { readOnly++; continue; }
                if (m.renderQueue >= 3000) { seeThrough++; continue; }   // glass and the like: left alone
                ConvertOne(m, lit);
                done++;
            }
        }
        AssetDatabase.SaveAssets();
        Scan();   // clears the status, so it is set below
        status = "Converted " + done + " material(s) to URP/Lit."
            + (seeThrough > 0 ? " Left " + seeThrough + " see-through material(s) alone (change those by hand)." : "")
            + (readOnly > 0 ? " Skipped " + readOnly + " in read-only packages." : "");
    }

    private static void ConvertOne(Material m, Shader lit) {
        // The old shader's values stay in the material file after the shader changes, but read them first anyway
        Texture baseTex = null, normalTex = null, emissionTex = null;
        Vector2 baseScale = Vector2.one, baseOffset = Vector2.zero;
        int basePriority = int.MaxValue;
        Color baseColor = Color.white, emissionColor = Color.black;
        int colorPriority = int.MaxValue;
        bool hasEmissionColor = false;

        var so = new SerializedObject(m);
        SerializedProperty texEnvs = so.FindProperty("m_SavedProperties.m_TexEnvs");
        for (int i = 0; texEnvs != null && i < texEnvs.arraySize; i++) {
            SerializedProperty el = texEnvs.GetArrayElementAtIndex(i);
            string name = el.FindPropertyRelative("first").stringValue.ToLowerInvariant();
            SerializedProperty env = el.FindPropertyRelative("second");
            var tex = env.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
            if (tex == null) continue;
            if (name.Contains("normal") || name.Contains("bump")) { if (normalTex == null) normalTex = tex; continue; }
            if (name.Contains("emiss")) { if (emissionTex == null) emissionTex = tex; continue; }
            if (name.Contains("mask") || name.Contains("metal") || name.Contains("occlu") || name.Contains("height")
                || name.Contains("detail") || name.Contains("rough") || name.Contains("smooth") || name.Contains("parallax") || name.EndsWith("_ao")) continue;
            int priority = TexturePriority(name);
            if (priority < basePriority) {
                basePriority = priority;
                baseTex = tex;
                baseScale = env.FindPropertyRelative("m_Scale").vector2Value;
                baseOffset = env.FindPropertyRelative("m_Offset").vector2Value;
            }
        }
        SerializedProperty colors = so.FindProperty("m_SavedProperties.m_Colors");
        for (int i = 0; colors != null && i < colors.arraySize; i++) {
            SerializedProperty el = colors.GetArrayElementAtIndex(i);
            string name = el.FindPropertyRelative("first").stringValue.ToLowerInvariant();
            Color c = el.FindPropertyRelative("second").colorValue;
            if (name.Contains("emiss")) { if (!hasEmissionColor) { emissionColor = c; hasEmissionColor = true; } continue; }
            int priority = ColorPriority(name);
            if (priority < colorPriority) { colorPriority = priority; baseColor = c; }
        }
        baseColor.a = 1f;

        Undo.RecordObject(m, "Fix Material Shader");
        m.shader = lit;
        m.SetTexture("_BaseMap", baseTex);
        if (baseTex != null) { m.SetTextureScale("_BaseMap", baseScale); m.SetTextureOffset("_BaseMap", baseOffset); }
        m.SetColor("_BaseColor", baseColor);

        // Matte, like the low-poly look these packs are made for
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", 0f);
        m.SetFloat("_SpecularHighlights", 0f);
        m.SetFloat("_EnvironmentReflections", 0f);
        m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

        if (normalTex != null) { m.SetTexture("_BumpMap", normalTex); m.EnableKeyword("_NORMALMAP"); }

        bool glows = emissionTex != null || (hasEmissionColor && emissionColor.maxColorComponent > 0.001f);
        if (glows) {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", hasEmissionColor && emissionColor.maxColorComponent > 0.001f ? emissionColor : Color.white);
            if (emissionTex != null) m.SetTexture("_EmissionMap", emissionTex);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        EditorUtility.SetDirty(m);
    }

    // Lower is better. Names that clearly mean "the colour texture" come first.
    private static int TexturePriority(string n) {
        if (n.Contains("basetex") || n.Contains("maintex") || n.Contains("basemap") || n.Contains("albedo") || n.Contains("diffuse") || n.Contains("basecolor")) return 0;
        return 1;
    }

    private static int ColorPriority(string n) {
        if (n == "_basecolor" || n == "_color" || n == "_maincolor") return 0;
        if (n.Contains("base") || n.Contains("main") || n.Contains("tint") || n.Contains("color")) return 1;
        return 2;
    }
}

}
