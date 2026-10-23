// BigPuzzleSDK: Tools > Big Puzzle SDK > Import Models. Reads the game models the editor previews
// use from your own copy of Big Walk (into this project's Library folder, never into Assets).
using UnityEditor;
using UnityEngine;

namespace BigPuzzleSDK.GameAssets {

public class ImportModelsWindow : EditorWindow {
    private string path;
    private string status = "";

    [MenuItem("Tools/Big Puzzle SDK/Import Models", false, 1)]
    public static void Open() {
        var window = GetWindow<ImportModelsWindow>(true, "Import Models");
        window.minSize = new Vector2(440, 190);
    }

    private void OnEnable() {
        path = BigWalkGameModels.SavedOrDefaultPath();
    }

    private void OnGUI() {
        GUILayout.Label("Import Big Walk Models", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Shows the game's real models on SDK components in the editor. They're read from your own copy " +
            "of Big Walk into this project's Library folder and are never exported with a puzzle.",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Big Walk's data.unity3d");
        EditorGUILayout.BeginHorizontal();
        path = EditorGUILayout.TextField(path);
        if (GUILayout.Button("Browse...", GUILayout.Width(80))) {
            string picked = BigWalkGameModels.Browse(path);
            if (picked != null) { path = picked; GUI.FocusControl(null); }
        }
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button("Reset to Steam default", EditorStyles.miniButton, GUILayout.Width(150))) {
            path = BigWalkGameModels.DefaultGamePath;
            GUI.FocusControl(null);
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Import", GUILayout.Height(30))) {
            status = "Importing...";
            Repaint();
            EditorApplication.delayCall += () => { DoImport(); Repaint(); };   // so "Importing..." is drawn first
        }

        EditorGUILayout.Space();
        string current = BigWalkGameModels.Ready
            ? $"Imported: {BigWalkGameModels.ModelCount} models, {BigWalkGameModels.SheetCount} image sheets."
            : "No models imported yet.";
        EditorGUILayout.LabelField(string.IsNullOrEmpty(status) ? current : status, EditorStyles.boldLabel);
    }

    private void DoImport() {
        if (BigWalkGameModels.Import(path)) {
            status = $"Imported {BigWalkGameModels.ModelCount} models and {BigWalkGameModels.SheetCount} image sheets.";
            return;
        }
        // Not there (or not readable): ask where it is
        bool found = BigWalkGameModels.Resolve(path) != null;
        string message = found
            ? "Big Walk's models couldn't be read from:\n" + path + "\n\nSee the Console for details. Pick a different data.unity3d?"
            : "Big Walk wasn't found at:\n" + path + "\n\nPick its data.unity3d file (in the 'Big Walk_Data' folder of the game).";
        if (!EditorUtility.DisplayDialog("Import Models", message, "Locate...", "Cancel")) { status = ""; return; }
        string picked = BigWalkGameModels.Browse(path);
        if (picked == null) { status = ""; return; }
        path = picked;
        if (BigWalkGameModels.Import(path))
            status = $"Imported {BigWalkGameModels.ModelCount} models and {BigWalkGameModels.SheetCount} image sheets.";
        else {
            status = "";
            EditorUtility.DisplayDialog("Import Models", "That isn't Big Walk's data.unity3d, or it couldn't be read. See the Console for details.", "OK");
        }
    }
}

}
