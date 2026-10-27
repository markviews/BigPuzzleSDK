using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// The seat's look. Must stay identical, in the same order, to the Unity SDK's enum.
public enum PuzzleSeatPose { MidcenturyChair, FoldingChair, WoodenChair, ListeningChair, CouchLeft, CouchMiddle, CouchRight, BenchA, BenchB, BenchC, Stump, CarouselPony, ChairliftLeft, ChairliftRight, TrainSeatA, TrainSeatB, TrainSeatC, TrainSeatD, TrainSeatE }

// Runtime (mod) version of the SDK's PuzzleSeat: a spot players click to sit in. Uses the game's own
// seat (the Midcentury chair's pose and click target, without the chair): the pose is placed at
// this object and the click area is a box the creator sizes. Players stand with Jump.
public class PuzzleSeat : MonoBehaviour {
    public PuzzleSeat(IntPtr ptr) : base(ptr) { }

    private const string CopyName = "VanillaSeat";

    // Set in the Inspector (filled in by the loader); the names must match the Unity-side fields
    private PuzzleSeatPose pose = PuzzleSeatPose.MidcenturyChair;
    private Vector3 seatOffset = Vector3.zero;     // where the player sits, from this object
    private float seatTurn = 0f;                   // which way they face (degrees around up)
    private Vector3 clickCenter = new Vector3(0f, 0.3f, 0f);
    private Vector3 clickSize = new Vector3(0.8f, 0.6f, 0.8f);

    // Filled in by the loader from the UnityEvents wired up in the Inspector
    private PuzzleEvent onSit;
    private PuzzleEvent onStand;

    private PlayerPose seat;
    private bool occupied;
    private float nextCheck;
    private ushort ticketId;

    // Late join: the game tells a new player who is sitting where, but that message can arrive
    // before this PC has loaded the puzzle, so the pose it points at doesn't exist yet and the sitter
    // is shown standing. For a while after loading, anyone whose pose points at this seat is seated again.
    private float reseatUntil;
    private float nextReseat;
    private readonly Dictionary<IntPtr, int> reseatTries = new Dictionary<IntPtr, int>();
    private static bool reportedCut;

    // Read-only for scripts
    public bool IsOccupied => occupied;

    // Every seat that exists, and who is sitting in this one (for PuzzlePlayer.seat)
    internal static readonly List<PuzzleSeat> all = new List<PuzzleSeat>();
    private void Awake() { all.Add(this); }
    private void OnDestroy() { all.Remove(this); }
    internal PlayerCharacter Occupant {
        get { try { return seat != null ? seat.occupant : null; } catch (Exception) { return null; } }
    }

    // The game's seat animations and the camera / body offsets that go with each, in the enum's order
    // (from a scan of every PlayerPose in the game).
    private struct SeatLook {
        public int id; public Vector3 camera, bum;
        public SeatLook(int id, Vector3 camera, Vector3 bum) { this.id = id; this.camera = camera; this.bum = bum; }
    }
    private static readonly SeatLook[] Looks = new[] {
        /* MidcenturyChair */ new SeatLook(70, new Vector3(0f, -0.11f, -0.43f), new Vector3(0f, 0f, 2.04f)),
        /* FoldingChair */ new SeatLook(40, new Vector3(0f, 0f, -0.3f), new Vector3(0f, -0.31f, 1.77f)),
        /* WoodenChair */ new SeatLook(74, new Vector3(0f, -0.03f, -0.29f), new Vector3(0f, 0f, 2.04f)),
        /* ListeningChair */ new SeatLook(84, new Vector3(0f, -0.13f, -0.5f), new Vector3(0f, 0f, 2.04f)),
        /* CouchLeft */ new SeatLook(71, new Vector3(-0.1f, -0.15f, -0.35f), new Vector3(0f, 0f, 1.78f)),
        /* CouchMiddle */ new SeatLook(73, new Vector3(0f, -0.09f, -0.34f), new Vector3(0f, -0.45f, 2.67f)),
        /* CouchRight */ new SeatLook(72, new Vector3(0f, -0.11f, -0.43f), new Vector3(0f, 0f, 2.04f)),
        /* BenchA */ new SeatLook(41, new Vector3(0f, 0f, -0.4f), new Vector3(0f, 0f, 1.91f)),
        /* BenchB */ new SeatLook(81, new Vector3(0f, 0.01f, -0.15f), new Vector3(0f, 0f, 1.42f)),
        /* BenchC */ new SeatLook(82, new Vector3(0f, 0f, -0.51f), new Vector3(0f, 0f, 1.91f)),
        /* Stump */ new SeatLook(80, new Vector3(0f, 0.05f, -0.41f), new Vector3(0f, 0f, 1.13f)),
        /* CarouselPony */ new SeatLook(60, new Vector3(0f, 0.06f, -0.1f), new Vector3(0f, 0f, 1.48f)),
        /* ChairliftLeft */ new SeatLook(87, new Vector3(0f, 0f, -0.41f), new Vector3(0f, 0f, 1.38f)),
        /* ChairliftRight */ new SeatLook(88, new Vector3(0f, 0f, -0.41f), new Vector3(0f, 0f, 1.38f)),
        /* TrainSeatA */ new SeatLook(90, new Vector3(-0.08f, 0f, 0.34f), new Vector3(0f, 0f, 2.04f)),
        /* TrainSeatB */ new SeatLook(91, new Vector3(0.08f, 0f, 0.34f), new Vector3(0f, 0f, 2.04f)),
        /* TrainSeatC */ new SeatLook(92, new Vector3(0.03f, 0f, 0.38f), new Vector3(0f, 0f, 2.04f)),
        /* TrainSeatD */ new SeatLook(93, new Vector3(0.04f, 0.07f, 0.31f), new Vector3(0f, 0f, 2.04f)),
        /* TrainSeatE */ new SeatLook(94, new Vector3(-0.1f, 0f, 0.19f), new Vector3(0f, 0f, 2.04f)),
    };

    // Called by the loader while the puzzle is still inactive
    internal void SetupSeat(int index) {
        var log = Plugin.log;
        GameObject template = Templates.Find(GameRefs.Seat.Paths);
        if (template == null) {
            log.LogWarning($"[PuzzleSeat] The game's chair isn't loaded, so '{name}' has nothing to copy.");
            return;
        }
        GameObject copy = Templates.Clone(template, transform);
        copy.name = CopyName;

        // Keep only the pose and its click target; the chair itself isn't wanted.
        // A stump group has several of each: take the click target whose outcome points at a pose, and that pose
        Transform poseT = null, triggerT = null;
        int poses = 0, targets = 0;
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PlayerPose>(), true)) if (c != null) poses++;
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<CastableTarget>(), true)) {
            if (c == null) continue;
            targets++;
            if (poseT != null) continue;
            try {
                CastableTarget target = c.TryCast<CastableTarget>();
                if (target == null || target.outcomes == null) continue;
                foreach (CastableOutcome outcome in target.outcomes) {
                    if (outcome != null && outcome.playerPose != null) { poseT = outcome.playerPose.transform; triggerT = c.transform; break; }
                }
            } catch (Exception) { }
        }
        if (poseT == null) {   // no link found: the first of each
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PlayerPose>(), true)) {
                if (c != null) { poseT = c.transform; break; }
            }
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<CastableTarget>(), true)) {
                if (c != null) { triggerT = c.transform; break; }
            }
        }
        if (poseT == null || triggerT == null) {
            log.LogWarning($"[PuzzleSeat] '{name}': the game's chair has no pose or click target.");
            UnityEngine.Object.DestroyImmediate(copy);
            return;
        }
        Vector3 parentScale = transform.lossyScale;
        Vector3 undo = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? 1f : 1f / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? 1f : 1f / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? 1f : 1f / parentScale.z);
        poseT.SetParent(transform, false);
        poseT.localPosition = seatOffset;
        poseT.localRotation = Quaternion.Euler(0f, seatTurn, 0f);
        poseT.localScale = undo;
        triggerT.SetParent(transform, false);
        triggerT.localPosition = Vector3.zero;
        triggerT.localRotation = Quaternion.identity;
        triggerT.localScale = Vector3.one;
        // The pose and click target may point at parts of the game's seat that are about to be destroyed (a stump
        // has more to it than a chair): cut those links first, or the game hits a missing object when sat on or clicked
        CutLinks(poseT.GetComponent<PlayerPose>(), triggerT.GetComponent<CastableTarget>());
        UnityEngine.Object.DestroyImmediate(copy);   // the rest of the chair

        seat = poseT.GetComponent<PlayerPose>();
        poseT.gameObject.SetActive(true);
        triggerT.gameObject.SetActive(true);

        // The click area: a box the creator sets (in this object's own space)
        BoxCollider box = triggerT.GetComponent<BoxCollider>();
        if (box != null) { box.center = clickCenter; box.size = clickSize; box.isTrigger = box.isTrigger; }

        // Which seat animation, and the camera / body offsets that go with it
        int look = (int)pose;
        if (look < 0 || look >= Looks.Length) look = 0;
        seat.animatorPoseId = Looks[look].id;
        seat.cameraOffset = Looks[look].camera;
        seat.localBumOffset = Looks[look].bum;

        // Its own network id, so sitting here never acts on the game's chair
        ticketId = Tickets.Allocate(1)[0];
        seat.ticket = ticketId;
        seat.shellReference = new SeaShell.ShellReference(ticketId);
    }

    private static void CutLinks(PlayerPose pose, CastableTarget target) {
        int cut = 0;
        try {
            if (pose != null) {
                if (pose.onEnterSwitch != null) { pose.onEnterSwitch = null; cut++; }
                if (pose.onExitSwitch != null) { pose.onExitSwitch = null; cut++; }
                if (pose.onEnterAction != null) { pose.onEnterAction = null; cut++; }
                if (pose.directSystemControl != null) { pose.directSystemControl = null; cut++; }
            }
            if (target != null && target.customCrosshairPoint != null) { target.customCrosshairPoint = null; cut++; }
            if (target != null && target.outcomes != null) {
                foreach (CastableOutcome outcome in target.outcomes) {
                    if (outcome == null) continue;
                    if (outcome.propHome != null) { outcome.propHome = null; cut++; }
                    if (outcome.peckSwitch != null) { outcome.peckSwitch = null; cut++; }
                    if (pose != null && outcome.playerPose != pose) outcome.playerPose = pose;
                }
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleSeat] could not cut a link to the game's seat: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Start() {
        if (seat == null) return;
        var office = LobbyNetworking.TicketOffice.instance;
        if (office == null) return;
        try {
            LobbyNetworking.ITicketed item = seat.TryCast<LobbyNetworking.ITicketed>();
            if (item != null) office.tickets[ticketId] = item;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleSeat] id registration failed: {ex.GetType().Name}: {ex.Message}");
        }
        reseatUntil = Time.time + 30f;
    }

    // Seats anyone who sat here before this PC had the seat (see reseatUntil). Reuses the game's own
    // "this player's pose changed" handler, with the pose it was told about.
    private void ReseatSittingPlayers() {
        try {
            if (seat.occupant != null) return;
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PlayerNetworking>())) {
                PlayerNetworking player = obj != null ? obj.TryCast<PlayerNetworking>() : null;
                if (player == null) continue;
                ShellReferenceWithActionNumber told = player.NetworkposeReference;
                if (told.shellReference.ticket != ticketId) continue;

                reseatTries.TryGetValue(player.Pointer, out int tries);
                if (tries >= 4) continue;
                reseatTries[player.Pointer] = tries + 1;

                player.OnSetPose(new ShellReferenceWithActionNumber(), told);
                return;
            }
        } catch (Exception ex) {
            Exception inner = ex.InnerException ?? ex;
            Plugin.log.LogWarning($"[PuzzleSeat] late join re-seat failed: {inner.GetType().Name}: {inner.Message}");
            reseatUntil = 0f;
        }
    }

    private void Update() {
        if (seat == null) return;
        if (!PuzzleSDK.IsHost && Time.time < reseatUntil && Time.time >= nextReseat) {
            nextReseat = Time.time + 0.5f;
            ReseatSittingPlayers();
        }
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.1f;
        bool now = false;
        try { now = seat.occupant != null; } catch (Exception) { }
        if (now == occupied) return;
        occupied = now;
        if (!PuzzleSDK.IsHost) return;   // the host fires events for everyone
        if (now) { if (onSit != null) onSit.Invoke(); }
        else if (onStand != null) onStand.Invoke();
    }
}

} // namespace BigPuzzleSDK
