using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleLiveSignal: copies the vanilla "live" signal lamp
// into place. It has no settings or functions; every one in a puzzle lights up while the
// puzzle is running (start switch on) and goes dark when it stops.
public class PuzzleLiveSignal : MonoBehaviour {
    public PuzzleLiveSignal(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.LiveSignal.ExactPath;

    private const string CopyName = "VanillaLiveSignal";
    private const string LogicName = GameRefs.LiveSignal.LogicName;
    private const string PreferredPath = GameRefs.LiveSignal.PreferredPath;
    private const int OnValue = 1;    // state value that lights the lamp (assumed)
    private const int OffValue = 0;

    internal static readonly List<PuzzleLiveSignal> all = new List<PuzzleLiveSignal>();

    private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
    private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
    private TrackedPeckState lampState;
    private Light lamp;
    private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)
    private bool on;
    private bool primed;

    // Called by the loader while the puzzle is still inactive
    internal void SetupSignal(int index) {
        var log = Plugin.log;

        GameObject template = Templates.Find(ExactPath);
        if (template == null) {
            // Not at its known path: search every loaded object instead (slow)
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<TrackedPeckState>())) {
                TrackedPeckState candidate = obj.TryCast<TrackedPeckState>();
                if (candidate == null || candidate.name != LogicName) continue;
                Transform signal = candidate.transform.parent;
                if (signal == null) continue;
                string path = PathOf(signal);
                if (path.Contains(CopyName)) continue;   // skip our own copies
                if (template == null) template = signal.gameObject;
                if (path.EndsWith(PreferredPath)) { template = signal.gameObject; break; }
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzleLiveSignal] No vanilla live signal is loaded, so '{name}' has nothing to copy.");
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
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Light>(), true)) {
            Light l = c != null ? c.TryCast<Light>() : null;
            if (l != null && lamp == null) lamp = l;
        }

        // Hide the creator's placeholder
        Renderer placeholder = GetComponent<Renderer>();
        if (placeholder != null) placeholder.enabled = false;
        Collider placeholderCollider = GetComponent<Collider>();
        if (placeholderCollider != null) placeholderCollider.enabled = false;

        // Everything that carries a network id must get its own, or the copy would act on the
        // vanilla signal it was copied from.
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckSwitch>(), true)) {
            PeckSwitch sw = c != null ? c.TryCast<PeckSwitch>() : null;
            if (sw == null) continue;
            switches.Add(sw);
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
            TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
            if (st == null) continue;
            states.Add(st);
            if (st.name == LogicName) lampState = st;
        }
        if (lampState == null && states.Count > 0) lampState = states[0];
        ticketIds = Tickets.Allocate(switches.Count + states.Count);
        AssignTickets(false);

        if (lampState == null) log.LogWarning($"[PuzzleLiveSignal] The copy for '{name}' has no state to drive; only its light will be switched.");
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
            Plugin.log.LogWarning($"[PuzzleLiveSignal] id registration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Start() {
        all.Add(this);
        AssignTickets(true);
    }

    private void OnDestroy() { all.Remove(this); }

    // On the first frame, make sure the lamp starts dark whatever the vanilla one was showing
    private void Update() {
        if (primed) return;
        primed = true;
        Apply(on, true);
    }

    // Lights the lamp or puts it out. Called by the puzzle's main class on start and stop.
    internal void SetOn(bool value) {
        if (on == value && primed) return;
        on = value;
        if (primed) Apply(value, false);
    }

    private void Apply(bool value, bool quiet) {
        var log = Plugin.log;
        try {
            if (lampState != null) { if (PuzzleSDK.IsHost) lampState.SetState(value ? OnValue : OffValue); }   // other players: arrives from the host
            else if (lamp != null) lamp.enabled = value;
        } catch (Exception ex) {
            log.LogWarning($"[PuzzleLiveSignal] '{name}' could not turn {(value ? "on" : "off")}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

} // namespace BigPuzzleSDK
