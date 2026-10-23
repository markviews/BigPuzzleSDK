using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

    // Runtime (mod) version of the SDK's PuzzlePokeButton: copies the vanilla poke button (one click,
    // no holding) into place. It can be made unclickable and clickable again with SetClickable.
    public class PuzzlePokeButton : PuzzleButtonBase {
        public PuzzlePokeButton(IntPtr ptr) : base(ptr) { }

        // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
        private const string ExactPath = GameRefs.PokeButton.ExactPath;
        // The same object in 3- and 2-player lobbies (the 4-player challenges aren't loaded there)
        private const string ExactPath3 = GameRefs.PokeButton.ExactPath3;
        private const string ExactPath2 = GameRefs.PokeButton.ExactPath2;

        private const string TemplatePrefix = GameRefs.PokeButton.TemplatePrefix;
        private const string LogicName = GameRefs.PokeButton.LogicName;
        private const string PreferredPath = GameRefs.PokeButton.PreferredPath;
        private const string CopyName = "VanillaPokeButton";

        // Set in the Inspector; the name must match the Unity-side field.
        private bool clickable = true;   // always starts clickable; creators set the starting state from On Puzzle Stop
        internal float autoEnableSeconds;   // 0 = stay off after a press; otherwise re-arm after this many seconds

        private float reEnableAt;         // when the pending automatic re-arm happens; 0 = none

        // Filled in by the loader from the UnityEvent wired up in the Inspector
        private PuzzleEvent onPressed;

        internal static readonly Dictionary<int, PuzzlePokeButton> byTarget = new Dictionary<int, PuzzlePokeButton>();

        private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
        private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
        private PeckSwitch primeSwitch;
        private PeckSwitch unprimeSwitch;
        private int targetId;
        private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)
        private bool applied;
        private bool appliedClickable;
        private TrackedPeckState logicState;   // the game's record of the button: 1 while pressed in (any player's press reaches the host)
        private int lastLogic = -1;
        private bool forceApply;   // SetClickable was called: apply even if the value did not change

        // Called by the loader while the puzzle is still inactive
        internal void SetupButton(int index) {
            var log = Plugin.log;

            GameObject template = Templates.Find(ExactPath, ExactPath3, ExactPath2);
            if (template == null) {
                // Not at its known path (the 4-player challenges aren't loaded in a 2- or 3-player
                // lobby): search every loaded object instead (slow). A plain "BasicPokeButton" is
                // wanted; "BasicPokeButtonRepeating" and the like belong to other puzzles' logic.
                int bestScore = int.MinValue;
                var seen = new List<string>();
                foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<CastableTarget>())) {
                    CastableTarget candidate = obj.TryCast<CastableTarget>();
                    if (candidate == null || !candidate.name.StartsWith(TemplatePrefix)) continue;
                    if (candidate.transform.Find(LogicName) == null) continue;
                    if (!candidate.gameObject.scene.IsValid()) continue;   // a prefab, not in the world
                    string path = PathOf(candidate.transform);
                    if (path.Contains(CopyName)) continue;   // skip our own copies
                    if (seen.Count < 20) seen.Add(path);
                    int score = 0;
                    string n = candidate.name;
                    if (n == TemplatePrefix || n.StartsWith(TemplatePrefix + " (")) score += 10;   // the plain button
                    if (path.Contains("TallButtonChallenge")) score += 5;
                    if (path.Contains("Centurion")) score -= 3;   // driven by the Centurion puzzle
                    if (score > bestScore) { bestScore = score; template = candidate.gameObject; }
                }
                if (template != null && bestScore < 10)
                    log.LogWarning("[PuzzlePokeButton] No plain BasicPokeButton is loaded; candidates were:\n  " + string.Join("\n  ", seen));
                Templates.FoundBySearch(this, null, ExactPath, template);
            }
            if (template == null) {
                log.LogWarning($"[PuzzlePokeButton] No vanilla poke button is loaded, so '{name}' has nothing to copy.");
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

            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckSwitch>(), true)) {
                PeckSwitch sw = c != null ? c.TryCast<PeckSwitch>() : null;
                if (sw == null) continue;
                switches.Add(sw);
                if (sw.name == "PrimeSwitch") primeSwitch = sw;
                if (sw.name == "UnprimeSwitch") unprimeSwitch = sw;
                TrackedPeckState target = sw.trackedStateSystem;
            }
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
                TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
                if (st == null) continue;
                states.Add(st);
                if (st.name == LogicName) logicState = st;
            }
            ticketIds = Tickets.Allocate(switches.Count + states.Count);
            AssignTickets(false);

            CastableTarget clickTarget = copy.GetComponent<CastableTarget>();
            if (clickTarget != null) {
                targetId = clickTarget.GetInstanceID();
                byTarget[targetId] = this;
            }
            if (primeSwitch == null || unprimeSwitch == null)
                log.LogWarning($"[PuzzlePokeButton] '{name}': prime/unprime switches not found, so SetClickable only blocks the events.");
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

        private void Start() { AssignTickets(true); }
        private void OnDestroy() { byTarget.Remove(targetId); }

        // The first frame puts the button in its starting state; after that it follows SetClickable
        private void Update() {
            WatchPress();
            if (reEnableAt > 0f && Time.time >= reEnableAt) {
                reEnableAt = 0f;
                clickable = true;
                forceApply = true;
            }
            if (applied && clickable == appliedClickable && !forceApply) return;
            applied = true;
            forceApply = false;
            appliedClickable = clickable;
            // The game's own prime / unprime switches light the button up or dim it and make it
            // pressable or not, exactly as on the vanilla challenge
            Fire(clickable ? primeSwitch : unprimeSwitch);
        }

        private void Fire(PeckSwitch sw) {
            if (sw == null) return;
            try {
                TrackedPeckState target = sw.trackedStateSystem;
                if (target == null) return;
                int value = sw.stateMode == PeckSwitch.StateMode.Specific ? sw.specificState : 1;
                if (PuzzleSDK.IsHost) target.SetState(value);   // other players: arrives from the host
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[PuzzlePokeButton] '{name}' could not use '{sw.name}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        // The host sees every player's press as the button's state going to 1 (pressed in), so the event
        // fires for presses by anyone; the host then tells everyone (see PuzzleEvent)
        private void WatchPress() {
            if (logicState == null || !PuzzleSDK.IsHost) return;
            if (!PeckValue.TryRead(logicState, out int now)) return;
            int before = lastLogic;
            lastLogic = now;
            if (before < 0 || now == before || now != 1) return;   // the first reading is only a starting point
            Pressed();
        }

        // Called by the click patch when the local player pokes this button
        internal void Poked() {
            if (logicState != null) return;   // the host's watch (WatchPress) handles it, for every player
            Pressed();
        }

        private void Pressed() {
            if (!clickable) {
                return;
            }
            if (onPressed != null) onPressed.Invoke();
            if (pressedHook != null) pressedHook();
            if (autoEnableSeconds > 0f) reEnableAt = Time.time + autoEnableSeconds;
        }

        // ---- Callable from code or from an Inspector event ----
        // Always re-applies, so SetClickable(true) also re-arms a button that was just pressed
        // (pressing the vanilla button switches it off by itself)
        public void SetClickable(bool value) {
            if (!PuzzleSDK.IsHost) return;   // the button's state reaches everyone from the host
            clickable = value;
            forceApply = true;
            reEnableAt = 0f;   // an explicit call replaces any pending automatic re-arm
        }
    }

    // Reports pokes on SDK poke buttons. Runs after the game has handled the click.
    [HarmonyPatch(typeof(PlayerDecisions), nameof(PlayerDecisions.UseCastableTarget))]
    internal static class PuzzlePokeButtonClickPatch {
        private static void Postfix(CastableTarget castableTarget) {
            try {
                if (castableTarget == null) return;
                if (PuzzlePokeButton.byTarget.TryGetValue(castableTarget.GetInstanceID(), out PuzzlePokeButton button) && button != null)
                    button.Poked();
            } catch (Exception) { }
        }
    }

} // namespace BigPuzzleSDK