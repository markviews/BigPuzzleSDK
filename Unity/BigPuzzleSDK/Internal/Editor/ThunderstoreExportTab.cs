using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

// The "Export for Thunderstore" tab of the exporter window (see MapExporter.cs).
//
// It builds one ready-to-upload Thunderstore package (.zip) from the puzzles ticked in the list:
//   manifest.json   name, version, description, website and the packages it depends on
//   icon.png        the chosen image, cropped to a square and resized to 256 x 256
//   README.md       what Thunderstore shows on the package's page
//   <puzzle>.puzzlepack   one per ticked puzzle (the same pack the Dev Export button writes)
// A mod manager unpacks these into BepInEx/plugins/<Author>-<PackageName>/, where the mod finds
// every .puzzlepack on its own.
public partial class PuzzleExporterWindow
{
    // What every exported package depends on (Thunderstore format: Author-PackageName-Version).
    // Keep SdkDependency's version in step with the SDK release the puzzles are made against.
    private const string SdkDependency = "markviews-BigPuzzleSDK-0.1.0";
    private const string BepInExDependency = "BepInEx-BepInExPack_IL2CPP-6.0.788";

    private const int IconSize = 256;               // Thunderstore wants exactly 256 x 256
    private const int MaxDescriptionLength = 250;   // Thunderstore's limit for the short description
    private const string PrefPackage = "BigPuzzleSDK.Thunderstore.Package";
    private const string PrefVersion = "BigPuzzleSDK.Thunderstore.Version";
    private const string PrefDescription = "BigPuzzleSDK.Thunderstore.Description";
    private const string PrefReadmeFile = "BigPuzzleSDK.Thunderstore.ReadmeFile";
    private const string PrefImage = "BigPuzzleSDK.Thunderstore.Image";
    private const string PrefSaveFolder = "BigPuzzleSDK.Thunderstore.SaveFolder";

    // What goes into manifest.json (the field names are Thunderstore's)
    [Serializable]
    internal class ThunderstoreManifest
    {
        public string name;
        public string version_number;
        public string website_url;
        public string description;
        public string[] dependencies;
    }

    private bool tsLoaded;
    private bool tsPackageDefaulted;
    private string tsPackage = "";
    private string tsVersion = "1.0.0";
    private string tsDescription = "";
    private string tsReadmePath = "";               // optional README.md; empty = one is written from the description
    private string tsImagePath = "";
    private Texture2D tsIcon;                     // the 256 x 256 result, also what the preview shows
    private Vector2 tsScroll;
    private string tsLastZip = "";

    // ------------------------------------------------------------------
    // The tab
    // ------------------------------------------------------------------

    private void DrawThunderstoreTab()
    {
        if (!tsLoaded) TsLoad();
        if (devPuzzles == null || devPuzzles.Exists(p => p == null)) RefreshDevPuzzles();
        if (string.IsNullOrEmpty(tsPackage) && !tsPackageDefaulted) TsDefaultPackageName();

        tsScroll = EditorGUILayout.BeginScrollView(tsScroll);
        GUILayout.Label("Thunderstore package", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        tsPackage = EditorGUILayout.TextField(new GUIContent("Package name *",
            "Letters, numbers and underscores only (an underscore shows as a space on Thunderstore). Part of the package's permanent address."), tsPackage);

        EditorGUILayout.BeginHorizontal();
        tsVersion = EditorGUILayout.TextField(new GUIContent("Version number *",
            "Major.Minor.Patch, like 1.0.0. Thunderstore never accepts the same version twice, so raise it for every upload."), tsVersion);
        if (GUILayout.Button(new GUIContent("+1", "Raise the last number (1.0.0 becomes 1.0.1)"), GUILayout.Width(32)))
        {
            tsVersion = TsBumpPatch(tsVersion);
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField(new GUIContent($"Description * ({tsDescription.Length}/{MaxDescriptionLength})",
            "One or two sentences, shown in Thunderstore's package list."));
        tsDescription = EditorGUILayout.TextArea(tsDescription, GUILayout.MinHeight(40));
        if (tsDescription.Length > MaxDescriptionLength) tsDescription = tsDescription.Substring(0, MaxDescriptionLength);

        if (EditorGUI.EndChangeCheck()) TsSave();

        DrawTsReadme();

        EditorGUILayout.Space();
        DrawTsPuzzleList();
        EditorGUILayout.Space();
        DrawTsImage();
        EditorGUILayout.Space();

        List<string> problems = TsProblems();
        foreach (string problem in problems) EditorGUILayout.HelpBox(problem, MessageType.Warning);

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && problems.Count == 0;
        if (GUILayout.Button(new GUIContent("Export Thunderstore package (.zip)",
            problems.Count == 0 ? "" : "Fill in every field marked * first."), GUILayout.Height(30))) TsStartExport();
        GUI.enabled = wasEnabled;

        if (!string.IsNullOrEmpty(status)) EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
        if (!string.IsNullOrEmpty(tsLastZip) && File.Exists(tsLastZip) && GUILayout.Button("Show the zip in its folder"))
            EditorUtility.RevealInFinder(tsLastZip);
        EditorGUILayout.EndScrollView();
    }

    private void DrawTsReadme()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(new GUIContent("README (optional)",
            "The package's page on Thunderstore, a Markdown file. Without one, a README is written for you from the description."));
        bool has = !string.IsNullOrEmpty(tsReadmePath);
        string text = !has ? "None: written from the description" : Path.GetFileName(tsReadmePath);
        EditorGUILayout.LabelField(new GUIContent(text, has ? tsReadmePath : ""), has && !File.Exists(tsReadmePath) ? EditorStyles.boldLabel : EditorStyles.label);
        if (GUILayout.Button("Choose...", GUILayout.Width(70)))
        {
            string start = has && File.Exists(tsReadmePath) ? Path.GetDirectoryName(tsReadmePath) : "";
            string picked = EditorUtility.OpenFilePanel("README.md", start, "md");
            if (!string.IsNullOrEmpty(picked)) { tsReadmePath = picked; TsSave(); }
        }
        if (has && GUILayout.Button("Clear", GUILayout.Width(50))) { tsReadmePath = ""; TsSave(); }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawTsPuzzleList()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Puzzles in the package *", EditorStyles.boldLabel);
        if (GUILayout.Button("Refresh", GUILayout.Width(64))) RefreshDevPuzzles();
        EditorGUILayout.EndHorizontal();

        if (devPuzzles.Count == 0)
        {
            EditorGUILayout.HelpBox("No puzzle found in the open scenes. A puzzle is an object with a PuzzleSDK component.", MessageType.Info);
            return;
        }
        foreach (BigPuzzleSDK.PuzzleSDK p in devPuzzles)
        {
            if (p == null) continue;
            bool on = IsTicked(p);
            bool now = EditorGUILayout.ToggleLeft(p.gameObject.name, on);
            if (now != on) SetTicked(p, now);
        }
    }

    private void DrawTsImage()
    {
        GUILayout.Label("Package image *", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        Rect preview = GUILayoutUtility.GetRect(96, 96, GUILayout.Width(96), GUILayout.Height(96));
        if (tsIcon != null) GUI.DrawTexture(preview, tsIcon, ScaleMode.ScaleToFit);
        else EditorGUI.DrawRect(preview, new Color(0.18f, 0.18f, 0.18f));

        EditorGUILayout.BeginVertical();
        if (GUILayout.Button("Choose image...", GUILayout.Height(24)))
        {
            string start = string.IsNullOrEmpty(tsImagePath) ? "" : Path.GetDirectoryName(tsImagePath);
            string picked = EditorUtility.OpenFilePanel("Package image", start, "png,jpg,jpeg");
            if (!string.IsNullOrEmpty(picked)) TsLoadImage(picked, true);
        }
        string note = tsIcon == null
            ? "Required. Any png or jpg; it is cropped to a square and resized to 256 x 256 for you."
            : $"{Path.GetFileName(tsImagePath)}\nCropped to a square and resized to 256 x 256 (this preview is what will be uploaded).";
        EditorGUILayout.LabelField(note, EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    // ------------------------------------------------------------------
    // Checks
    // ------------------------------------------------------------------

    private List<string> TsProblems()
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(tsPackage) || !Regex.IsMatch(tsPackage, @"^[A-Za-z0-9_]+$"))
            list.Add("The package name needs letters, numbers or underscores only (no spaces).");
        if (!Regex.IsMatch(tsVersion ?? "", @"^\d+\.\d+\.\d+$")) list.Add("The version must look like 1.0.0 (three numbers separated by dots).");
        if (string.IsNullOrWhiteSpace(tsDescription)) list.Add("Enter a short description.");
        if (tsIcon == null) list.Add("Choose a package image.");
        if (!string.IsNullOrEmpty(tsReadmePath) && !File.Exists(tsReadmePath)) list.Add("The README file no longer exists. Choose another or clear it.");
        if (TsSelected().Count == 0) list.Add("Tick at least one puzzle.");
        return list;
    }

    private List<BigPuzzleSDK.PuzzleSDK> TsSelected()
    {
        return DevSelected();
    }

    // ------------------------------------------------------------------
    // Export
    // ------------------------------------------------------------------

    private void TsStartExport()
    {
        if (TsProblems().Count > 0) return;   // the button is greyed out then; this is only a safety net
        TsSave();
        List<BigPuzzleSDK.PuzzleSDK> chosen = TsSelected();
        if (chosen.Count == 0) return;
        if (!TsConfirmChecks(chosen)) return;

        string folder = EditorPrefs.GetString(PrefSaveFolder, "");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) folder = "";
        string zipPath = EditorUtility.SaveFilePanel("Save the Thunderstore package", folder,
            tsPackage + "-" + tsVersion + ".zip", "zip");
        if (string.IsNullOrEmpty(zipPath)) return;
        EditorPrefs.SetString(PrefSaveFolder, Path.GetDirectoryName(zipPath));

        // Show "Saving..." first, then build on the next editor tick so it is drawn
        status = "Saving...";
        statusClearAt = 0;
        Repaint();
        EditorApplication.delayCall += () => { TsBuild(chosen, zipPath); Repaint(); };
    }

    // The setup checks of every ticked puzzle in one dialog. True = nothing found, or export anyway.
    private bool TsConfirmChecks(List<BigPuzzleSDK.PuzzleSDK> chosen)
    {
        var lines = new List<string>();
        int count = 0;
        foreach (BigPuzzleSDK.PuzzleSDK p in chosen)
        {
            foreach (BigPuzzleSDK.PuzzleChecks.Issue issue in BigPuzzleSDK.PuzzleChecks.ForPuzzle(p))
            {
                Debug.LogWarning("[PuzzleExporter] " + issue.message, issue.context);
                count++;
                if (lines.Count < 8) lines.Add("• " + p.gameObject.name + ": " + issue.message);
            }
        }
        if (count == 0) return true;
        if (count > lines.Count) lines.Add($"…and {count - lines.Count} more (see the Console).");
        return EditorUtility.DisplayDialog("Export for Thunderstore",
            $"{count} warning(s):\n\n" + string.Join("\n", lines) +
            "\n\nThey're also in the Console: click one to select the object.",
            "Export anyway", "Cancel");
    }

    private void TsBuild(List<BigPuzzleSDK.PuzzleSDK> chosen, string zipPath)
    {
        // A folder of its own: BuildPack clears "PuzzleExportTemp" every time
        string stage = Path.Combine(Application.temporaryCachePath, "PuzzleThunderstoreTemp");
        string packageDir = Path.Combine(stage, "package");
        BigPuzzleSDK.PuzzleSDK previous = puzzle;
        bool success = false;
        try
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            Directory.CreateDirectory(packageDir);

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var puzzleNames = new List<string>();
            for (int i = 0; i < chosen.Count; i++)
            {
                BigPuzzleSDK.PuzzleSDK p = chosen[i];
                EditorUtility.DisplayProgressBar("Export for Thunderstore", $"Building '{p.gameObject.name}' ({i + 1} of {chosen.Count})", (float)i / chosen.Count);
                puzzle = p;
                string fileName = TsUniqueName(TsSafeFileName(p.gameObject.name), usedNames) + ".puzzlepack";
                if (!BuildPack(Path.Combine(packageDir, fileName), packageDir))
                    throw new Exception($"Could not build '{p.gameObject.name}'. The Console has the reason.");
                puzzleNames.Add(p.gameObject.name);
            }

            File.WriteAllBytes(Path.Combine(packageDir, "icon.png"), ImageConversion.EncodeToPNG(tsIcon));

            var manifest = new ThunderstoreManifest
            {
                name = tsPackage,
                version_number = tsVersion,
                website_url = "",   // Thunderstore needs the field, but it may be empty
                description = tsDescription.Trim(),
                dependencies = new[] { BepInExDependency, SdkDependency }
            };
            var utf8 = new UTF8Encoding(false);   // no byte order mark: Thunderstore reads plain JSON
            File.WriteAllText(Path.Combine(packageDir, "manifest.json"), JsonUtility.ToJson(manifest, true), utf8);
            if (!string.IsNullOrEmpty(tsReadmePath) && File.Exists(tsReadmePath))
                File.Copy(tsReadmePath, Path.Combine(packageDir, "README.md"), true);   // the creator's own README, as written
            else
                File.WriteAllText(Path.Combine(packageDir, "README.md"), TsReadmeText(puzzleNames), utf8);

            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(packageDir, zipPath);
            success = true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            puzzle = previous;
            EditorUtility.ClearProgressBar();
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch (Exception) { }
        }

        if (success)
        {
            tsLastZip = zipPath;
            status = "Saved " + zipPath;
            statusClearAt = 0;
            Debug.Log($"[PuzzleExporter] Thunderstore package saved to {zipPath}");
        }
        else
        {
            status = "Export failed. See the Console for details.";
            statusClearAt = 0;
        }
    }

    private string TsReadmeText(List<string> puzzleNames)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(tsPackage.Replace('_', ' ')).Append("\n\n");
        sb.Append(tsDescription.Trim()).Append("\n\n");
        sb.Append("## Puzzles\n\n");
        foreach (string name in puzzleNames) sb.Append("- ").Append(name).Append('\n');
        sb.Append("\n## Requirements\n\nThis package needs the Big Puzzle SDK mod. A mod manager installs it for you.\n");
        return sb.ToString();
    }

    // "My Puzzle!" -> "My_Puzzle"; the file name only matters for messages
    private static string TsSafeFileName(string name)
    {
        string safe = Regex.Replace(name ?? "", @"[^A-Za-z0-9_\-]+", "_").Trim('_');
        return string.IsNullOrEmpty(safe) ? "puzzle" : safe;
    }

    // Two puzzles with the same name still get two files
    private static string TsUniqueName(string name, HashSet<string> used)
    {
        string unique = name;
        for (int n = 2; !used.Add(unique); n++) unique = name + "_" + n;
        return unique;
    }

    private static string TsBumpPatch(string version)
    {
        Match m = Regex.Match(version ?? "", @"^(\d+)\.(\d+)\.(\d+)$");
        if (!m.Success) return "1.0.0";
        return m.Groups[1].Value + "." + m.Groups[2].Value + "." + (int.Parse(m.Groups[3].Value) + 1);
    }

    // ------------------------------------------------------------------
    // The puzzle list, the saved fields and the image
    // ------------------------------------------------------------------

    // The package name starts as the first ticked puzzle's name, once, so it is not refilled while editing
    private void TsDefaultPackageName()
    {
        List<BigPuzzleSDK.PuzzleSDK> first = DevSelected();
        if (first.Count == 0) return;
        tsPackageDefaulted = true;
        tsPackage = Regex.Replace(first[0].gameObject.name, @"[^A-Za-z0-9_]+", "_").Trim('_');
    }

    private void TsLoad()
    {
        tsLoaded = true;
        tsPackage = EditorPrefs.GetString(PrefPackage, "");
        tsVersion = EditorPrefs.GetString(PrefVersion, "1.0.0");
        if (!Regex.IsMatch(tsVersion, @"^\d+\.\d+\.\d+$")) tsVersion = "1.0.0";   // never start from an empty or broken saved value
        tsDescription = EditorPrefs.GetString(PrefDescription, "");
        tsReadmePath = EditorPrefs.GetString(PrefReadmeFile, "");
        tsPackageDefaulted = !string.IsNullOrEmpty(tsPackage);
        string image = EditorPrefs.GetString(PrefImage, "");
        if (!string.IsNullOrEmpty(image) && File.Exists(image)) TsLoadImage(image, false);
    }

    private void TsSave()
    {
        EditorPrefs.SetString(PrefPackage, tsPackage ?? "");
        EditorPrefs.SetString(PrefVersion, tsVersion ?? "");
        EditorPrefs.SetString(PrefDescription, tsDescription ?? "");
        EditorPrefs.SetString(PrefReadmeFile, tsReadmePath ?? "");
        EditorPrefs.SetString(PrefImage, tsImagePath ?? "");
    }

    // Reads the picture and makes the 256 x 256 icon from it
    private void TsLoadImage(string path, bool tellOnFailure)
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(source, File.ReadAllBytes(path)))
                throw new Exception("The file is not a picture Unity can read.");
            Texture2D icon = TsMakeIcon(source);
            if (tsIcon != null) DestroyImmediate(tsIcon);
            tsIcon = icon;
            tsImagePath = path;
            TsSave();
        }
        catch (Exception ex)
        {
            if (tellOnFailure) EditorUtility.DisplayDialog("Package image", "Could not use that image.\n\n" + ex.Message, "OK");
            else Debug.LogWarning("[PuzzleExporter] Could not load the package image " + path + ": " + ex.Message);
        }
        finally
        {
            DestroyImmediate(source);
        }
    }

    // Crops the middle square out of the picture and resizes it to IconSize x IconSize.
    // Smaller: every output pixel averages the source pixels it covers. Bigger: smooth (bilinear).
    // Colours are averaged with their transparency, so soft edges don't get dark fringes.
    private static Texture2D TsMakeIcon(Texture2D source)
    {
        int width = source.width, height = source.height;
        int side = Mathf.Min(width, height);
        int left = (width - side) / 2, bottom = (height - side) / 2;
        Color32[] src = source.GetPixels32();   // row 0 is the bottom row
        var result = new Color32[IconSize * IconSize];
        float scale = (float)side / IconSize;   // source pixels per output pixel

        for (int y = 0; y < IconSize; y++)
        {
            for (int x = 0; x < IconSize; x++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f, total = 0f;
                if (scale > 1f)
                {
                    float x0 = x * scale, x1 = x0 + scale, y0 = y * scale, y1 = y0 + scale;
                    int ix0 = (int)Mathf.Floor(x0), ix1 = Mathf.Min((int)Mathf.Ceil(x1), side);
                    int iy0 = (int)Mathf.Floor(y0), iy1 = Mathf.Min((int)Mathf.Ceil(y1), side);
                    for (int sy = iy0; sy < iy1; sy++)
                    {
                        float wy = Mathf.Min(sy + 1f, y1) - Mathf.Max(sy, y0);
                        for (int sx = ix0; sx < ix1; sx++)
                        {
                            float w = wy * (Mathf.Min(sx + 1f, x1) - Mathf.Max(sx, x0));
                            Color32 c = src[(bottom + sy) * width + left + sx];
                            float alpha = c.a / 255f;
                            r += c.r * alpha * w; g += c.g * alpha * w; b += c.b * alpha * w;
                            a += alpha * w; total += w;
                        }
                    }
                }
                else
                {
                    // the source position of this output pixel's centre, then the four pixels around it
                    float fx = Mathf.Clamp((x + 0.5f) * scale - 0.5f, 0f, side - 1f);
                    float fy = Mathf.Clamp((y + 0.5f) * scale - 0.5f, 0f, side - 1f);
                    int sx0 = (int)Mathf.Floor(fx), sy0 = (int)Mathf.Floor(fy);
                    int sx1 = Mathf.Min(sx0 + 1, side - 1), sy1 = Mathf.Min(sy0 + 1, side - 1);
                    float tx = fx - sx0, ty = fy - sy0;
                    for (int k = 0; k < 4; k++)
                    {
                        int sx = (k & 1) == 0 ? sx0 : sx1, sy = (k & 2) == 0 ? sy0 : sy1;
                        float w = ((k & 1) == 0 ? 1f - tx : tx) * ((k & 2) == 0 ? 1f - ty : ty);
                        Color32 c = src[(bottom + sy) * width + left + sx];
                        float alpha = c.a / 255f;
                        r += c.r * alpha * w; g += c.g * alpha * w; b += c.b * alpha * w;
                        a += alpha * w; total += w;
                    }
                }

                if (a <= 0f || total <= 0f) { result[y * IconSize + x] = new Color32(0, 0, 0, 0); continue; }
                result[y * IconSize + x] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(r / a), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(g / a), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(b / a), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(a / total * 255f), 0, 255));
            }
        }

        var icon = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
        icon.hideFlags = HideFlags.HideAndDontSave;
        icon.SetPixels32(result);
        icon.Apply();
        return icon;
    }
}