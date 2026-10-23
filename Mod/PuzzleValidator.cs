using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleValidator: copies only the vanilla validator screen
// (the one next to the peg tile validator in the Teller Window), without its button or its
// peg-tile logic. The puzzle decides what "correct" means: call SetCorrect(bool) whenever the
// answer changes, then Check() shows a tick or a cross, then wins the puzzle (PuzzleSDK.Win) if right or fires onIncorrect if wrong.
public class PuzzleValidator : MonoBehaviour {
    public PuzzleValidator(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.Validator.ExactPath;

    private const string ScreenName = GameRefs.Validator.ScreenName;
    private const string PreferredPath = GameRefs.Validator.PreferredPath;
    private const string CopyName = "VanillaValidatorScreen";

    // The screen's state values, from the vanilla ValidatorDisplay.state enum:
    // Off = 0, Blank = 1 (static), Success = 2, Failure = 3, Thinking = 4 (moving dots)
    private const int StateOff = 0;
    private const int StateBlank = 1;
    private const int StateChecking = 4;
    private const int StateSuccess = 2;
    private const int StateFail = 3;

    // Changed only through SetCorrect() and TurnOn() / TurnOff()
    private bool correct;
    private bool active = true;         // false: screen off and Check() does nothing; true: blank screen, ready
    // What TurnOn() / TurnOff() asked for last. The screen is only on while this is true AND the puzzle is
    // switched on, so it can be kept up to date silently while the puzzle is off and comes on with it.
    private bool desired = true;
    private bool Wanted() { return desired && PuzzleSDK.IsOnFor(transform); }

    // Read-only for scripts
    public bool IsCorrect => correct;
    public bool IsOn => active;

    private const float CheckSeconds = 5f;   // how long the "thinking" animation plays before the result

    private float resultAt;             // when the pending result shows; 0 = none

    // Filled in by the loader from the UnityEvents wired up in the Inspector
    private PuzzleEvent onIncorrect;

    // Set in the Inspector (optional; the name must match the Unity-side field): pressing it calls
    // Check(), and it is switched on and off along with the screen
    private PuzzlePokeButton pokeButton;

    private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
    private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
    private TrackedPeckState screenState;
    private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)
    private bool primed;

    // Called by the loader while the puzzle is still inactive
    internal void SetupValidator(int index) {
        var log = Plugin.log;

        GameObject template = Templates.Find(ExactPath);
        if (template == null) {
            // Not at its known path: search every loaded object instead (slow)
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<ValidatorDisplay>())) {
                ValidatorDisplay candidate = obj.TryCast<ValidatorDisplay>();
                if (candidate == null || !NameMatches(candidate.name, ScreenName)) continue;
                string path = PathOf(candidate.transform);
                if (path.Contains(CopyName)) continue;   // skip our own copies
                if (template == null) template = candidate.gameObject;
                if (path.EndsWith(PreferredPath)) { template = candidate.gameObject; break; }
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzleValidator] No vanilla validator screen is loaded, so '{name}' has nothing to copy.");
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

        screenState = copy.GetComponent<TrackedPeckState>();

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
        if (screenState == null)
            log.LogWarning($"[PuzzleValidator] '{name}': the screen has no state, so Check() can only fire the events.");

        ticketIds = Tickets.Allocate(switches.Count + states.Count);
        AssignTickets(false);
    }

    private static bool NameMatches(string actual, string wanted) {
        return actual == wanted || actual.StartsWith(wanted + " (");
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
        if (pokeButton != null) {
            pokeButton.autoEnableSeconds = 0f;   // the screen re-arms the button itself
            pokeButton.pressedHook += Check;   // runs on the host, like Check()
        }
    }

    private void Update() {
        // First frame: the screen starts off or blank
        if (!primed) {
            primed = true;
            active = Wanted();
            SetScreen(active ? StateBlank : StateOff);
            if (pokeButton != null) pokeButton.SetClickable(active);
        } else if (PuzzleSDK.IsHost) {
            // The puzzle was switched on or off
            bool want = Wanted();
            if (want != active) Apply(want);
        }

        if (resultAt > 0f && Time.time >= resultAt) {
            resultAt = 0f;
            ShowResult();
        }
    }

    private void SetScreen(int value) {
        if (screenState == null) return;
        if (!PuzzleSDK.IsHost) return;   // other players: the screen arrives from the host
        try { screenState.SetState(value); }
        catch (Exception ex) { Plugin.log.LogWarning($"[PuzzleValidator] '{name}' could not set the screen to {value}: {ex.GetType().Name}: {ex.Message}"); }
    }

    // ---- Callable from code or from an Inspector event ----
    // Tells the validator whether the current answer is right. Nothing shows until Check().
    // Host only (the host shows the result and fires the events).
    public void SetCorrect(bool value) {
        if (!PuzzleSDK.IsHost) return;
        correct = value;
    }

    // Turns the screen on: blank, ready to check
    // (Only shows while the puzzle is switched on; until then it is remembered and shown when the puzzle starts.)
    public void TurnOn() {
        if (!PuzzleSDK.IsHost) return;
        desired = true;
        if (Wanted()) Apply(true);
    }

    // Turns the screen off (cancels a check in progress); Check() does nothing while off
    public void TurnOff() {
        if (!PuzzleSDK.IsHost) return;
        desired = false;
        if (active) Apply(false);
    }

    private void Apply(bool value) {
        active = value;
        resultAt = 0f;
        if (primed) SetScreen(value ? StateBlank : StateOff);
        if (pokeButton != null) pokeButton.SetClickable(value);
    }

    // Plays the "checking" animation, then shows the result, then wins the puzzle (right) or fires onIncorrect (wrong).
    // The answer is read when the result shows, so SetCorrect during the animation still counts.
    public void Check() {
        if (!PuzzleSDK.IsHost) return;
        if (!active) {
            return;
        }
        if (resultAt > 0f) return;   // already checking
        SetScreen(StateChecking);
        resultAt = Time.time + CheckSeconds;
    }

    private void ShowResult() {
        SetScreen(correct ? StateSuccess : StateFail);
        if (correct) { PuzzleSDK puzzle = PuzzleSDK.Of(transform); if (puzzle != null) puzzle.Win(); }
        else { if (onIncorrect != null) onIncorrect.Invoke(); }
        // A press switches the button off by itself: ready for another try, unless the events above turned the screen off
        if (pokeButton != null && active) pokeButton.SetClickable(true);
    }
}

} // namespace BigPuzzleSDK
