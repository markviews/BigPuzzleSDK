using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Mirror;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleBox: copies a vanilla openable glass box into place
// and opens or closes it: Open() stays open until Close(); HoldToOpen() from a hold button's
// onPressed stays open only while that button is held, like PuzzleGourdPlinth. The game's own lid animation and sounds are used.
public class PuzzleBox : MonoBehaviour {
    public PuzzleBox(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.Box.ExactPath;

    private const string CopyName = "VanillaBox";
    private const string TemplateName = GameRefs.Box.TemplateName;
    private const string PreferredPath = GameRefs.Box.PreferredPath;

    // Parts of the vanilla box that belong to the game's own puzzle and are switched off in the
    // copy: the switch players use to open it directly, and the slot that holds a real gourd.
    private static readonly string[] DisabledChildren = GameRefs.Box.DisabledChildren;

    internal static readonly List<PuzzleBox> all = new List<PuzzleBox>();

    // Whether the lid should be open; changed only through Open() / HoldToOpen() / Close()
    private bool open;

    // Read-only for scripts
    public bool IsOpen => open;

    private LobbyDoor door;
    private TrackedPeckState lidState;
    private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
    private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
    private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)
    private bool applied, appliedOpen;
    private global::PuzzleButton heldBy;   // the hold button that opened it; closes when let go

    // Called by the loader while the puzzle is still inactive
    internal void SetupBox(int index) {
        var log = Plugin.log;

        GameObject template = Templates.Find(ExactPath);
        if (template == null) {
            // Not at its known path: search every loaded object instead (slow). Many doors in
            // the game use LobbyDoor, so only accept one that sits inside an OpenableBox.
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<LobbyDoor>())) {
                LobbyDoor candidate = obj.TryCast<LobbyDoor>();
                if (candidate == null) continue;
                Transform box = candidate.transform.parent;
                if (box == null || box.name != TemplateName) continue;
                if (template == null) template = box.gameObject;
                if (PathOf(box).Contains(PreferredPath)) { template = box.gameObject; break; }
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzleBox] No vanilla '{TemplateName}' is loaded, so '{name}' has nothing to copy.");
            return;
        }

        Vector3 templateScale = template.transform.lossyScale;

        GameObject copy = Templates.Clone(template, transform);
        copy.name = CopyName;
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.SetActive(true);

        // Keep the vanilla box's own world size whatever the creator's objects are scaled to
        Vector3 parentScale = transform.lossyScale;
        copy.transform.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? templateScale.x : templateScale.x / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? templateScale.y : templateScale.y / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? templateScale.z : templateScale.z / parentScale.z);

        // Where the vanilla box keeps its gourd (that part is switched off just below, but still has a place)
        gourdHome = FindDeep(copy.transform, "BoxGourdHome");

        // Switch off the game-puzzle parts before the copy wakes up, so they never register
        foreach (string childName in DisabledChildren) {
            Transform child = FindDeep(copy.transform, childName);
            if (child == null) continue;
            child.gameObject.SetActive(false);
        }

        // The game switches off renderers of far-away objects; the copy inherits that
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
            Renderer r = c != null ? c.TryCast<Renderer>() : null;
            if (r != null) r.enabled = true;
        }

        // Hide the creator's placeholder
        Renderer placeholder = GetComponent<Renderer>();
        if (placeholder != null) placeholder.enabled = false;
        Collider placeholderCollider = GetComponent<Collider>();
        if (placeholderCollider != null) placeholderCollider.enabled = false;

        Component found = copy.GetComponentInChildren(Il2CppType.Of<LobbyDoor>(), true);
        door = found != null ? found.TryCast<LobbyDoor>() : null;
        if (door == null) { log.LogWarning($"[PuzzleBox] The copy for '{name}' has no LobbyDoor."); return; }
        // The lid's on/off state sits on the same object as the door (boxLogic)
        Component stateComponent = door.gameObject.GetComponent(Il2CppType.Of<TrackedPeckState>());
        lidState = stateComponent != null ? stateComponent.TryCast<TrackedPeckState>() : null;

        // Everything that carries a network id must get its own, or the copy would act on the
        // vanilla box it was copied from. Only parts that are still active are collected.
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckSwitch>(), false)) {
            PeckSwitch sw = c != null ? c.TryCast<PeckSwitch>() : null;
            if (sw != null) switches.Add(sw);
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), false)) {
            TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
            if (st != null) states.Add(st);
        }
        ticketIds = Tickets.Allocate(switches.Count + states.Count);
        AssignTickets(false);
        SetupGourd();
    }

    // Finds a child by name at any depth (Transform.Find only looks at direct children)
    private static Transform FindDeep(Transform parent, string childName) {
        for (int i = 0; i < parent.childCount; i++) {
            Transform child = parent.GetChild(i);
            if (child.name == childName) return child;
            Transform deeper = FindDeep(child, childName);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private static string PathOf(Transform t) {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    // Gives every switch and state its own id; after enable, also fixes the registry
    private void AssignTickets(bool register) {
        int slot = 0;
        foreach (PeckSwitch sw in switches) {
            if (register) Register(sw.Cast<LobbyNetworking.ITicketed>(), sw.ticket, ticketIds[slot]);
            sw.useTicket = true;
            sw.ticket = ticketIds[slot];
            sw.shellReference = new SeaShell.ShellReference(ticketIds[slot]);
            slot++;
        }
        foreach (TrackedPeckState st in states) {
            if (register) Register(st.Cast<LobbyNetworking.ITicketed>(), st.ticket, ticketIds[slot]);
            st.ticket = ticketIds[slot];
            st.shellReference = new SeaShell.ShellReference(ticketIds[slot]);
            slot++;
        }
    }

    private static void Register(LobbyNetworking.ITicketed item, ushort current, ushort wanted) {
        var office = LobbyNetworking.TicketOffice.instance;
        if (office == null) return;
        try {
            var tickets = office.tickets;
            // Only remove an old entry if it really is this object (never the vanilla original)
            if (current != wanted && tickets.ContainsKey(current) && tickets[current].Pointer == item.Pointer)
                tickets.Remove(current);
            tickets[wanted] = item;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleBox] id registration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private float startedAt;
    private void Start() {
        startedAt = Time.time;
        all.Add(this);
        AssignTickets(true);

        // Other players: the sleeping gourd waits outside the box, so that wherever the host's
        // spawn message puts it, it lands in the right place in the world (same as the gourd vice)
        if (!NetworkServer.active && realGourd != null) {
            Vector3 size = realGourd.transform.lossyScale;
            realGourd.transform.SetParent(null, true);
            realGourd.transform.localScale = size;
            realGourd.name = "PuzzleGourd";
        }
    }

    private void Update() {
        UpdateGourd();
        if (door == null) return;

        // Opened by a hold button: close again as soon as nobody is holding it
        if (heldBy != null && (!open || !heldBy.IsHeld)) {
            open = false;
            heldBy = null;
        }

        if (applied && open == appliedOpen) return;

        // Nothing to do on the first frame if the box should simply stay closed
        bool first = !applied;
        applied = true;
        appliedOpen = open;
        if (first && !open) return;

        Apply(open);
    }

    // Tells the game's box to open or close. Going through the lid's tracked state runs
    // everything attached to it (lid animation, sounds, material and toggle effects).
    private void Apply(bool wantOpen) {
        var log = Plugin.log;
        int value = (wantOpen != door.peckAndDoorStatesAreInverse) ? 1 : 0;
        try {
            if (lidState != null) {
                if (PuzzleSDK.IsHost) lidState.SetState(value);   // other players: arrives from the host
                return;
            }
        } catch (Exception ex) {
            log.LogWarning($"[PuzzleBox] '{name}' could not set the lid state ({ex.GetType().Name}: {ex.Message}); moving the lid directly.");
        }
        try {
            if (value == 1) door.TestPeck1(); else door.TestPeck0();
        } catch (Exception ex) {
            log.LogWarning($"[PuzzleBox] '{name}' could not move the lid: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- Callable from code or from an Inspector event ----

    // Host only: the lid reaches everyone from the host

    // Opens and stays open until Close()
    public void Open() { if (!PuzzleSDK.IsHost) return; open = true; heldBy = null; }

    // For a hold button's onPressed: open while the button is held, closed again when let go
    // (no Close() wiring needed). Called from anything else it acts like Open().
    public void HoldToOpen() {
        if (!PuzzleSDK.IsHost) return;
        open = true;
        heldBy = global::PuzzleButton.pressing;
        if (heldBy == null && !warnedHold) {
            warnedHold = true;
            Plugin.log.LogWarning($"[PuzzleBox] '{name}': HoldToOpen() was not called from a hold button's onPressed, so it stays open until Close().");
        }
    }
    private bool warnedHold;

    public void Close() { if (!PuzzleSDK.IsHost) return; open = false; heldBy = null; }

    private void OnDestroy() {
        all.Remove(this);
        if (realGourd != null) UnityEngine.Object.Destroy(realGourd);   // it lives outside the puzzle once spawned
    }

    // ---- The puzzle's gourd ----
    // A Puzzle with a box (and no gourd vice) keeps its one gourd in the box. The gourd is a normal loose
    // gourd that rests where the vanilla box keeps its own; the lid and glass stop players reaching it until
    // the box is open. The moment someone picks it up the puzzle counts as done (there is no win event):
    // it is saved as "held" and, whatever happens to it after that, never put back in the box.

    private Transform gourdHome;
    private GameObject realGourd;
    private ulong gourdNetId;
    private bool holdsGourd;     // this box is the puzzle's gourd holder
    private bool gourdTaken;     // host: someone has held the gourd (once true, always true)
    private bool gourdPlaced;    // host: the gourd has been spawned (in the box or from a save)

    internal bool HoldsGourd => holdsGourd;
    internal GameObject Gourd => realGourd;
    internal bool GourdTaken => gourdTaken;

    // The box that holds the gourd of the puzzle with this root (null: that puzzle's gourd isn't in a box)
    internal static PuzzleBox HolderOf(Transform root) {
        int id = root.GetInstanceID();
        foreach (PuzzleBox box in all)
            if (box != null && box.holdsGourd && box.transform.root.GetInstanceID() == id) return box;
        return null;
    }

    // Called from SetupBox. Only a Puzzle with no gourd vice gets a gourd, and only from its first box.
    private void SetupGourd() {
        var log = Plugin.log;
        Transform root = transform.root;
        PuzzleSDK sdk = root.GetComponentInChildren<PuzzleSDK>(true);
        if (sdk == null || sdk.puzzleType != PuzzleType.Puzzle) return;
        if (root.GetComponentsInChildren<PuzzleGourdVice>(true).Length > 0) return;   // the vice gives out the gourd
        var boxes = root.GetComponentsInChildren<PuzzleBox>(true);
        if (boxes.Length == 0 || boxes[0].Pointer != Pointer) return;                // only one gourd per puzzle

        GameObject source = PuzzleGourdVice.FindAnyGourd();
        if (source == null) {
            log.LogWarning($"[PuzzleBox] '{name}': no gourd is loaded to copy, so the box holds no gourd.");
            return;
        }
        GameObject gourd = UnityEngine.Object.Instantiate((UnityEngine.Object)source, transform).Cast<GameObject>();
        gourd.name = "PuzzleGourd";
        gourd.SetActive(false);
        PuzzleGourdVice.ResetIdentity(gourd);
        PuzzleGourdVice.DetachFromVanillaHome(gourd);
        realGourd = gourd;
        holdsGourd = true;

        ModGourd.Tint(realGourd);              // modded gourds are teal
        ModGourd.MakeModdedProp(realGourd);    // fits modded holders only
        gourdNetId = NetItems.IdFor(transform, "gourd");
        NetItems.Prepare(realGourd, gourdNetId);   // the host's spawn reaches every player by this id
    }

    private void UpdateGourd() {
        if (!holdsGourd || realGourd == null || !NetworkServer.active) return;

        if (!gourdPlaced) {
            // Normally the save puts the gourd back (see PuzzleSave.ApplyGourds). If that never happens
            // (no game save loaded), it goes in the box after a while.
            if (Time.time - startedAt > 12f && !PuzzleSave.GourdsPending) PlaceInBox();
            return;
        }

        // Host: held once means taken for good
        if (!gourdTaken && realGourd.activeSelf && PuzzleSave.WhereIs(realGourd) != PuzzleSave.Lost) gourdTaken = true;
    }

    // Host: wakes the gourd in the box
    internal bool PlaceInBox() {
        Transform spot = gourdHome != null ? gourdHome : transform;
        return Spawn(spot.position + Vector3.up * 0.02f, spot.rotation, false);
    }

    // Host: wakes a gourd that was already taken, lying at a spot (a pillow, the spawn area)
    internal bool PlaceGourdAt(Vector3 position) {
        gourdTaken = true;
        return Spawn(position, Quaternion.identity, true);
    }

    // Host: wakes a gourd that was already taken, straight into a plinth's slot (the tower)
    internal bool PlaceGourdIn(PuzzleGourdPlinth plinth) {
        PropHome home = plinth != null ? plinth.Home : null;
        if (home == null) return false;
        gourdTaken = true;
        if (!Spawn(home.transform.position, home.transform.rotation, true)) return false;
        try {
            Prop prop = realGourd.GetComponent<Prop>();
            plinth.MarkFilled();   // before pinning, so the plinth doesn't fire onGourdPlaced
            prop.ServerSetPinned(home);
            return true;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleBox] '{name}' could not put its gourd in '{plinth.name}': {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private bool Spawn(Vector3 position, Quaternion rotation, bool free) {
        if (!NetworkServer.active || realGourd == null || realGourd.activeSelf) return false;
        try {
            GameObject gourd = realGourd;
            Vector3 size = gourd.transform.lossyScale;
            gourd.transform.SetParent(null, true);
            gourd.transform.position = position;
            gourd.transform.rotation = rotation;
            gourd.transform.localScale = size;
            PuzzleGourdVice.ResetIdentity(gourd);
            NetItems.Prepare(gourd, gourdNetId);
            foreach (Component c in gourd.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
                Renderer r = c != null ? c.TryCast<Renderer>() : null;
                if (r != null) r.enabled = true;
            }
            if (!NetItems.Spawn(gourd)) return false;
            ModGourd.Tint(gourd);
            ModGourd.MakeModdedProp(gourd);
            PuzzleGourdVice.DetachFromVanillaHome(gourd);
            if (free) PuzzleGourdVice.FreeGourd(gourd);
            gourd.transform.position = position;
            gourd.transform.rotation = rotation;
            gourdPlaced = true;
            return true;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleBox] '{name}' could not wake its gourd: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}

} // namespace BigPuzzleSDK
