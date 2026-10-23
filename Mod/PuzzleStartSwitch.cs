using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleStartSwitch: copies a vanilla knob switch into place.
// Flipping it away from its default position starts the puzzle; flipping it back stops it.
// It has no events or functions of its own: a puzzle can have several, they all stay in the
// same position, and what happens on start and stop is set up on PuzzleSDK.
public class PuzzleStartSwitch : MonoBehaviour {
    public PuzzleStartSwitch(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.StartSwitch.ExactPath;

    private const string CopyName = "VanillaStartSwitch";
    private const string PreferredPath = GameRefs.StartSwitch.PreferredPath;

    internal bool on;   // true while the switch is flipped away from its default position

    internal static readonly List<PuzzleStartSwitch> all = new List<PuzzleStartSwitch>();

    private TrackedPeckState knobState;   // the game's record of this knob's position
    private float settleUntil;            // while another switch is moving this one, don't react

    private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
    private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
    private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)
    private GameObject knobOn;        // 'knobState1' visual
    private GameObject knobOff;       // 'knobState0' visual
    private bool defaultKnown;
    private bool defaultVisual;       // whether knobState1 is shown in the default position
    private float readDefaultAt;

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
                if (knob == null || !knob.name.StartsWith("BasicKnob")) continue;
                string path = PathOf(knob);
                if (path.Contains(CopyName)) continue;   // skip our own copies
                if (template == null) template = knob.gameObject;
                if (path.EndsWith(PreferredPath)) { template = knob.gameObject; break; }
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzleStartSwitch] No vanilla knob switch is loaded, so '{name}' has nothing to copy.");
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

        foreach (Component t in copy.GetComponentsInChildren(Il2CppType.Of<Transform>(), true)) {
            if (t.name == "knobState1") knobOn = t.gameObject;
            if (t.name == "knobState0") knobOff = t.gameObject;
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

        // Everything that carries a network id must get its own, or the copy would flip the
        // vanilla switch it was copied from.
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckSwitch>(), true)) {
            PeckSwitch sw = c != null ? c.TryCast<PeckSwitch>() : null;
            if (sw == null) continue;
            switches.Add(sw);
            TrackedPeckState target = sw.trackedStateSystem;
            if (knobState == null && target != null && sw.GetComponent<CastableTarget>() != null) knobState = target;
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
            TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
            if (st == null) continue;
            states.Add(st);
        }
        if (knobState == null && states.Count > 0) knobState = states[0];
        ticketIds = Tickets.Allocate(switches.Count + states.Count);
        AssignTickets(false);

        if (knobOn == null) log.LogWarning($"[PuzzleStartSwitch] The copy for '{name}' has no 'knobState1', so flips cannot be detected.");
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
            Plugin.log.LogWarning($"[PuzzleStartSwitch] id registration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnDestroy() { all.Remove(this); }

    private void Start() {
        all.Add(this);
        AssignTickets(true);
        readDefaultAt = Time.time + 0.5f;   // let the game put the knob in its default position first
    }

    // The game shows 'knobState1' or 'knobState0' depending on the switch's position, so the
    // position is read from which of the two is showing.
    private void Update() {
        if (knobOn == null) return;
        bool visual = knobOn.activeSelf;

        if (!defaultKnown) {
            if (Time.time < readDefaultAt) return;
            defaultKnown = true;
            defaultVisual = visual;
            return;
        }

        bool nowOn = visual != defaultVisual;
        if (Time.time < settleUntil) return;   // being moved by another switch
        if (nowOn == on) { settleUntil = 0f; return; }

        if (settleUntil > 0f) {
            // Another switch tried to move this one and it did not follow
            settleUntil = 0f;
            Plugin.log.LogWarning($"[PuzzleStartSwitch] '{name}' did not follow the other switch (it is {(nowOn ? "on" : "off")})");
            on = nowOn;
            return;
        }
        on = nowOn;

        // A player flipped this one: move every other start switch in the puzzle to match
        int myRoot = transform.root.GetInstanceID();
        int moved = 0;
        foreach (PuzzleStartSwitch other in all.ToArray()) {
            if (other == null || other.Pointer == Pointer || other.transform.root.GetInstanceID() != myRoot) continue;
            other.Follow(on);
            moved++;
        }

        PuzzleSDK puzzle = FindPuzzle();
        if (puzzle == null) { Plugin.log.LogWarning($"[PuzzleStartSwitch] '{name}' found no PuzzleSDK in its puzzle"); return; }
        if (on) puzzle.StartPuzzle(); else puzzle.StopPuzzle();
    }

    // Moves this switch to match one a player flipped, without starting or stopping anything
    private void Follow(bool wantOn) {
        if (!defaultKnown || on == wantOn) return;
        on = wantOn;
        settleUntil = Time.time + 0.75f;
        if (knobState == null) return;
        try {
            int defaultValue = knobState.hasInitialState ? knobState.initialState : 0;
            int onValue = defaultValue == 0 ? 1 : 0;
            if (PuzzleSDK.IsHost) knobState.SetState(wantOn ? onValue : defaultValue);   // other players: arrives from the host
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleStartSwitch] '{name}' could not follow: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private PuzzleSDK FindPuzzle() {
        int myRoot = transform.root.GetInstanceID();
        foreach (PuzzleSDK puzzle in PuzzleSDK.all)
            if (puzzle != null && puzzle.transform.root.GetInstanceID() == myRoot) return puzzle;
        return null;
    }
}

} // namespace BigPuzzleSDK
