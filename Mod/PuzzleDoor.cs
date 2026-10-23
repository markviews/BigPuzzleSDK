using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleDoor: copies a vanilla door into place.
// Creators set whether it starts open and call Open() and Close().
public enum PuzzleDoorMode { OpenWhenPuzzleActive, CloseWhenPuzzleActive, ControlWithFunctionsOnly }

public class PuzzleDoor : MonoBehaviour {
    public PuzzleDoor(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.Door.ExactPath;

    private const string CopyName = "VanillaDoor";
    private const string LogicName = GameRefs.Door.LogicName;
    private const string PreferredPath = GameRefs.Door.PreferredPath;

    private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
    private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
    private LobbyDoor door;
    private TrackedPeckState doorState;
    private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)
    private bool open;

    // Set in the Inspector; the name must match the Unity-side field. true = the door blocks
    // players' voices while closed (like the game's soundproof rooms) and lets them through open.
    internal bool soundproof;

    // Set in the Inspector. Must stay identical, in the same order, to the Unity SDK's enum.
    // The door follows the puzzle (open or closed while it is on) unless this says functions only.
    internal PuzzleDoorMode mode = PuzzleDoorMode.CloseWhenPuzzleActive;
    private bool modeApplied;
    private bool lastOn;

    // The door's two moving leaves; their colliders slide away when it opens
    private static readonly string[] LeafColliders = GameRefs.Door.LeafColliders;
    private const int VoiceBlockingLayer = 17;   // "Glass": the only layer voices are fully blocked on
    private bool applied;
    private bool appliedOpen;

    // Called by the loader (after fields are assigned) while the puzzle is still inactive
    internal void SetupDoor(int index) {
        var log = Plugin.log;

        GameObject template = Templates.Find(ExactPath);
        if (template == null) {
            // Not at its known path: search every loaded object instead (slow)
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<LobbyDoor>())) {
                LobbyDoor candidate = obj.TryCast<LobbyDoor>();
                if (candidate == null || candidate.name != LogicName) continue;
                Transform parent = candidate.transform.parent;
                if (parent == null) continue;
                string path = PathOf(parent);
                if (path.Contains(CopyName)) continue;   // skip our own copies
                if (template == null) template = parent.gameObject;
                if (path.EndsWith(PreferredPath)) { template = parent.gameObject; break; }
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzleDoor] No vanilla door is loaded, so '{name}' has nothing to copy.");
            return;
        }

        Vector3 templateScale = template.transform.lossyScale;
        GameObject copy = Templates.Clone(template, transform);
        copy.name = CopyName;
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.SetActive(true);

        Vector3 parentScale = transform.lossyScale;
        copy.transform.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? templateScale.x : templateScale.x / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? templateScale.y : templateScale.y / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? templateScale.z : templateScale.z / parentScale.z);


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

        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<LobbyDoor>(), true)) {
            LobbyDoor d = c != null ? c.TryCast<LobbyDoor>() : null;
            if (d == null) continue;
            if (door == null || d.name == LogicName) door = d;
        }
        if (door != null) {
            Component stateComponent = door.gameObject.GetComponent(Il2CppType.Of<TrackedPeckState>());
            doorState = stateComponent != null ? stateComponent.TryCast<TrackedPeckState>() : null;
        } else {
            log.LogWarning($"[PuzzleDoor] The copy for '{name}' has no door logic, so it cannot be opened.");
        }

        // Everything that carries a network id must get its own, or the copy would act on the
        // vanilla door it was copied from.
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckSwitch>(), true)) {
            PeckSwitch sw = c != null ? c.TryCast<PeckSwitch>() : null;
            if (sw == null) continue;
            switches.Add(sw);
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
            TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
            if (st == null) continue;
            states.Add(st);
        }
        ticketIds = Tickets.Allocate(switches.Count + states.Count);
        AssignTickets(false);

        if (soundproof) MakeSoundproof(copy);
    }

    // The door's leaves become voice-blocking: the game blocks voices only at colliders on the
    // Glass layer with a "voiceblocking" material. The leaves move with the door, so it blocks
    // while closed and lets voices through once open.
    private void MakeSoundproof(GameObject copy) {
        PhysicsMaterial mat = PuzzleSoundProof.FindMaterial("audio voiceblocking wood");
        if (mat == null) {
            Plugin.log.LogWarning($"[PuzzleDoor] '{name}': the game's soundproof material wasn't found, so the door can't be made soundproof.");
            return;
        }
        int count = 0;
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Collider>(), true)) {
            Collider col = c != null ? c.TryCast<Collider>() : null;
            if (col == null || col.isTrigger || Array.IndexOf(LeafColliders, col.gameObject.name) < 0) continue;
            col.gameObject.layer = VoiceBlockingLayer;
            col.sharedMaterial = mat;
            count++;
        }
        if (count == 0) Plugin.log.LogWarning($"[PuzzleDoor] '{name}': no door leaf colliders found, so the door isn't soundproof.");
    }

    private static string PathOf(Transform t, Transform stopAt = null) {
        string path = t.name;
        for (Transform p = t.parent; p != null && p != stopAt; p = p.parent) path = p.name + "/" + path;
        return path;
    }

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
            if (current != wanted && tickets.ContainsKey(current) && tickets[current].Pointer == item.Pointer)
                tickets.Remove(current);
            tickets[wanted] = item;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleDoor] id registration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Start() { AssignTickets(true); }

    // The door starts closed. The first frame applies whatever Open()/Close() asked for by then
    // (e.g. from onPuzzleStop or a switch at spawn); after that it follows Open()/Close()
    private void Update() {
        if (door == null) return;
        // Follows the puzzle switching on and off (every PC works it out for itself; Open() and Close() still work in between)
        if (mode != PuzzleDoorMode.ControlWithFunctionsOnly) {
            bool on = PuzzleSDK.IsOnFor(transform);
            if (!modeApplied || on != lastOn) {
                modeApplied = true;
                lastOn = on;
                open = (mode == PuzzleDoorMode.OpenWhenPuzzleActive) == on;
            }
        }
        if (applied && open == appliedOpen) return;
        applied = true;
        appliedOpen = open;
        Apply(open);
    }

    // Going through the door's tracked state runs everything attached to it (animation, sound)
    private void Apply(bool wantOpen) {
        var log = Plugin.log;
        int value = (wantOpen != door.peckAndDoorStatesAreInverse) ? 1 : 0;
        try {
            if (doorState != null) {
                if (PuzzleSDK.IsHost) doorState.SetState(value);   // other players: arrives from the host
                return;
            }
        } catch (Exception ex) {
            log.LogWarning($"[PuzzleDoor] '{name}' could not set the door state ({ex.GetType().Name}: {ex.Message}); moving the door directly.");
        }
        try {
            if (value == 1) door.TestPeck1(); else door.TestPeck0();
        } catch (Exception ex) {
            log.LogWarning($"[PuzzleDoor] '{name}' could not move the door: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- Callable from code or from an Inspector event ----
    // Host only: the door reaches everyone from the host
    public void Open() { if (!PuzzleSDK.IsHost) return; open = true; }
    public void Close() { if (!PuzzleSDK.IsHost) return; open = false; }
}

} // namespace BigPuzzleSDK
