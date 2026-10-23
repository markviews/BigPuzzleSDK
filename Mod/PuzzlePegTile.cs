using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using Mirror;
using UnityEngine;

namespace BigPuzzleSDK {

// Must stay identical to the Unity SDK's enum.
// The peg tile picture sets. The numbers match PuzzleImageSet's (A = 0, B = 1, D = 3,
// Pontiff = 6, Custom = 9), so the game's set is found by the same number and older tiles keep
// their setting.
public enum PuzzlePegSet { Shapes = 0, Peg = 1, Emotes = 3, Scribbles = 6, Custom = 9 }

// Runtime (mod) version of the SDK's PuzzlePegTile: spawns a real, grabbable peg tile showing
// the chosen picture. The picture is chosen the same way as on PuzzleDisplay.
public class PuzzlePegTile : MonoBehaviour {
    public PuzzlePegTile(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.PegTile.ExactPath;

    private const string TemplateName = GameRefs.PegTile.TemplateName;
    private const string PreferredPath = GameRefs.PegTile.PreferredPath;

    // Set in the Inspector (filled in by the loader); the names must match the Unity-side fields
    private PuzzlePegSet imageSet;
    private int image = 1;
    private Texture2D customSheet;
    private bool invisibleInkView;                // picture only shows through a telescope with Invisible Ink View on

    // Read-only for scripts
    internal PuzzlePegSet ImageSet { [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] get { return imageSet; } }
    public int Image => image;
    internal Texture2D CustomSheet => customSheet;

    private GameObject tile;               // the copy; lives outside the puzzle once spawned
    private PegTileRenderer face;
    private bool spawned;
    private PuzzlePegSet shownSet;
    private int shownImage = -1;
    private float reapplyAt;
    private int shownHidden = -1;          // invisibleInkView tiles: the hidden state last applied (-1 = none yet)
    private ulong netId;                   // the tile's fixed network id (see NetItems in Plugin.cs)

    // Every peg tile in the loaded puzzles (PuzzlePile deals from the ones in the puzzle)
    internal static readonly List<PuzzlePegTile> all = new List<PuzzlePegTile>();
    private void Awake() { all.Add(this); }

    // Called by the loader while the puzzle is still inactive
    internal void SetupTile() {
        var log = Plugin.log;

        GameObject template = Templates.Find(ExactPath);
        if (template == null) {
            // Not at its known path: search every loaded object instead (slow)
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PegTileRenderer>())) {
                PegTileRenderer candidate = obj.TryCast<PegTileRenderer>();
                if (candidate == null || candidate.name != TemplateName) continue;
                if (candidate.gameObject.GetComponent<Prop>() == null) continue;
                string path = PathOf(candidate.transform);
                if (path.Contains("PuzzleLoadHolder") || path.StartsWith("PuzzleTile")) continue;   // skip our own copies
                if (template == null) template = candidate.gameObject;
                if (path.EndsWith(PreferredPath)) { template = candidate.gameObject; break; }
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzlePegTile] No vanilla '{TemplateName}' is loaded, so '{name}' has nothing to copy.");
            return;
        }

        // The tile is a networked game object. Copies of those are rejected the moment they
        // wake, so the copy is kept asleep until it has been made to look brand new.
        tile = Templates.Clone(template, transform);
        tile.name = "PuzzleTile";
        tile.SetActive(false);
        tile.transform.localPosition = Vector3.zero;
        tile.transform.localRotation = Quaternion.identity;
        // Always the game's own tile size, whatever the creator's object is scaled to
        Vector3 templateScale = template.transform.lossyScale;
        Vector3 parentScale = transform.lossyScale;
        tile.transform.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? templateScale.x : templateScale.x / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? templateScale.y : templateScale.y / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? templateScale.z : templateScale.z / parentScale.z);
        ResetIdentity(tile);
        DetachFromVanillaHome(tile);
        netId = NetItems.IdFor(transform, "tile");
        NetItems.Prepare(tile, netId);
        ModGourd.MakeCrossProp(tile);   // fits modded and the game's own peg boards
        face = tile.GetComponent<PegTileRenderer>();

        // Hide the creator's placeholder
        Renderer placeholder = GetComponent<Renderer>();
        if (placeholder != null) placeholder.enabled = false;
        Collider placeholderCollider = GetComponent<Collider>();
        if (placeholderCollider != null) placeholderCollider.enabled = false;
    }

    private static string PathOf(Transform t) {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    // The puzzle has woken: release the tile into the world. The host spawns it for everyone;
    // on other players' PCs the tile stays asleep until the host's spawn message wakes it.
    private void Start() {
        if (tile == null) return;
        var log = Plugin.log;
        if (!NetworkServer.active && tile.activeSelf) return;   // already arrived from the host
        try {
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            Vector3 size = tile.transform.lossyScale;
            tile.transform.SetParent(null, true);
            tile.transform.position = position;
            tile.transform.rotation = rotation;
            tile.transform.localScale = size;
            ResetIdentity(tile);
            NetItems.Prepare(tile, netId);

            if (!NetworkServer.active) return;   // the host's copy will arrive by itself
            if (!NetItems.Spawn(tile)) return;

            DetachFromVanillaHome(tile);
            Prop prop = tile.GetComponent<Prop>();
            if (prop != null) {
                try { prop.ServerSetUnpinned(); } catch (Exception) { }
                try { prop.SetLoose(); } catch (Exception ex) { log.LogWarning($"[PuzzlePegTile] SetLoose failed: {ex.GetType().Name}: {ex.Message}"); }
            }
            tile.transform.position = position;
            tile.transform.rotation = rotation;
            Woke();
        } catch (Exception ex) {
            Exception inner = ex.InnerException ?? ex;
            log.LogWarning($"[PuzzlePegTile] '{name}' could not spawn its tile: {inner.GetType().Name}: {inner.Message}");
        }
    }

    // The tile is in the world (spawned here, or arrived from the host): show it and its picture
    private void Woke() {
        foreach (Component c in tile.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
            Renderer r = c != null ? c.TryCast<Renderer>() : null;
            if (r != null) r.enabled = true;
        }
        spawned = true;
        ShowImage();
        reapplyAt = Time.time + 0.5f;   // once more after the tile has settled in
    }

    private void Update() {
        if (tile == null || face == null) return;
        if (!spawned) {
            if (tile.activeSelf) Woke();   // other players: the host's spawn message just woke it
            return;
        }
        if (reapplyAt > 0f && Time.time >= reapplyAt) { reapplyAt = 0f; shownImage = -1; }
        if (image != shownImage || imageSet != shownSet) ShowImage();

        // Invisible tiles: the picture only shows while this player looks through a lobby-vision
        // telescope (like the Invisible Ink challenge's). Each PC decides for itself.
        if (invisibleInkView) {
            int hide = LobbyVision.On ? 0 : 1;
            if (hide != shownHidden) {
                shownHidden = hide;
                try { face.hidden = hide == 1; face.Refresh(); }
                catch (Exception ex) { Plugin.log.LogWarning($"[PuzzlePegTile] '{name}' could not hide its picture: {ex.GetType().Name}: {ex.Message}"); invisibleInkView = false; }
            }
        }
    }

    // Draws the chosen picture on the tile
    private void ShowImage() {
        var log = Plugin.log;
        shownImage = image;
        shownSet = imageSet;
        try {
            PegTileDataSet data = face.pegTileDataSet;
            if (originalData == null) originalData = data;
            PegTileDataSet wanted = originalData;
            if (imageSet == PuzzlePegSet.Custom) {
                if (customSheet == null) log.LogWarning($"[PuzzlePegTile] '{name}' is set to Custom but has no customSheet; showing set A.");
                else wanted = PuzzleDisplay.BuildCustomData(originalData, customSheet, name);
            }
            if (wanted != null && (data == null || data.Pointer != wanted.Pointer)) face.pegTileDataSet = wanted;

            PropGroup group = PuzzleDisplay.GroupFor((PuzzleImageSet)(int)imageSet, image);
            face.SetAndRefresh(group);
            shownHidden = -1;   // an invisibleInkView tile hides its new picture again
        } catch (Exception ex) {
            log.LogWarning($"[PuzzlePegTile] '{name}' could not show image {image}: {ex.GetType().Name}: {ex.Message}");
        }
    }
    private PegTileDataSet originalData;

    // The tile lives outside the puzzle once spawned, so it goes when the puzzle does
    private void OnDestroy() {
        all.Remove(this);
        if (tile != null) UnityEngine.Object.Destroy(tile);
    }

    // ---- Making a copied networked object acceptable to the game (same as the gourd vice) ----

    private static void ResetIdentity(GameObject go) {
        NetworkIdentity identity = go.GetComponent<NetworkIdentity>();
        if (identity == null) return;
        SetMember(identity, "hasSpawned", false);
        SetMember(identity, "sceneId", 0UL);
        SetMember(identity, "netId", 0u);
    }

    private static void SetMember(NetworkIdentity target, string member, object value) {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        try {
            PropertyInfo prop = target.GetType().GetProperty(member, flags);
            if (prop != null && prop.CanWrite) { prop.SetValue(target, value); return; }
            FieldInfo field = target.GetType().GetField(member, flags);
            if (field != null) field.SetValue(target, value);
        } catch (Exception) { }
    }

    // The copy remembers the vanilla rack as its home and would jump back to it; it also shares
    // the vanilla tile's save identity. Clear both.
    private static void DetachFromVanillaHome(GameObject go) {
        try {
            Prop prop = go.GetComponent<Prop>();
            if (prop == null) return;
            prop.startHome = null;
            prop.originalParent = null;
            prop.propHomeShellReference = new SeaShell.ShellReference(0);
            prop.canSaveHomeWithGuid = false;
            prop.savablePropGuid = "";
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzlePegTile] could not detach the tile from its home: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

} // namespace BigPuzzleSDK
