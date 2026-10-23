using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BigPuzzleSDK {

// The image sets a display can show. Each name is the game's PegTileSet name without the
// "PegTileSet" prefix. This list must be identical, in the same order, to the one in the
// Unity SDK, because the choice travels in the pack as a position in the list.
// "Custom" is not a game set: it shows the creator's own sheet of images (customSheet).
public enum PuzzleImageSet { A, B, C, D, Scout, Circle, Pontiff, Camo, LargeA, Custom }

// How many flaps a display has (the game's 1-, 2-, 3- and 5-slot frames). Same names, same order as the Unity SDK.
internal enum PuzzleDisplaySize { One, Two, Three, Five }

// Runtime (mod) version of the SDK's PuzzleDisplay: copies a vanilla split-flap display into
// place and lets code or Inspector events choose which image it shows. The game's own
// SplitFlapGlyph does the flipping animation and sound.
// A display has 1, 2, 3 or 5 flaps in one frame, numbered from the left (flap 1). Each flap is
// its own picture: it takes its own tile from the puzzle's pile, and a Peg Board follows one flap per slot.
public class PuzzleDisplay : MonoBehaviour {
    public PuzzleDisplay(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.Display.ExactPath;
    // The same object in 3- and 2-player lobbies (the 4-player challenges aren't loaded there)
    private const string ExactPath3 = GameRefs.Display.ExactPath3;
    private const string ExactPath2 = GameRefs.Display.ExactPath2;

    // Name of the vanilla object that gets copied (the parent of its SplitFlapGlyph)
    private const string TemplateName = GameRefs.Display.TemplateName;
    private const string CopyName = "VanillaDisplay";

    // How far apart single-slot frames are placed when a bigger frame isn't loaded (the frame's width)
    private const float SingleFrameWidth = 0.6f;

    internal static readonly List<PuzzleDisplay> all = new List<PuzzleDisplay>();

    // Set in the Inspector (filled in by the loader); the names must match the Unity-side fields.
    // Changes only through the SDK (the puzzle deals out tiles when it starts); scripts read it with Image.
    // Optional: shows exactly what this other display shows (same picture, set and flip), on every PC.
    private PuzzleDisplay copyDisplay;

    // How many flaps (1, 2, 3 or 5)
    internal PuzzleDisplaySize size = PuzzleDisplaySize.One;

    private Flap[] flaps = new Flap[0];
    private bool followLoopLogged;   // a Copy Display loop has been reported

    internal static int FlapsOf(PuzzleDisplaySize s) {
        switch (s) {
            case PuzzleDisplaySize.Two: return 2;
            case PuzzleDisplaySize.Three: return 3;
            case PuzzleDisplaySize.Five: return 5;
            default: return 1;
        }
    }

    // How many flaps this display will have (before the frame is built): the chosen Size, or the size of the display it
    // copies (through any chain of copies), so a copy can always show every flap of its source
    internal int ConfiguredFlaps {
        get {
            PuzzleDisplay cur = this;
            for (int hops = 0; cur != null && hops < 8; hops++) {
                if (cur.copyDisplay == null) return FlapsOf(cur.size);
                cur = cur.copyDisplay;
            }
            return FlapsOf(size);   // a loop of copies: it stays blank anyway
        }
    }

    // How many flaps this display really has (0 if there was no frame to copy)
    internal int FlapCount => flaps.Length;

    // Read-only for scripts: the image number being shown on flap 1 (or flipped towards); 0 is blank
    public int Image => flaps.Length > 0 ? flaps[0].image : 0;

    // Read-only for scripts: the image number on a flap (1 is the far left); 0 is blank
    public int GetImage(int flap) {
        return flap >= 1 && flap <= flaps.Length ? flaps[flap - 1].image : 0;
    }

    // Read-only for scripts: flap 1's set
    internal PuzzleImageSet ImageSet { [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] get { return flaps.Length > 0 ? flaps[0].imageSet : PuzzleImageSet.A; } }

    // The picture on a flap (0 is the first) and its set; 0 / A when there is no such flap
    internal int ImageAt(int flap) { return flap >= 0 && flap < flaps.Length ? flaps[flap].image : 0; }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    internal PuzzleImageSet ImageSetAt(int flap) { return flap >= 0 && flap < flaps.Length ? flaps[flap].imageSet : PuzzleImageSet.A; }

    // The glyph of any flap (for looking up the game's picture names)
    internal SplitFlapGlyph AnyGlyph { get { return flaps.Length > 0 ? flaps[0].glyph : null; } }

    // ---- Finding the frame to copy ----

    // The 1-slot frame
    private static GameObject FindSingle() {
        // The display sits under "PlayerCountToggledContents", which may be switched off;
        // the exact-path lookup and the search both find inactive objects.
        GameObject template = Templates.Find(ExactPath, ExactPath3, ExactPath2);
        if (template != null) return template;
        // Not at its known path: search every loaded object instead (slow)
        foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<SplitFlapGlyph>())) {
            SplitFlapGlyph candidate = obj.TryCast<SplitFlapGlyph>();
            if (candidate == null) continue;
            Transform parent = candidate.transform.parent;
            if (parent != null && parent.name == TemplateName) { template = parent.gameObject; break; }
        }
        Templates.FoundBySearch(null, null, ExactPath, template);
        return template;
    }

    // The 2-, 3- or 5-slot frame (horizontal), or null if none is loaded in this lobby
    private static GameObject FindFrame(int flapCount) {
        string exact = flapCount == 2 ? GameRefs.Display.ExactPathTwo : flapCount == 3 ? GameRefs.Display.ExactPathThree : GameRefs.Display.ExactPathFive;
        GameObject template = Templates.Find(exact);
        if (template != null) return template;
        string wanted = "MasterSequenceSplitFlap" + flapCount + "Slot";
        foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Transform>())) {
            Transform t = obj != null ? obj.TryCast<Transform>() : null;
            if (t == null || !t.gameObject.scene.IsValid()) continue;
            if (t.name != wanted && !t.name.StartsWith(wanted + " (")) continue;
            string path = Templates.FullPath(t);
            if (path.Contains("BigPuzzleSDK") || path.Contains(CopyName)) continue;
            if (GlyphChildren(t) != flapCount || IsVertical(t)) continue;
            Templates.FoundBySearch(null, null, exact, t.gameObject);
            return t.gameObject;
        }
        return null;
    }

    private static int GlyphChildren(Transform frame) {
        int n = 0;
        for (int i = 0; i < frame.childCount; i++) {
            Transform child = frame.GetChild(i);
            if (child != null && child.name.StartsWith("SplitFlapGlyph")) n++;
        }
        return n;
    }

    // The game's upright 3-slot frame (the goodbye gate's) is not offered
    private static bool IsVertical(Transform frame) {
        for (int i = 0; i < frame.childCount; i++) {
            Transform child = frame.GetChild(i);
            if (child == null || !child.name.StartsWith("splitflap_case")) continue;
            MeshFilter filter = child.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null && mesh.name.Contains("vertical")) return true;
        }
        return false;
    }

    // Called by the loader while the puzzle is still inactive
    internal void SetupDisplay() {
        var log = BigPuzzleSDK.Plugin.log;
        int wanted = ConfiguredFlaps;

        // The 1-slot frame sets the size: the bigger frames sit at a bigger scale in the game, so they are brought to the
        // same world scale as the 1-slot one (0.7), or the flaps would come out too large
        GameObject single = FindSingle();
        Vector3 worldScale = single != null ? single.transform.lossyScale : new Vector3(0.7f, 0.7f, 0.7f);

        GameObject frame = wanted > 1 ? FindFrame(wanted) : null;
        var copies = new List<GameObject>();
        if (frame != null) {
            copies.Add(CloneFrame(frame, 0f, worldScale));
        } else {
            if (wanted > 1)
                log.LogWarning($"[PuzzleDisplay] No {wanted}-slot frame is loaded in this lobby, so '{name}' uses {wanted} single frames side by side.");
            if (single == null) {
                log.LogWarning($"[PuzzleDisplay] No vanilla '{TemplateName}' is loaded, so '{name}' has nothing to copy.");
                return;
            }
            for (int i = 0; i < wanted; i++) copies.Add(CloneFrame(single, (i - (wanted - 1) / 2f) * SingleFrameWidth * worldScale.x, worldScale));
        }

        // Hide the creator's placeholder
        Renderer placeholder = GetComponent<Renderer>();
        if (placeholder != null) placeholder.enabled = false;
        Collider placeholderCollider = GetComponent<Collider>();
        if (placeholderCollider != null) placeholderCollider.enabled = false;

        // Every flap, in the game's own order: the frame's first SplitFlapGlyph is flap 1, then (1), (2)... (the game lays them out in a row from there)
        var glyphs = new List<SplitFlapGlyph>();
        foreach (GameObject copy in copies) {
            var inFrame = new List<SplitFlapGlyph>();
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<SplitFlapGlyph>(), true)) {
                SplitFlapGlyph g = c != null ? c.TryCast<SplitFlapGlyph>() : null;
                if (g != null) inFrame.Add(g);
            }
            glyphs.AddRange(inFrame);
        }
        if (glyphs.Count != wanted)
            log.LogWarning($"[PuzzleDisplay] '{name}' wanted {wanted} flap(s) but the copy has {glyphs.Count}.");
        int count = Math.Min(wanted, glyphs.Count);
        if (count == 0) {
            log.LogWarning($"[PuzzleDisplay] The copy for '{name}' has no SplitFlapGlyph.");
            return;
        }
        // A frame with too many flaps: the extra flaps are switched off
        for (int i = count; i < glyphs.Count; i++) glyphs[i].gameObject.SetActive(false);

        flaps = new Flap[count];
        for (int i = 0; i < count; i++) {
            var flap = new Flap(this, i, glyphs[i]);
            flaps[i] = flap;
            // Choose the image set before the display wakes up
            flap.ApplyImageSet("before wake-up");
            // Must be made here, during setup, so every PC hands out the same network ids in order
            string label = name + (count > 1 ? " flap " + (i + 1) : "");
            flap.sync = new SyncedValue(transform, "display " + label);
            flap.syncSet = new SyncedValue(transform, "display set " + label);
        }
    }

    // One copy of a frame under this object, at the given world scale. 'offset' moves it sideways (in metres) when several single frames make a row.
    private GameObject CloneFrame(GameObject template, float offset, Vector3 templateScale) {

        GameObject copy = Templates.Clone(template, transform);
        copy.name = CopyName;
        Vector3 parentScale = transform.lossyScale;
        copy.transform.localPosition = new Vector3(Mathf.Approximately(parentScale.x, 0f) ? offset : offset / parentScale.x, 0f, 0f);
        copy.transform.localRotation = Quaternion.identity;
        copy.SetActive(true);

        // Keep the vanilla display's own world size whatever the creator's objects are scaled to
        copy.transform.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? templateScale.x : templateScale.x / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? templateScale.y : templateScale.y / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? templateScale.z : templateScale.z / parentScale.z);

        // The game switches off the renderers of displays the player is far away from, and the
        // copy inherits that. Nothing manages the copy, so switch them back on for good.
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
            Renderer r = c != null ? c.TryCast<Renderer>() : null;
            if (r == null || r.enabled) continue;
            r.enabled = true;
        }

        // The game's own puzzle wiring for this display: switch it off so only the mod
        // decides what is shown.
        Transform logic = copy.transform.Find("masterSequenceLogic");
        if (logic != null) logic.gameObject.SetActive(false);
        return copy;
    }

    // Which of the game's tile pictures is image number 'image' of a set. The game works this
    // out on its split-flap displays, so one is borrowed for the lookup and put back as it was.
    internal static PropGroup GroupFor(PuzzleImageSet set, int image) {
        if (image <= 0) return Enum.TryParse("PegTileBlank", out PropGroup blank) ? blank : default;
        if (set == PuzzleImageSet.Custom) set = PuzzleImageSet.A;   // custom sheets use set A's slots
        SplitFlapGlyph glyph = null;
        foreach (PuzzleDisplay display in all) if (display != null && display.AnyGlyph != null) { glyph = display.AnyGlyph; break; }
        if (glyph == null)
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<SplitFlapGlyph>())) {
                glyph = obj.TryCast<SplitFlapGlyph>();
                if (glyph != null) break;
            }
        if (glyph == null || !Enum.TryParse("PegTileSet" + set, out PegTileSet wanted)) return default;
        PegTileSet before = glyph.tileSet;
        try {
            glyph.tileSet = wanted;
            return glyph.GetPropGroupByIndex(image);
        } finally {
            glyph.tileSet = before;
        }
    }

    // Copies the game's artwork table, adds customSheet as an extra 4 x 4 sheet, and points
    // set A's images at its cells. Cells are numbered reading across then down from the
    // top-left; cell 0 is blank and image N shows cell N (images 1 to 8 are used).
    // The grid is 4 x 4, not 3 x 3, so that the exact centre of the sheet falls on a corner
    // between cells and is always empty, as it is on the game's own 6 x 6 sheets.
    // One custom table per (game table, sheet) pair, shared by every display and tile using it
    private static readonly Dictionary<string, PegTileDataSet> customTables = new Dictionary<string, PegTileDataSet>();

    internal static PegTileDataSet BuildCustomData(PegTileDataSet originalData, Texture2D customSheet, string name) {
        string key = originalData.Pointer.ToString() + "|" + customSheet.Pointer.ToString();
        if (customTables.TryGetValue(key, out PegTileDataSet cached) && cached != null) return cached;
        const int Columns = 4, Rows = 4;

        PegTileDataSet copy = UnityEngine.Object.Instantiate((UnityEngine.Object)originalData).Cast<PegTileDataSet>();
        copy.name = "PegTileData (custom for " + name + ")";

        var materials = originalData.materialPerTextureIndex;
        var sizes = originalData.sizePerTextureIndex;
        int sheet = materials.Length; // number of the new sheet

        Material material = UnityEngine.Object.Instantiate((UnityEngine.Object)materials[0]).Cast<Material>();
        material.name = "PegTile Custom";
        material.SetTexture("_MainTex", customSheet);

        var newMaterials = new Il2CppReferenceArray<Material>(sheet + 1);
        for (int i = 0; i < sheet; i++) newMaterials[i] = materials[i];
        newMaterials[sheet] = material;

        var newSizes = new Il2CppStructArray<PegTileDataSet.Int2>(sheet + 1);
        for (int i = 0; i < sheet && i < sizes.Length; i++) newSizes[i] = sizes[i];
        newSizes[sheet] = new PegTileDataSet.Int2 { x = Columns, y = Rows };

        copy.materialPerTextureIndex = newMaterials;
        copy.sizePerTextureIndex = newSizes;

        var entries = copy.indexDatas;
        int moved = 0;
        for (int image = 1; image <= 9; image++) {
            if (!Enum.TryParse("PegTileA" + image, out PropGroup group)) continue;
            for (int e = 0; e < entries.Length; e++) {
                PegTileDataSet.TileIndexData entry = entries[e];
                if (entry.propGroup != group) continue;
                entry.textureIndex = sheet;
                // The sheet's top-left cell is cell 0 and is left blank, so image N is cell N.
                // The ninth borrowed slot has no cell of its own and points at the blank one.
                int cell = image < Columns * Rows ? image : 0;
                entry.xIndex = cell % Columns;
                entry.yIndex = cell / Columns;
                entries[e] = entry;
                moved++;
                break;
            }
        }

        // The blank image has no entry of its own in the game's table. Give it one that points
        // at the sheet's blank top-left cell, so image 0 is drawn from the custom sheet too.
        if (Enum.TryParse("PegTileBlank", out PropGroup blank)) {
            bool hasBlank = false;
            for (int e = 0; e < entries.Length; e++) {
                PegTileDataSet.TileIndexData entry = entries[e];
                if (entry.propGroup != blank) continue;
                entry.textureIndex = sheet; entry.xIndex = 0; entry.yIndex = 0;
                entries[e] = entry;
                hasBlank = true;
                break;
            }
            if (!hasBlank) {
                var longer = new Il2CppStructArray<PegTileDataSet.TileIndexData>(entries.Length + 1);
                for (int e = 0; e < entries.Length; e++) longer[e] = entries[e];
                longer[entries.Length] = new PegTileDataSet.TileIndexData { propGroup = blank, xIndex = 0, yIndex = 0, textureIndex = sheet };
                copy.indexDatas = longer;
            }
        }

        customTables[key] = copy;
        return copy;
    }

    private void Start() {
        all.Add(this);
        foreach (Flap flap in flaps) flap.Start();
    }

    private void Update() {
        foreach (Flap flap in flaps) flap.Tick();
    }

    // Whether the host can already send this display's pictures (the puzzle deals tiles out once this is true)
    internal bool Ready {
        get {
            if (copyDisplay != null) return true;
            foreach (Flap flap in flaps) if (flap.sync == null || !flap.sync.Ready) return false;
            return true;
        }
    }
    internal bool CopiesAnother { get { return copyDisplay != null; } }

    // Host only. Shows this tile's picture on a flap (0 is the first): its image set, its number and, for Custom, its sheet.
    internal void ShowTile(PuzzlePegTile tile, int flap) {
        if (!PuzzleSDK.IsHost || copyDisplay != null || tile == null || flap < 0 || flap >= flaps.Length) return;
        flaps[flap].ShowTile(tile);
    }

    // Host only. Every flap goes blank (when the puzzle stops).
    internal void ShowBlank() {
        if (!PuzzleSDK.IsHost || copyDisplay != null) return;
        foreach (Flap flap in flaps) flap.SetImage(0);
    }

    // Host only. One flap (0 is the first) goes blank: there is no tile left for it. The others keep their picture.
    internal void ShowBlank(int flap) {
        if (!PuzzleSDK.IsHost || copyDisplay != null || flap < 0 || flap >= flaps.Length) return;
        flaps[flap].SetImage(0);
    }

    // The display this one finally follows (through other copying displays), or null: nothing to follow, or a loop
    private PuzzleDisplay FollowedSource() {
        var seen = new HashSet<IntPtr> { Pointer };
        PuzzleDisplay cur = copyDisplay;
        for (int hops = 0; cur != null && hops < 8; hops++) {
            if (cur.copyDisplay == null) return cur;
            if (!seen.Add(cur.Pointer)) break;
            cur = cur.copyDisplay;
        }
        if (!followLoopLogged) {
            followLoopLogged = true;
            BigPuzzleSDK.Plugin.log.LogWarning($"[PuzzleDisplay] '{name}': Copy Display leads back to a display already followed (or is too long), so it stays blank.");
        }
        return null;
    }

    private void OnDestroy() { all.Remove(this); }

    // One flap: its own picture, image set, custom sheet and network values.
    // (Plain C#, not part of the injected component, so nothing here is seen by the game.)
    private sealed class Flap {
        private readonly PuzzleDisplay owner;
        private readonly int index;
        internal readonly SplitFlapGlyph glyph;

        internal int image;               // number of the image being shown (or flipped towards); 0 is blank
        internal PuzzleImageSet imageSet; // which of the game's image sets to show
        private Texture2D customSheet;    // for imageSet = Custom: one square texture, 4 x 4 grid, top-left cell blank, then images 1 to 8
        private int imageCount = -1;      // number of images in the set; -1 until counted

        private int appliedImage = -1;    // the last image number actually sent to the game's display
        private bool setApplied;          // has appliedSet been set yet
        private PuzzleImageSet appliedSet;
        private PegTileDataSet originalData;   // the game's own artwork table
        private PegTileDataSet customData;     // private copy that points set A at customSheet

        // The host's image and set, sent to every other player (see SyncedValue in Plugin.cs). On other
        // players' PCs the flap shows only what the host shows; their own calls are ignored.
        internal SyncedValue sync, syncSet;
        private bool applyingFromHost;
        private bool mirroring;           // this flap is copying another right now (so SetImage is allowed)

        internal Flap(PuzzleDisplay owner, int index, SplitFlapGlyph glyph) {
            this.owner = owner; this.index = index; this.glyph = glyph;
        }

        private string Label { get { return owner.name + (owner.flaps.Length > 1 ? " flap " + (index + 1) : ""); } }

        // Sets the game's image set from the creator's choice. Matched by name, so the game's own numbering does not matter.
        internal void ApplyImageSet(string when) {
            var log = BigPuzzleSDK.Plugin.log;
            setApplied = true;
            appliedSet = imageSet;
            ApplyDataSet();

            string setName = "PegTileSet" + (imageSet == PuzzleImageSet.Custom ? PuzzleImageSet.A : imageSet);   // a custom set borrows set A's slots
            PegTileSet before = glyph.tileSet;
            if (Enum.TryParse(setName, out PegTileSet chosen) && Enum.IsDefined(typeof(PegTileSet), chosen)) {
                glyph.tileSet = chosen;
            } else {
                log.LogWarning($"[PuzzleDisplay] '{Label}': the game has no image set called {setName}, keeping {before}.");
            }
        }

        // Gives the flap and its six faces either the game's artwork table or the custom one
        private void ApplyDataSet() {
            var log = BigPuzzleSDK.Plugin.log;
            try {
                if (originalData == null) originalData = glyph.pegTileDataSet;

                PegTileDataSet wanted = originalData;
                if (imageSet == PuzzleImageSet.Custom) {
                    if (customSheet == null) {
                        log.LogWarning($"[PuzzleDisplay] '{Label}' is set to Custom but has no customSheet; showing set A.");
                    } else {
                        if (customData == null) customData = BuildCustomData(originalData, customSheet, owner.name);
                        if (customData != null) wanted = customData;
                    }
                }
                if (wanted == null || (glyph.pegTileDataSet != null && glyph.pegTileDataSet.Pointer == wanted.Pointer)) return;

                glyph.pegTileDataSet = wanted;
                foreach (PegTileRenderer face in new[] { glyph.upperFlapTop, glyph.upperFlapBottom, glyph.lowerFlapTop, glyph.lowerFlapBottom, glyph.rearFlapTop, glyph.rearFlapBottom }) {
                    if (face == null) continue;
                    face.pegTileDataSet = wanted;
                    face.Refresh();
                }
            } catch (Exception ex) {
                log.LogWarning($"[PuzzleDisplay] '{Label}' could not switch artwork table: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal void Start() {
            if (sync != null) {
                sync.onChanged = OnHostImage;
                sync.Register();
            }
            if (syncSet != null) {
                syncSet.onChanged = v => { if (Enum.IsDefined(typeof(PuzzleImageSet), v)) { imageSet = (PuzzleImageSet)v; AdoptCustomSheet(image); } };
                syncSet.Register();
            }

            // Again after the display has woken up, in case its own start-up put the set back
            ApplyImageSet("after wake-up");

            CountImages();
            // The starting image, on every PC (later changes come from the host)
            applyingFromHost = true;
            try { SetImage(image); } finally { applyingFromHost = false; }
        }

        // Notices when 'image' or 'imageSet' was changed directly and passes it on
        internal void Tick() {
            SyncImageSet();

            // A display that copies another follows it on every PC; nothing is sent over the network for it
            if (owner.copyDisplay != null) { Mirror(); return; }

            // Other players: the host's image arrives through OnHostImage; nothing to do here
            if (sync != null && sync.Ready && !SyncedValue.IsHost) return;

            if (image != appliedImage) SetImage(image);

            // Host: send what is showing
            if (sync != null && sync.Ready && SyncedValue.IsHost && appliedImage >= 0) sync.Push(appliedImage);
            if (syncSet != null && syncSet.Ready && SyncedValue.IsHost) syncSet.Push((int)imageSet);
        }

        // Counts the images the flap's set has
        private void CountImages() {
            // The game does not check the number, so asking past the last image returns junk.
            // Stop at the first answer that is not a real image.
            int count = 0;
            try {
                for (int i = 0; i < 64; i++) {
                    PropGroup group = glyph.GetPropGroupByIndex(i);
                    if (group == PropGroup.NoPropGroup || !Enum.IsDefined(typeof(PropGroup), group)) break;
                    count++;
                }
            } catch (Exception) {
                // ran past the last image
            }
            imageCount = count;
            // A custom sheet has a blank top-left cell plus 8 images, so images run 0 to 8
            if (imageSet == PuzzleImageSet.Custom && customData != null && imageCount > 9) imageCount = 9;
        }

        // Picks up a change to imageSet made after loading: passes it on to the game's display,
        // recounts the images and redraws the current one.
        private void SyncImageSet() {
            if (setApplied && appliedSet == imageSet) return;
            ApplyImageSet("changed");
            CountImages();
            try {
                glyph.SetFlaps(glyph.currentIndex, glyph.currentIndex); // redraw with the new set
            } catch (Exception ex) {
                BigPuzzleSDK.Plugin.log.LogWarning($"[PuzzleDisplay] '{Label}' could not redraw after changing set: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Flip to the image with this number. Not for creators: the puzzle deals the pictures out (see PuzzlePile).
        internal void SetImage(int number) {
            if (owner.copyDisplay != null && !mirroring) return;   // a copying display only shows what it follows
            if (!applyingFromHost && !mirroring && !PuzzleSDK.IsHost) return;   // other players: the host decides
            SyncImageSet();
            if (number < 0 || (imageCount > 0 && number >= imageCount)) {
                BigPuzzleSDK.Plugin.log.LogWarning($"[PuzzleDisplay] '{Label}': image {number} does not exist (valid: 0 to {imageCount - 1}).");
                image = Mathf.Max(appliedImage, 0); // put the variable back to what is really showing
                return;
            }
            image = number;
            appliedImage = number;
            try {
                glyph.SetAimIndex(number);
            } catch (Exception ex) {
                BigPuzzleSDK.Plugin.log.LogWarning($"[PuzzleDisplay] '{Label}' could not show image {number}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Host only. Shows this tile's picture (its image set, its number and, for Custom, its sheet).
        internal void ShowTile(PuzzlePegTile tile) {
            var set = (PuzzleImageSet)(int)tile.ImageSet;
            if (set != imageSet || (set == PuzzleImageSet.Custom && !SameSheet(tile.CustomSheet))) {
                imageSet = set;
                if (set == PuzzleImageSet.Custom && tile.CustomSheet != null) { customSheet = tile.CustomSheet; customData = null; }
                setApplied = false;   // SetImage passes the new set on to the game
            }
            SetImage(tile.Image);
        }

        // Shows what the same flap of the followed display shows, flipping to it the same way (every PC does this itself)
        private void Mirror() {
            PuzzleDisplay source = owner.FollowedSource();
            Flap followed = source != null && index < source.flaps.Length ? source.flaps[index] : null;
            int wantImage = 0;
            if (followed != null) {
                wantImage = followed.image;
                if (followed.imageSet != imageSet || (followed.imageSet == PuzzleImageSet.Custom && !SameSheet(followed.customSheet))) {
                    imageSet = followed.imageSet;
                    if (imageSet == PuzzleImageSet.Custom && followed.customSheet != null) { customSheet = followed.customSheet; customData = null; }
                    setApplied = false;
                }
            }
            if (wantImage == appliedImage && setApplied) return;
            mirroring = true;
            try { SetImage(wantImage); } finally { mirroring = false; }
        }

        private bool SameSheet(Texture2D sheet) {
            return sheet == null || (customSheet != null && sheet.Pointer == customSheet.Pointer);
        }

        // Other players: a Custom picture uses the sheet of the puzzle's tile that shows it
        private void AdoptCustomSheet(int img) {
            if (imageSet != PuzzleImageSet.Custom) return;
            int myRoot = owner.transform.root.GetInstanceID();
            foreach (PuzzlePegTile tile in PuzzlePegTile.all) {
                if (tile == null || tile.transform.root.GetInstanceID() != myRoot) continue;
                if ((PuzzleImageSet)(int)tile.ImageSet != PuzzleImageSet.Custom || tile.Image != img || tile.CustomSheet == null) continue;
                if (SameSheet(tile.CustomSheet)) return;
                customSheet = tile.CustomSheet;
                customData = null;
                setApplied = false;
                return;
            }
        }

        // Other players: the host showed a new image
        private void OnHostImage(int value) {
            AdoptCustomSheet(value);
            if (appliedImage == value) return;
            applyingFromHost = true;
            try { SetImage(value); } finally { applyingFromHost = false; }
        }
    }
}

} // namespace BigPuzzleSDK
