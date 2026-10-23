// BigPuzzleSDK: finds Big Walk on this computer, reads the models the SDK components copy, and
// keeps them in memory for the editor previews.
//
// Nothing here is an asset: the models live only in memory and in a cache file under Library/
// (which Unity never exports or commits), so none of the game's files can end up in a
// .puzzlepack or be shared.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BigPuzzleSDK.GameAssets {

[InitializeOnLoad]
public static class BigWalkGameModels {
    private const string DefaultPath = @"C:\Program Files (x86)\Steam\steamapps\common\Big Walk\Big Walk_Data\data.unity3d";
    private const string PathPref = "BigPuzzleSDK.GameDataPath";
    private const string AskedKey = "BigPuzzleSDK.AskedForGame";   // per editor session
    private const string CacheMagic = "BPSDK-GM2";

    private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
    private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    private static bool loaded;

    public static bool Ready => loaded && meshes.Count > 0;
    public static event Action Changed;

    private static string CacheFile => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "BigPuzzleSDK", "GameModels.cache"));

    static BigWalkGameModels() {
        EditorApplication.delayCall += LoadOnStartup;
    }

    // Some names are used by more than one game model (e.g. two sizes of counter dial): those are
    // told apart by their vertex count
    internal static Mesh Get(string name, int vertices = -1) {
        if (vertices > 0 && meshes.TryGetValue(name + "#" + vertices, out Mesh exact)) return exact;
        return meshes.TryGetValue(name, out Mesh m) ? m : null;
    }
    internal static Texture2D GetTexture(string name) => textures.TryGetValue(name, out Texture2D t) ? t : null;

    // ---- Loading ----
    // On editor start: the cache if it matches the installed game, else read the game again.
    // With no game and no cache, the Import Models window opens (once per editor session).
    private static void LoadOnStartup() {
        string game = Resolve(SavedOrDefaultPath()) ?? AutoDetect();
        if (game != null) {
            if (TryLoadCache(game)) return;
            Rip(game);
            return;
        }
        if (TryLoadCache(null)) return;   // the game moved or isn't installed: an older cache still works
        Clear();
        if (!SessionState.GetBool(AskedKey, false)) {
            SessionState.SetBool(AskedKey, true);
            ImportModelsWindow.Open();
        }
    }

    // The path to show in the Import window: the last one used, else Steam's default
    internal static string SavedOrDefaultPath() {
        string saved = EditorPrefs.GetString(PathPref, "");
        return string.IsNullOrEmpty(saved) ? (AutoDetect() ?? DefaultPath) : saved;
    }
    internal static string DefaultGamePath => AutoDetect() ?? DefaultPath;

    // Looks for the game without asking: Steam's default folder, the other Steam libraries, then the
    // install path the game itself wrote to its Player.log (this works whatever launched it, including
    // r2modman / the Thunderstore app, which run the game from the Steam folder). Null if not found.
    internal static string AutoDetect() {
        string found = Resolve(DefaultPath);
        if (found != null) return found;
        try {
            string steam = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(DefaultPath), "..", "..", "..", ".."));
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf)) {
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"")) {
                    string lib = m.Groups[1].Value.Replace("\\\\", "\\");
                    found = Resolve(Path.Combine(lib, "steamapps", "common", "Big Walk"));
                    if (found != null) return found;
                }
            }
        } catch { }
        try {
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "House House", "Big Walk", "Player.log");
            if (File.Exists(log)) {
                using (var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs)) {
                    string line;
                    while ((line = sr.ReadLine()) != null) {
                        const string key = "Loading player data from ";
                        int i = line.IndexOf(key, StringComparison.Ordinal);
                        if (i < 0) continue;
                        found = Resolve(line.Substring(i + key.Length).Trim().Replace('/', '\\'));
                        if (found != null) return found;
                        break;
                    }
                }
            }
        } catch { }
        return null;
    }

    // data.unity3d from a path to the file itself, the 'Big Walk_Data' folder or the game folder;
    // null if it isn't there
    internal static string Resolve(string path) {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Trim().Trim('"');
        if (File.Exists(path)) return Path.GetFileName(path).Equals("data.unity3d", StringComparison.OrdinalIgnoreCase) ? path : null;
        if (Directory.Exists(path)) {
            string inData = Path.Combine(path, "data.unity3d");
            if (File.Exists(inData)) return inData;
            string inGame = Path.Combine(path, "Big Walk_Data", "data.unity3d");
            if (File.Exists(inGame)) return inGame;
        }
        return null;
    }

    // Reads the game's models from this path (always, even if the cache is current).
    // False if the path isn't Big Walk's data.unity3d or nothing could be read.
    internal static bool Import(string path) {
        string game = Resolve(path);
        if (game == null) return false;
        EditorPrefs.SetString(PathPref, game);
        return Rip(game) && Ready;
    }

    internal static int ModelCount => meshes.Count;
    internal static int SheetCount => textures.Count;

    // A file dialog for data.unity3d; null if cancelled
    internal static string Browse(string current) {
        string start = "";
        try { start = Path.GetDirectoryName(Resolve(current) ?? current) ?? ""; } catch (Exception) { }
        if (!Directory.Exists(start)) start = Directory.Exists(@"C:\Program Files (x86)\Steam\steamapps\common") ? @"C:\Program Files (x86)\Steam\steamapps\common" : "";
        string picked = EditorUtility.OpenFilePanel("Find Big Walk's data.unity3d (in the 'Big Walk_Data' folder)", start, "unity3d");
        return string.IsNullOrEmpty(picked) ? null : picked;
    }

    private static bool Rip(string game) {
        var started = DateTime.Now;
        var ripped = new List<RippedMesh>();
        var rippedTextures = new List<RippedTexture>();
        var problems = new List<string>();
        try {
            EditorUtility.DisplayProgressBar("Big Puzzle SDK", "Opening Big Walk's data...", 0f);
            using (var assets = new GameAssets(game, (what, p) => EditorUtility.DisplayProgressBar("Big Puzzle SDK", what, p * 0.5f))) {
                int i = 0;
                int steps = PreviewTable.Meshes.Length + PreviewTable.Slices.Length + PreviewTable.Sheets.Length;
                foreach (Slice slice in PreviewTable.Slices) {
                    i++;
                    EditorUtility.DisplayProgressBar("Big Puzzle SDK", "Reading model " + slice.name, 0.5f + 0.5f * i / steps);
                    try {
                        RippedMesh combined = assets.ReadMesh(slice.source, slice.sourceVertices);
                        if (combined == null || combined.vertexCount != slice.sourceVertices) { problems.Add(slice.name + " (its combined mesh changed)"); continue; }
                        ripped.Add(MeshSlicer.Cut(combined, slice));
                    } catch (GameAssetException ex) {
                        problems.Add(slice.name + ": " + ex.Message);
                    }
                }
                foreach (string sheet in PreviewTable.Sheets) {
                    i++;
                    EditorUtility.DisplayProgressBar("Big Puzzle SDK", "Reading image sheet " + sheet, 0.5f + 0.5f * i / steps);
                    try {
                        RippedTexture t = assets.ReadTexture(sheet);
                        if (t == null) { problems.Add(sheet + " (image sheet not in the game any more)"); continue; }
                        rippedTextures.Add(t);
                    } catch (GameAssetException ex) {
                        problems.Add(sheet + ": " + ex.Message);
                    }
                }
                foreach (var (name, vertices) in PreviewTable.Meshes) {
                    i++;
                    EditorUtility.DisplayProgressBar("Big Puzzle SDK", "Reading model " + name, 0.5f + 0.5f * i / steps);
                    try {
                        RippedMesh m = assets.ReadMesh(name, vertices);
                        if (m == null) { problems.Add(name + " (not in the game any more)"); continue; }
                        if (m.vertexCount != vertices) problems.Add($"{name} (changed: {m.vertexCount} vertices, expected {vertices})");
                        ripped.Add(m);
                    } catch (GameAssetException ex) {
                        problems.Add(name + ": " + ex.Message);
                    }
                }
            }
        } catch (Exception ex) {
            EditorUtility.ClearProgressBar();
            Debug.LogWarning($"[PuzzleSDK] Could not read Big Walk's models from {game}: {ex.Message}");
            if (!TryLoadCache(null)) Clear();
            return false;
        }
        EditorUtility.ClearProgressBar();

        var info = new FileInfo(game);
        try { WriteCache(info, ripped, rippedTextures); }
        catch (Exception ex) { Debug.LogWarning("[PuzzleSDK] Could not write the game model cache: " + ex.Message); }
        Use(ripped, rippedTextures);
        int wanted = PreviewTable.Meshes.Length + PreviewTable.Slices.Length;
        Debug.Log($"[PuzzleSDK] Read {ripped.Count} of {wanted} game models and {rippedTextures.Count} of {PreviewTable.Sheets.Length} image sheets in {(DateTime.Now - started).TotalSeconds:0.0}s.");
        if (problems.Count > 0) Debug.LogWarning("[PuzzleSDK] Some game models had problems:\n  " + string.Join("\n  ", problems));
        return ripped.Count > 0;
    }

    // ---- Cache (Library/BigPuzzleSDK/GameModels.cache) ----
    // 'game' = the installed data.unity3d to compare against, or null to accept any cache
    private static bool TryLoadCache(string game) {
        string file = CacheFile;
        if (!File.Exists(file)) return false;
        try {
            using (var r = new BinaryReader(File.OpenRead(file))) {
                if (r.ReadString() != CacheMagic) return false;
                long size = r.ReadInt64();
                long time = r.ReadInt64();
                if (game != null) {
                    var info = new FileInfo(game);
                    if (info.Length != size || info.LastWriteTimeUtc.Ticks != time) return false;   // the game was updated
                }
                int count = r.ReadInt32();
                var list = new List<RippedMesh>(count);
                for (int i = 0; i < count; i++) list.Add(ReadMesh(r));
                int texCount = r.ReadInt32();
                var texList = new List<RippedTexture>(texCount);
                for (int i = 0; i < texCount; i++) {
                    var t = new RippedTexture { name = r.ReadString(), width = r.ReadInt32(), height = r.ReadInt32(), format = r.ReadInt32(), mipCount = r.ReadInt32() };
                    t.data = r.ReadBytes(r.ReadInt32());
                    texList.Add(t);
                }
                Use(list, texList);
                return true;
            }
        } catch (Exception) {
            return false;
        }
    }

    private static void WriteCache(FileInfo game, List<RippedMesh> list, List<RippedTexture> texList) {
        Directory.CreateDirectory(Path.GetDirectoryName(CacheFile));
        using (var w = new BinaryWriter(File.Create(CacheFile))) {
            w.Write(CacheMagic);
            w.Write(game.Length);
            w.Write(game.LastWriteTimeUtc.Ticks);
            w.Write(list.Count);
            foreach (RippedMesh m in list) WriteMesh(w, m);
            w.Write(texList.Count);
            foreach (RippedTexture t in texList) {
                w.Write(t.name); w.Write(t.width); w.Write(t.height); w.Write(t.format); w.Write(t.mipCount);
                w.Write(t.data.Length); w.Write(t.data);
            }
        }
    }

    private static void WriteMesh(BinaryWriter w, RippedMesh m) {
        w.Write(m.name);
        w.Write(m.vertexCount);
        WriteFloats(w, m.positions); WriteFloats(w, m.normals); WriteFloats(w, m.colors); WriteFloats(w, m.uv0);
        w.Write(m.subMeshes.Count);
        foreach (RippedSubMesh s in m.subMeshes) {
            w.Write(s.topology);
            w.Write(s.indices.Length);
            foreach (int i in s.indices) w.Write(i);
        }
    }

    private static RippedMesh ReadMesh(BinaryReader r) {
        var m = new RippedMesh { name = r.ReadString(), vertexCount = r.ReadInt32() };
        m.positions = ReadFloats(r); m.normals = ReadFloats(r); m.colors = ReadFloats(r); m.uv0 = ReadFloats(r);
        int subs = r.ReadInt32();
        for (int s = 0; s < subs; s++) {
            var sub = new RippedSubMesh { topology = r.ReadInt32() };
            sub.indices = new int[r.ReadInt32()];
            for (int i = 0; i < sub.indices.Length; i++) sub.indices[i] = r.ReadInt32();
            m.subMeshes.Add(sub);
        }
        return m;
    }

    private static void WriteFloats(BinaryWriter w, float[] a) {
        w.Write(a == null ? -1 : a.Length);
        if (a != null) foreach (float f in a) w.Write(f);
    }

    private static float[] ReadFloats(BinaryReader r) {
        int n = r.ReadInt32();
        if (n < 0) return null;
        var a = new float[n];
        for (int i = 0; i < n; i++) a[i] = r.ReadSingle();
        return a;
    }

    // ---- Unity meshes (in memory only) ----
    private static void Use(List<RippedMesh> list, List<RippedTexture> texList) {
        Clear(notify: false);
        foreach (RippedMesh m in list) {
            try {
                Mesh built = ToUnity(m);
                meshes[m.name + "#" + m.vertexCount] = built;
                if (!meshes.ContainsKey(m.name)) meshes[m.name] = built;
            }
            catch (Exception ex) { Debug.LogWarning($"[PuzzleSDK] Game model '{m.name}' couldn't be built: {ex.Message}"); }
        }
        foreach (RippedTexture t in texList) {
            try { textures[t.name] = ToUnity(t); }
            catch (Exception ex) { Debug.LogWarning($"[PuzzleSDK] Game image sheet '{t.name}' (format {t.format}) couldn't be loaded: {ex.Message}"); }
        }
        loaded = true;
        Changed?.Invoke();
    }

    private static void Clear(bool notify = true) {
        foreach (Mesh m in meshes.Values) if (m != null) UnityEngine.Object.DestroyImmediate(m);
        meshes.Clear();
        foreach (Texture2D t in textures.Values) if (t != null) UnityEngine.Object.DestroyImmediate(t);
        textures.Clear();
        loaded = false;
        if (notify) Changed?.Invoke();
    }

    // The game's own GPU data (e.g. DXT5) is loaded as is; Unity decodes it
    private static Texture2D ToUnity(RippedTexture r) {
        var tex = new Texture2D(r.width, r.height, (TextureFormat)r.format, Math.Max(1, r.mipCount), false) {
            name = "BigWalk " + r.name, hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
        };
        try {
            tex.LoadRawTextureData(r.data);
            tex.Apply(false, true);   // upload, then free the CPU copy
        } catch (Exception) {
            UnityEngine.Object.DestroyImmediate(tex);
            throw;
        }
        return tex;
    }

    private static Mesh ToUnity(RippedMesh r) {
        var mesh = new Mesh { name = "BigWalk " + r.name, hideFlags = HideFlags.HideAndDontSave };
        int n = r.vertexCount;
        if (n > 65535) mesh.indexFormat = IndexFormat.UInt32;
        var v = new Vector3[n];
        for (int i = 0; i < n; i++) v[i] = new Vector3(r.positions[i * 3], r.positions[i * 3 + 1], r.positions[i * 3 + 2]);
        mesh.vertices = v;
        if (r.colors != null) {
            var c = new Color[n];
            for (int i = 0; i < n; i++) c[i] = new Color(r.colors[i * 4], r.colors[i * 4 + 1], r.colors[i * 4 + 2], r.colors[i * 4 + 3]);
            mesh.colors = c;
        }
        if (r.uv0 != null) {
            var uv = new Vector2[n];
            for (int i = 0; i < n; i++) uv[i] = new Vector2(r.uv0[i * 2], r.uv0[i * 2 + 1]);
            mesh.uv = uv;
        }
        mesh.subMeshCount = r.subMeshes.Count;
        for (int s = 0; s < r.subMeshes.Count; s++) {
            RippedSubMesh sub = r.subMeshes[s];
            MeshTopology topology = sub.topology == 0 ? MeshTopology.Triangles : (MeshTopology)sub.topology;
            mesh.SetIndices(sub.indices, topology, s, false);
        }
        if (r.normals != null) {
            var nr = new Vector3[n];
            for (int i = 0; i < n; i++) nr[i] = new Vector3(r.normals[i * 3], r.normals[i * 3 + 1], r.normals[i * 3 + 2]);
            mesh.normals = nr;
        } else {
            mesh.RecalculateNormals();
        }
        mesh.RecalculateBounds();
        return mesh;
    }
}

} // namespace BigPuzzleSDK.GameAssets
