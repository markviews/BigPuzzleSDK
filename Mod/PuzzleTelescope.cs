using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleTelescope: copies the Invisible Ink challenge's fixed
// telescope into place. Players step up and look through it like the game's; it has the game's
// "lobby vision" filter (PeckEffectLobbyVision), which shows invisible peg tiles.
public class PuzzleTelescope : MonoBehaviour {
    public PuzzleTelescope(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.Telescope.ExactPath;
    // Invisible Ink View off: the Optical Telegraph's plain telescope (no colour filter)
    private const string NormalPath = GameRefs.Telescope.NormalPath;

    // Set in the Inspector (filled in by the loader); the name must match the Unity-side field
    private bool invisibleInkView;   // the colour filter that shows invisible peg tiles
    private const string CopyName = "VanillaTelescope";
    private const string PreferredPath = GameRefs.Telescope.PreferredPath;

    private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
    private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
    private readonly List<PlayerPose> poses = new List<PlayerPose>();
    private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)

    // Called by the loader while the puzzle is still inactive
    internal void SetupTelescope(int index) {
        var log = Plugin.log;

        GameObject template = !invisibleInkView ? Templates.Find(NormalPath) : null;
        if (!invisibleInkView && template == null)
            log.LogWarning($"[PuzzleTelescope] '{name}': the plain telescope isn't loaded; using the lobby-vision one.");
        if (template == null) template = Templates.Find(ExactPath);
        if (template == null) {
            // Not at its known path: find a telescope with the lobby-vision filter (slow)
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PeckEffectLobbyVision>())) {
                PeckEffectLobbyVision lv = obj.TryCast<PeckEffectLobbyVision>();
                if (lv == null || !lv.gameObject.scene.IsValid()) continue;
                Component rotator = lv.GetComponentInParent(Il2CppType.Of<TelescopeRotator>(), true);
                if (rotator == null) continue;
                string path = Templates.FullPath(rotator.transform);
                if (path.Contains(CopyName)) continue;   // one of our own copies
                if (template == null) template = rotator.gameObject;
                if (path.Contains(PreferredPath)) { template = rotator.gameObject; break; }
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzleTelescope] No vanilla lobby-vision telescope is loaded, so '{name}' has nothing to copy.");
            return;
        }

        Vector3 templateScale = template.transform.lossyScale;
        GameObject copy = Templates.Clone(template, transform);
        copy.name = CopyName;
        RemoveInheritedPlayer(copy);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;

        // Keep the game's own size whatever the creator's objects are scaled to
        Vector3 parentScale = transform.lossyScale;
        copy.transform.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? templateScale.x : templateScale.x / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? templateScale.y : templateScale.y / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? templateScale.z : templateScale.z / parentScale.z);

        // The pillar's CastableTarget is what the crosshair clicks to step into the pose. Its outcome
        // points at the pose inside the copy (Instantiate re-points references within the copy);
        // check that, in case the game ever points it at something outside.
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<CastableTarget>(), true)) {
            CastableTarget target = c != null ? c.TryCast<CastableTarget>() : null;
            if (target == null) continue;
            try {
                var outcomes = target.outcomes;
                for (int i = 0; outcomes != null && i < outcomes.Length; i++) {
                    PlayerPose p = outcomes[i] != null ? outcomes[i].playerPose : null;
                    if (p != null && !p.transform.IsChildOf(copy.transform))
                        log.LogWarning($"[PuzzleTelescope] '{name}': the copy's step-in target points at a pose outside the copy ({Templates.FullPath(p.transform)}).");
                }
            } catch (Exception ex) {
                log.LogWarning($"[PuzzleTelescope] could not check the step-in target: {ex.GetType().Name}: {ex.Message}");
            }
        }

        copy.SetActive(true);

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

        // Everything that carries a network id gets its own, or using the copy would act on the
        // vanilla telescope: its state, the pose players step into, and any switches
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckSwitch>(), false)) {
            PeckSwitch sw = c != null ? c.TryCast<PeckSwitch>() : null;
            if (sw != null) switches.Add(sw);
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), false)) {
            TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
            if (st != null) states.Add(st);
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PlayerPose>(), false)) {
            PlayerPose pose = c != null ? c.TryCast<PlayerPose>() : null;
            if (pose != null) poses.Add(pose);
        }
        // The clone inherits the original's current user: someone looking through the vanilla telescope
        // while the copy is built would show up in every copy and never leave
        foreach (PlayerPose pose in poses) {
            try {
                pose.occupant = null;
                pose.chosenPlayer = null;
            } catch (Exception ex) { log.LogWarning($"[PuzzleTelescope] '{name}': could not clear the copy's user: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
        }
        if (poses.Count == 0) log.LogWarning($"[PuzzleTelescope] The copy for '{name}' has no PlayerPose; players can't look through it.");
        ticketIds = Tickets.Allocate(switches.Count + states.Count + poses.Count);
        AssignTickets(false);
    }

    private static readonly HashSet<string> KnownParts = new HashSet<string> {
        "telescope", "PillarCastableTarget", "swiveler", "swivler", "telescope_swivel", "telescope_scope", "Pose",
        "telescope_swivel_lv", "telescope_scope_lv", "telescope_lobbyvision", "gourdValetPillowModel" };

    // Copied while someone was looking through the vanilla telescope, the clone also contains the
    // game's stand-in for that player (it follows the player's animations). Take out anything with a
    // character rig, and list any other part that isn't part of a normal telescope.
    private void RemoveInheritedPlayer(GameObject copy) {
        var doomed = new List<GameObject>();
        try {
            // the whole player (named "PlayerCharacter <id>") sits under the pose while it is occupied
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Transform>(), true)) {
                if (c != null && c.name.StartsWith("PlayerCharacter") && c.transform != copy.transform) doomed.Add(c.gameObject);
            }
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Animator>(), true)) if (c != null) doomed.Add(c.gameObject);
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<SkinnedMeshRenderer>(), true)) if (c != null) doomed.Add(c.gameObject);
            var strange = new List<string>();
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Transform>(), true)) {
                if (c == null || c.transform == copy.transform) continue;
                if (!KnownParts.Contains(c.name) && !c.name.StartsWith("PropZoner")) strange.Add(Templates.FullPath(c.transform).Replace(Templates.FullPath(copy.transform), ""));
            }
            foreach (GameObject go in doomed) {
                if (go == null || go == copy) continue;
                UnityEngine.Object.DestroyImmediate(go);
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleTelescope] '{name}': could not check the copy for an inherited player: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    private float reseatUntil, nextReseat;
    private readonly Dictionary<IntPtr, int> reseatTries = new Dictionary<IntPtr, int>();

    private void Start() { AssignTickets(true); reseatUntil = Time.time + 30f; }

    // Late join: the game tells a new player who is looking through what, but that can arrive before
    // this PC has loaded the puzzle. For a while after loading, anyone whose pose points at this
    // telescope is put back into it (same as PuzzleSeat).
    private void Update() {
        if (PuzzleSDK.IsHost || Time.time >= reseatUntil || Time.time < nextReseat || poses.Count == 0) return;
        nextReseat = Time.time + 0.5f;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        try {
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PlayerNetworking>())) {
                PlayerNetworking player = obj != null ? obj.TryCast<PlayerNetworking>() : null;
                if (player == null) continue;
                object told = PuzzleSeat.MemberValue(player, GameRefs.Hooks.PoseReferenceMember, flags);
                if (told == null) continue;
                int ticket = PuzzleSeat.TicketIn(told, flags);
                PlayerPose mine = null;
                foreach (PlayerPose p in poses) if (p != null && p.ticket == ticket) { mine = p; break; }
                if (mine == null || mine.occupant != null) continue;
                reseatTries.TryGetValue(player.Pointer, out int tries);
                if (tries >= 4) continue;
                reseatTries[player.Pointer] = tries + 1;
                MethodInfo hook = player.GetType().GetMethod(GameRefs.Hooks.OnSetPose, flags);
                if (hook == null || hook.GetParameters().Length != 2) { reseatUntil = 0f; return; }
                object none = Activator.CreateInstance(hook.GetParameters()[0].ParameterType);
                hook.Invoke(player, new[] { none, told });
                return;
            }
        } catch (Exception ex) {
            Exception inner = ex.InnerException ?? ex;
            Plugin.log.LogWarning($"[PuzzleTelescope] late join re-seat failed: {inner.GetType().Name}: {inner.Message}");
            reseatUntil = 0f;
        }
    }

    private void AssignTickets(bool register) {
        int slot = 0;
        foreach (PeckSwitch sw in switches) {
            if (slot >= ticketIds.Count) return;
            if (register) Register(sw.Cast<LobbyNetworking.ITicketed>(), sw.ticket, ticketIds[slot]);
            sw.useTicket = true;
            sw.ticket = ticketIds[slot];
            sw.shellReference = new SeaShell.ShellReference(ticketIds[slot]);
            slot++;
        }
        foreach (TrackedPeckState st in states) {
            if (slot >= ticketIds.Count) return;
            if (register) Register(st.Cast<LobbyNetworking.ITicketed>(), st.ticket, ticketIds[slot]);
            st.ticket = ticketIds[slot];
            st.shellReference = new SeaShell.ShellReference(ticketIds[slot]);
            slot++;
        }
        foreach (PlayerPose pose in poses) {
            if (slot >= ticketIds.Count) return;
            if (register) {
                LobbyNetworking.ITicketed item = null;
                try { item = pose.TryCast<LobbyNetworking.ITicketed>(); } catch (Exception) { }
                if (item != null) Register(item, pose.ticket, ticketIds[slot]);
            }
            pose.ticket = ticketIds[slot];
            pose.shellReference = new SeaShell.ShellReference(ticketIds[slot]);
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
            Plugin.log.LogWarning($"[PuzzleTelescope] id registration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

} // namespace BigPuzzleSDK
