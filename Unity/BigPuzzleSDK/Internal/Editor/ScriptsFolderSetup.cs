// BigPuzzleSDK: creates the folder a puzzle's own scripts live in (Assets/PuzzleScripts).
// It holds an assembly definition, so Unity compiles it into its own DLL. On export, the SDK adds a
// plugin class to a copy of that DLL (PluginInjector) that registers it with the SDK. Runs once automatically per project, and
// from Tools > Big Puzzle SDK > Setup.
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BigPuzzleSDK.GameAssets {

[InitializeOnLoad]
public static class ScriptsFolderSetup {
    public const string FolderPath = "Assets/PuzzleScripts";
    private static string DonePref => "BigPuzzleSDK.ScriptsFolderSetup." + Application.dataPath.GetHashCode();

    static ScriptsFolderSetup() {
        EditorApplication.delayCall += () => {
            if (EditorPrefs.GetBool(DonePref, false)) return;   // done once; a folder the creator deleted stays deleted
            Create(false);
        };
    }

    public static bool Exists => Directory.Exists(FolderPath) && Directory.GetFiles(FolderPath, "*.asmdef").Length > 0;

    public static void Create(bool announce) {
        EditorPrefs.SetBool(DonePref, true);
        if (Exists) return;

        string name = AssemblyName();
        Directory.CreateDirectory(FolderPath);

        File.WriteAllText(Path.Combine(FolderPath, name + ".asmdef"),
            "{\n" +
            "    \"name\": \"" + name + "\",\n" +
            "    \"rootNamespace\": \"" + name + "\",\n" +
            "    \"references\": [\n" +
            "        \"BigPuzzleSDK\"\n" +
            "    ],\n" +
            "    \"includePlatforms\": [],\n" +
            "    \"excludePlatforms\": [],\n" +
            "    \"allowUnsafeCode\": false,\n" +
            "    \"overrideReferences\": false,\n" +
            "    \"precompiledReferences\": [],\n" +
            "    \"autoReferenced\": true,\n" +
            "    \"defineConstraints\": [],\n" +
            "    \"versionDefines\": [],\n" +
            "    \"noEngineReferences\": false\n" +
            "}\n", new UTF8Encoding(false));

        AssetDatabase.Refresh();
        if (announce) Debug.Log("[Big Puzzle SDK] Created " + FolderPath + ". Put your puzzle's scripts in it.");
    }

    // The project's name (letters and digits only, so it is a valid C# namespace) plus a random tag, made once
    // when the folder is created. Two creators who both leave Unity's default "My project" would otherwise end
    // up with the same assembly name, and two puzzles with the same plugin name can't both load in game.
    private static string AssemblyName() {
        var sb = new StringBuilder();
        foreach (char c in Application.productName) if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
        if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, "My");
        return sb + "Puzzle_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}

}
