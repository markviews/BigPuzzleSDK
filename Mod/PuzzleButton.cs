using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

// Runtime (mod) version of the SDK's PuzzleButton. The Unity-side version only holds the
// Inspector fields; this one copies a vanilla button into place and reports press and release.
public class PuzzleButton : BigPuzzleSDK.PuzzleButtonBase {
    public PuzzleButton(IntPtr ptr) : base(ptr) { }

    private const string TemplatePath = BigPuzzleSDK.GameRefs.Button.TemplatePath;

    internal static readonly Dictionary<int, PuzzleButton> byTarget = new Dictionary<int, PuzzleButton>();

    // Filled in by the loader from the UnityEvents the creator wired up in the Inspector.
    // The names must match the Unity-side fields.
    private BigPuzzleSDK.PuzzleEvent onPressed;
    private BigPuzzleSDK.PuzzleEvent onReleased;

    private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
    private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
    private PeckSwitch mainSwitch;
    private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)
    private int targetId;
    private bool wasHeld;


    // The button whose onPressed is running right now, so whatever it calls (e.g. a gourd
    // plinth's Open) can tell it came from a hold and close again when it is let go
    internal static PuzzleButton pressing;

    // Whether a player is holding the button down
    internal bool IsHeld { get { return wasHeld; } }

    // Called by the loader while the puzzle is still inactive
    internal void SetupInteraction(int index) {
        var log = BigPuzzleSDK.Plugin.log;

        GameObject template = BigPuzzleSDK.Templates.Find(TemplatePath);
        if (template == null) {
            log.LogWarning($"[PuzzleButton] Vanilla button not found at '{TemplatePath}' (is that area loaded?)");
            return;
        }

        // Copy the vanilla button under this (still inactive) object
        GameObject copy = BigPuzzleSDK.Templates.Clone(template, transform);
        copy.name = "VanillaButton";
        copy.SetActive(true);   // the original was switched off while copying, so the copy starts off too
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;

        // Force the copy to world scale (1, 1, 1) whatever the creator's objects are scaled to
        Vector3 parentScale = transform.lossyScale;
        copy.transform.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? 1f : 1f / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? 1f : 1f / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? 1f : 1f / parentScale.z);

        // Hide the creator's placeholder
        Renderer placeholder = GetComponent<Renderer>();
        if (placeholder != null) placeholder.enabled = false;
        Collider placeholderCollider = GetComponent<Collider>();
        if (placeholderCollider != null) placeholderCollider.enabled = false;

        // Collect everything that carries a ticket
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckSwitch>(), true)) {
            PeckSwitch sw = c.TryCast<PeckSwitch>();
            if (sw == null) continue;
            switches.Add(sw);
            if (mainSwitch == null && sw.GetComponent<CastableTarget>() != null) mainSwitch = sw;
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
            TrackedPeckState st = c.TryCast<TrackedPeckState>();
            if (st != null) states.Add(st);
        }

        ticketIds = BigPuzzleSDK.Tickets.Allocate(switches.Count + states.Count);
        AssignTickets(false);

        if (mainSwitch != null) {
            try { if (mainSwitch.trackedStateSystem != null) BigPuzzleSDK.StateMirrors.HoldStates.Add(mainSwitch.trackedStateSystem.Pointer); } catch (Exception) { }
            targetId = mainSwitch.GetComponent<CastableTarget>().GetInstanceID();
            byTarget[targetId] = this;
        }
    }

    private void Start() {
        AssignTickets(true);
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
            BigPuzzleSDK.Plugin.log.LogWarning($"[PuzzleButton] ticket registration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Press and release follow the game's record of who is holding the switch (host only)
    private void Update() {
        if (mainSwitch == null) return;
        // Only the host's game knows who holds the switch; the host fires the events and they
        // reach every player from there (see PuzzleEvent in Plugin.cs)
        bool held = mainSwitch.playerHoldingThis != null;
        if (held == wasHeld) return;
        wasHeld = held;

        if (held) {
            if (onPressed != null) {
                pressing = this;
                try { onPressed.Invoke(); } finally { pressing = null; }
            }
            if (pressedHook != null) pressedHook();
        } else {
            if (onReleased != null) onReleased.Invoke();
        }
    }

    private void OnDestroy() { byTarget.Remove(targetId); }
}