using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using Mirror;
using UnityEngine;

namespace BigPuzzleSDK {

    // Runtime (mod) version of the SDK's PuzzleGourdVice: copies a vanilla gourd vice into place.
    // It has no functions for creators; the puzzle's main class releases it from Win().
    //
    // FIRST VERSION, EXPLORATORY: the vice is tied into the game's gourd, save and map systems,
    // which have not been examined yet. The parts that write to the save file and the gourd map
    // are removed from the copy, and everything else is logged.
    public class PuzzleGourdVice : MonoBehaviour {
        public PuzzleGourdVice(IntPtr ptr) : base(ptr) { }

        // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
        private const string ExactPath = GameRefs.GourdVice.ExactPath;

        private const string CopyName = "VanillaGourdVice";
        private const string VictorySwitchName = GameRefs.GourdVice.VictorySwitchName;
        private const string PreferredPath = GameRefs.GourdVice.PreferredPath;

        // Components removed from the copy so a custom puzzle cannot touch the player's save file
        // or mark a real gourd as collected on the map.
        private static readonly string[] RemovedComponents = GameRefs.GourdVice.RemovedComponents;

        internal static readonly List<PuzzleGourdVice> all = new List<PuzzleGourdVice>();

        private const string PinnedSwitchName = GameRefs.GourdVice.PinnedSwitchName;

        private PeckSwitch victorySwitch;
        private PeckSwitch pinnedSwitch;
        private readonly List<GameObject> gourdVisuals = new List<GameObject>(); // display-only gourd
        private bool primed;
        private GameObject realGourd;   // sleeping copy of the vanilla gourd, woken at launch

        private const string LaunchSwitchName = GameRefs.GourdVice.LaunchSwitchName;
        private const float LaunchTimeout = 19f;     // length of the vice opening animation, in seconds
        private TrackedPeckState launchState;
        private bool launchFiredAtRelease;
        private float releaseTime;
        private bool launched;
        private LobbyDoor victoryDoor;
        private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
        private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
        private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)
        private bool released;

        // Called by the loader while the puzzle is still inactive
        internal void SetupVice(int index) {
            var log = Plugin.log;

            // Search every loaded object, including inactive ones
            GameObject template = Templates.Find(ExactPath);
            if (template == null) {
                // Not at its known path: search every loaded object instead (slow)
                foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PeckSwitch>())) {
                    PeckSwitch candidate = obj.TryCast<PeckSwitch>();
                    if (candidate == null || candidate.name != VictorySwitchName) continue;
                    Transform vice = candidate.transform.parent;
                    if (vice == null || PathOf(vice).Contains(CopyName)) continue;   // skip our own copies
                    if (template == null) template = vice.gameObject;
                    if (PathOf(vice).Contains(PreferredPath)) { template = vice.gameObject; break; }
                }
                Templates.FoundBySearch(this, null, ExactPath, template);
            }
            if (template == null) {
                log.LogWarning($"[PuzzleGourdVice] No vanilla gourd vice is loaded, so '{name}' has nothing to copy.");
                return;
            }

            Vector3 templateScale = template.transform.lossyScale;

            GameObject copy = Templates.Clone(template, transform);
            copy.name = CopyName;
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.SetActive(true);

            // Keep the vanilla vice's own world size whatever the creator's objects are scaled to
            Vector3 parentScale = transform.lossyScale;
            copy.transform.localScale = new Vector3(
                Mathf.Approximately(parentScale.x, 0f) ? templateScale.x : templateScale.x / parentScale.x,
                Mathf.Approximately(parentScale.y, 0f) ? templateScale.y : templateScale.y / parentScale.y,
                Mathf.Approximately(parentScale.z, 0f) ? templateScale.z : templateScale.z / parentScale.z);

            // Take out the save and map parts before the copy wakes up
            var doomed = new List<Component>();
            var networkObjects = new List<GameObject>();
            foreach (Component t in copy.GetComponentsInChildren(Il2CppType.Of<Transform>(), true)) {
                foreach (Component c in t.gameObject.GetComponents(Il2CppType.Of<Component>())) {
                    if (c == null) continue;
                    string typeName = c.GetIl2CppType().Name;
                    if (Array.IndexOf(RemovedComponents, typeName) >= 0) doomed.Add(c);
                    if (typeName == "NetworkIdentity") networkObjects.Add(c.gameObject);
                }
            }
            // The gourd in the vice is a networked game object. The game refuses copies of those and
            // deletes them, which is why the vice came out empty. Replace it with a display-only
            // copy of its mesh (not grabbable, not a real reward) and remove the networked copy
            // before it wakes up.
            foreach (GameObject networkObject in networkObjects)
                TakeGourd(networkObject, networkObject.transform.parent);

            // The vanilla vice is empty once a player has won its gourd (or it was knocked out), and
            // then the copy has none either. Copy a gourd from anywhere else into the copy's holder.
            if (realGourd == null) {
                Component homeComponent = copy.GetComponentInChildren(Il2CppType.Of<PropHome>(), true);
                Transform home = homeComponent != null ? homeComponent.transform : copy.transform;
                GameObject source = FindAnyGourd();
                if (source == null) {
                    log.LogWarning($"[PuzzleGourdVice] '{name}': the vanilla vice is empty and no other gourd is loaded to copy, so the vice holds no gourd.");
                } else {
                    GameObject spare = UnityEngine.Object.Instantiate((UnityEngine.Object)source, home).Cast<GameObject>();
                    spare.transform.localPosition = Vector3.zero;
                    spare.transform.localRotation = Quaternion.identity;
                    TakeGourd(spare, home);
                }
            }

            foreach (Component c in doomed) {
                UnityEngine.Object.DestroyImmediate(c);
            }
            FinishSetup(copy, index);

            // 1 once the host has released the vice, so every other player's vice opens too
            syncReleased = new SyncedValue(transform, "vice released " + name);

            // Modded gourds are teal (both the display one and the real one)
            foreach (GameObject visual in gourdVisuals) ModGourd.Tint(visual);
            ModGourd.Tint(realGourd);
            ModGourd.MakeModdedProp(realGourd);   // fits modded holders only

            // The real gourd gets a fixed network id, so the host's spawn reaches every player
            if (realGourd != null) {
                gourdNetId = NetItems.IdFor(transform, "gourd");
                NetItems.Prepare(realGourd, gourdNetId);
            }
        }
        private ulong gourdNetId;
        private SyncedValue syncReleased;
        private bool hostReleased;   // other players: the host has released its vice
                                     // The loose gourd rolls on the host only; every other player's copy just follows it
        private const float FollowSeconds = 6f;     // how long after launch the roll is streamed
        private const float FollowInterval = 0.1f;  // how often the host sends the gourd's place
        private float followUntil, nextFollow;      // host
        private float arrivedAt = -1f;              // other players: when the host's gourd appeared
        private bool frozenHere;                    // other players: physics paused on this PC

        // Any gourd to copy: a loaded gourd asset first (always complete), else one in the world
        internal static GameObject FindAnyGourd() {
            GameObject inWorld = null;
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<RewardGourd>())) {
                RewardGourd gourd = obj.TryCast<RewardGourd>();
                if (gourd == null) continue;
                GameObject go = gourd.gameObject;
                if (Templates.FullPath(go.transform).Contains("PuzzleLoadHolder") || go.name == "PuzzleGourd" || ModGourd.IsModded(go)) continue;   // ours (this or another puzzle's)
                if (!go.scene.IsValid()) return go;   // an asset, not a gourd someone may be holding
                if (inWorld == null) inWorld = go;
            }
            return inWorld;
        }

        // Turns a networked gourd inside the copy into a display-only gourd (shown until launch) plus
        // a sleeping real gourd that is woken and spawned at launch
        private void TakeGourd(GameObject networkObject, Transform home) {
            {
                foreach (Component rc in networkObject.GetComponentsInChildren(Il2CppType.Of<MeshRenderer>(), true)) {
                    Transform source = rc.transform;
                    GameObject visual = UnityEngine.Object.Instantiate((UnityEngine.Object)source.gameObject, home).Cast<GameObject>();
                    visual.name = "GourdVisual";
                    visual.transform.position = source.position;
                    visual.transform.rotation = source.rotation;
                    Vector3 want = source.lossyScale, have = home.lossyScale;
                    visual.transform.localScale = new Vector3(
                        Mathf.Approximately(have.x, 0f) ? want.x : want.x / have.x,
                        Mathf.Approximately(have.y, 0f) ? want.y : want.y / have.y,
                        Mathf.Approximately(have.z, 0f) ? want.z : want.z / have.z);
                    foreach (Component cc in visual.GetComponentsInChildren(Il2CppType.Of<Collider>(), true)) {
                        Collider col = cc != null ? cc.TryCast<Collider>() : null;
                        if (col != null) col.enabled = false;
                    }
                    gourdVisuals.Add(visual);
                }
                // Keep the networked copy asleep; it becomes the real gourd at launch
                networkObject.SetActive(false);
                ResetIdentity(networkObject);
                DetachFromVanillaHome(networkObject);
                if (realGourd == null) realGourd = networkObject;
                else UnityEngine.Object.DestroyImmediate(networkObject);
            }
        }

        // The rest of the setup, after the gourd is in place
        private void FinishSetup(GameObject copy, int index) {
            var log = Plugin.log;

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

            // Everything that carries a network id must get its own, or the copy would act on the
            // vanilla vice it was copied from.
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckSwitch>(), false)) {
                PeckSwitch sw = c != null ? c.TryCast<PeckSwitch>() : null;
                if (sw == null) continue;
                if (sw.useTicket) switches.Add(sw); // the vice's own switches are local and carry no id
                TrackedPeckState target = sw.trackedStateSystem;
                if (sw.name == VictorySwitchName) victorySwitch = sw;
                if (sw.name == PinnedSwitchName) pinnedSwitch = sw;
                if (sw.name == LaunchSwitchName) launchState = target;
            }
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), false)) {
                TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
                if (st == null) continue;
                states.Add(st);
            }
            // The copy's gourd holder shares its id with the vanilla vice's holder; give it its own.
            // Holders get the ids after the switches and states.
            var homes = new List<PropHome>();
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PropHome>(), true)) {
                PropHome home = c != null ? c.TryCast<PropHome>() : null;
                if (home != null) homes.Add(home);
            }
            ticketIds = Tickets.Allocate(switches.Count + states.Count + homes.Count);
            int homeSlot = switches.Count + states.Count;
            foreach (PropHome home in homes) {
                try {
                    ushort homeTicket = ticketIds[homeSlot++];
                    home._ticket_k__BackingField = homeTicket;
                    home.shellReference = new SeaShell.ShellReference(homeTicket);
                    home.pinnedProp = null;
                    ModGourd.MakeModdedHome(home);   // modded gourds only
                } catch (Exception ex) { log.LogWarning($"[PuzzleGourdVice]   holder re-id failed: {ex.GetType().Name}: {ex.Message}"); }
            }
            AssignTickets(false);

            if (victorySwitch == null) {
                log.LogWarning($"[PuzzleGourdVice] The copy for '{name}' has no '{VictorySwitchName}'.");
            } else {
                Component doorComponent = victorySwitch.gameObject.GetComponent(Il2CppType.Of<LobbyDoor>());
                victoryDoor = doorComponent != null ? doorComponent.TryCast<LobbyDoor>() : null;
            }
        }

        private static string PathOf(Transform t, Transform stopAt = null) {
            string path = t.name;
            for (Transform p = t.parent; p != null && p != stopAt; p = p.parent) path = p.name + "/" + path;
            return path;
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
                Plugin.log.LogWarning($"[PuzzleGourdVice] id registration failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private const float JoinWindow = 45f;   // seconds after the puzzle is built that a released value counts as a join sync
        private float startedAt;
        private bool instantOpen;               // other players: joined after the vice was already opened
        private void Start() {
            startedAt = Time.time;
            all.Add(this);
            AssignTickets(true);
            if (syncReleased != null) {
                // other players: 1 = the host released its vice, 2 = the host's save says it was already won
                syncReleased.onChanged = v => {
                    // a value that arrives right after joining is the host's current state: already open, no animation
                    if (Time.time - startedAt < JoinWindow) instantOpen = true;
                    if (v == 1) hostReleased = true; else if (v == 2) hostRestoredEmpty = true;
                };
                syncReleased.Register();
            }

            // Other players: the sleeping real gourd waits outside the vice, so that wherever the
            // host's spawn message puts it, it lands in the right place in the world
            if (!NetworkServer.active && realGourd != null) {
                Vector3 size = realGourd.transform.lossyScale;
                realGourd.transform.SetParent(null, true);
                realGourd.transform.localScale = size;
                realGourd.name = "PuzzleGourd";
            }
        }

        // On the first frame, put the vice in its "holding a gourd" state. The copy starts in the
        // state the victory switch sets (open), so without this Win() would change nothing and no
        // animation or sound would play.
        private void Update() {
            // Host: while the gourd rolls out, send everyone its place ten times a second, plus
            // once more at the end so every PC has the same resting spot
            if (followUntil > 0f && realGourd != null && Time.time >= nextFollow) {
                NetItems.ResendNow(realGourd);
                nextFollow = Time.time + FollowInterval;
                if (Time.time > followUntil) followUntil = 0f;
            }

            // Other players: while the host streams the roll, this PC's physics stays out of it
            // (otherwise each PC rolls the gourd its own way); afterwards it is a normal prop again
            if (!NetworkServer.active && realGourd != null && realGourd.activeSelf) {
                Rigidbody body = realGourd.GetComponent<Rigidbody>();
                if (arrivedAt < 0f) {
                    arrivedAt = Time.time;
                    if (body != null && !body.isKinematic) { body.isKinematic = true; frozenHere = true; }
                } else if (frozenHere && Time.time - arrivedAt > FollowSeconds + 1f) {
                    if (body != null) body.isKinematic = false;
                    frozenHere = false;
                }
            }

            if (restoreAt > 0f && Time.time >= restoreAt) RestoreDoor();
            // Other players: open the vice when the host did (Win() runs on the host only)
            if (hostReleased && !released && primed) Release();
            // Other players: the host's save says this puzzle was won before: open and empty
            if (hostRestoredEmpty && !restoredEmpty && primed) {
                restoredEmpty = true;
                HideDisplayGourd();
                launched = true;
                if (!released) Release();
            }

            // Other players: Win() runs on the host only. When the host's real gourd arrives, hide
            // the display gourd it replaces.
            if (!NetworkServer.active && !launched && realGourd != null && realGourd.activeSelf) {
                launched = true;
                ModGourd.Tint(realGourd);   // once more now that it is awake
                ModGourd.MakeModdedProp(realGourd);
                foreach (GameObject visual in gourdVisuals) if (visual != null) visual.SetActive(false);
            }

            // After Win(): wait for the vice to finish opening, then swap the display gourd for a real one
            if (released && !launched) {
                bool fired = false;
                try { fired = launchState != null && launchState._firedOnce && !launchFiredAtRelease; } catch (Exception) { }
                if (fired || Time.time - releaseTime > LaunchTimeout) {
                    launched = true;
                    Launch(fired ? "the vice finished opening" : "timed out waiting for the vice");
                }
            }

            if (primed) return;
            primed = true;
            // Host: won in an earlier session (see PuzzleSave): open and empty instead of holding a gourd
            if (restoredEmpty) {
                SetVictoryState();
                if (syncReleased != null) syncReleased.Push(2);
                return;
            }
            if (pinnedSwitch == null) return;
            try {
                TrackedPeckState target = pinnedSwitch.trackedStateSystem;
                if (target == null) return;
                if (NetworkServer.active) {
                    target.SetState(pinnedSwitch.specificState);
                } else {
                    // Other players may not set network states; close it on this PC only
                    var ctx = new PeckContext();
                    ctx.state = pinnedSwitch.specificState;
                    target.DoPeck(ctx, false);
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[PuzzleGourdVice] '{name}' could not close the vice: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Opens the vice on this PC at a huge animation speed with its sounds removed, then puts them back.
        private void OpenInstantly() {
            float speed = 1f; AudioAsset a1 = null, a2 = null, a3 = null, a4 = null, a5 = null, a6 = null; PeckEffectAudio pa = null;
            bool saved = false;
            if (victoryDoor != null) {
                try {
                    speed = victoryDoor.animationSpeed;
                    a1 = victoryDoor.openSound; a2 = victoryDoor.closeSound; a3 = victoryDoor.openEndSound;
                    a4 = victoryDoor.closeEndSound; a5 = victoryDoor.openLoopSound; a6 = victoryDoor.closeLoopSound;
                    pa = victoryDoor.peckAudio;
                    victoryDoor.animationSpeed = 1000f;
                    victoryDoor.openSound = null; victoryDoor.closeSound = null; victoryDoor.openEndSound = null;
                    victoryDoor.closeEndSound = null; victoryDoor.openLoopSound = null; victoryDoor.closeLoopSound = null;
                    victoryDoor.peckAudio = null;
                    saved = true;
                } catch (Exception) { }
            }
            try {
                TrackedPeckState target = victorySwitch.trackedStateSystem;
                if (target != null) {
                    var ctx = new PeckContext();
                    ctx.state = victorySwitch.stateMode == PeckSwitch.StateMode.Specific ? victorySwitch.specificState : 1;
                    target.DoPeck(ctx, false);
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[PuzzleGourdVice] '{name}' could not open instantly: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
            if (saved) { restoreAt = Time.time + 1.5f; rSpeed = speed; r1 = a1; r2 = a2; r3 = a3; r4 = a4; r5 = a5; r6 = a6; rPa = pa; }
        }

        private float restoreAt;
        private float rSpeed; private AudioAsset r1, r2, r3, r4, r5, r6; private PeckEffectAudio rPa;
        private void RestoreDoor() {
            restoreAt = 0f;
            if (victoryDoor == null) return;
            try {
                victoryDoor.animationSpeed = rSpeed;
                victoryDoor.openSound = r1; victoryDoor.closeSound = r2; victoryDoor.openEndSound = r3;
                victoryDoor.closeEndSound = r4; victoryDoor.openLoopSound = r5; victoryDoor.closeLoopSound = r6;
                victoryDoor.peckAudio = rPa;
            } catch (Exception) { }
        }

        // Opens the vice and lets the gourd go. Called by the puzzle's main class from Win().
        // It does what a press of the vice's own victory switch would do, without needing a player.
        internal void Release() {
            var log = Plugin.log;
            if (released) { return; }
            if (victorySwitch == null) { log.LogWarning($"[PuzzleGourdVice] '{name}' cannot release: no victory switch"); return; }
            released = true;
            releaseTime = Time.time;
            if (syncReleased != null) syncReleased.Push(1);
            try { launchFiredAtRelease = launchState != null && launchState._firedOnce; } catch (Exception) { }

            // Other players: the host's state change does not move this PC's vice, so play the
            // opening on the vice itself (local animation and sound only)
            if (!NetworkServer.active) {
                if (instantOpen) {
                    // joined an already-won puzzle: no gourd, and the vice snaps open without sound
                    launched = true;
                    HideDisplayGourd();
                    OpenInstantly();
                    return;
                }
                // Apply the "open" value to the vice's state on this PC only, the same call the game
                // makes when a state change arrives from the host
                try {
                    TrackedPeckState target = victorySwitch.trackedStateSystem;
                    if (target != null) {
                        int value = victorySwitch.stateMode == PeckSwitch.StateMode.Specific ? victorySwitch.specificState : 1;
                        var ctx = new PeckContext();
                        ctx.state = value;
                        target.DoPeck(ctx, false);
                        return;
                    }
                } catch (Exception ex) {
                    log.LogWarning($"[PuzzleGourdVice] '{name}' could not open locally: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                }
                if (victoryDoor != null) {
                    try { victoryDoor.TestPeck1(); return; } catch (Exception ex) { log.LogWarning($"[PuzzleGourdVice] '{name}' could not move the vice locally: {ex.GetType().Name}: {ex.Message}"); }
                }
            }

            try {
                TrackedPeckState target = victorySwitch.trackedStateSystem;
                if (target != null) {
                    int value = victorySwitch.stateMode == PeckSwitch.StateMode.Specific ? victorySwitch.specificState : 1;
                    target.SetState(value);
                    return;
                }
                log.LogWarning($"[PuzzleGourdVice] '{name}': the victory switch has no state attached; moving the vice directly.");
            } catch (Exception ex) {
                log.LogWarning($"[PuzzleGourdVice] '{name}' could not set the victory state ({ex.GetType().Name}: {ex.Message}); moving the vice directly.");
            }

            try {
                if (victoryDoor != null) {
                    victoryDoor.TestPeck1();
                }
            } catch (Exception ex) {
                log.LogWarning($"[PuzzleGourdVice] '{name}' could not move the vice: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Hides the display gourd and tries to put a real, grabbable one in its place
        private void Launch(string why) {
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            foreach (GameObject visual in gourdVisuals) {
                if (visual == null) continue;
                position = visual.transform.position;
                rotation = visual.transform.rotation;
                visual.SetActive(false);
            }
            SpawnRealGourd(position, rotation);
        }

        // The copied gourd remembers the vanilla vice's holder as its home (by network id) and
        // jumps there when it wakes. Forget that home, and the save identity it shares with the
        // vanilla gourd, so the copy is a free-standing gourd.
        internal static void DetachFromVanillaHome(GameObject go) {
            var log = Plugin.log;
            try {
                Prop prop = go.GetComponent<Prop>();
                if (prop == null) { log.LogWarning("[PuzzleGourdVice]   the gourd has no Prop component"); return; }
                prop.startHome = null;
                prop.originalParent = null;
                prop.propHomeShellReference = new SeaShell.ShellReference(0);
                prop.canSaveHomeWithGuid = false;
                prop.savablePropGuid = "";
            } catch (Exception ex) {
                log.LogWarning($"[PuzzleGourdVice]   could not detach the gourd from its home: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Unfreezes the gourd so it drops out of the vice and can be picked up
        internal static void FreeGourd(GameObject go) {
            var log = Plugin.log;
            Prop prop = go.GetComponent<Prop>();
            if (prop == null) return;
            try { prop.ServerSetUnpinned(); } catch (Exception ex) { }
            try { prop.SetLoose(); } catch (Exception ex) { log.LogWarning($"[PuzzleGourdVice]   SetLoose failed: {ex.GetType().Name}: {ex.Message}"); }
        }

        // Makes a copied networked object look brand new to Mirror, so it is not rejected as a
        // duplicate of the scene object it was copied from.
        internal static void ResetIdentity(GameObject go) {
            NetworkIdentity identity = go.GetComponent<NetworkIdentity>();
            if (identity == null) return;
            SetMember(identity, "hasSpawned", false);
            SetMember(identity, "sceneId", 0UL);
            SetMember(identity, "netId", 0u);
        }

        private static void SetMember(NetworkIdentity target, string member, object value) {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            try {
                PropertyInfo prop = target.GetType().GetProperty(member, flags);
                if (prop != null && prop.CanWrite) { prop.SetValue(target, value); return; }
                FieldInfo field = target.GetType().GetField(member, flags);
                if (field != null) { field.SetValue(target, value); return; }
                Plugin.log.LogWarning($"[PuzzleGourdVice] NetworkIdentity has no settable '{member}'");
            } catch (Exception ex) {
                Exception inner = ex.InnerException ?? ex;
                Plugin.log.LogWarning($"[PuzzleGourdVice] could not set '{member}': {inner.GetType().Name}: {inner.Message}");
            }
        }

        // Moves the sleeping copy of the vanilla gourd out of the vice. The host then wakes it and
        // spawns it for everyone; on other players' PCs it waits for the host's spawn message.
        private bool SpawnRealGourd(Vector3 position, Quaternion rotation, bool launch = true) {
            var log = Plugin.log;
            try {
                if (realGourd == null) { log.LogWarning($"[PuzzleGourdVice] '{name}': no gourd copy was kept"); return false; }

                GameObject gourd = realGourd;
                if (!NetworkServer.active && gourd.activeSelf) return false;   // already arrived from the host

                // Both sides: out of the vice and into the world (the spawn message places it there)
                Vector3 size = gourd.transform.lossyScale;
                gourd.transform.SetParent(null, true);
                gourd.transform.position = position;
                gourd.transform.rotation = rotation;
                gourd.transform.localScale = size;
                gourd.name = "PuzzleGourd";
                ResetIdentity(gourd);
                NetItems.Prepare(gourd, gourdNetId);
                foreach (Component c in gourd.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
                    Renderer r = c != null ? c.TryCast<Renderer>() : null;
                    if (r != null) r.enabled = true;
                }

                if (!NetworkServer.active) return false;   // the host's gourd will arrive by itself
                if (!NetItems.Spawn(gourd)) return false;
                ModGourd.Tint(gourd);   // once more now that it is awake
                ModGourd.MakeModdedProp(gourd);
                followUntil = Time.time + FollowSeconds;   // see Update
                nextFollow = Time.time + FollowInterval;

                DetachFromVanillaHome(gourd);
                FreeGourd(gourd);
                gourd.transform.position = position;

                gourd.transform.rotation = rotation;

                Rigidbody body = gourd.GetComponent<Rigidbody>();
                if (body != null && launch) {
                    body.AddForce(Vector3.up * 3f, ForceMode.VelocityChange);
                }
                return true;
            } catch (Exception ex) {
                Exception inner = ex.InnerException ?? ex;
                log.LogWarning($"[PuzzleGourdVice] '{name}' could not wake the real gourd: {inner.GetType().Name}: {inner.Message}");
                return false;
            }
        }

        // ---- Restoring a save (host only; see PuzzleSave in PuzzleSave.cs) ----

        private bool restoredEmpty;        // won in an earlier session: open and empty from the start
        private bool hostRestoredEmpty;    // other players: the host's vice was restored empty

        // The gourd this vice gives out (asleep until the vice launches it or a save places it)
        internal GameObject Gourd => realGourd;
        internal bool HasLaunched => launched;

        // Called while the puzzle is being built, before its first frame: the vice starts open and
        // empty, with no animation or gourd launch. The gourd itself is placed by PlaceGourdAt or
        // PlaceGourdIn a moment later.
        internal void StartEmpty() {
            restoredEmpty = true;
            released = true;
            launched = true;
            HideDisplayGourd();
        }

        private void HideDisplayGourd() {
            foreach (GameObject visual in gourdVisuals) if (visual != null) visual.SetActive(false);
        }

        private void SetVictoryState() {
            if (victorySwitch == null) return;
            try {
                TrackedPeckState target = victorySwitch.trackedStateSystem;
                int value = victorySwitch.stateMode == PeckSwitch.StateMode.Specific ? victorySwitch.specificState : 1;
                if (target != null) target.SetState(value);
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[PuzzleGourdVice] '{name}' could not open the restored vice: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Host: wakes the gourd lying at a spot (a pillow, the spawn area)
        internal bool PlaceGourdAt(Vector3 position) {
            if (!NetworkServer.active || realGourd == null || realGourd.activeSelf) return false;
            launched = true;
            return SpawnRealGourd(position, Quaternion.identity, false);
        }

        // Host: wakes the gourd straight into a plinth's slot (the tower)
        internal bool PlaceGourdIn(PuzzleGourdPlinth plinth) {
            PropHome home = plinth != null ? plinth.Home : null;
            if (home == null || !NetworkServer.active || realGourd == null || realGourd.activeSelf) return false;
            launched = true;
            if (!SpawnRealGourd(home.transform.position, home.transform.rotation, false)) return false;
            followUntil = 0f;   // pinned: nothing to stream
            try {
                Prop prop = realGourd.GetComponent<Prop>();
                plinth.MarkFilled();   // before pinning, so the plinth doesn't fire onGourdPlaced
                prop.ServerSetPinned(home);
                return true;
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[PuzzleGourdVice] '{name}' could not put its gourd in '{plinth.name}': {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static bool HasComponentNamed(GameObject go, string typeName) {
            foreach (Component c in go.GetComponents(Il2CppType.Of<Component>()))
                if (c != null && c.GetIl2CppType().Name == typeName) return true;
            return false;
        }

        private void OnDestroy() {
            all.Remove(this);
            if (realGourd != null) UnityEngine.Object.Destroy(realGourd);   // it lives outside the puzzle once launched
        }
    }

} // namespace BigPuzzleSDK