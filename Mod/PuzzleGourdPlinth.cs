using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleGourdPlinth: copies a vanilla gourd plinth (one slot of
// a gourd tower) into place. Like PuzzleBox: Open() stays open until Close(); HoldToOpen() from a
// hold button's onPressed stays open only while that button is held. Once its flap is fully open, players can place a gourd in
// it; onGourdPlaced fires when one goes in (a placed gourd can't be taken back out).
public class PuzzleGourdPlinth : MonoBehaviour {
    public PuzzleGourdPlinth(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.GourdPlinth.ExactPath;

    private const string CopyName = "VanillaGourdPlinth";
    private const string TemplatePrefix = GameRefs.GourdPlinth.TemplatePrefix;
    private const string PinName = GameRefs.GourdPlinth.PinName;

    // The flap's states, under GourdPin. 'Buttoned' is what the vanilla hold button drives (the
    // flap then opens by itself and the slot accepts a gourd once it is fully open); 'open' is the
    // flap itself, used only if the first is missing.
    private const string ButtonedStateName = GameRefs.GourdPlinth.ButtonedStateName;
    private const string OpenStateName = GameRefs.GourdPlinth.OpenStateName;

    // Parts of the vanilla plinth that report to the game's own progress (the gourd map / tower
    // count). Removed from the copy so a gourd placed here never counts as a vanilla tower gourd.
    private static readonly string[] RemovedComponents = GameRefs.GourdPlinth.RemovedComponents;

    internal static readonly List<PuzzleGourdPlinth> all = new List<PuzzleGourdPlinth>();

    // Whether the flap should be open; changed only through Open() / HoldToOpen() / Close()
    private bool open;

    // Whether a gourd sits in the slot right now
    private bool hasGourd;

    // Read-only for scripts
    public bool IsOpen => open;
    public bool HasGourd => hasGourd;

    // Filled in by the loader from the UnityEvents wired up in the Inspector
    private PuzzleEvent onGourdPlaced;

    private TrackedPeckState buttonedState;
    private TrackedPeckState flapState;
    private PropHome home;
    private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
    private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
    private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)
    private bool applied, appliedOpen;
    private global::PuzzleButton heldBy;   // the hold button that opened it; closes when let go
    private float nextCheck;

    // Called by the loader while the puzzle is still inactive
    internal void SetupPlinth(int index) {
        var log = Plugin.log;

        GameObject template = Templates.Find(ExactPath);
        if (template == null) {
            // Not at its known path: search every loaded object instead (slow). A plinth is the
            // object above a 'GourdPin' that holds a gourd home.
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PropHome>())) {
                PropHome candidate = obj.TryCast<PropHome>();
                if (candidate == null) continue;
                Transform pin = candidate.transform.parent;
                Transform plinth = pin != null ? pin.parent : null;
                if (pin == null || pin.name != PinName || plinth == null || !plinth.name.StartsWith(TemplatePrefix)) continue;
                if (Templates.FullPath(plinth).Contains(CopyName)) continue;   // skip our own copies
                template = plinth.gameObject;
                break;
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzleGourdPlinth] No vanilla gourd plinth is loaded, so '{name}' has nothing to copy.");
            return;
        }

        Vector3 templateScale = template.transform.lossyScale;
        GameObject copy = Templates.Clone(template, transform);
        copy.name = CopyName;
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.SetActive(true);

        // Keep the vanilla plinth's own world size whatever the creator's objects are scaled to
        Vector3 parentScale = transform.lossyScale;
        copy.transform.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? templateScale.x : templateScale.x / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? templateScale.y : templateScale.y / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? templateScale.z : templateScale.z / parentScale.z);

        // Cut the copy off from the game's own gourd progress before it wakes
        var doomed = new List<Component>();
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Component>(), true))
            if (c != null && Array.IndexOf(RemovedComponents, c.GetIl2CppType().Name) >= 0) doomed.Add(c);
        foreach (Component c in doomed) UnityEngine.Object.DestroyImmediate(c);

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

        // Everything that carries a network id gets its own, so the copy never acts on the vanilla
        // plinth it was copied from. The gourd slot comes last.
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckSwitch>(), true)) {
            PeckSwitch sw = c != null ? c.TryCast<PeckSwitch>() : null;
            if (sw != null) switches.Add(sw);
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
            TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
            if (st == null) continue;
            states.Add(st);
            if (st.name == ButtonedStateName) buttonedState = st;
            if (st.name == OpenStateName) flapState = st;
        }
        Component homeComponent = copy.GetComponentInChildren(Il2CppType.Of<PropHome>(), true);
        home = homeComponent != null ? homeComponent.TryCast<PropHome>() : null;

        ticketIds = Tickets.Allocate(switches.Count + states.Count + (home != null ? 1 : 0));
        if (home != null) {
            try {
                // Its own id, empty, and never saved as the vanilla tower's slot
                ushort homeTicket = ticketIds[switches.Count + states.Count];
                home._ticket_k__BackingField = homeTicket;
                home.shellReference = new SeaShell.ShellReference(homeTicket);
                home.pinnedProp = null;
                home.saveableHomeName = default(SaveableHomeName);
                home.keepSavedWhenLeavingHome = false;
                ModGourd.MakeModdedHome(home);   // modded gourds only (a vanilla one would save a modded location)
            } catch (Exception ex) { log.LogWarning($"[PuzzleGourdPlinth] '{name}': slot setup failed: {ex.GetType().Name}: {ex.Message}"); }
        } else {
            log.LogWarning($"[PuzzleGourdPlinth] '{name}': the copy has no gourd slot.");
        }
        AssignTickets(false);

        if (buttonedState == null && flapState == null)
            log.LogWarning($"[PuzzleGourdPlinth] '{name}': no flap state found, so it cannot open.");
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
            Plugin.log.LogWarning($"[PuzzleGourdPlinth] id registration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Start() {
        all.Add(this);
        AssignTickets(true);
    }

    private void Update() {
        if (buttonedState == null && flapState == null) return;

        // Opened by a hold button: close again as soon as nobody is holding it
        if (heldBy != null && (!open || !heldBy.IsHeld)) {
            open = false;
            heldBy = null;
        }

        // Open or close when 'open' changes. Nothing to do on the first frame if it starts closed.
        if (!applied || open != appliedOpen) {
            bool first = !applied;
            applied = true;
            appliedOpen = open;
            if (!(first && !open)) Apply(open);
        }

        // Watch the slot a few times a second until a gourd goes in
        if (hasGourd || home == null || Time.time < nextCheck) return;
        nextCheck = Time.time + 0.2f;
        bool now = false;
        try { now = home.pinnedProp != null; } catch (Exception) { }
        if (!now || hasGourd) return;
        hasGourd = true;
        if (onGourdPlaced != null) onGourdPlaced.Invoke();
    }

    // Opens or closes the flap through the game's own states, so the animation, sounds and the
    // "slot accepts a gourd once fully open" rule all come from the vanilla plinth
    private void Apply(bool wantOpen) {
        TrackedPeckState target = buttonedState != null ? buttonedState : flapState;
        try {
            if (PuzzleSDK.IsHost) target.SetState(wantOpen ? 1 : 0);   // other players: arrives from the host
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleGourdPlinth] '{name}' could not {(wantOpen ? "open" : "close")}: {ex.GetType().Name}: {ex.Message}");
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
            Plugin.log.LogWarning($"[PuzzleGourdPlinth] '{name}': HoldToOpen() was not called from a hold button's onPressed, so it stays open until Close().");
        }
    }
    private bool warnedHold;

    // The gourd slot, and marking it filled without onGourdPlaced (a save putting a gourd back)
    internal PropHome Home => home;
    internal void MarkFilled() { hasGourd = true; }

    // TowerButton: open while that button is held (host only)
    internal void HoldOpenFor(global::PuzzleButton by) {
        if (!PuzzleSDK.IsHost || by == null) return;
        open = true;
        heldBy = by;
    }

    public void Close() { if (!PuzzleSDK.IsHost) return; open = false; heldBy = null; }

    private void OnDestroy() { all.Remove(this); }
}

} // namespace BigPuzzleSDK
