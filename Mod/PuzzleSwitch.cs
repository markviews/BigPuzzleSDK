using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleSwitch: copies a vanilla light switch into place.
// It is a general on/off switch: players flip it, and the creator reacts through the two
// events or reads 'on'.
public class PuzzleSwitch : MonoBehaviour {
    public PuzzleSwitch(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.Switch.ExactPath;

    private const string CopyName = "VanillaSwitch";
    private const string TemplateName = GameRefs.Switch.TemplateName;
    private const string PreferredPath = GameRefs.Switch.PreferredPath;

    internal bool on;   // true while the switch is flipped on

    // Read-only for scripts
    public bool IsOn => on;

    internal static readonly List<PuzzleSwitch> all = new List<PuzzleSwitch>();

    // True once the switch has settled in its starting position, so 'IsOn' can be trusted (saving waits for it)
    internal bool Settled => initialFired;

    private TrackedPeckState switchState;   // the game's record of this switch's position
    private float quietUntil;               // while moving to its starting position, fire no events

    // Filled in by the loader from the UnityEvents wired up in the Inspector.
    // The names must match the Unity-side fields.
    private PuzzleEvent onTurnedOn;
    private PuzzleEvent onTurnedOff;

    private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
    private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
    private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)
    private Transform copyRoot;
    private bool started;             // the starting position has been put in place
    private int offValue;             // the state value that means "off"
    private float startAt;
    private bool initialFired;        // the event for the starting position has been fired
    private float initialFireAt;      // when to fire it (after the switch has settled)

    // Called by the loader while the puzzle is still inactive
    internal void SetupSwitch(int index) {
        var log = Plugin.log;

        GameObject template = Templates.Find(ExactPath);
        if (template == null) {
            // Not at its known path: search every loaded object instead (slow)
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PeckSwitch>())) {
                PeckSwitch candidate = obj.TryCast<PeckSwitch>();
                if (candidate == null) continue;
                Transform knob = candidate.transform.parent;
                if (knob == null || knob.name != TemplateName) continue;
                string path = PathOf(knob);
                if (path.Contains(CopyName)) continue;   // skip our own copies
                if (template == null) template = knob.gameObject;
                if (path.EndsWith(PreferredPath)) { template = knob.gameObject; break; }
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzleSwitch] No vanilla light switch is loaded, so '{name}' has nothing to copy.");
            return;
        }

        Vector3 templateScale = template.transform.lossyScale;
        GameObject copy = Templates.Clone(template, transform);
        copy.name = CopyName;
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.SetActive(true);
        copyRoot = copy.transform;

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

        // Everything that carries a network id must get its own, or the copy would flip the
        // vanilla switch it was copied from.
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckSwitch>(), true)) {
            PeckSwitch sw = c != null ? c.TryCast<PeckSwitch>() : null;
            if (sw == null) continue;
            switches.Add(sw);
            TrackedPeckState target = sw.trackedStateSystem;
            if (switchState == null && target != null && sw.GetComponent<CastableTarget>() != null) switchState = target;
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
            TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
            if (st == null) continue;
            states.Add(st);
        }
        if (switchState == null && states.Count > 0) switchState = states[0];

        // The vanilla switch remembers its saved position as its "initial state" (on, if that
        // player left it flipped on). The copy would start from it and read it as "off", so the
        // copy forgets it: 0 is always off.
        foreach (TrackedPeckState st in states) {
            st.hasInitialState = false;
            st.initialState = 0;
        }

        ticketIds = Tickets.Allocate(switches.Count + states.Count);
        AssignTickets(false);
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
            Plugin.log.LogWarning($"[PuzzleSwitch] id registration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnDestroy() { all.Remove(this); }

    private void Start() {
        all.Add(this);
        AssignTickets(true);
        startAt = Time.time + 0.5f;   // let the copy finish waking up first
    }

    // The switch's position is its state's value: 'offValue' is off, anything else is on.
    // (It used to be read from how the switch looked, which went backwards whenever the vanilla
    // switch it was copied from was flipped on in that player's world.)
    private bool ReadOn() {
        int value;
        return BigPuzzleSDK.PeckValue.TryRead(switchState, out value) && value != offValue;
    }

    // Puts the switch in its starting position: off, unless the save says the host left it on. The copy starts out looking like the vanilla
    // switch it was copied from, which may be flipped either way in this player's world, so the
    // position is always applied, not just when it differs.
    private void PlaceAtStart() {
        quietUntil = Time.time + 2f;
        if (switchState == null) {
            Plugin.log.LogWarning($"[PuzzleSwitch] '{name}' has no state, so its position cannot be read.");
            return;
        }
        offValue = 0;
        int startValue = PuzzleSave.SwitchWasOn(this) ? (offValue == 0 ? 1 : 0) : offValue;
        try {
            if (SyncedValue.IsHost) {
                bool repeats = switchState.ignoreStateRepeats;
                switchState.ignoreStateRepeats = false;   // apply it even if the value already matches
                switchState.SetState(startValue);
                switchState.ignoreStateRepeats = repeats;
            } else {
                // Other players may not set network states: make this PC's copy look right for
                // the value it holds; the host's value then arrives (or catches up) as usual
                int current;
                if (!BigPuzzleSDK.PeckValue.TryRead(switchState, out current)) current = offValue;
                var ctx = new PeckContext();
                ctx.state = current;
                switchState.DoPeck(ctx, false);
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleSwitch] '{name}' could not set its starting position: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    private void Update() {
        if (copyRoot == null) return;

        if (!started) {
            if (Time.time < startAt) return;
            started = true;
            PlaceAtStart();
            initialFireAt = quietUntil;
            return;
        }

        bool nowOn = ReadOn();
        bool changed = nowOn != on;
        on = nowOn;

        // Once settled in its starting position, fire the event for that position once, so
        // everything wired to the switch starts in step with it. Every PC does this for itself
        // (players who join later included); later flips come from the host.
        if (!initialFired) {
            if (Time.time < initialFireAt) {
                return;
            }
            initialFired = true;
            if (on) { if (onTurnedOn != null) onTurnedOn.InvokeLocal(); }
            else { if (onTurnedOff != null) onTurnedOff.InvokeLocal(); }
            return;
        }
        if (!changed) return;

        if (on) {
            if (onTurnedOn != null) onTurnedOn.Invoke();
        } else {
            if (onTurnedOff != null) onTurnedOff.Invoke();
        }
    }
}

} // namespace BigPuzzleSDK
