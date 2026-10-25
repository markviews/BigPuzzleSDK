using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// The sound set. Must stay identical, in the same order, to the Unity SDK's enum.
public enum PuzzleItemSounds { Peg, Cowbell, Buoy, Football, Cannonball, Torch, FoldingChair, WalkieTalkie, Binoculars, Kettle, NightVisionGoggles, LaserPointer, Clock, Pomodoro, Sign, FlareGun, Blindfold, Megaphone, SalonBrush, Laptop, FoldingMap, CoordinatesTracker, GourdHolder, GourdCradle, Key, PermoSign }

// Runtime (mod) version of the SDK's PuzzleItem: the creator's own model as a grabbable item that
// players pick up, carry and throw like the game's. Built on a copy of the game's large cowbell
// (all its pick-up, physics and network parts), with the cowbell's model and sounds taken out and
// the creator's model put in. The host spawns it for everyone (see PuzzleVanillaCopy).
public class PuzzleItem : PuzzleVanillaCopy, ISavedHeld {
    public PuzzleItem(IntPtr ptr) : base(ptr) { }

    // Set in the Inspector (filled in by the loader); the names must match the Unity-side fields
    private float mass = 1f;
    private PuzzleItemSounds sounds = PuzzleItemSounds.Peg;   // grab, release, impact and roll sounds
    private bool canBelt = true;           // can go on a player's belt (holster)
    private bool canBackpack = true;       // can go in a player's backpack
    private bool savesOnReload = true;     // saved like gourds: held when the game saves or reloads = back at the spawn area
    private Transform pivot;               // optional: the point (and direction) the item is held, thrown and spun around

    // Filled in by the loader from the UnityEvents wired up in the Inspector
    private PuzzleEvent onPickedUp;
    private PuzzleEvent onDropped;

    private Prop prop;
    private ulong itemId;
    // Came back from a "held" save and has not been taken away from the spawn area since:
    // lying there counts as held, so it comes back again next time
    private bool fromSave;
    private Vector3 spawnCenter;
    private const float SpawnRadius = 8f;
    private bool held, watching;
    private float nextCheck;

    // Read-only for scripts
    public bool IsHeld => held;

    // The cowbell's parts that don't belong on a generic item
    private static readonly string[] RemovedParts = GameRefs.Item.RemovedParts;
    private static readonly string[] HiddenModels = GameRefs.Item.HiddenModels;
    private const string BellName = GameRefs.Item.BellName;
    private const string SwitchName = GameRefs.Item.SwitchName;

    // Called from SetupCopy, before the copy wakes and before the creator's placeholder is hidden
    internal void SetupItem(GameObject copy) {
        var log = Plugin.log;
        prop = copy.GetComponent<Prop>();
        if (prop == null) { log.LogWarning($"[PuzzleItem] '{name}': the copied item has no Prop."); return; }

        // Same size as the creator's object (the copy normally takes the game's own size)
        copy.transform.localScale = Vector3.one;

        // Cowbell-only parts: the shake sound and the use switch
        try { prop.useHeldSwitch = null; } catch (Exception) { }
        var doomed = new List<UnityEngine.Object>();
        Transform bell = null;
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Component>(), true)) {
            if (c == null) continue;
            string type = c.GetIl2CppType().Name;
            if (Array.IndexOf(RemovedParts, type) >= 0) doomed.Add(c);
            Transform t = c.TryCast<Transform>();
            if (t == null) continue;
            if (t.name == SwitchName) doomed.Add(t.gameObject);
            else if (t.name == BellName) bell = t;
            else if (Array.IndexOf(HiddenModels, t.name) >= 0) t.gameObject.SetActive(false);
        }
        foreach (UnityEngine.Object o in doomed) UnityEngine.Object.DestroyImmediate(o);
        if (bell == null) { log.LogWarning($"[PuzzleItem] '{name}': the copied item has no model to replace."); return; }

        // The creator's model: the mesh on this object, and everything under it
        MeshFilter ownFilter = GetComponent<MeshFilter>();
        MeshRenderer ownRenderer = GetComponent<MeshRenderer>();
        MeshFilter bellFilter = bell.GetComponent<MeshFilter>();
        MeshRenderer bellRenderer = bell.GetComponent<MeshRenderer>();
        // The pivot: the item's own origin is moved to the pivot object, so the model sits where it does
        // relative to it. Without one the origin is this object's own.
        // (position and rotation worked out directly, so turning the pivot turns the model the other way round it)
        Vector3 pivotPos = Vector3.zero;
        Quaternion pivotRotInverse = Quaternion.identity;
        if (pivot != null && pivot != transform) {
            pivotPos = transform.InverseTransformPoint(pivot.position);
            pivotRotInverse = Quaternion.Inverse(Quaternion.Inverse(transform.rotation) * pivot.rotation);
        } else pivot = null;
        // The item itself sits where the pivot is, turned the way the pivot is, so the model (which is
        // moved and turned the opposite way inside it) stays exactly where and how you placed it
        // while it lies there. Only how it is held and thrown follows the pivot.
        if (pivot != null) {
            copy.transform.localPosition = pivotPos;
            copy.transform.localRotation = Quaternion.Inverse(pivotRotInverse);
        }
        bell.localRotation = pivotRotInverse;
        bell.localPosition = pivotRotInverse * (-pivotPos);
        bell.localScale = Vector3.one;
        bool any = false;
        Bounds? box = null;
        if (ownFilter != null && ownFilter.sharedMesh != null && ownRenderer != null) {
            bellFilter.sharedMesh = ownFilter.sharedMesh;
            bellRenderer.sharedMaterials = ownRenderer.sharedMaterials;
            bellRenderer.enabled = true;
            box = Grow(box, ownFilter.sharedMesh.bounds, Matrix4x4.identity);
            any = true;
        } else {
            bellRenderer.enabled = false;   // nothing of its own to show: only the children
        }

        // Children keep their place; any colliders on them are removed (the item has its own box)
        var children = new List<Transform>();
        // (by index: foreach over a Transform hands back plain Il2Cpp objects that can't be cast)
        for (int ci = 0; ci < transform.childCount; ci++) {
            Transform child = transform.GetChild(ci);
            if (child != null && child.gameObject != copy && child != pivot) children.Add(child);
        }
        Transform kernal = null;
        try { kernal = prop.kernal; } catch (Exception) { }
        if (kernal == null) kernal = bell.parent;
        int removedColliders = 0;
        foreach (Transform child in children) {
            foreach (Component c in child.GetComponentsInChildren(Il2CppType.Of<Collider>(), true)) {
                if (c == null) continue;
                UnityEngine.Object.DestroyImmediate(c);
                removedColliders++;
            }
            foreach (Component c in child.GetComponentsInChildren(Il2CppType.Of<MeshFilter>(), true)) {
                MeshFilter mf = c != null ? c.TryCast<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null) continue;
                // the mesh's box in this object's space (works while switched off)
                Matrix4x4 toThis = transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                box = Grow(box, mf.sharedMesh.bounds, toThis);
                any = true;
            }
            Vector3 inPos = child.localPosition;     // where it sits on this object
            Quaternion inRot = child.localRotation;
            child.SetParent(kernal, false);
            if (pivot != null) {
                child.localPosition = pivotRotInverse * (inPos - pivotPos);
                child.localRotation = pivotRotInverse * inRot;
            }
        }
        if (!any) log.LogWarning($"[PuzzleItem] '{name}': no mesh found on this object or under it, so the item will be invisible.");

        // One box around the whole model, on the cowbell's own (registered) collider
        BoxCollider collider = bell.GetComponent<BoxCollider>();
        if (collider != null && box.HasValue) {
            collider.center = box.Value.center;
            collider.size = Vector3.Max(box.Value.size, new Vector3(0.02f, 0.02f, 0.02f));
        }

        ApplyGroups();
        ApplySounds();

        itemId = NetItems.IdFor(transform, "prop0");   // the same id the spawn uses: it names the item in the save file

        try { prop.rb.mass = Mathf.Max(0.01f, mass); } catch (Exception ex) { log.LogWarning($"[PuzzleItem] could not set the mass: {ex.GetType().Name}: {ex.Message}"); }
    }

    // Belt (holster) and backpack are groups on the prop; the pose limit stays. The cowbell this is
    // copied from doesn't have them, so they are added when ticked and taken off when not.
    private void ApplyGroups() {
        try {
            var groups = prop.propGroups;
            if (groups == null) { Plugin.log.LogWarning($"[PuzzleItem] '{name}': the item has no group list, so belt / backpack can't be set."); return; }
            SetGroup(groups, PropGroup.GoesInBackpack, canBackpack);
            SetGroup(groups, PropGroup.GoesInHolster, canBelt);
            ApplyCustomHomes();
        } catch (Exception ex) { Plugin.log.LogWarning($"[PuzzleItem] '{name}': could not set belt / backpack: {ex.GetType().Name}: {ex.Message}"); }
    }

    // How the item sits in each kind of home (backpack, belt): the game's belt / backpack items have one
    // entry per group; the cowbell copy may not, so one is added for each ticked box.
    private void ApplyCustomHomes() {
        try {
            var old = prop.customHomes;
            var keep = new List<PropCustomHome>();
            for (int i = 0; old != null && i < old.Length; i++) {
                string g = old[i].propGroup.ToString();
                if ((g == "GoesInBackpack" && !canBackpack) || (g == "GoesInHolster" && !canBelt)) continue;
                keep.Add(old[i]);
            }
            AddHome(keep, PropGroup.GoesInBackpack, canBackpack);
            AddHome(keep, PropGroup.GoesInHolster, canBelt);
            prop.customHomes = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<PropCustomHome>(keep.ToArray());
        } catch (Exception ex) { Plugin.log.LogWarning($"[PuzzleItem] '{name}': could not set belt / backpack positions: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void AddHome(List<PropCustomHome> list, PropGroup group, bool wanted) {
        if (!wanted) return;
        foreach (PropCustomHome h in list) if (h.propGroup == group) return;
        var home = new PropCustomHome();
        home.propGroup = group;
        list.Add(home);
    }

    private static void SetGroup(Il2CppSystem.Collections.Generic.List<PropGroup> groups, PropGroup group, bool wanted) {
        bool has = groups.Contains(group);
        if (wanted && !has) groups.Add(group);
        else if (!wanted && has) while (groups.Remove(group)) { }
    }

    // The game's sound sets (the asset behind each vanilla item's grab / release / impact sounds), in
    // the enum's order. The set is borrowed from an item of that kind in the world; nothing is changed.
    private static readonly string[] SoundSets = GameRefs.Item.SoundSets;
    private static readonly Dictionary<string, PropAudioReferences> setCache = new Dictionary<string, PropAudioReferences>();

    private void ApplySounds() {
        int index = (int)sounds;
        if (index < 0 || index >= SoundSets.Length) index = 0;
        string asset = SoundSets[index];
        try {
            PropAudioReferences set = FindSet(asset);
            if (set == null) {
                Plugin.log.LogWarning($"[PuzzleItem] '{name}': no '{asset}' sound set is loaded in this lobby, so it keeps the cowbell's sounds.");
                return;
            }
            prop.propAudio = set;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleItem] '{name}': could not set its sounds: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The sound set with this asset name, from any item in the world that uses it
    private PropAudioReferences FindSet(string asset) {
        if (setCache.TryGetValue(asset, out PropAudioReferences cached) && cached != null) return cached;
        foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Prop>())) {
            Prop p = obj != null ? obj.TryCast<Prop>() : null;
            if (p == null || p.Pointer == prop.Pointer || !p.gameObject.scene.IsValid()) continue;
            PropAudioReferences refs = null;
            try { refs = p.propAudio; } catch (Exception) { }
            if (refs == null || refs.name != asset) continue;
            setCache[asset] = refs;
            return refs;
        }
        return null;
    }

    // ---- Saved progress (see PuzzleSave) ----
    public bool SavesOnReload => savesOnReload;
    public ulong ItemId => itemId;
    public MonoBehaviour Owner => this;
    internal static IEnumerable<PuzzleItem> Items() {
        foreach (PuzzleVanillaCopy c in all) {
            PuzzleItem item = c as PuzzleItem;
            if (item != null && item.prop != null) yield return item;
        }
    }

    // What the save file records as "held": carried, or (while it came from the save and hasn't left)
    // lying at the spawn area
    public bool CountsAsHeld() {
        if (IsCarried()) return true;
        return fromSave && NearSpawn();
    }

    private bool NearSpawn() {
        try { return Vector3.Distance(prop.transform.position, spawnCenter) <= SpawnRadius; } catch (Exception) { return false; }
    }

    // In someone's hands, belt or backpack right now
    internal bool IsCarried() {
        try {
            if (prop.exclusiveHolder != null || prop.isInInventory) return true;
            PropHome home = prop.currentHome;
            return home != null && (home.parentCharacter != null || home.isInventory);
        } catch (Exception) { return false; }
    }

    // Host: lays it down at a spot (a saved held item comes back at the spawn area)
    public void SendTo(Vector3 position) {
        try {
            fromSave = true;
            spawnCenter = position;
            Transform t = prop.transform;
            try { prop.ServerSetUnpinned(); } catch (Exception) { }
            t.position = position;
            Rigidbody body = prop.rb;
            if (body != null) { body.position = position; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleItem] '{name}': could not move it to the spawn area: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The box of 'bounds' after 'matrix' (all eight corners), added to 'into'
    private static Bounds Grow(Bounds? into, Bounds bounds, Matrix4x4 matrix) {
        Vector3 c = bounds.center, e = bounds.extents;
        Bounds result = into ?? new Bounds(matrix.MultiplyPoint3x4(c), Vector3.zero);
        for (int i = 0; i < 8; i++) {
            Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            result.Encapsulate(matrix.MultiplyPoint3x4(corner));
        }
        return result;
    }

    // Host: events when a player picks it up or lets go (in a backpack counts as held)
    internal void Watch() {
        if (prop == null || Time.time < nextCheck) return;
        nextCheck = Time.time + 0.1f;
        bool now = false;
        try { now = prop._collidersAreHeld || prop._isInInventory; } catch (Exception) { }
        // Taken out of the spawn area and put down elsewhere: from now on it only counts when held
        if (fromSave && PuzzleSDK.IsHost && !now && !IsCarried() && !NearSpawn()) fromSave = false;
        if (now == held) return;
        held = now;
        if (!PuzzleSDK.IsHost) return;
        if (now) { if (onPickedUp != null) onPickedUp.Invoke(); }
        else if (onDropped != null) onDropped.Invoke();
    }
}

} // namespace BigPuzzleSDK
