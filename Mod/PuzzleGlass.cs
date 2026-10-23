using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Must stay identical, in the same order, to the enums in the Unity SDK
public enum PuzzleGlassStyle { Window, Rippled, Clear }
public enum PuzzleGlassSound { Muffles, Thick, Soundproof }

// Runtime (mod) version of the SDK's PuzzleGlass: the creator's own mesh (any shape or size)
// becomes the game's puzzle glass. Nothing of the game's is in the puzzlepack: the glass
// material is looked up in the player's own game when the puzzle loads.
//
// How the game does it: glass panes use the 'hhGlass' materials (shader househouse/Glass; the
// three differ only in how much they distort what's behind them) and solid colliders on the
// "Glass" layer with an "audio glass" physics material, which muffles voices through it.
public class PuzzleGlass : MonoBehaviour {
    public PuzzleGlass(IntPtr ptr) : base(ptr) { }

    private const int GlassLayer = 17;

    // Set in the Inspector; the names must match the Unity-side fields
    internal PuzzleGlassStyle style;
    internal PuzzleGlassSound sound;
    internal bool wire;                      // the black wire mesh of the game's soundproof windows
    private const float WireTilesPerMeter = 6f;   // how many times the wire picture repeats per metre

    private const string WireMaterialName = GameRefs.Glass.WireMaterialName;

    private static readonly Dictionary<string, Material> looks = new Dictionary<string, Material>();
    private bool set;

    private static string LookName(PuzzleGlassStyle s) {
        switch (s) {
            case PuzzleGlassStyle.Rippled: return "hhGlass_hifreq";   // the gourd boxes' glass
            case PuzzleGlassStyle.Clear: return "hhGlass_lowfreq";
            default: return "hhGlass";                                // most windows
        }
    }

    private static string SoundName(PuzzleGlassSound s) {
        switch (s) {
            case PuzzleGlassSound.Thick: return "audio glass thick";
            case PuzzleGlassSound.Soundproof: return "audio voiceblocking glass";
            default: return "audio glass";
        }
    }

    private void Awake() {
        if (set) return;
        set = true;
        var log = Plugin.log;
        try {
            // Looks
            string lookName = LookName(style);
            Material look = FindLook(lookName);
            if (look == null) log.LogWarning($"[PuzzleGlass] the game's '{lookName}' material wasn't found; '{name}' keeps its own material.");
            int renderers = 0;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) {
                if (look != null) {
                    int slots = Math.Max(1, r.sharedMaterials.Length);
                    var mats = new Material[slots];
                    for (int i = 0; i < slots; i++) mats[i] = look;
                    r.sharedMaterials = mats;
                }
                r.gameObject.layer = GlassLayer;
                if (wire) AddWire(r);
                renderers++;
            }
            if (renderers == 0) log.LogWarning($"[PuzzleGlass] '{name}' has no renderer, so there is no glass to see.");

            // Solid, and muffles (or blocks) voices like the game's glass
            string soundName = SoundName(sound);
            PhysicsMaterial physics = PuzzleSoundProof.FindMaterial(soundName);
            if (physics == null) log.LogWarning($"[PuzzleGlass] the game's '{soundName}' material wasn't found; '{name}' won't affect voices.");
            int colliders = 0;
            foreach (Collider c in GetComponentsInChildren<Collider>(true)) {
                if (c.isTrigger) {
                    log.LogWarning($"[PuzzleGlass] '{c.name}' is a trigger; glass is solid, so it was made solid.");
                    c.isTrigger = false;
                }
                c.gameObject.layer = GlassLayer;
                if (physics != null) c.sharedMaterial = physics;
                colliders++;
            }
            if (colliders == 0) log.LogWarning($"[PuzzleGlass] '{name}' has no collider, so players walk straight through it. Add a Box Collider.");
        } catch (Exception ex) {
            log.LogWarning($"[PuzzleGlass] '{name}' could not be set up: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The game's windows have the wire as a separate surface just behind the glass, so the glass
    // ripples it. Same here: a copy of the mesh, flattened to half its thickness along its
    // thinnest side (it sits inside the pane), drawn with the game's wire material.
    private void AddWire(Renderer glass) {
        MeshFilter filter = glass.GetComponent<MeshFilter>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null) return;
        Material source = FindLook(WireMaterialName);
        if (source == null) {
            Plugin.log.LogWarning($"[PuzzleGlass] the game's '{WireMaterialName}' material wasn't found; '{name}' has no wire.");
            return;
        }

        Bounds b = mesh.bounds;
        Vector3 world = Vector3.Scale(b.size, glass.transform.lossyScale);
        world = new Vector3(Mathf.Abs(world.x), Mathf.Abs(world.y), Mathf.Abs(world.z));
        int thin = world.x <= world.y && world.x <= world.z ? 0 : (world.y <= world.z ? 1 : 2);
        Vector3 squash = Vector3.one;
        squash[thin] = 0.5f;

        var go = new GameObject("PuzzleGlass wire");
        go.layer = glass.gameObject.layer;
        go.transform.SetParent(glass.transform, false);
        go.transform.localScale = squash;
        go.transform.localPosition = b.center - Vector3.Scale(b.center, squash);   // squashed around the mesh's centre
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();

        // Tiles across the pane's two big sides, so the cells stay square at any size
        float u = thin == 0 ? world.z : world.x;
        float v = thin == 1 ? world.z : world.y;
        var mat = new Material(source) { name = WireMaterialName + " (PuzzleGlass)" };
        float tiles = WireTilesPerMeter;
        mat.SetTextureScale("_AlbedoMap", new Vector2(u * tiles, v * tiles));
        mat.SetTextureScale("_MainTex", new Vector2(u * tiles, v * tiles));

        int slots = Mathf.Max(1, mesh.subMeshCount);
        var mats = new Material[slots];
        for (int i = 0; i < slots; i++) mats[i] = mat;
        renderer.sharedMaterials = mats;
    }

    private static Material FindLook(string wanted) {
        if (looks.TryGetValue(wanted, out Material known) && known != null) return known;
        foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>())) {
            Material m = o.TryCast<Material>();
            if (m != null && m.name == wanted) { looks[wanted] = m; return m; }
        }
        return null;
    }
}

} // namespace BigPuzzleSDK
