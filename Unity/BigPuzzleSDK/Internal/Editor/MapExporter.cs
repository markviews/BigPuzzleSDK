using UnityEngine;
using UnityEditor;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

// Exports the selected object as a .puzzlepack: a ZIP containing
//   puzzle.bundle  - an AssetBundle with the object as a prefab (all built-in components and assets)
//   puzzle.json    - the custom scripts that were on it, with their serialized fields and events
// Custom scripts are stripped from the bundle because their classes do not exist in the game;
// the mod re-creates them from puzzle.json.
public partial class PuzzleExporterWindow : EditorWindow
{
    private const string TempFolderName = "__PuzzleExportTemp";
    private const string TempFolder = "Assets/" + TempFolderName;
    private const string PrefabPath = TempFolder + "/puzzle.prefab";
    private const string BundleFileName = "puzzle.bundle";

    // Scripts in this assembly are SDK scripts: their real code lives in the mod.
    // Custom scripts in any other assembly are creator code, and their DLL is shipped in the pack.
    // The game's "World" layer: the one players can stand on (see the layer move in the export)
    private const int WorldLayer = 10;

    private static readonly Assembly SdkAssembly = typeof(BigPuzzleSDK.PuzzleButton).Assembly;

    // The puzzle being built right now: its PuzzleSDK component (on the puzzle's root object)
    private BigPuzzleSDK.PuzzleSDK puzzle;
    private GameObject rootObject => puzzle != null ? puzzle.gameObject : null;

    // Root of the hierarchy currently being exported (used to validate object references)
    private Transform exportRoot;

    // Creator assemblies used by this export: assembly name -> DLL path
    private Dictionary<string, string> creatorAssemblies;

    // Project assets (textures) that script fields point at; they are added to the bundle
    private HashSet<string> extraAssets;

    [MenuItem("Tools/Big Puzzle SDK/Export Puzzle", false, 2)]
    public static void ShowWindow()
    {
        GetWindow<PuzzleExporterWindow>("Export Puzzle");
    }

    private int tab;   // 0 = Dev Export (below), 1 = Export for Thunderstore (ThunderstoreExportTab.cs)

    private void OnGUI()
    {
        tab = GUILayout.Toolbar(tab, new[] { "Dev Export", "Export for Thunderstore" });
        EditorGUILayout.Space();
        if (tab == 0) DrawDevExport(); else DrawThunderstoreTab();
    }

    // Every puzzle (PuzzleSDK component) in the open scenes, found automatically. Shared by both tabs.
    private List<BigPuzzleSDK.PuzzleSDK> devPuzzles;
    // Puzzles the creator unticked. Remembered between sessions (per project); a new puzzle starts ticked.
    private HashSet<string> devUnticked;
    private readonly Dictionary<int, string> devKeys = new Dictionary<int, string>();   // instance id -> lasting key

    private static string UntickedPref { get { return "BigPuzzleSDK.Unticked." + Hash128.Compute(Application.dataPath); } }

    // A key for the puzzle that survives restarting Unity (Unity's own id for the object). An object in an
    // unsaved scene has no lasting id yet, so its scene and name stand in for it.
    private string DevKey(BigPuzzleSDK.PuzzleSDK p)
    {
        int id = p.GetInstanceID();
        if (!devKeys.TryGetValue(id, out string key))
        {
            GlobalObjectId gid = GlobalObjectId.GetGlobalObjectIdSlow(p.gameObject);
            key = gid.assetGUID.Empty() ? "name:" + p.gameObject.scene.name + "/" + p.gameObject.name : gid.ToString();
            devKeys[id] = key;
        }
        return key;
    }

    private void LoadUnticked()
    {
        devUnticked = new HashSet<string>();
        foreach (string line in EditorPrefs.GetString(UntickedPref, "").Split('\n'))
            if (line.Length > 0) devUnticked.Add(line);
    }

    private bool IsTicked(BigPuzzleSDK.PuzzleSDK p)
    {
        if (devUnticked == null) LoadUnticked();
        return !devUnticked.Contains(DevKey(p));
    }

    private void SetTicked(BigPuzzleSDK.PuzzleSDK p, bool ticked)
    {
        if (devUnticked == null) LoadUnticked();
        if (ticked) devUnticked.Remove(DevKey(p)); else devUnticked.Add(DevKey(p));
        EditorPrefs.SetString(UntickedPref, string.Join("\n", devUnticked));
    }
    private Vector2 devScroll;

    private void OnHierarchyChange() { devPuzzles = null; Repaint(); }

    private void RefreshDevPuzzles()
    {
        devKeys.Clear();
        devPuzzles = new List<BigPuzzleSDK.PuzzleSDK>();
        foreach (BigPuzzleSDK.PuzzleSDK p in Resources.FindObjectsOfTypeAll<BigPuzzleSDK.PuzzleSDK>())
        {
            if (p == null) continue;
            GameObject go = p.gameObject;
            if (EditorUtility.IsPersistent(go) || !go.scene.IsValid()) continue;   // not a prefab asset
            if ((go.hideFlags & (HideFlags.HideInHierarchy | HideFlags.DontSave)) != 0) continue;
            devPuzzles.Add(p);
        }
        devPuzzles.Sort((a, b) =>
        {
            int c = string.Compare(a.gameObject.name, b.gameObject.name, System.StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : a.GetInstanceID().CompareTo(b.GetInstanceID());
        });
    }

    private List<BigPuzzleSDK.PuzzleSDK> DevSelected()
    {
        if (devPuzzles == null) RefreshDevPuzzles();
        var chosen = new List<BigPuzzleSDK.PuzzleSDK>();
        foreach (BigPuzzleSDK.PuzzleSDK p in devPuzzles)
            if (p != null && IsTicked(p)) chosen.Add(p);
        return chosen;
    }

    private void DrawDevExport()
    {
        if (devPuzzles == null || devPuzzles.Exists(p => p == null)) RefreshDevPuzzles();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Puzzles to export", EditorStyles.boldLabel);
        if (GUILayout.Button("Refresh", GUILayout.Width(64))) RefreshDevPuzzles();
        EditorGUILayout.EndHorizontal();

        if (devPuzzles.Count == 0)
        {
            EditorGUILayout.HelpBox("No puzzle found in the open scenes. A puzzle is an object with a PuzzleSDK component.", MessageType.Info);
        }
        else
        {
            devScroll = EditorGUILayout.BeginScrollView(devScroll, GUILayout.MaxHeight(220));
            foreach (BigPuzzleSDK.PuzzleSDK p in devPuzzles)
            {
                if (p == null) continue;
                bool on = IsTicked(p);
                bool now = EditorGUILayout.ToggleLeft(p.gameObject.name, on);
                if (now != on) SetTicked(p, now);
            }
            EditorGUILayout.EndScrollView();
        }

        List<BigPuzzleSDK.PuzzleSDK> chosen = DevSelected();
        EditorGUILayout.Space();
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && chosen.Count > 0;
        string label = chosen.Count == 1 ? "Export 1 puzzle to .puzzlepack" : $"Export {chosen.Count} puzzles to .puzzlepack";
        if (GUILayout.Button(label, GUILayout.Height(30)))
        {
            // Setup problems: listed in the Console (click one to select the object) and in a dialog
            if (ConfirmChecks(chosen))
            {
                // Show "Saving..." first, then export on the next editor tick so it is drawn
                status = "Saving...";
                statusClearAt = 0;
                Repaint();
                EditorApplication.delayCall += () => { ExportSelected(chosen); Repaint(); };
            }
        }
        GUI.enabled = wasEnabled;
        if (!string.IsNullOrEmpty(status)) EditorGUILayout.LabelField(status, EditorStyles.boldLabel);

        // Export again every time a scene is saved (the scene's ticked puzzles only)
        bool autoOn = AutoExportOnSave;
        bool autoNow = EditorGUILayout.ToggleLeft(new GUIContent("Export when the scene is saved", "Exports the ticked puzzles of a scene each time you save it, into the export folder below."), autoOn);
        if (autoNow != autoOn) AutoExportOnSave = autoNow;

        // Export again every time Unity has recompiled your scripts
        bool compileOn = AutoExportOnCompile;
        bool compileNow = EditorGUILayout.ToggleLeft(new GUIContent("Export when code is compiled", "Exports the ticked puzzles of every open scene each time Unity finishes recompiling your scripts, into the export folder below."), compileOn);
        if (compileNow != compileOn) AutoExportOnCompile = compileNow;

        // Where the last export went; the button above reuses it without asking
        string folder = ExportFolder();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Export folder", string.IsNullOrEmpty(folder) ? "(asked on first export)" : folder, EditorStyles.wordWrappedMiniLabel);
        if (GUILayout.Button("Change export folder..."))
        {
            string picked = EditorUtility.OpenFolderPanel("Puzzle export folder", folder, "");
            if (!string.IsNullOrEmpty(picked)) EditorPrefs.SetString(ExportFolderKey, DevFolderFor(picked));
        }
    }

    // Runs the SDK's setup checks on every puzzle in the list, in one dialog.
    // True = nothing found, or the creator chose to export anyway.
    private bool ConfirmChecks(List<BigPuzzleSDK.PuzzleSDK> chosen)
    {
        var lines = new List<string>();
        int count = 0;
        foreach (BigPuzzleSDK.PuzzleSDK p in chosen)
        {
            foreach (BigPuzzleSDK.PuzzleChecks.Issue issue in BigPuzzleSDK.PuzzleChecks.ForPuzzle(p))
            {
                Debug.LogWarning("[PuzzleExporter] " + issue.message, issue.context);
                count++;
                if (lines.Count < 8) lines.Add("• " + (chosen.Count > 1 ? p.gameObject.name + ": " : "") + issue.message);
            }
        }
        if (count == 0) return true;
        if (count > lines.Count) lines.Add($"…and {count - lines.Count} more (see the Console).");
        return EditorUtility.DisplayDialog("Export Puzzle",
            $"{count} warning(s):\n\n" + string.Join("\n", lines) +
            "\n\nThey're also in the Console: click one to select the object.",
            "Export anyway", "Cancel");
    }

    private string status = "";
    private double statusClearAt;   // editor time at which the status line disappears; 0 = stays

    // Small enough to dock, big enough that every row and button of both tabs stays visible
    private static readonly Vector2 MinWindowSize = new Vector2(460, 540);

    private void OnEnable()
    {
        minSize = MinWindowSize;
        EditorApplication.update += TickStatus;
    }
    private void OnDisable() { EditorApplication.update -= TickStatus; }

    private void TickStatus()
    {
        if (statusClearAt <= 0 || EditorApplication.timeSinceStartup < statusClearAt) return;
        status = "";
        statusClearAt = 0;
        Repaint();
    }

    private const string ExportFolderKey = "BigPuzzleSDK.ExportFolder";

    // The remembered export folder. If it is a BepInEx plugins folder, puzzles go in its BigPuzzleSDK
    // subfolder instead: that is where the mod treats puzzles as ones being tested (creator keys work).
    private static string ExportFolder()
    {
        string folder = EditorPrefs.GetString(ExportFolderKey, "");
        return string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ? folder : DevFolderFor(folder);
    }

    private static string DevFolderFor(string picked)
    {
        string trimmed = picked.TrimEnd('/', '\\');
        DirectoryInfo dir = new DirectoryInfo(trimmed);
        if (dir.Name.Equals("plugins", System.StringComparison.OrdinalIgnoreCase) && dir.Parent != null
            && dir.Parent.Name.Equals("BepInEx", System.StringComparison.OrdinalIgnoreCase))
        {
            string dev = Path.Combine(trimmed, "BigPuzzleSDK");
            Directory.CreateDirectory(dev);
            return dev;
        }
        return picked;
    }

    // ---- Export on save ----
    private static string AutoExportPref { get { return "BigPuzzleSDK.AutoExportOnSave." + Hash128.Compute(Application.dataPath); } }
    internal static bool AutoExportOnSave
    {
        get { return EditorPrefs.GetBool(AutoExportPref, true); }
        set { EditorPrefs.SetBool(AutoExportPref, value); }
    }

    // ---- Export after a compile ----
    private static string AutoCompilePref { get { return "BigPuzzleSDK.AutoExportOnCompile." + Hash128.Compute(Application.dataPath); } }
    internal static bool AutoExportOnCompile
    {
        get { return EditorPrefs.GetBool(AutoCompilePref, true); }
        set { EditorPrefs.SetBool(AutoCompilePref, value); }
    }

    private const string CompiledKey = "BigPuzzleSDK.CompiledSinceExport";

    // A compile started: remember it across the domain reload that follows (entering play mode reloads too, but isn't a compile)
    internal static void OnCompileStarted()
    {
        if (AutoExportOnCompile) SessionState.SetBool(CompiledKey, true);
    }

    // The scripts were reloaded. If that was a compile, export once Unity has settled.
    internal static void OnScriptsReloaded()
    {
        if (!SessionState.GetBool(CompiledKey, false)) return;
        SessionState.SetBool(CompiledKey, false);
        if (!AutoExportOnCompile) return;
        EditorApplication.update += WaitThenExport;
    }

    private static void WaitThenExport()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;   // still importing: wait
        EditorApplication.update -= WaitThenExport;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        AutoExport(default(UnityEngine.SceneManagement.Scene), true);
    }

    private static bool autoExporting;   // an export is running: saves it causes don't start another

    internal static void OnSceneSaved(UnityEngine.SceneManagement.Scene scene)
    {
        if (autoExporting || !AutoExportOnSave || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.delayCall += () => AutoExport(scene, false);
    }

    // One scene (after a save), or every open scene (after a compile)
    private static void AutoExport(UnityEngine.SceneManagement.Scene scene, bool allScenes)
    {
        if (autoExporting || (!allScenes && !scene.IsValid())) return;
        string folder = ExportFolder();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Debug.LogWarning("[PuzzleExporter] Automatic export is on, but there's no export folder yet. Use the Export button once to choose one.");
            return;
        }
        PuzzleExporterWindow window = null;
        foreach (PuzzleExporterWindow open in Resources.FindObjectsOfTypeAll<PuzzleExporterWindow>()) { window = open; break; }
        bool temporary = window == null;
        if (temporary)
        {
            window = ScriptableObject.CreateInstance<PuzzleExporterWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;
        }
        try
        {
            window.RefreshDevPuzzles();
            List<BigPuzzleSDK.PuzzleSDK> chosen = allScenes ? window.DevSelected() : window.DevSelected().FindAll(p => p.gameObject.scene == scene);
            if (chosen.Count == 0) return;
            autoExporting = true;
            window.ExportSelected(chosen);
        }
        finally
        {
            autoExporting = false;
            if (temporary) Object.DestroyImmediate(window);
        }
    }

    private void ExportSelected(List<BigPuzzleSDK.PuzzleSDK> chosen)
    {
        // Export straight into the remembered folder, replacing the old packs without asking.
        // The folder is only asked for the first time (or if it no longer exists).
        string folder = ExportFolder();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            folder = EditorUtility.OpenFolderPanel("Puzzle export folder", "", "");
            if (string.IsNullOrEmpty(folder)) { status = ""; return; }
            folder = DevFolderFor(folder);
            EditorPrefs.SetString(ExportFolderKey, folder);
        }

        BigPuzzleSDK.PuzzleSDK previous = puzzle;
        var usedNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        int done = 0;
        try
        {
            for (int i = 0; i < chosen.Count; i++)
            {
                BigPuzzleSDK.PuzzleSDK p = chosen[i];
                if (p == null) continue;
                if (chosen.Count > 1)
                    EditorUtility.DisplayProgressBar("Export Puzzles", $"Building '{p.gameObject.name}' ({i + 1} of {chosen.Count})", (float)i / chosen.Count);
                puzzle = p;
                string fileName = TsUniqueName(p.gameObject.name, usedNames) + ".puzzlepack";
                string path = Path.Combine(folder, fileName);
                if (BuildPack(path, Path.GetDirectoryName(path)))
                {
                    done++;
                    string written = fileName;
                    for (int d = 0; d < writtenDlls.Count; d++)
                        written += (d == writtenDlls.Count - 1 ? " and " : ", ") + writtenDlls[d];
                    Debug.Log($"[PuzzleExporter] Exported {written} to {folder}");
                }
            }
        }
        finally
        {
            puzzle = previous;
            EditorUtility.ClearProgressBar();
        }

        if (done == chosen.Count)
        {
            status = "Complete";
            statusClearAt = EditorApplication.timeSinceStartup + 2.0;   // shown for two seconds
        }
        else
        {
            status = $"Exported {done} of {chosen.Count}. See the Console for details.";   // stays until the next export
            statusClearAt = 0;
        }
    }

    // Builds the .puzzlepack of the selected puzzle at 'path' (an old file there is replaced).
    // True = it was written. If the puzzle uses the creator's own scripts, their plugin DLL (the scripts
    // DLL with the SDK's plugin class added) is written to 'pluginDir', beside the pack.
    // The plugin DLLs the last BuildPack wrote beside the pack (for the one log line per puzzle)
    private readonly List<string> writtenDlls = new List<string>();

    private bool BuildPack(string path, string pluginDir)
    {
        writtenDlls.Clear();
        string stagingDir = Path.Combine(Application.temporaryCachePath, "PuzzleExportTemp");
        string buildDir = Path.Combine(stagingDir, "build"); // raw AssetBundle build output
        string packDir = Path.Combine(stagingDir, "pack");   // files that go into the .puzzlepack
        if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
        Directory.CreateDirectory(buildDir);
        Directory.CreateDirectory(packDir);

        GameObject copy = null;
        bool success = false;
        try
        {
            exportRoot = rootObject.transform;
            creatorAssemblies = new Dictionary<string, string>();
            extraAssets = new HashSet<string>();

            // 1. Record the custom scripts and their serialized fields (read from the original object)
            var pack = new PuzzlePackData
            {
                formatVersion = 5,   // must match PuzzlePackData.SupportedFormat in the mod (5 = API clean-up)
                puzzleId = PuzzleIds.For(rootObject),
                rootName = rootObject.name,
                scripts = new List<PuzzleScriptData>()
            };
            CollectScripts(exportRoot, "/", pack.scripts);
            File.WriteAllText(Path.Combine(packDir, "puzzle.json"), JsonUtility.ToJson(pack, true));

            // 3. Make a copy without the custom scripts and save it as a temporary prefab.
            //    The editor previews of the game's models are never part of the puzzle, but they
            //    are removed first anyway, and the copy is checked for any game model.
            BigPuzzleSDK.GameAssets.GamePreviews.RemoveAll();
            copy = Instantiate(rootObject);
            copy.name = rootObject.name;
            StripCustomScripts(copy);
            BigPuzzleSDK.GameAssets.GamePreviews.StripFrom(copy);
            foreach (MeshFilter filter in copy.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh used = filter.sharedMesh;
                if (used != null && (used.hideFlags & HideFlags.DontSave) != 0 && used.name.StartsWith("BigWalk "))
                    throw new System.Exception($"'{filter.name}' uses the game's own model '{used.name}'. Game models can't be exported; use your own mesh.");
            }

            // The game only lets players stand on certain layers, and Default isn't one of them. Everything
            // on Default moves to layer 10 ("World") in the exported copy; your scene is left as it is.
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.layer != 0) continue;
                t.gameObject.layer = WorldLayer;
            }

            if (!AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.CreateFolder("Assets", TempFolderName);
            PrefabUtility.SaveAsPrefabAsset(copy, PrefabPath, out bool prefabSaved);
            if (!prefabSaved) throw new System.Exception("Could not save the temporary prefab.");

            // 4. Build the AssetBundle. Meshes, materials, textures, audio, animations etc. that the
            //    prefab uses are pulled in automatically.
            var assetNames = new List<string> { PrefabPath };
            assetNames.AddRange(extraAssets); // textures and sounds that script fields refer to
            PrepareSounds(assetNames);
            var build = new AssetBundleBuild
            {
                assetBundleName = BundleFileName,
                assetNames = assetNames.ToArray()
            };
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                buildDir,
                new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new System.Exception("AssetBundle build failed, see the Console for details.");

            File.Copy(Path.Combine(buildDir, BundleFileName), Path.Combine(packDir, BundleFileName), true);

            // 5. Zip everything into the .puzzlepack
            if (File.Exists(path)) File.Delete(path);
            ZipFile.CreateFromDirectory(packDir, path);

            // 6. The creator's code, as a plugin DLL next to the .puzzlepack
            foreach (KeyValuePair<string, string> entry in creatorAssemblies)
            {
                string target = Path.Combine(pluginDir, entry.Key + ".dll");
                try
                {
                    BigPuzzleSDK.GameAssets.PluginInjector.Inject(entry.Value, target);
                    writtenDlls.Add(entry.Key + ".dll");
                }
                catch (System.Exception copyError)
                {
                    // Usually the game has the old copy open (Windows locks a loaded DLL)
                    Debug.LogWarning($"[PuzzleExporter] Could not write {entry.Key}.dll to {pluginDir}: {copyError.Message}");
                }
            }
            success = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            if (copy != null) DestroyImmediate(copy);
            if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder);
            if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
        }
        return success;
    }

    // ------------------------------------------------------------------
    // Custom scripts
    // ------------------------------------------------------------------

    // A "custom script" is a MonoBehaviour whose source lives in this project's Assets folder.
    // Package components (TextMeshPro, UI, ...) are left in the bundle because the game has them.
    private static bool IsCustomScript(MonoBehaviour mb)
    {
        MonoScript script = MonoScript.FromMonoBehaviour(mb);
        if (script == null) return false;
        return AssetDatabase.GetAssetPath(script).StartsWith("Assets/");
    }

    private static void StripCustomScripts(GameObject copy)
    {
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        }

        // Reverse order so scripts that [RequireComponent] another custom script go first
        MonoBehaviour[] all = copy.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = all.Length - 1; i >= 0; i--)
        {
            if (all[i] != null && IsCustomScript(all[i])) DestroyImmediate(all[i]);
        }
    }

    private void CollectScripts(Transform tr, string path, List<PuzzleScriptData> output)
    {
        foreach (MonoBehaviour mb in tr.GetComponents<MonoBehaviour>())
        {
            if (mb == null)
            {
                Debug.LogWarning($"[PuzzleExporter] Missing script on '{tr.name}', skipped.", tr);
                continue;
            }
            if (!IsCustomScript(mb)) continue;

            System.Type type = mb.GetType();
            var script = new PuzzleScriptData
            {
                path = path,
                typeName = type.FullName,
                assembly = "",
                enabled = mb.enabled,
                fields = new List<PuzzleFieldData>()
            };

            // Creator code (anything outside the SDK assembly): remember its DLL so it ships as a plugin
            if (type.Assembly != SdkAssembly)
            {
                string assemblyName = type.Assembly.GetName().Name;
                string location = type.Assembly.Location;
                if (assemblyName == "Assembly-CSharp")
                {
                    Debug.LogWarning($"[PuzzleExporter] {type.Name} is not in your puzzle scripts folder ({BigPuzzleSDK.GameAssets.ScriptsFolderSetup.FolderPath}), so it will not run in game. Move it there.", tr);
                }
                else if (!string.IsNullOrEmpty(location) && File.Exists(location))
                {
                    script.assembly = assemblyName;
                    creatorAssemblies[assemblyName] = location;
                }
                else
                {
                    Debug.LogWarning($"[PuzzleExporter] Could not find the compiled DLL for {type.Name} ({assemblyName}); it will not run in game.", tr);
                }
            }

            // Reflection gives us the field names (base classes first); SerializedObject tells us
            // whether Unity actually serializes each one, so [SerializeField] private fields,
            // public fields and [NonSerialized] are all handled by Unity's own rules.
            var so = new SerializedObject(mb);
            var chain = new List<System.Type>();
            for (System.Type t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType) chain.Insert(0, t);

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (System.Type t in chain)
            {
                foreach (FieldInfo fi in t.GetFields(flags))
                {
                    SerializedProperty prop = so.FindProperty(fi.Name);
                    if (prop == null) continue; // not serialized by Unity

                    PuzzleFieldData field = SerializeField(prop, tr);
                    if (field != null) script.fields.Add(field);
                    else Debug.LogWarning($"[PuzzleExporter] {type.Name}.{fi.Name} on '{tr.name}' has an unsupported type ({prop.propertyType}), skipped.", tr);
                }
            }

            output.Add(script);
        }

        for (int i = 0; i < tr.childCount; i++)
        {
            string childPath = (path == "/" ? "" : path) + "/" + i.ToString(CultureInfo.InvariantCulture);
            CollectScripts(tr.GetChild(i), childPath, output);
        }
    }

    private PuzzleFieldData SerializeField(SerializedProperty prop, Transform owner)
    {
        // UnityEvent: stored as a list of "call this function on that object" entries
        SerializedProperty calls = prop.FindPropertyRelative("m_PersistentCalls.m_Calls");
        if (calls != null && calls.isArray) return SerializeEvent(prop.name, calls, owner);

        var field = new PuzzleFieldData { name = prop.name, items = new List<string>() };

        // Arrays and Lists (strings also report isArray, so exclude them)
        if (prop.isArray && prop.propertyType != SerializedPropertyType.String)
        {
            field.isArray = true;
            field.kind = "none"; // stays "none" for empty arrays
            for (int i = 0; i < prop.arraySize; i++)
            {
                string value = EncodeValue(prop.GetArrayElementAtIndex(i), out string kind);
                if (value == null) return null;
                field.kind = kind;
                field.items.Add(value);
            }
            return field;
        }

        field.value = EncodeValue(prop, out string k);
        field.kind = k;
        return field.value == null ? null : field;
    }

    // Each entry of a UnityEvent becomes one JSON string in items (kind "event").
    private PuzzleFieldData SerializeEvent(string fieldName, SerializedProperty calls, Transform owner)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        var field = new PuzzleFieldData { name = fieldName, kind = "event", isArray = true, items = new List<string>() };

        for (int i = 0; i < calls.arraySize; i++)
        {
            SerializedProperty call = calls.GetArrayElementAtIndex(i);
            if (call.FindPropertyRelative("m_CallState").intValue == 0) continue; // entry is switched "Off"

            Object target = call.FindPropertyRelative("m_Target").objectReferenceValue;
            string method = call.FindPropertyRelative("m_MethodName").stringValue;
            if (target == null || string.IsNullOrEmpty(method)) continue; // empty slot

            string encodedTarget = EncodeReference(target);
            if (encodedTarget == "")
            {
                Debug.LogWarning($"[PuzzleExporter] {fieldName} on '{owner.name}' calls '{method}' on an object outside the puzzle; that entry was skipped.", owner);
                continue;
            }

            var entry = new PuzzleEventCall { target = encodedTarget, method = method, argKind = "none", arg = "" };
            SerializedProperty args = call.FindPropertyRelative("m_Arguments");

            // m_Mode: 0 = event defined, 1 = void, 2 = object, 3 = int, 4 = float, 5 = string, 6 = bool
            switch (call.FindPropertyRelative("m_Mode").intValue)
            {
                case 2:
                    entry.argKind = "ref";
                    entry.arg = EncodeReference(args.FindPropertyRelative("m_ObjectArgument").objectReferenceValue);
                    break;
                case 3:
                    entry.argKind = "int";
                    entry.arg = args.FindPropertyRelative("m_IntArgument").intValue.ToString(inv);
                    break;
                case 4:
                    entry.argKind = "float";
                    entry.arg = args.FindPropertyRelative("m_FloatArgument").floatValue.ToString("R", inv);
                    break;
                case 5:
                    entry.argKind = "string";
                    entry.arg = args.FindPropertyRelative("m_StringArgument").stringValue ?? "";
                    break;
                case 6:
                    entry.argKind = "bool";
                    entry.arg = args.FindPropertyRelative("m_BoolArgument").boolValue ? "1" : "0";
                    break;
            }

            field.items.Add(JsonUtility.ToJson(entry));
        }

        return field;
    }

    // Encodes a single value as a culture-invariant string. Returns null if unsupported.
    private string EncodeValue(SerializedProperty p, out string kind)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer:
            case SerializedPropertyType.Character:
                kind = "int";
                return p.longValue.ToString(inv);
            case SerializedPropertyType.Enum:
                kind = "enum";
                return p.intValue.ToString(inv);
            case SerializedPropertyType.Boolean:
                kind = "bool";
                return p.boolValue ? "1" : "0";
            case SerializedPropertyType.Float:
                kind = "float";
                return p.doubleValue.ToString("R", inv);
            case SerializedPropertyType.String:
                kind = "string";
                return p.stringValue ?? "";
            case SerializedPropertyType.Vector2:
                kind = "vector2";
                return Join(p.vector2Value.x, p.vector2Value.y);
            case SerializedPropertyType.Vector3:
                kind = "vector3";
                return Join(p.vector3Value.x, p.vector3Value.y, p.vector3Value.z);
            case SerializedPropertyType.Vector4:
                kind = "vector4";
                return Join(p.vector4Value.x, p.vector4Value.y, p.vector4Value.z, p.vector4Value.w);
            case SerializedPropertyType.Quaternion:
                kind = "quaternion";
                return Join(p.quaternionValue.x, p.quaternionValue.y, p.quaternionValue.z, p.quaternionValue.w);
            case SerializedPropertyType.Color:
                kind = "color";
                return Join(p.colorValue.r, p.colorValue.g, p.colorValue.b, p.colorValue.a);
            case SerializedPropertyType.ObjectReference:
                // A texture from the project: ship it in the bundle and store its path
                if (p.objectReferenceValue is Texture2D texture && AssetDatabase.Contains(texture))
                {
                    string texturePath = AssetDatabase.GetAssetPath(texture);
                    if (texturePath.StartsWith("Assets/"))
                    {
                        extraAssets.Add(texturePath);
                        kind = "texture";
                        return texturePath;
                    }
                }
                // A sound from the project: ship it in the bundle and store its path
                if (p.objectReferenceValue is AudioClip clip && AssetDatabase.Contains(clip))
                {
                    string clipPath = AssetDatabase.GetAssetPath(clip);
                    if (clipPath.StartsWith("Assets/"))
                    {
                        extraAssets.Add(clipPath);
                        kind = "audio";
                        return clipPath;
                    }
                }
                kind = "ref";
                return EncodeReference(p.objectReferenceValue);
            default:
                kind = null;
                return null;
        }
    }

    // The mod opens the pack only while it builds the puzzle, then closes it. A sound whose data
    // is read later (streamed, or loaded in the background or on first play) then can't be found
    // ("Cannot create FMOD::Sound instance ... File not found"). So every sound the puzzle uses is
    // set to load completely with the puzzle; the change is saved in the sound's import settings.
    private static void PrepareSounds(List<string> roots)
    {
        var changed = new List<string>();
        foreach (string dependency in AssetDatabase.GetDependencies(roots.ToArray(), true))
        {
            var importer = AssetImporter.GetAtPath(dependency) as AudioImporter;
            if (importer == null) continue;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            bool dirty = false;
            if (settings.loadType == AudioClipLoadType.Streaming) { settings.loadType = AudioClipLoadType.CompressedInMemory; dirty = true; }
            if (!settings.preloadAudioData) { settings.preloadAudioData = true; dirty = true; }
            if (importer.loadInBackground) { importer.loadInBackground = false; dirty = true; }
            if (!dirty) continue;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            changed.Add(dependency);
        }
        if (changed.Count > 0)
            Debug.Log("[PuzzleSDK] These sounds were set to load with the puzzle (not streamed or loaded later), so the game can play them:\n  " + string.Join("\n  ", changed));
    }

    private static string Join(params float[] values)
    {
        var parts = new string[values.Length];
        for (int i = 0; i < values.Length; i++) parts[i] = values[i].ToString("R", CultureInfo.InvariantCulture);
        return string.Join(",", parts);
    }

    // Encodes a reference as "<path>|<type full name>|<index>".
    //   path  = sibling indices from the export root: "/" is the root, "/0/2" is root -> child 0 -> child 2
    //   index = which component of that exact type on the target object (0 for GameObjects)
    // Anything that is not the export root or one of its descendants (other scene objects,
    // prefabs, assets) becomes "" which the loader treats as null.
    private string EncodeReference(Object target)
    {
        if (target == null) return "";

        Transform t;
        string typeName;
        int index = 0;

        if (target is GameObject go)
        {
            t = go.transform;
            typeName = "UnityEngine.GameObject";
        }
        else if (target is Component comp)
        {
            t = comp.transform;
            System.Type compType = comp.GetType();
            typeName = compType.FullName;
            foreach (Component other in comp.GetComponents<Component>())
            {
                if (other == comp) break;
                if (other != null && other.GetType() == compType) index++;
            }
        }
        else
        {
            return ""; // asset reference (material, texture, ScriptableObject, ...)
        }

        if (!t.IsChildOf(exportRoot)) return ""; // IsChildOf is also true for exportRoot itself

        string path = "";
        for (Transform cur = t; cur != exportRoot; cur = cur.parent)
        {
            path = "/" + cur.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) + path;
        }
        if (path.Length == 0) path = "/";

        return path + "|" + typeName + "|" + index.ToString(CultureInfo.InvariantCulture);
    }
}

[System.Serializable]
public class PuzzleFieldData
{
    public string name;        // field name on the script
    public string kind;        // int, enum, bool, float, string, vector2/3/4, quaternion, color, ref, texture, audio, event, none
    public bool isArray;       // true for arrays / Lists / events (values are in items)
    public string value;       // encoded value when isArray is false
    public List<string> items; // encoded elements when isArray is true
}

// One entry of a UnityEvent: "call <method> on <target>", with at most one argument.
[System.Serializable]
public class PuzzleEventCall
{
    public string target;  // encoded reference to the object the function is called on
    public string method;  // function name
    public string argKind; // none, int, float, string, bool, ref
    public string arg;     // encoded argument
}

[System.Serializable]
public class PuzzleScriptData
{
    public string path;     // object the script sits on: "/" is the root, "/0/2" is root -> child 0 -> child 2
    public string typeName; // full type name of the MonoBehaviour
    public string assembly; // creator assembly that contains it ("" for SDK scripts, whose code is in the mod)
    public bool enabled;
    public List<PuzzleFieldData> fields;
}

[System.Serializable]
public class PuzzlePackData
{
    public int formatVersion;
    public string puzzleId;   // permanent id of this puzzle (see PuzzleIds); never changes between exports
    public string rootName;
    public List<PuzzleScriptData> scripts;
}

// Every puzzle gets a permanent id the first time it's exported. The mod uses it (not the file
// name) to tell puzzles apart, to load them in the same order on every PC (which keeps their
// network ids the same) and to compare players' puzzles. So renaming the .puzzlepack, or the
// root object, changes nothing.
// The ids are kept in ProjectSettings/BigWalkPuzzleIds.json, matched to the root object by
// Unity's own id for it (which survives renames). Keep that file with the project (it's
// included when ProjectSettings is in version control); losing it gives the puzzle a new id,
// which only means players need the new export, as with any update.
public static class PuzzleIds
{
    private const string FilePath = "ProjectSettings/BigWalkPuzzleIds.json";

    [System.Serializable]
    private class Entry
    {
        public string objectKey;   // Unity's id for the root object
        public string puzzleId;
        public string name;        // the root's name when the id was made (just for reading the file)
    }

    [System.Serializable]
    private class IdFile { public List<Entry> puzzles = new List<Entry>(); }

    // The puzzle's id, made (and saved) if it has none yet
    public static string For(GameObject root)
    {
        string existing = Find(root);
        if (existing != null) return existing;
        IdFile file = Load();
        var entry = new Entry
        {
            objectKey = KeyOf(root, true),
            puzzleId = System.Guid.NewGuid().ToString("N"),
            name = root.name
        };
        file.puzzles.Add(entry);
        File.WriteAllText(FilePath, JsonUtility.ToJson(file, true));
        Debug.Log($"[PuzzleExporter] '{root.name}' got its permanent puzzle id {entry.puzzleId}");
        return entry.puzzleId;
    }

    // The puzzle's id, or null if it hasn't been exported yet
    public static string Find(GameObject root)
    {
        if (root == null) return null;
        string key = KeyOf(root, false);
        foreach (Entry entry in Load().puzzles)
            if (entry.objectKey == key) return entry.puzzleId;
        return null;
    }

    private static string KeyOf(GameObject root, bool warn)
    {
        GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(root);
        // An object in a scene that was never saved has no lasting id yet
        if (warn && id.assetGUID.Empty())
            Debug.LogWarning($"[PuzzleExporter] Save the scene before exporting '{root.name}', or its puzzle id won't be kept.");
        return id.ToString();
    }

    // A file that exists but can't be read stops the export, so its ids are never overwritten
    private static IdFile Load()
    {
        if (!File.Exists(FilePath)) return new IdFile();
        IdFile file;
        try { file = JsonUtility.FromJson<IdFile>(File.ReadAllText(FilePath)); }
        catch (System.Exception ex) { throw new System.Exception($"{FilePath} could not be read ({ex.Message}); fix or restore it before exporting."); }
        if (file == null) throw new System.Exception($"{FilePath} could not be read; fix or restore it before exporting.");
        if (file.puzzles == null) file.puzzles = new List<Entry>();
        return file;
    }
}

// Listens for scene saves and compiles for the "Export when the scene is saved" / "Export when code is compiled" options (works with the window closed)
[InitializeOnLoad]
internal static class PuzzleAutoExportHook
{
    static PuzzleAutoExportHook()
    {
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += scene => PuzzleExporterWindow.OnSceneSaved(scene);
        UnityEditor.Compilation.CompilationPipeline.compilationStarted += _ => PuzzleExporterWindow.OnCompileStarted();
    }

    [UnityEditor.Callbacks.DidReloadScripts]
    private static void AfterReload()
    {
        PuzzleExporterWindow.OnScriptsReloaded();
    }
}
