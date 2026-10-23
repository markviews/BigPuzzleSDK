using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleSimPressSwitch: copies the vanilla sim-press lever
// (from the "press together" challenge) into place.
//   off (armed) --press--> active (yellow) for activeSeconds --> timeout (red) for timeoutSeconds --> off
// Switches in the same puzzle with the same `group` are linked: when every one of them is active
// at the same moment, they all turn green and fire onSuccess. Green also lasts timeoutSeconds,
// then the switch is off (armed) again, like the game's lever. Nothing can cut a wait short:
// there is deliberately no way to re-arm it from code.
// Multiplayer: everything runs on the host. A press by any player sets the vanilla lever's press
// state on the host, which the host hears about (SyncedValue.WatchOnHost) and runs the switch's
// logic for; the lights then reach every player through the game's state sync. The lever's own
// timers move it from yellow to red to off, using the creator's activeSeconds/timeoutSeconds.
public class PuzzleSimPressSwitch : MonoBehaviour {
    public PuzzleSimPressSwitch(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.SimPressSwitch.ExactPath;
    // The same object in 3- and 2-player lobbies (the 4-player challenges aren't loaded there)
    private const string ExactPath3 = GameRefs.SimPressSwitch.ExactPath3;
    private const string ExactPath2 = GameRefs.SimPressSwitch.ExactPath2;

    private const string PreferredPath = GameRefs.SimPressSwitch.PreferredPath;
    private const string CopyName = "VanillaSimPressSwitch";

    // Values for the vanilla light state: 0 = all lamps off, 1 = yellow, 2 = red, 3 = green.
    private const int StateOff = 0;
    private const int StateActive = 1;
    private const int StateTimeout = 2;
    private const int StateSuccess = 3;

    private enum Phase { Off, Active, Timeout, Success }

    // Set in the Inspector (filled in by the loader); the names must match the Unity-side fields
    private float activeSeconds = 2f;    // yellow: the window in which the others must also be pressed
    private float timeoutSeconds = 2f;   // red: how long before it can be pressed again
    private string group = "";           // switches in the same puzzle with the same group are linked

    // Filled in by the loader from the UnityEvents wired up in the Inspector
    private PuzzleEvent onSuccess;

    internal static readonly List<PuzzleSimPressSwitch> all = new List<PuzzleSimPressSwitch>();

    private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
    private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
    private TrackedPeckState pressSystem;
    private TrackedPeckState stateSystem;
    private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)
    private bool primed;
    private Phase phase = Phase.Off;
    private float phaseEndsAt;

    // Called by the loader while the puzzle is still inactive
    internal void SetupSwitch(int index) {
        var log = Plugin.log;

        GameObject template = Templates.Find(ExactPath, ExactPath3, ExactPath2);
        if (template == null) {
            // Not at its known path: search every loaded object instead (slow)
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<SimPressSwitch>())) {
                SimPressSwitch candidate = obj.TryCast<SimPressSwitch>();
                if (candidate == null) continue;
                string path = PathOf(candidate.transform);
                if (path.Contains(CopyName)) continue;   // skip our own copies
                if (template == null) template = candidate.gameObject;
                if (path.EndsWith(PreferredPath)) { template = candidate.gameObject; break; }
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzleSimPressSwitch] No vanilla sim-press switch is loaded, so '{name}' has nothing to copy.");
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

        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
            Renderer r = c != null ? c.TryCast<Renderer>() : null;
            if (r != null) r.enabled = true;
        }
        Renderer placeholder = GetComponent<Renderer>();
        if (placeholder != null) placeholder.enabled = false;
        Collider placeholderCollider = GetComponent<Collider>();
        if (placeholderCollider != null) placeholderCollider.enabled = false;

        SimPressSwitch vanilla = copy.GetComponent<SimPressSwitch>();
        if (vanilla != null) {
            pressSystem = vanilla.pressSystem;
            stateSystem = vanilla.stateSystem;
        }

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
        if (switches.Count + states.Count > 16)
            log.LogWarning($"[PuzzleSimPressSwitch] '{name}' needs {switches.Count + states.Count} ticket ids but only has 16.");
        ticketIds = Tickets.Allocate(switches.Count + states.Count);
        AssignTickets(false);

        // The lever runs its own timers on the light state: active (1) -> cooldown (2) after one,
        // cooldown -> armed (0) after the other. Use those (they also drive the red light) with
        // the creator's durations.
        if (stateSystem != null) {
            foreach (Component c in stateSystem.gameObject.GetComponents(Il2CppType.Of<PeckEffectTimer>())) {
                PeckEffectTimer timer = c != null ? c.TryCast<PeckEffectTimer>() : null;
                if (timer == null) continue;
                try {
                    PeckSwitch next = timer.onFinish;
                    int to = next != null ? next.specificState : -1;
                    if (to == StateTimeout) timer.duration = Mathf.Max(0.05f, activeSeconds);
                    // The cooldown timer runs in red and in green (success), turning either back to armed
                    else if (to == StateOff) timer.duration = Mathf.Max(0.05f, timeoutSeconds);
                } catch (Exception ex) { log.LogWarning($"[PuzzleSimPressSwitch] '{name}' could not read a timer: {ex.Message}"); }
            }
        }

        if (pressSystem == null)
            log.LogWarning($"[PuzzleSimPressSwitch] '{name}': no press state found, so presses can't be detected.");
        if (stateSystem == null)
            log.LogWarning($"[PuzzleSimPressSwitch] '{name}': no state system found, so the lights can't change.");
    }

    private static string PathOf(Transform t) {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
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
        } catch (Exception) { }
    }

    private void Start() {
        AssignTickets(true);
        // Host: a press by any player arrives as a change of the lever's press state
        SyncedValue.WatchOnHost(pressSystem, OnPressState);
        // Host: the lever's own timers move the lights on; keep our phase in step with them
        SyncedValue.WatchOnHost(stateSystem, OnLightsState);
    }

    private void OnLightsState(int value) {
        Phase now = value == StateActive ? Phase.Active : value == StateTimeout ? Phase.Timeout
                  : value == StateSuccess ? Phase.Success : value == StateOff ? Phase.Off : phase;
        if (now == phase) return;
        phase = now;
        if (phase == Phase.Active) phaseEndsAt = Time.time + Mathf.Max(0f, activeSeconds);
        else if (phase == Phase.Timeout || phase == Phase.Success) phaseEndsAt = Time.time + Mathf.Max(0f, timeoutSeconds);
    }
    private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    private void OnDestroy() { all.Remove(this); }

    // Host: the lever's press state was applied. Pressing the lever applies the same value (0)
    // again, so every application is a press; we never set this state ourselves. Pressed()
    // ignores presses while the switch isn't armed (one press arrives as several applications).
    private void OnPressState(int value) {
        Pressed();
    }

    private void Update() {
        // Other players: the host runs the switch; its lights arrive through the game's state sync
        if (!SyncedValue.IsHost) return;

        // First frame: put the lever in its starting state
        if (!primed) {
            primed = true;
            EnterOff();
            return;
        }
        // The lever's own timers do this; these only step in if they didn't (one second late)
        if (phase == Phase.Active && Time.time >= phaseEndsAt + 1f) EnterTimeout();
        else if ((phase == Phase.Timeout || phase == Phase.Success) && Time.time >= phaseEndsAt + 1f) EnterOff();
    }

    private void EnterOff() {
        phase = Phase.Off;
        SetSystem(stateSystem, StateOff);
    }

    private void EnterActive() {
        phase = Phase.Active;
        phaseEndsAt = Time.time + Mathf.Max(0f, activeSeconds);
        SetSystem(stateSystem, StateActive);
    }

    private void EnterTimeout() {
        phase = Phase.Timeout;
        phaseEndsAt = Time.time + Mathf.Max(0f, timeoutSeconds);
        SetSystem(stateSystem, StateTimeout);
    }

    private void EnterSuccess() {
        phase = Phase.Success;
        phaseEndsAt = Time.time + Mathf.Max(0f, timeoutSeconds);
        SetSystem(stateSystem, StateSuccess);
    }

    private void SetSystem(TrackedPeckState st, int value) {
        if (st == null) return;
        try { st.SetState(value); }
        catch (Exception ex) { Plugin.log.LogWarning($"[PuzzleSimPressSwitch] '{name}' could not set '{st.name}' to {value}: {ex.GetType().Name}: {ex.Message}"); }
    }

    private bool SameGroup(PuzzleSimPressSwitch other) {
        return other != null
            && other.transform.root.GetInstanceID() == transform.root.GetInstanceID()
            && (other.group ?? "") == (group ?? "");
    }

    // Host: a player pressed the lever
    internal void Pressed() {
        if (phase != Phase.Off) return;
        EnterActive();
        CheckGroup();
    }

    // If every switch in the group is active right now, they all turn green together
    private void CheckGroup() {
        var members = new List<PuzzleSimPressSwitch>();
        foreach (PuzzleSimPressSwitch other in all.ToArray()) {
            if (!SameGroup(other)) continue;
            if (other.phase != Phase.Active) return;
            members.Add(other);
        }
        if (members.Count == 0) return;
        foreach (PuzzleSimPressSwitch m in members) m.EnterSuccess();
        foreach (PuzzleSimPressSwitch m in members)
            if (m.onSuccess != null) m.onSuccess.Invoke();
    }
}

} // namespace BigPuzzleSDK
