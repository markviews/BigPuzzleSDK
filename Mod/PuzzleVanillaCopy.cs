using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using Mirror;
using UnityEngine;
using static PropertyBlockHelper;

namespace BigPuzzleSDK {

// Shared runtime (mod) code for the "copy a game object as it is" components from the batch scan
// (blind mask, counter, intercom, chair, buoy light...). Each one is a small subclass that only
// names its template; this does the copying:
//   - copies the template into place at the game's own size and hides the creator's placeholder
//   - removes the parts that write to the game's save or move the object (SaveIdentity, BuoySaver...)
//   - gives every switch, state, pose and prop holder its own network id (Tickets), so using the
//     copy never acts on the original
//   - grabbable props inside it (the chair, the mask...) are kept asleep and spawned by the host
//     for everyone, like peg tiles; one that sat in a holder inside the copy is put back in it
//   - its props and holders are modded, like modded gourds: a modded prop never fits the game's
//     own holders (a modded circle peg can't solve the Egg Hunt) and a vanilla prop never fits a
//     modded holder (it would be saved somewhere that is gone next time). Pegs and peg boards are
//     the exception: they work with the game's own boards and pegs too (see CrossesWithGame)
// The game's own scripts on the copy keep running with their own settings. What creators can do
// with each one (events, functions) is added per component later.
public class PuzzleVanillaCopy : MonoBehaviour {
    public PuzzleVanillaCopy(IntPtr ptr) : base(ptr) { }

    // Each component's template: exact paths (any lobby size), first one found wins
    private static readonly Dictionary<string, string[]> TemplatePaths = GameRefs.Copies.TemplatePaths;

    // Parts of the game's objects that would write to the player's save, move the copy onto the
    // terrain, or clear the ground around it. Removed from every copy.
    private static readonly HashSet<string> RemovedComponents = new HashSet<string> {
        "SaveIdentity", "BuoySaver", "PeckEffectSavableHome", "PeckEffectGourdMap", "LandmarkSaveSetter",
        "CullingLocation", "TerrainAligner", "ClearStamp",
    };

    internal static readonly List<PuzzleVanillaCopy> all = new List<PuzzleVanillaCopy>();

    protected GameObject copy;   // the copy (props inside it live outside it once spawned)
    private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
    private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
    private readonly List<PlayerPose> poses = new List<PlayerPose>();
    private readonly List<PropHome> homes = new List<PropHome>();
    private List<ushort> ticketIds = new List<ushort>();

    // Grabbable props in the copy: asleep until the host spawns them
    // (Network objects that aren't props, like the firework launcher's, stay where they are in the copy)
    private sealed class HeldProp { public GameObject go; public ulong id; public PropHome home; public bool spawned; public bool isProp; }
    private readonly List<HeldProp> props = new List<HeldProp>();

    // The first grabbable part of the copy (null until it exists)
    internal GameObject FirstPropObject() { return props.Count > 0 ? props[0].go : null; }

    private string Kind => GetType().Name;

    // Pegs and peg boards work with the game's own boards and pegs too (see ModGourd)
    private bool CrossesWithGame { get { return this is PuzzleCirclePeg || this is PuzzleCirclePegBoard || this is PuzzleSoundPeg || this is PuzzleSoundPegAnswer; } }

    // Which template: some components have a variant chosen in the Inspector
    private string TemplateKey {
        get {
            PuzzleNoItemsGate gate = this as PuzzleNoItemsGate;
            if (gate != null && gate.blindMask) return "PuzzleNoItemsGate BlindMask";   // made one-way in MakeOneWay
            if (gate != null && !gate.oneWay) return "PuzzleNoItemsGate TwoWay";
            if (this is PuzzleVanillaItem pedestal && pedestal.itemType == PuzzleVanillaItemType.BuoyRed) return "PuzzleBuoyLight Red";
            if (this is PuzzleVanillaItem vitem) {
                if (vitem.itemType == PuzzleVanillaItemType.Buoy) return "PuzzleBuoyLight";   // (the pedestal's template; Include Base off removes the pedestal afterwards)
                return "PuzzleVanillaItem " + (vitem.itemType == PuzzleVanillaItemType.CowbellBig ? "Cowbell" : vitem.itemType.ToString());
            }   // (the big cowbell's template is just "Cowbell")
            return Kind;
        }
    }

    // The item with the template's name, wherever it is now: one nobody is holding if there is one.
    // Never one of our own (they sit in the puzzle's load holder, or are marked as modded once spawned).
    private static GameObject FindMovedProp(string[] paths) {
        try {
            var names = new HashSet<string>();
            foreach (string path in paths) {
                int slash = path.LastIndexOf('/');
                names.Add(slash < 0 ? path : path.Substring(slash + 1));
            }
            GameObject any = null;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Prop>())) {
                Prop p = o != null ? o.TryCast<Prop>() : null;
                if (p == null || !p.gameObject.scene.IsValid() || !names.Contains(p.gameObject.name)) continue;
                if (p.GetComponentInParent<PuzzleVanillaCopy>() != null || ModGourd.IsModded(p.gameObject)
                    || Templates.FullPath(p.transform).Contains("PuzzleLoadHolder")) continue;
                bool free = false;
                try { free = p.exclusiveHolder == null; } catch (Exception) { }
                if (free) return p.gameObject;
                if (any == null) any = p.gameObject;
            }
            return any;
        } catch (Exception) { return null; }
    }

    // Called by the loader while the puzzle is still inactive
    internal void SetupCopy(int index) {
        var log = Plugin.log;
        if (!TemplatePaths.TryGetValue(TemplateKey, out string[] paths)) {
            log.LogWarning($"[{Kind}] has no template path.");
            return;
        }
        GameObject template = Templates.Find(paths);
        if (template == null && this is PuzzleVanillaItem) {
            // A player has moved the item off its usual place (a ball off its stick, a torch out of its holder): find it where it is now
            template = FindMovedProp(paths);
            if (template != null) Templates.FoundBySearch(this, TemplateKey, paths[0], template);
        }
        if (template == null) {
            log.LogWarning($"[{Kind}] The game object it copies isn't loaded in this lobby ({paths[0]}), so '{name}' has nothing to copy.");
            return;
        }

        if (this is PuzzleVanillaItem buoyTemplate && buoyTemplate.IsBuoy) template = buoyTemplate.RestingTemplate(template);   // never copy a lit one
        Vector3 templateScale = template.transform.lossyScale;
        copy = Templates.Clone(template, transform);
        copy.name = "Vanilla" + Kind.Replace("Puzzle", "");
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        // The 2 and 3 player lobbies' goal is the holder inside the receptacle, which sits turned and low
        if (this is PuzzleTimerGoal && template.name == "pomodoro_holder") {
            copy.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            copy.transform.localRotation = Quaternion.Euler(270f, 0f, 0f);
        }
        Vector3 parentScale = transform.lossyScale;   // always the game's own size
        copy.transform.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? templateScale.x : templateScale.x / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? templateScale.y : templateScale.y / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? templateScale.z : templateScale.z / parentScale.z);

        // Out with the save / terrain parts before anything wakes
        var doomed = new List<Component>();
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Component>(), true))
            if (c != null && RemovedComponents.Contains(c.GetIl2CppType().Name)) doomed.Add(c);
        foreach (Component c in doomed) UnityEngine.Object.DestroyImmediate(c);

        // The game has no one-way blind mask gate: make one from the two-way one
        PuzzleNoItemsGate gate = this as PuzzleNoItemsGate;
        if (gate != null && gate.blindMask && gate.oneWay) MakeOneWay(copy);
        PuzzleTimerSpawner spawner = this as PuzzleTimerSpawner;
        if (spawner != null) spawner.ApplyTime(copy);
        if (this is PuzzleSoundPeg soundPeg) SoundPegAudio.Apply(this, copy, soundPeg.sound, soundPeg.customClip);
        if (this is PuzzleSoundPegAnswer answer) { SoundPegAudio.Apply(this, copy, PuzzleSoundTrack.Sound1, null); answer.SetupSound(copy); }
        if (this is PuzzleCountInput counter) counter.SetupCounter(copy);
        if (this is PuzzleButtonNHold nhold) nhold.SetupButton(copy);
        if (this is PuzzleItem item) item.SetupItem(copy);
        if (this is PuzzleCountValidator screen) screen.SetupScreen(copy);
        PuzzleCirclePeg peg = this as PuzzleCirclePeg;
        if (peg != null) peg.ApplyColor(copy);
        PuzzleFireworkLauncher launcher = this as PuzzleFireworkLauncher;
        if (launcher != null) { launcher.RemoveButton(copy); launcher.ApplyColor(copy); }

        if (this is PuzzleVanillaItem buoyCopy && buoyCopy.IsBuoy) { buoyCopy.EnsureBuoy(copy); buoyCopy.ResetBuoy(copy); buoyCopy.RemoveBase(copy); }
        if (this is PuzzleVanillaItem vanillaItem) vanillaItem.SetupItem(copy);

        // Grabbable props: kept asleep, with a fixed network id, and cut off from the original's holder
        int propIndex = 0;
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<NetworkIdentity>(), true)) {
            if (c == null) continue;
            GameObject go = c.gameObject;
            Prop prop = go.GetComponent<Prop>();
            PropHome home = null;
            try { home = prop != null ? prop.startHome : null; } catch (Exception) { }
            if (home != null && !home.transform.IsChildOf(copy.transform)) home = null;   // the original's holder
            go.SetActive(false);
            ResetIdentity(go);
            Detach(go);
            if (prop != null) {
                if (CrossesWithGame) ModGourd.MakeCrossProp(go);   // pegs also fit the game's own boards
                else ModGourd.MakeModdedProp(go);   // fits modded holders only, never the game's own (and the other way round)
            }
            var held = new HeldProp { go = go, home = home, isProp = prop != null, id = NetItems.IdFor(transform, "prop" + propIndex++) };
            NetItems.Prepare(go, held.id);
            props.Add(held);
        }

        // A copy that is itself a prop (the chair, the mask...) stays asleep until spawned
        if (copy.GetComponent<NetworkIdentity>() == null) copy.SetActive(true);

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

        // Own network ids for everything that has one (props included: their states and holders)
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckSwitch>(), true)) {
            PeckSwitch sw = c != null ? c.TryCast<PeckSwitch>() : null;
            if (sw != null) switches.Add(sw);
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
            TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
            if (st != null) states.Add(st);
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PlayerPose>(), true)) {
            PlayerPose pose = c != null ? c.TryCast<PlayerPose>() : null;
            if (pose != null) poses.Add(pose);
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PropHome>(), true)) {
            PropHome home = c != null ? c.TryCast<PropHome>() : null;
            if (home == null) continue;
            homes.Add(home);
            if (CrossesWithGame) ModGourd.MakeCrossHome(home);   // peg boards also take the game's own pegs
            else ModGourd.MakeModdedHome(home);   // takes modded props only
            try {
                home.pinnedProp = null;
                home.saveableHomeName = default(SaveableHomeName);
                home.keepSavedWhenLeavingHome = false;
            } catch (Exception) { }
        }
        ticketIds = Tickets.Allocate(switches.Count + states.Count + poses.Count + homes.Count, Kind + " '" + name + "' (" + switches.Count + " switches, " + states.Count + " states, " + poses.Count + " poses, " + homes.Count + " homes)");
        AssignTickets(false);
    }

    // ---- One-way turnstile ----
    // The game's one-way gate is its two-way gate with the back-to-front pose and the back entry
    // target(s) switched off, and red plaques showing the one-way arrow (from the two gates' scans)
    private static readonly Color OneWayTint = new Color(0.774f, 0.091f, 0.119f, 1f);
    protected readonly List<PropertyBlockHelper> repaint = new List<PropertyBlockHelper>();   // refreshed in Start

    private void MakeOneWay(GameObject gate) {
        int off = 0;
        foreach (Component c in gate.GetComponentsInChildren(Il2CppType.Of<Transform>(), true)) {
            Transform t = c != null ? c.TryCast<Transform>() : null;
            if (t == null) continue;
            if (t.name == "TurnstilePose BackToFront" || t.name.StartsWith("PoseEntryCastableTarget Back")) {
                t.gameObject.SetActive(false);
                off++;
                continue;
            }
            // The arrow's cell in the plaque texture: right half for the front plaque, top half for the back one
            Vector4 cell;
            if (t.name == "turnstile_plaque") cell = new Vector4(1f, 1f, 0.5f, 0f);
            else if (t.name == "turnstile_plaque (1)") cell = new Vector4(1f, 1f, 0f, 0.5f);
            else continue;
            PropertyBlockHelper helper = t.GetComponent<PropertyBlockHelper>();
            if (helper == null) continue;
            try {
                var colors = helper.colorSettings;
                for (int i = 0; colors != null && i < colors.Count; i++) {
                    ColorSetting cs = colors[i];
                    if (cs.propertyName == "_Tint") { cs.color = OneWayTint; colors[i] = cs; }
                }
                var vectors = helper.vectorSettings;
                for (int i = 0; vectors != null && i < vectors.Count; i++) {
                    VectorSetting vs = vectors[i];
                    if (vs.propertyName == "_AlbedoMap_ST") { vs.vector4 = cell; vectors[i] = vs; }
                }
                repaint.Add(helper);
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[PuzzleNoItemsGate] '{name}': could not paint the one-way plaque: {ex.GetType().Name}: {ex.Message}");
            }
        }
        if (off < 2) Plugin.log.LogWarning($"[PuzzleNoItemsGate] '{name}': expected to switch off the back pose and back entry, found {off} part(s); it may still open both ways.");
    }

    private void AssignTickets(bool register) {
        int slot = 0;
        foreach (PeckSwitch sw in switches) {
            if (slot >= ticketIds.Count) return;
            if (register && IsAwake(sw)) Register(sw.TryCast<LobbyNetworking.ITicketed>(), sw.ticket, ticketIds[slot]);
            sw.useTicket = true;
            sw.ticket = ticketIds[slot];
            sw.shellReference = new SeaShell.ShellReference(ticketIds[slot]);
            slot++;
        }
        foreach (TrackedPeckState st in states) {
            if (slot >= ticketIds.Count) return;
            if (register && IsAwake(st)) Register(st.TryCast<LobbyNetworking.ITicketed>(), st.ticket, ticketIds[slot]);
            st.ticket = ticketIds[slot];
            st.shellReference = new SeaShell.ShellReference(ticketIds[slot]);
            slot++;
        }
        foreach (PlayerPose pose in poses) {
            if (slot >= ticketIds.Count) return;
            if (register && IsAwake(pose)) Register(TryTicketed(pose), pose.ticket, ticketIds[slot]);
            pose.ticket = ticketIds[slot];
            pose.shellReference = new SeaShell.ShellReference(ticketIds[slot]);
            slot++;
        }
        foreach (PropHome home in homes) {
            if (slot >= ticketIds.Count) return;
            if (register && IsAwake(home)) Register(TryTicketed(home), home._ticket_k__BackingField, ticketIds[slot]);
            home._ticket_k__BackingField = ticketIds[slot];
            home.shellReference = new SeaShell.ShellReference(ticketIds[slot]);
            slot++;
        }
    }

    // A part inside a sleeping prop registers its own id when the prop wakes (the game does that itself);
    // putting it in the list early made the game's own registration fail as a duplicate
    private static bool IsAwake(Component c) {
        try { return c != null && c.gameObject.activeInHierarchy; } catch (Exception) { return false; }
    }

    private static LobbyNetworking.ITicketed TryTicketed(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase o) {
        try { return o.TryCast<LobbyNetworking.ITicketed>(); } catch (Exception) { return null; }
    }

    private void Register(LobbyNetworking.ITicketed item, ushort current, ushort wanted) {
        if (item == null) return;
        var office = LobbyNetworking.TicketOffice.instance;
        if (office == null) return;
        try {
            var tickets = office.tickets;
            // Only remove an old entry if it really is this object (never the vanilla original)
            if (current != wanted && tickets.ContainsKey(current) && tickets[current].Pointer == item.Pointer)
                tickets.Remove(current);
            tickets[wanted] = item;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[{Kind}] id registration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Start() {
        all.Add(this);
        AssignTickets(true);
        RefreshRepaint();   // one-way plaques, peg colours
        if (this is PuzzleSoundPegAnswer answer) answer.StartSound();
        if (this is PuzzleCountInput counter) counter.StartCounter();
        if (this is PuzzleCountValidator screen) screen.StartScreen();

        // Props: out of the puzzle and into the world (the spawn message places them). The host
        // spawns them for everyone; other players wait for the host's spawn message.
        foreach (HeldProp p in props) {
            if (p.go == null) continue;
            try {
                if (!p.isProp) {
                    // A network object inside the copy: stays in place; the host wakes it for everyone
                    ResetIdentity(p.go);
                    NetItems.Prepare(p.go, p.id);
                    if (NetworkServer.active && NetItems.Spawn(p.go)) { p.spawned = true; ShowRenderers(p.go); RefreshRepaint(); }
                    continue;
                }
                Vector3 position = p.go.transform.position, size = p.go.transform.lossyScale;
                Quaternion rotation = p.go.transform.rotation;
                if (!NetworkServer.active && p.go.activeSelf) { p.spawned = true; continue; }   // already arrived
                p.go.transform.SetParent(null, true);
                p.go.transform.position = position;
                p.go.transform.rotation = rotation;
                p.go.transform.localScale = size;
                ResetIdentity(p.go);
                NetItems.Prepare(p.go, p.id);
                if (!NetworkServer.active) continue;
                if (!NetItems.Spawn(p.go)) continue;
                p.spawned = true;
                Detach(p.go);
                Prop prop = p.go.GetComponent<Prop>();
                if (prop != null) {
                    try { prop.ServerSetUnpinned(); } catch (Exception) { }
                    if (p.home != null) {
                        try { prop.ServerSetPinned(p.home); }   // back in its holder inside the copy
                        catch (Exception ex) { Plugin.log.LogWarning($"[{Kind}] '{name}' could not put '{p.go.name}' back in its holder: {ex.GetType().Name}: {ex.Message}"); }
                    } else {
                        try { prop.SetLoose(); } catch (Exception) { }
                    }
                }
                p.go.transform.position = position;
                p.go.transform.rotation = rotation;
                ShowRenderers(p.go); RefreshRepaint();
            } catch (Exception ex) {
                Exception inner = ex.InnerException ?? ex;
                Plugin.log.LogWarning($"[{Kind}] '{name}' could not spawn '{p.go.name}': {inner.GetType().Name}: {inner.Message}");
            }
        }
    }

    // The copy's first grabbable prop and its network id (for the components that save it)
    internal Prop FirstProp(out ulong id) {
        foreach (HeldProp p in props) {
            if (!p.isProp || p.go == null) continue;
            id = p.id;
            return p.go.GetComponent<Prop>();
        }
        id = 0UL;
        return null;
    }

    // The copy's prop holders (for the components that watch them)
    internal List<PropHome> Homes => homes;

    private void Update() {
        // Component-specific watching
        PuzzleTimerGoal goal = this as PuzzleTimerGoal;
        if (goal != null) goal.Watch();
        if (this is PuzzleCirclePegBoard board) board.Watch();
        if (this is PuzzleCountValidator screen) screen.Watch();
        if (this is PuzzleCountInput countInput) countInput.Watch();
        if (this is PuzzleButtonNHold nhold) nhold.Watch();
        if (this is PuzzleItem item) item.Watch();
        if (this is PuzzleVanillaItem vanillaItemWatch) { vanillaItemWatch.Watch(); vanillaItemWatch.ApplyPending(); vanillaItemWatch.ApplyLight(); vanillaItemWatch.WatchFlare(); vanillaItemWatch.WatchTorch(); vanillaItemWatch.WatchColor(); }
        if (this is PuzzleFireworkLauncher launcherWatch) launcherWatch.WatchFired();
        FlareSweep.Tick();

        // Other players: a prop woke because the host's spawn message arrived
        foreach (HeldProp p in props) {
            if (p.spawned || p.go == null || !p.go.activeSelf) continue;
            p.spawned = true;
            ShowRenderers(p.go); RefreshRepaint();
        }
    }

    // Re-applies changed colour settings (again once a prop has woken up)
    private void RefreshRepaint() {
        foreach (PropertyBlockHelper helper in repaint) {
            try { if (helper != null && helper.gameObject.activeInHierarchy) helper.Refresh(); } catch (Exception) { }
        }
    }

    private static void ShowRenderers(GameObject go) {
        foreach (Component c in go.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
            Renderer r = c != null ? c.TryCast<Renderer>() : null;
            if (r != null) r.enabled = true;
        }
    }

    // Before a rebuild: the props go now. Destroyed at the end of the frame (as OnDestroy below
    // would), an old prop could still unregister its network ids after the new copy has taken them.
    internal static void DestroyAllProps() {
        foreach (PuzzleVanillaCopy c in all.ToArray()) {
            if (c == null) continue;
            foreach (HeldProp p in c.props) {
                if (p.go == null || !p.isProp) continue;
                try { UnityEngine.Object.DestroyImmediate(p.go); } catch (Exception) { }
                p.go = null;
            }
        }
    }

    // After a rebuild (RegistryCleanup): every copy puts its network ids back in the game's list
    internal static void RegisterAgain() {
        foreach (PuzzleVanillaCopy c in all.ToArray())
            if (c != null) { try { c.AssignTickets(true); } catch (Exception) { } }
    }

    // The props live outside the puzzle once spawned, so they go when the puzzle does
    private void OnDestroy() {
        all.Remove(this);
        foreach (HeldProp p in props) if (p.go != null && p.isProp) UnityEngine.Object.Destroy(p.go);
    }

    // ---- Network object helpers (the same as the peg tile's) ----

    private static void ResetIdentity(GameObject go) {
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
            if (field != null) field.SetValue(target, value);
        } catch (Exception) { }
    }

    // The copy remembers the original's holder (by network id) and its save identity: forget both
    private static void Detach(GameObject go) {
        try {
            Prop prop = go.GetComponent<Prop>();
            if (prop == null) return;
            prop.startHome = null;
            prop.originalParent = null;
            prop.propHomeShellReference = new SeaShell.ShellReference(0);
            prop.canSaveHomeWithGuid = false;
            prop.savablePropGuid = "";
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[Copy] could not detach '{go.name}' from its original holder: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// ---- The components (each only names its template, in TemplatePaths above) ----
// Which game item a Vanilla Item is. The order must match the Unity-side enum.
public enum PuzzleVanillaItemType { Blindfold, FoldingChair, Torch, FmRadio, WalkieTalkie, Binoculars, XrayGoggles, Clock, Laser, Megaphone, Kettle, CannonBall, PermoSign, SpareSign, GourdHolder, Belt, Backpack, Gps, FoldingMap, Compass, FlareGun, CowbellBig, CowbellMedium, CowbellSmall, Paintbrush, Football, Buoy, BuoyRed }
// Any of the game's items, copied as it is; the creator picks which. Held items can be saved like
// Grabbable Items (see PuzzleSave); the Spare Sign also has a default text and saves what players write.
public class PuzzleVanillaItem : PuzzleVanillaCopy, ISavedHeld {
    public PuzzleVanillaItem(IntPtr ptr) : base(ptr) { }
    // Set in the Inspector (filled in by the loader); the names must match the Unity-side fields
    internal PuzzleVanillaItemType itemType;
    private string defaultText = "";      // Spare Sign: the text until a player writes something
    private Color itemColor = Color.white;   // Kettle, Walkie Talkie, Backpack, Belt, Flare Gun: white keeps the game's own colour
    private Color flareColor = Color.white;  // Flare Gun: the flare's colour (white = the game's own)
    private bool savesOnReload = true;    // held when the game saves or reloads = back at the spawn area

    private Prop prop;
    private ulong itemId;
    private bool fromSave;
    private Vector3 spawnCenter;
    private const float SpawnRadius = 8f;
    private float nextCheck;

    // ---- Held items (the same rules as Grabbable Item's Saves On Reload) ----
    public bool SavesOnReload => savesOnReload;
    public ulong ItemId { get { Find(); return itemId; } }
    public MonoBehaviour Owner => this;

    private bool Find() {
        if (prop != null) return true;
        prop = FirstProp(out itemId);
        return prop != null;
    }

    public bool CountsAsHeld() {
        if (!Find()) return false;
        if (IsCarried()) return true;
        return fromSave && NearSpawn();
    }

    private bool NearSpawn() {
        try { return Vector3.Distance(prop.transform.position, spawnCenter) <= SpawnRadius; } catch (Exception) { return false; }
    }

    private bool IsCarried() {
        try {
            if (prop.exclusiveHolder != null || prop.isInInventory) return true;
            PropHome home = prop.currentHome;
            return home != null && (home.parentCharacter != null || home.isInventory);
        } catch (Exception) { return false; }
    }

    public void SendTo(Vector3 position) {
        if (!Find()) return;
        try {
            fromSave = true;
            spawnCenter = position;
            Transform t = prop.transform;
            try { prop.ServerSetUnpinned(); } catch (Exception) { }
            t.position = position;
            Rigidbody body = prop.rb;
            if (body != null) { body.position = position; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': could not move it to the spawn area: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Host: taken out of the spawn area and put down elsewhere: from then on it only counts when held
    internal void Watch() {
        if (!savesOnReload || !fromSave || !PuzzleSDK.IsHost || Time.time < nextCheck || !Find()) return;
        nextCheck = Time.time + 0.5f;
        if (!IsCarried() && !NearSpawn()) fromSave = false;
    }

    // ---- Buoy and Red Buoy: the game's buoy light on its pedestal ----
    internal bool IsBuoy { get { return itemType == PuzzleVanillaItemType.Buoy || itemType == PuzzleVanillaItemType.BuoyRed; } }
    private bool RedBuoy { get { return itemType == PuzzleVanillaItemType.BuoyRed; } }
    private bool includeBase = true;   // Buoy / Red Buoy: the pedestal comes with it; off = just the buoy

    // Without the base: the buoy alone, as a loose prop where this object is (the pedestal and its holder are removed)
    internal void RemoveBase(GameObject copy) {
        if (includeBase) return;
        try {
            Prop buoy = null;
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Prop>(), true)) {
                Prop p = c != null ? c.TryCast<Prop>() : null;
                if (p != null) { buoy = p; break; }
            }
            if (buoy == null) { Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': no buoy found in the copy, so Include Base does nothing."); return; }
            try { buoy.startHome = null; } catch (Exception) { }
            Transform t = buoy.transform;
            t.SetParent(copy.transform, false);   // out of the pedestal's holder
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            var doomed = new List<GameObject>();
            for (int i = 0; i < copy.transform.childCount; i++) {
                Transform child = copy.transform.GetChild(i);
                if (child != t) doomed.Add(child.gameObject);
            }
            foreach (GameObject g in doomed) UnityEngine.Object.DestroyImmediate(g);
            Renderer rootRenderer = copy.GetComponent<Renderer>();
            if (rootRenderer != null) rootRenderer.enabled = false;
            Collider rootCollider = copy.GetComponent<Collider>();
            if (rootCollider != null) rootCollider.enabled = false;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': could not remove the buoy's base: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    // Whether this buoy sits on (or came from) a red pedestal: the pedestal is named "..._Red"
    // One of our own props (they are moved out of their copy, into the puzzle's load holder, so being
    // under a copy is not enough to recognise them)
    private static bool IsOurs(Prop p) {
        try {
            if (p.GetComponentInParent<PuzzleVanillaCopy>() != null) return true;
            if (ModGourd.IsModded(p.gameObject)) return true;
            return Templates.FullPath(p.transform).Contains("PuzzleLoadHolder");
        } catch (Exception) { return false; }
    }

    private static bool OnRedPedestal(Transform t) {
        for (int up = 0; up < 5 && t != null; up++, t = t.parent)
            if (t.name.StartsWith("buoypedestal") && t.name.Contains("_Red")) return true;
        return false;
    }

    private static bool IsOff(Prop p) {
        try {
            TrackedPeckState st = p.GetComponent<TrackedPeckState>();
            int value;
            return st != null && PeckValue.TryRead(st, out value) && value == 0;
        } catch (Exception) { return false; }
    }

    // A lit vanilla buoy carries its lit look into any copy of it (and some of that look can't be
    // undone afterwards). So when the buoy at the usual path is lit, copy another pedestal of the
    // same colour whose buoy is off; an empty pedestal is fine too (it gets a buoy in EnsureBuoy).
    internal GameObject RestingTemplate(GameObject found) {
        try {
            Prop inside = found.GetComponentInChildren<Prop>(true);
            if (inside == null || IsOff(inside)) return found;
            bool wantRed = RedBuoy;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Prop>())) {
                Prop p = o != null ? o.TryCast<Prop>() : null;
                if (p == null || !p.gameObject.scene.IsValid() || !p.gameObject.name.StartsWith("BuoyLight")) continue;
                if (IsOurs(p)) continue;
                if (OnRedPedestal(p.transform) != wantRed || !IsOff(p)) continue;
                Transform pedestal = p.transform;
                for (int up = 0; up < 5 && pedestal != null && !pedestal.name.StartsWith("buoypedestal"); up++) pedestal = pedestal.parent;
                if (pedestal == null || !pedestal.name.StartsWith("buoypedestal")) continue;   // never a holder or anything else
                Plugin.log.LogInfo($"[PuzzleVanillaItem] '{name}': the buoy at the usual place is lit, so '{Templates.FullPath(pedestal)}' is copied instead");
                return pedestal.gameObject;
            }
            Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': every buoy of this colour is lit, so the copy starts from a lit one and is switched off afterwards");
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': could not look for an unlit buoy: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
        return found;
    }

    // The buoy is copied as it is right now: a lit original gives a lit copy, in whatever colour the
    // light had been cycling to. Every copy starts off and in its own colour; the host's real state
    // (on or off) then reaches everyone through the synced states.
    private static readonly Color OrangeFlare = new Color(1f, 0.228f, 0f, 1f);
    private static readonly Color RedFlare = new Color(1f, 0.03f, 0.03f, 1f);

    internal void ResetBuoy(GameObject copy) {
        try {
            Color flareColor = RedBuoy ? RedFlare : OrangeFlare;
            Color lightColor = flareColor;
            FindRestingColors(copy, ref flareColor, ref lightColor);

            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
                TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
                if (st == null || st.GetComponent<Prop>() == null) continue;
                try {
                    PeckContext ctx = st.currentPeckContext;
                    ctx.state = 0;
                    st.currentPeckContext = ctx;
                } catch (Exception) { }
            }
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Transform>(), true)) {
                Transform t = c != null ? c.TryCast<Transform>() : null;
                if (t == null) continue;
                if (t.name == "bouyLight" || t.name == "disco lighting" || t.name == "LobbyFlare") t.gameObject.SetActive(false);
            }
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Light>(), true)) {
                Light l = c != null ? c.TryCast<Light>() : null;
                if (l != null) l.color = lightColor;
            }
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
                Renderer r = c != null ? c.TryCast<Renderer>() : null;
                if (r == null || r.gameObject.name != "LobbyFlareMesh") continue;
                try { r.material.SetColor("_MainColor", flareColor); } catch (Exception) { }
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': could not reset the buoy: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    // The colours of a buoy of this colour that is switched off (they are the real ones: a lit
    // buoy has been changed by the disco effect), else the built-in values
    private void FindRestingColors(GameObject copy, ref Color flareColor, ref Color lightColor) {
        foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Prop>())) {
            Prop p = o != null ? o.TryCast<Prop>() : null;
            if (p == null || !p.gameObject.scene.IsValid() || !p.gameObject.name.StartsWith("BuoyLight")) continue;
            if (p.transform.IsChildOf(copy.transform) || IsOurs(p)) continue;
            if (OnRedPedestal(p.transform) != (RedBuoy)) continue;
            TrackedPeckState st = p.GetComponent<TrackedPeckState>();
            int value;
            if (st == null || !PeckValue.TryRead(st, out value) || value != 0) continue;
            bool gotFlare = false, gotLight = false;
            foreach (Component c in p.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
                Renderer r = c != null ? c.TryCast<Renderer>() : null;
                if (r == null || r.gameObject.name != "LobbyFlareMesh") continue;
                try { Color col = r.sharedMaterial.GetColor("_MainColor"); if (col.a > 0f) { flareColor = col; gotFlare = true; } } catch (Exception) { }
            }
            foreach (Component c in p.GetComponentsInChildren(Il2CppType.Of<Light>(), true)) {
                Light l = c != null ? c.TryCast<Light>() : null;
                if (l == null) continue;
                lightColor = l.color; gotLight = true; break;
            }
            if (gotFlare && !gotLight) lightColor = flareColor;
            if (gotFlare || gotLight) return;
        }
    }

    // The pedestal is copied as it stands: if a player has taken that pedestal's buoy out, the
    // copy would be an empty pedestal. Then copy a buoy from wherever one is (held, dropped or on
    // another pedestal) into this pedestal's holder.
    internal void EnsureBuoy(GameObject copy) {
        try {
            PropHome holder = null;
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PropHome>(), true)) {
                PropHome h = c != null ? c.TryCast<PropHome>() : null;
                if (h != null) { holder = h; break; }
            }
            if (holder == null) return;
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Prop>(), true))
                if (c != null) return;   // it still has its buoy

            Prop source = null, any = null;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Prop>())) {
                Prop p = o != null ? o.TryCast<Prop>() : null;
                if (p == null || !p.gameObject.scene.IsValid() || !p.gameObject.name.StartsWith("BuoyLight")) continue;
                if (IsOurs(p)) continue;   // one of our own
                if (p.transform.IsChildOf(copy.transform)) continue;
                if (any == null) any = p;
                if (OnRedPedestal(p.transform) != (RedBuoy)) continue;
                if (IsOff(p)) { source = p; break; }
                if (source == null) source = p;
            }
            if (source == null) source = any;
            if (source == null) {
                Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': its pedestal is empty and there is no buoy anywhere to copy.");
                return;
            }
            GameObject buoy = UnityEngine.Object.Instantiate((UnityEngine.Object)source.gameObject, holder.transform).Cast<GameObject>();
            buoy.name = "BuoyLight";
            buoy.transform.localPosition = Vector3.zero;
            buoy.transform.localRotation = Quaternion.identity;
            buoy.transform.localScale = Vector3.one;
            Prop prop = buoy.GetComponent<Prop>();
            if (prop != null) prop.startHome = holder;
            Plugin.log.LogInfo($"[PuzzleVanillaItem] '{name}': its pedestal was empty, so a buoy was copied in from {Templates.FullPath(source.transform)}");
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': could not add a buoy to the empty pedestal: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    // ---- The light's on / off state is saved too (see PuzzleSave) ----
    // Whether the buoy's light is on now. The copy is cut off from the vanilla buoy it came from.
    internal bool LightOn {
        get {
            if (!Find()) return false;
            try {
                TrackedPeckState st = prop.GetComponent<TrackedPeckState>();
                int value;
                return st != null && PeckValue.TryRead(st, out value) && value != 0;
            } catch (Exception) { return false; }
        }
    }

    private int pendingLight = -1;   // host: a saved state to put the light in once the buoy exists
    private float lightUntil, lightNext;

    internal void RestoreLight(bool on) {
        pendingLight = on ? 1 : 0;
        lightUntil = Time.time + 15f;
        lightNext = 0f;
    }

    internal void ApplyLight() {
        if (pendingLight < 0 || !PuzzleSDK.IsHost || Time.time < lightNext) return;
        lightNext = Time.time + 0.5f;
        if (Time.time > lightUntil) { pendingLight = -1; return; }
        if (!Find()) return;
        try {
            TrackedPeckState st = prop.GetComponent<TrackedPeckState>();
            if (st == null) { pendingLight = -1; return; }
            int value;
            if (PeckValue.TryRead(st, out value) && (value != 0) == (pendingLight != 0)) { pendingLight = -1; return; }
            st.SetState(pendingLight);
        } catch (Exception) { }
    }

    // ---- The Spare Sign's text ----
    private PeckEffectTextInput signText;
    private string pendingText;           // host: a saved text to put on the sign once it exists
    private float pendingUntil, pendingNext;

    // Called while the copy is still asleep: the sign starts with the default text, not the "hi"
    // the vanilla one had when it was copied
    internal void SetupItem(GameObject copy) {
        if (itemType == PuzzleVanillaItemType.Torch) { ResetTorch(copy); return; }
        if (itemType == PuzzleVanillaItemType.PermoSign) { SetupPermoSign(copy); return; }
        if (itemType == PuzzleVanillaItemType.Kettle || itemType == PuzzleVanillaItemType.WalkieTalkie
            || itemType == PuzzleVanillaItemType.Backpack || itemType == PuzzleVanillaItemType.Belt || itemType == PuzzleVanillaItemType.FlareGun) {
            ApplyItemColor(copy);
            if (itemType == PuzzleVanillaItemType.FlareGun) { ApplyFlareColor(copy); ResetPropStates(copy); }
            return;
        }
        if (itemType != PuzzleVanillaItemType.SpareSign) return;
        try {
            Component c = copy.GetComponentInChildren(Il2CppType.Of<PeckEffectTextInput>(), true);
            signText = c != null ? c.TryCast<PeckEffectTextInput>() : null;
            if (signText == null) { Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': the sign's text part wasn't found, so Default Text does nothing."); return; }
            string text = defaultText ?? "";
            signText.networkedText = text;
            signText.authorIdentifier = "";
            try { if (signText.textField != null) signText.textField.rawValue = text; } catch (Exception) { }
            try { signText.RefreshText(); } catch (Exception) { }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': could not set the sign's default text: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    // A copy starts from the original's current state (a flare gun that was reloading stays "reloading"
    // for good, because the timer isn't copied): every state of the prop goes back to 0
    private void ResetPropStates(GameObject copy) {
        int n = 0, was = 0;
        try {
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
                TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
                if (st == null || st.GetComponent<Prop>() == null) continue;
                try {
                    PeckContext ctx = st.currentPeckContext;
                    was = ctx.state; ctx.state = 0; st.currentPeckContext = ctx;
                    PeckContext predicted = st.peckContextForPrediction;
                    predicted.state = 0; st.peckContextForPrediction = predicted;
                    n++;
                } catch (Exception) { }
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': could not reset the item: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    // The torch's light and glow are only redrawn by the game when its state changes, so a torch copied
    // while the original was lit can show lit while its state says off. Once the torch has woken up
    // (the host's spawn arrived), tell the game, on this PC only, what the state really is (twice, in
    // case the host's value arrives a moment later), so the light follows it.
    private int torchShown = -1;
    private float torchAt, torchUntil;
    internal void WatchTorch() {
        if (itemType != PuzzleVanillaItemType.Torch) return;
        GameObject go = FirstPropObject();
        if (go == null || !go.activeInHierarchy) return;
        if (torchAt == 0f) { torchAt = Time.time + 0.5f; torchUntil = Time.time + 60f; return; }
        if (Time.time < torchAt || Time.time > torchUntil) return;
        torchAt = Time.time + 0.5f;
        try {
            foreach (Component c in go.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
                TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
                if (st == null || st.GetComponent<Prop>() == null) continue;
                if (!PeckValue.TryRead(st, out int now) || now == torchShown) continue;
                torchShown = now;
                bool repeats = st.ignoreStateRepeats;
                st.ignoreStateRepeats = false;
                var ctx = new PeckContext();
                ctx.state = now;
                st.DoPeck(ctx, false);
                st.ignoreStateRepeats = repeats;
            }
        } catch (Exception ex) {
            torchUntil = 0f;
            Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': could not redraw the torch: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    // The torch is copied as it is right now: a lit original gives a copy that starts lit (its light, its
    // glow), whatever the copy's real state is. Every copy starts off; the host's real state then reaches
    // everyone through the synced states.
    private void ResetTorch(GameObject copy) {
        int states = 0, lights = 0, glows = 0, wasState = 0;
        try {
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
                TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
                if (st == null || st.GetComponent<Prop>() == null) continue;
                try {
                    PeckContext ctx = st.currentPeckContext;
                    wasState = ctx.state;
                    ctx.state = 0;
                    st.currentPeckContext = ctx;
                    // the game's effects also read this second copy of the state (they are set to "predict")
                    PeckContext predicted = st.peckContextForPrediction;
                    predicted.state = 0;
                    st.peckContextForPrediction = predicted;
                    states++;
                } catch (Exception) { }
            }
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Transform>(), true)) {
                Transform t = c != null ? c.TryCast<Transform>() : null;
                if (t != null && (t.name == "torchlight" || t.name == "flare")) { t.gameObject.SetActive(false); lights++; }
            }
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PropertyBlockHelper>(), true)) {
                PropertyBlockHelper helper = c != null ? c.TryCast<PropertyBlockHelper>() : null;
                if (helper == null) continue;
                try {
                    var floats = helper.floatSettings;
                    for (int i = 0; floats != null && i < floats.Count; i++) {
                        FloatSetting fs = floats[i];
                        string n = (fs.propertyName ?? "").ToLowerInvariant();
                        if (!n.Contains("emissi")) continue;
                        fs.floatValue = 0f;
                        floats[i] = fs;
                        glows++;
                    }
                    helper.Refresh();
                } catch (Exception) { }
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': could not switch the torch off: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    // The flare gun's flare: the copy gets its own recoloured particle system (the game's flares are shared)
    // The flare system(s) this gun plays (the game's shared ones, or our own coloured copy), watched for use
    private List<ParticleSystem> flareTargets;
    private float nextFlareCheck;
    internal void WatchFlare() {
        if (itemType != PuzzleVanillaItemType.FlareGun || copy == null || Time.time < nextFlareCheck) return;
        nextFlareCheck = Time.time + 0.2f;
        if (flareTargets == null) flareTargets = FlareSweep.TargetsOf(copy);
        FlareSweep.Watch(flareTargets);
    }

    private GameObject ownFlare;
    private void ApplyFlareColor(GameObject copy) {
        if (Mathf.Abs(flareColor.r - 1f) < 0.002f && Mathf.Abs(flareColor.g - 1f) < 0.002f && Mathf.Abs(flareColor.b - 1f) < 0.002f) return;
        ownFlare = PuzzleFireworkLauncher.MakeColouredFlare(transform, copy, flareColor, name);
        if (ownFlare == null) Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': no flare particle system found, so Flare Color does nothing.");
    }

    // Most items' colour is a PropertyBlockHelper setting (_TintColor); the
    // walkie-talkie's is the _TintColor of its body material. White keeps the game's own colour.
    private void ApplyItemColor(GameObject copy) {
        if (Mathf.Abs(itemColor.r - 1f) < 0.002f && Mathf.Abs(itemColor.g - 1f) < 0.002f && Mathf.Abs(itemColor.b - 1f) < 0.002f) return;
        int set = 0;
        try {
            if (itemType != PuzzleVanillaItemType.WalkieTalkie) {
                string property = "_TintColor";
                foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PropertyBlockHelper>(), true)) {
                    PropertyBlockHelper helper = c != null ? c.TryCast<PropertyBlockHelper>() : null;
                    if (helper == null) continue;
                    var colors = helper.colorSettings;
                    for (int i = 0; colors != null && i < colors.Count; i++) {
                        ColorSetting cs = colors[i];
                        if (cs.propertyName != property) continue;
                        // the flare gun's second (blue) part keeps its colour
                        if (itemType == PuzzleVanillaItemType.FlareGun && cs.color.b > cs.color.r) continue;
                        cs.color = new Color(itemColor.r, itemColor.g, itemColor.b, cs.color.a);
                        colors[i] = cs;
                        set++;
                    }
                    helper.Refresh();
                }
            } else {
                // The body: the first material of the renderer called WalkieTalkie
                tintBodies.Clear();
                foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
                    Renderer r = c != null ? c.TryCast<Renderer>() : null;
                    if (r == null || r.gameObject.name != "WalkieTalkie") continue;
                    if (PaintBody(r)) { tintBodies.Add(r); set++; }
                }
                // If the game colours the body with a colour setting too (it wins over the material), change that one
                foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PropertyBlockHelper>(), true)) {
                    PropertyBlockHelper helper = c != null ? c.TryCast<PropertyBlockHelper>() : null;
                    if (helper == null || helper.gameObject.name != "WalkieTalkie") continue;
                    var colors = helper.colorSettings;
                    bool changed = false;
                    for (int i = 0; colors != null && i < colors.Count; i++) {
                        ColorSetting cs = colors[i];
                        if (Array.IndexOf(TintProperties, cs.propertyName) < 0) continue;
                        cs.color = new Color(itemColor.r, itemColor.g, itemColor.b, cs.color.a);
                        colors[i] = cs;
                        changed = true;
                        set++;
                    }
                    if (changed) { helper.Refresh(); repaint.Add(helper); }
                }
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': could not colour the {itemType}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
        if (set == 0) Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': found nothing to colour on the {itemType}, so Color does nothing.");
    }

    // The colour properties the body's material may use, in the order tried
    private static readonly string[] TintProperties = { "_TintColor", "_Color", "_BaseColor", "_MainColor", "_Tint" };
    private readonly List<Renderer> tintBodies = new List<Renderer>();   // the walkie-talkie's body, kept coloured
    private float tintUntil = -1f, nextTint;

    // Colours the walkie-talkie's body material. False if it has no colour property to set.
    private bool PaintBody(Renderer r) {
        try {
            Material body = r.material;   // the first material is the body (this makes it this copy's own)
            if (body == null) return false;
            string property = null;
            foreach (string candidate in TintProperties) if (body.HasProperty(candidate)) { property = candidate; break; }
            if (property == null) {
                Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': the walkie-talkie's material ({body.shader.name}) has no colour property I know, so Color does nothing.");
                return false;
            }
            Color old = body.GetColor(property);
            if (Mathf.Abs(old.r - itemColor.r) > 0.002f || Mathf.Abs(old.g - itemColor.g) > 0.002f || Mathf.Abs(old.b - itemColor.b) > 0.002f)
                body.SetColor(property, new Color(itemColor.r, itemColor.g, itemColor.b, old.a));
            return true;
        } catch (Exception) { return false; }
    }

    // The game can set the walkie-talkie's own colour again when it wakes up or spawns, after the copy
    // was painted: for the first minute, put the chosen colour back if it changed
    internal void WatchColor() {
        if (itemType != PuzzleVanillaItemType.WalkieTalkie || tintBodies.Count == 0) return;
        if (tintUntil < 0f) tintUntil = Time.time + 60f;
        if (Time.time > tintUntil || Time.time < nextTint) return;
        nextTint = Time.time + 0.25f;
        foreach (Renderer r in tintBodies) if (r != null) PaintBody(r);
    }

    // The Permo Sign is fixed text: a coordinates helper writes two spots of the vanilla map on it. Cut that
    // off and show the default text instead (the same on every PC, nothing to sync or save).
    private void SetupPermoSign(GameObject copy) {
        try {
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<CoordinatesHelper>(), true))
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            Component lc = copy.GetComponentInChildren(Il2CppType.Of<LocalizedText>(), true);
            LocalizedText label = lc != null ? lc.TryCast<LocalizedText>() : null;
            if (label == null) { Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': the sign's text wasn't found, so Default Text does nothing."); return; }
            string text = defaultText ?? "";
            label.rawValue = text;
            try { label.Refresh(); } catch (Exception) { }
            try { if (label.textElement != null) label.textElement.text = text; } catch (Exception) { }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleVanillaItem] '{name}': could not set the sign's text: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    // Save: the text a player wrote (false while it still shows the default)
    internal bool TryGetWrittenText(out string text) {
        text = null;
        if (signText == null) return false;
        try {
            string now = signText.networkedText ?? "";
            if (now == (defaultText ?? "")) return false;
            text = now;
            return true;
        } catch (Exception) { return false; }
    }

    internal void RestoreText(string text) {
        if (signText == null) return;
        pendingText = text ?? "";
        pendingUntil = Time.time + 15f;
        pendingNext = 0f;
    }

    internal void ApplyPending() {
        if (pendingText == null || !PuzzleSDK.IsHost || Time.time < pendingNext) return;
        pendingNext = Time.time + 0.5f;
        if (Time.time > pendingUntil) { pendingText = null; return; }
        try {
            if ((signText.networkedText ?? "") == pendingText) { pendingText = null; return; }
            signText.NetworknetworkedText = pendingText;
            signText.NetworkauthorIdentifier = "";
            try { signText.RefreshText(); } catch (Exception) { }
        } catch (Exception) { }
    }
}
// The Perspective Counting counter: + and - buttons and a two-digit dial, 0 to max. The game's
// CountingMachine runs it: the buttons step a networked state (valueStorage) that holds the count,
// so every player sees the same number. Without the game's puzzle controller its buttons start
// unprimed, so the host primes them. Max is enforced by the host (see StartCounter).
public class PuzzleCountInput : PuzzleVanillaCopy {
    public PuzzleCountInput(IntPtr ptr) : base(ptr) { }

    // The counter always counts from 0 to 99
    private const int Max = 99;

    // Filled in by the loader from the UnityEvent wired up in the Inspector
    private PuzzleEvent onValueChanged;

    private CountingMachine machine;
    private TrackedPeckState storage;

    // Read-only for scripts: the number on the counter (every player sees the same)
    public int Value { get { return storage != null && PeckValue.TryRead(storage, out int v) ? Mathf.Clamp(v, 0, Max) : 0; } }

    // Called while the copy is still asleep
    internal void SetupCounter(GameObject copy) {
        machine = copy.GetComponent<CountingMachine>();
        if (machine == null) {
            Component c = copy.GetComponentInChildren(Il2CppType.Of<CountingMachine>(), true);
            machine = c != null ? c.TryCast<CountingMachine>() : null;
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
            TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
            if (st != null && st.name == "valueStorage") { storage = st; break; }
        }
        if (machine == null || storage == null)
            Plugin.log.LogWarning($"[PuzzleCountInput] '{name}': the counter's parts weren't found; it may not count.");
    }

    internal void StartCounter() {
        if (!PuzzleSDK.IsHost) return;
        try { if (machine != null) machine.ServerPrime(); } catch (Exception ex) { Plugin.log.LogWarning($"[PuzzleCountInput] '{name}' could not switch its buttons on: {ex.Message}"); }
        // Host: every change of the count (a button, or SetValue) fires onValueChanged for everyone.
        // The game's own limit (CountingMachine.maxValue) is shared by every counter in the game, so
        // this counter's limit of 99 is kept here instead: counting past it puts it back to 99.
        SyncedValue.WatchOnHost(storage, v => {
            if (v > Max) { try { storage.SetState(Max); } catch (Exception) { } return; }   // fires again with Max
            if (onValueChanged != null) onValueChanged.Invoke();
        });
    }

    // Other players: the dial follows the counter's state. A player who joins late (or whose copy of the
    // game's counter had been used) can have a dial that doesn't match the state the host sent, so
    // once the state is there the dial is set to it, and set again whenever they differ.
    private float nextDialCheck;
    private int lastSeen = int.MinValue;
    internal void Watch() {
        if (PuzzleSDK.IsHost || storage == null || machine == null || Time.time < nextDialCheck) return;
        nextDialCheck = Time.time + 0.25f;
        if (!PeckValue.TryRead(storage, out int v)) return;
        bool changed = v != lastSeen;
        lastSeen = v;
        try {
            CountingDisplay dial = machine.countingDisplay;
            if (dial == null) return;
            if (changed || Mathf.RoundToInt(dial.currentValue) != v) {
                machine.currentValue = v;
                dial.SetValue(v);
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleCountInput] '{name}' could not update its dial: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- Callable from code or from an Inspector event ----

    // Host only. Sets the counter (kept between 0 and Max); fires On Value Changed if it changes.
    public void SetValue(int value) {
        if (!PuzzleSDK.IsHost || storage == null) return;
        int v = Mathf.Clamp(value, 0, Max);
        if (v == Value) return;
        try { storage.SetState(v); }
        catch (Exception ex) { Plugin.log.LogWarning($"[PuzzleCountInput] '{name}' could not set {v}: {ex.GetType().Name}: {ex.Message}"); }
    }
}

// The Perspective Counting submit screen. Shows 00 until Check(); then its dials count up to the
// ANSWER (not the player's number) while the screen "thinks", and when they get there it shows
// whether the counter matched (tick or cross) and fires On Correct / On Incorrect. Changing the
// counter afterwards puts it back to 00. The host decides; every player sees the same.
public class PuzzleCountValidator : PuzzleVanillaCopy {
    public PuzzleCountValidator(IntPtr ptr) : base(ptr) { }

    // Set in the Inspector (filled in by the loader); the names must match the Unity-side fields
    private PuzzleCountInput input;   // the counter it checks
    private int answer = -1;          // the number Check() wants; -1 = not set by a script yet, so it is never right

    // Filled in by the loader from the UnityEvents wired up in the Inspector
    private PuzzleEvent onIncorrect;

    // Set in the Inspector (optional): pressing it calls Check(), and it is switched on and off with the screen
    private PuzzlePokeButton pokeButton;

    private const int None = 0, Correct = 1, Incorrect = 2, Checking = 3, Off_ = 4;   // Off_: screen off

    private ValidatorDisplay screen;
    private CountingDisplay digits;
    private SyncedValue syncResult;    // the host's result (None, Correct, Incorrect, Checking)
    private SyncedValue syncAnswer;    // the host's answer, for the count-up on every PC
    private int result, shownAnswer;
    private int shownResult = -1, shownNumber = -1;
    private int checkedValue = -1, lastValue = -1;
    private float checkDoneAt;         // host: when the count-up ends

    // Read-only for scripts
    public int Answer => answer;
    public bool IsCorrect => result == Correct;
    internal PuzzleCountInput Input => input;
    // The puzzle's only Count Input, when the creator left Input empty
    internal void AutoLink(PuzzleCountInput only) { if (input == null) input = only; }
    public bool IsChecking => result == Checking;
    public bool IsOn => result != Off_;
    internal bool IsDesired => desired;

    internal void SetupScreen(GameObject copy) {
        Component v = copy.GetComponentInChildren(Il2CppType.Of<ValidatorDisplay>(), true);
        screen = v != null ? v.TryCast<ValidatorDisplay>() : null;
        Component d = copy.GetComponentInChildren(Il2CppType.Of<CountingDisplay>(), true);
        digits = d != null ? d.TryCast<CountingDisplay>() : null;
        shownAnswer = answer;
        syncResult = new SyncedValue(transform, "count result " + name);
        syncAnswer = new SyncedValue(transform, "count answer " + name);
        if (input == null) Plugin.log.LogWarning($"[PuzzleCountValidator] '{name}' has no Input set, so Check() is always incorrect.");
    }

    internal void StartScreen() {
        if (pokeButton != null) {
            pokeButton.autoEnableSeconds = 0f;   // the screen re-arms the button itself
            pokeButton.pressedHook += Check;   // runs on the host, like Check()
        }
        if (syncResult == null) return;
        syncAnswer.onChanged = a => { shownAnswer = a; Refresh(); };   // other players: the host's answer
        syncResult.onChanged = r => { result = r; Refresh(); };        // other players: the host's result
        syncAnswer.Register();
        syncResult.Register();
    }

    // The count-up, one number at a time, slowing down near the answer like the game's (measured
    // from the vanilla screen: about 0.3 s per number at first, about 1 s for the last ones), then
    // a second's pause before the result
    private const float ResultPause = 2.5f;   // thinking at the answer before the result (the game waits about this long)
    private static float StepDelay(int step, int target) {
        float f = target > 0 ? (float)step / target : 1f;
        return 0.3f + 0.7f * f * f;
    }
    private static float CountUpSeconds(int target) {
        float total = 0f;
        for (int k = 1; k <= target; k++) total += StepDelay(k, target);
        return total;
    }

    // Every PC: the number the dials are on during the count-up
    private int countShown;
    private float nextStepAt = -1f;

    // Other players: the host's state has arrived, so the screen is set to it for sure (the game's own
    // screen may have been left in another state), and again a moment later once everything has settled
    private float refreshAt1 = -1f, refreshAt2 = -1f;
    private void Refresh() {
        shownResult = -1; shownNumber = -1;
        refreshAt1 = Time.time + 0.5f; refreshAt2 = Time.time + 2f;
    }

    // What TurnOn() / TurnOff() asked for last. The screen is only on while this is true AND the puzzle is
    // switched on, so it can be kept up to date silently while the puzzle is off and comes on with it.
    private bool desired = true;
    private bool Wanted() { return desired && PuzzleSDK.IsOnFor(transform); }

    // Host: the puzzle was switched on or off
    private void SyncPower() {
        bool want = Wanted();
        if (want && result == Off_) { SetResult(None); if (pokeButton != null) pokeButton.SetClickable(true); }
        else if (!want && result != Off_) { SetResult(Off_); if (pokeButton != null) pokeButton.SetClickable(false); }
    }

    // Called from Update every frame, on every PC
    internal void Watch() {
        if (PuzzleSDK.IsHost) SyncPower();
        if (refreshAt1 > 0f && Time.time >= refreshAt1) { refreshAt1 = -1f; shownResult = -1; shownNumber = -1; }
        else if (refreshAt2 > 0f && Time.time >= refreshAt2) { refreshAt2 = -1f; shownResult = -1; shownNumber = -1; }
        if (PuzzleSDK.IsHost) {
            // The count-up has reached the answer: show and fire the result
            if (result == Checking && Time.time >= checkDoneAt) {
                bool correct = input != null && checkedValue == answer;
                SetResult(correct ? Correct : Incorrect);
                if (correct) { PuzzleSDK puzzle = PuzzleSDK.Of(transform); if (puzzle != null) puzzle.Win(); }
                else if (onIncorrect != null) onIncorrect.Invoke();
                // A press switches the button off by itself: ready for another try, unless the events turned the screen off
                if (pokeButton != null && result != Off_) pokeButton.SetClickable(true);
            }
            // The counter changed after a result: back to 00
            int value = input != null ? input.Value : 0;
            if (value != lastValue) {
                lastValue = value;
                if ((result == Correct || result == Incorrect) && value != checkedValue) SetResult(None);
            }
        }

        // Every PC: 00, counting up one number at a time while checking, the answer once done
        int target = Mathf.Clamp(shownAnswer, 0, 99);
        int number;
        if (result == Checking) {
            if (nextStepAt < 0f) { countShown = 0; nextStepAt = Time.time + StepDelay(1, target); }
            while (countShown < target && Time.time >= nextStepAt) {
                countShown++;
                nextStepAt += StepDelay(countShown + 1, target);
            }
            number = countShown;
        } else {
            nextStepAt = -1f;
            number = result == Correct || result == Incorrect ? target : 0;   // 00 when blank or off
        }
        if (number != shownNumber) {
            shownNumber = number;
            try { if (digits != null) digits.SetValue(number); } catch (Exception) { }
        }
        if (result != shownResult) {
            shownResult = result;
            var state = result == Correct ? ValidatorDisplay.ValidatorState.Success
                      : result == Incorrect ? ValidatorDisplay.ValidatorState.Failure
                      : result == Checking ? ValidatorDisplay.ValidatorState.Thinking
                      : result == Off_ ? ValidatorDisplay.ValidatorState.Off
                      : ValidatorDisplay.ValidatorState.Blank;
            try { if (screen != null) screen.SetDisplay(state); } catch (Exception) { }
        }
    }

    private void SetResult(int r) {
        result = r;
        if (syncResult != null) syncResult.Push(r);
    }

    // ---- Callable from code or from an Inspector event ----

    // Host only. Counts the dials up to the answer, then shows whether the counter matched and
    // fires On Correct or On Incorrect. The counter's number is taken now.
    public void Check() {
        if (!PuzzleSDK.IsHost || result == Checking || result == Off_) return;   // nothing while off or busy
        checkedValue = input != null ? input.Value : -1;
        lastValue = checkedValue;
        shownAnswer = answer;
        if (syncAnswer != null) syncAnswer.Push(Mathf.Clamp(answer, 0, 99));
        checkDoneAt = Time.time + CountUpSeconds(Mathf.Clamp(answer, 0, 99)) + ResultPause;
        SetResult(Checking);
    }

    // Host only. Changes the answer; the screen goes back to 00 (an off screen stays off).
    public void SetAnswer(int value) {
        if (!PuzzleSDK.IsHost) return;
        answer = value;
        if (result != Off_) SetResult(None);
    }

    // Host only. Turns the screen on: 00, ready to check.
    // (Only shows while the puzzle is switched on; until then it is remembered and shown when the puzzle starts.)
    public void TurnOn() {
        if (!PuzzleSDK.IsHost) return;
        desired = true;
        if (!Wanted()) return;
        SetResult(None);
        if (pokeButton != null) pokeButton.SetClickable(true);
    }

    // Host only. Turns the screen off (cancels a check in progress; shows 00). Check() does
    // nothing while it is off.
    public void TurnOff() {
        if (!PuzzleSDK.IsHost) return;
        desired = false;
        SetResult(Off_);
        if (pokeButton != null) pokeButton.SetClickable(false);
    }
}
public class PuzzleIntercom : PuzzleVanillaCopy { public PuzzleIntercom(IntPtr ptr) : base(ptr) { } }
public class PuzzleIntercomSpeaker : PuzzleVanillaCopy { public PuzzleIntercomSpeaker(IntPtr ptr) : base(ptr) { } }
// The sound match pegs (a grabbable speaker peg, and the fixed speaker it is matched to). Set in
// the Inspector: one of the game's nine sound match tracks, or a custom sound (see SoundPegAudio).
public class PuzzleSoundPeg : PuzzleVanillaCopy {
    public PuzzleSoundPeg(IntPtr ptr) : base(ptr) { }
    internal PuzzleSoundTrack sound;   // Inspector: the game's track
    internal AudioClip customClip;     // Inspector: plays instead of the track when set
    internal PuzzleSoundTrack Sound { [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] get { return sound; } }
}
// The fixed answer speaker. When the puzzle starts it plays the sound of one Sound Peg from the puzzle (the
// puzzle deals the pegs out, see PuzzlePile), and it is silent while the puzzle is off.
public class PuzzleSoundPegAnswer : PuzzleVanillaCopy {
    public PuzzleSoundPegAnswer(IntPtr ptr) : base(ptr) { }

    private PegTileMusicConnector connector;
    private MusicPlayer player;
    private readonly List<AudioSource> sources = new List<AudioSource>();
    private SyncedValue sync;   // the host's choice, plus one: 0 = silent, 1-9 = game track 1-9, 11+ = custom sound (see ClipsIn)
    private int current = -1;   // -1 = silent; 0-8 = game track 1-9; 10+ = custom sound
    private bool wasCustom;

    internal bool Ready { get { return sync == null || sync.Ready; } }

    // Read-only for scripts: what it plays now ("sound_1".."sound_9", or "sound_" and a custom sound's name); "" while silent
    public string CurrentSound { get { return current < 0 ? "" : current < 10 ? "sound_" + (current + 1) : (ClipFor(current) != null ? "sound_" + ClipFor(current).name : ""); } }

    // The custom sounds on the Sound Pegs in this puzzle, in a fixed order (by name), so every PC numbers them the same
    private List<AudioClip> Clips {
        get {
            int root = transform.root.GetInstanceID();
            var list = new List<AudioClip>();
            foreach (PuzzleVanillaCopy other in all) {
                PuzzleSoundPeg peg = other as PuzzleSoundPeg;
                if (peg == null || peg.transform.root.GetInstanceID() != root || peg.customClip == null || list.Contains(peg.customClip)) continue;
                list.Add(peg.customClip);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }
    }
    private AudioClip ClipFor(int code) { var list = Clips; int i = code - 10; return i >= 0 && i < list.Count ? list[i] : null; }

    // Called while the copy is still asleep (every PC, in the same order, so the sync gets the same id)
    internal void SetupSound(GameObject copy) {
        Component c = copy.GetComponentInChildren(Il2CppType.Of<PegTileMusicConnector>(), true);
        connector = c != null ? c.TryCast<PegTileMusicConnector>() : null;
        Component m = copy.GetComponentInChildren(Il2CppType.Of<MusicPlayer>(), true);
        player = m != null ? m.TryCast<MusicPlayer>() : null;
        foreach (Component a in copy.GetComponentsInChildren(Il2CppType.Of<AudioSource>(), true)) {
            AudioSource source = a != null ? a.TryCast<AudioSource>() : null;
            if (source != null) { sources.Add(source); source.mute = true; }   // silent until the puzzle deals it a sound
        }
        sync = new SyncedValue(transform, "sound " + name);
    }

    internal void StartSound() {
        if (sync == null) return;
        sync.onChanged = v => Apply(v - 1);   // other players: the host changed the sound
        sync.Register();
    }

    // Host only. Plays this Sound Peg's sound for everyone.
    internal void PlayPeg(PuzzleSoundPeg peg) {
        if (!PuzzleSDK.IsHost || peg == null) return;
        int code = (int)peg.Sound;
        if (peg.customClip != null) {
            int index = Clips.IndexOf(peg.customClip);
            if (index >= 0) code = 10 + index;
        }
        Apply(code);
        if (sync != null) sync.Push(code + 1);
    }

    // Host only. Silent (the puzzle is off, or there was no Sound Peg left for it).
    internal void Silence() {
        if (!PuzzleSDK.IsHost) return;
        Apply(-1);
        if (sync != null) sync.Push(0);
    }

    private void Apply(int code) {
        if (code < 0) {
            current = -1;
            foreach (AudioSource source in sources) { try { source.mute = true; } catch (Exception) { } }
            return;
        }
        Play(code);
        foreach (AudioSource source in sources) { try { source.mute = false; } catch (Exception) { } }
    }

    private void Play(int code) {
        bool wasCustomBefore = wasCustom;
        current = code;
        wasCustom = code >= 10;
        try {
            if (code < 10) {
                SoundPegAudio.Unset(player);
                PropGroup group = (PropGroup)Enum.Parse(typeof(PropGroup), "PegTileA" + (Mathf.Clamp(code, 0, 8) + 1));
                if (connector != null) {
                    if (wasCustomBefore) {
                        // The connector skips a track it believes it already has, and the player is busy with
                        // the custom clip: make the track look new, then load it into the player again
                        PropGroup other = (PropGroup)Enum.Parse(typeof(PropGroup), "PegTileA" + (code == 0 ? 2 : 1));
                        connector.propGroup = other;
                    }
                    connector.SetPegTile(group);
                }
                if (wasCustomBefore && player != null) {
                    try { player.Index = Mathf.Clamp(code, 0, 8); } catch (Exception) { }
                    try { MusicGroup config = player.MusicConfig; if (config != null) player.SetAsset(config, Mathf.Clamp(code, 0, 8)); }
                    catch (Exception ex) { Plugin.log.LogWarning($"[PuzzleSoundPegAnswer] '{name}' could not reload the game's track: {ex.GetType().Name}: {ex.Message}"); }
                }
            } else {
                AudioClip clip = ClipFor(code);
                if (clip == null || player == null) return;
                SoundPegAudio.Set(player, clip);
                player.Play(clip);
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleSoundPegAnswer] '{name}' could not change its sound: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// The game's sound tracks (musicGroup_soundMatch's nine stems). Must stay identical, in the
// same order, to the Unity SDK's enum.
public enum PuzzleSoundTrack { Sound1, Sound2, Sound3, Sound4, Sound5, Sound6, Sound7, Sound8, Sound9 }

// How the game picks a sound peg's track: the peg's PegTileMusicConnector has a peg group
// (PegTileA1..A9); its music set maps that to a stem of musicGroup_soundMatch (A1 -> stem 0 ...
// A9 -> stem 8) and its MusicPlayer plays that stem, in sync with the others. A peg also carries
// its group in its own prop groups.
//   Track: the copy gets that track's peg group everywhere, so the game sets it up itself.
//   Custom clip: the music player plays the creator's sound instead (a Harmony hook on
//   MusicPlayer.Play swaps the clip for players that have one).
internal static class SoundPegAudio {
    private static readonly Dictionary<IntPtr, AudioClip> customByPlayer = new Dictionary<IntPtr, AudioClip>();
    private static readonly Dictionary<IntPtr, string> soundByProp = new Dictionary<IntPtr, string>();

    // The sound a sound peg plays ("sound_1".."sound_9", or "sound_" and its custom sound's name); null if the
    // prop isn't a sound peg
    // The game's own sound pegs are recognised too: they carry a PegTileMusicConnector whose peg group
    // (PegTileA1..A9) is the track, so they count as "sound_1".."sound_9"
    internal static string SoundOf(Prop prop) {
        if (prop == null) return null;
        if (soundByProp.TryGetValue(prop.Pointer, out string s)) return s;
        if (vanillaSoundByProp.TryGetValue(prop.Pointer, out string known)) return known;
        string found = ReadVanillaSound(prop);
        if (found != null) vanillaSoundByProp[prop.Pointer] = found;   // only remember a find: a peg still being set up may read null for a moment
        return found;
    }

    private static readonly Dictionary<IntPtr, string> vanillaSoundByProp = new Dictionary<IntPtr, string>();

    private static string ReadVanillaSound(Prop prop) {
        try {
            Component c = prop.GetComponentInChildren(Il2CppType.Of<PegTileMusicConnector>(), true);
            PegTileMusicConnector connector = c != null ? c.TryCast<PegTileMusicConnector>() : null;
            if (connector == null) return null;   // not a sound peg (a picture tile has none)
            string track = TrackOf(connector.propGroup.ToString());
            if (track != null) return track;
            var groups = prop.propGroups;
            for (int i = 0; groups != null && i < groups.Count; i++) {
                track = TrackOf(groups[i].ToString());
                if (track != null) return track;
            }
        } catch (Exception) { }
        return null;
    }

    // "PegTileA3" -> "sound_3" (A1..A9); null for anything else
    private static string TrackOf(string group) =>
        group != null && group.StartsWith("PegTileA") && int.TryParse(group.Substring(8), out int n) && n >= 1 && n <= 9 ? "sound_" + n : null;

    // "sound_3", "Sound 3", "SOUND3" -> "sound_3"; "Sound_level_up" -> "sound_level_up"; other names as they are (trimmed)
    internal static string Normalize(string name) {
        string t = (name ?? "").Trim();
        string squeezed = t.ToLowerInvariant().Replace(" ", "");
        if (squeezed.StartsWith("sound") && int.TryParse(squeezed.Substring(5).TrimStart('_'), out int n) && n >= 1 && n <= 9) return "sound_" + n;
        if (t.ToLowerInvariant().StartsWith("sound_")) return "sound_" + t.Substring(6).Trim();
        return t;
    }

    internal static void Apply(PuzzleVanillaCopy owner, GameObject copy, PuzzleSoundTrack track, AudioClip clip) {
        var log = Plugin.log;
        int index = Mathf.Clamp((int)track, 0, 8);
        PropGroup group;
        try { group = (PropGroup)Enum.Parse(typeof(PropGroup), "PegTileA" + (index + 1)); }
        catch (Exception) { log.LogWarning($"[{owner.GetType().Name}] '{owner.name}': the game has no peg group PegTileA{index + 1}."); return; }

        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PegTileMusicConnector>(), true)) {
            PegTileMusicConnector connector = c != null ? c.TryCast<PegTileMusicConnector>() : null;
            if (connector == null) continue;
            try { connector.propGroup = group; } catch (Exception ex) { log.LogWarning($"[{owner.GetType().Name}] could not set the track: {ex.Message}"); }
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<MusicPlayer>(), true)) {
            MusicPlayer player = c != null ? c.TryCast<MusicPlayer>() : null;
            if (player == null) continue;
            try { player.Index = index; } catch (Exception) { }
            if (clip != null) customByPlayer[player.Pointer] = clip;
        }
        // A sound peg remembers its sound by name, for peg boards that check sounds
        if (owner is PuzzleSoundPeg) {
            string soundName = clip != null ? "sound_" + clip.name : "sound_" + (index + 1);
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Prop>(), true)) {
                Prop p = c != null ? c.TryCast<Prop>() : null;
                if (p != null) soundByProp[p.Pointer] = soundName;
            }
        }

        // The peg prop's own groups: swap its sound match group for the chosen one
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Prop>(), true)) {
            Prop prop = c != null ? c.TryCast<Prop>() : null;
            if (prop == null) continue;
            try {
                var groups = prop.propGroups;
                for (int i = 0; groups != null && i < groups.Count; i++)
                    if (groups[i].ToString().StartsWith("PegTileA")) groups[i] = group;
            } catch (Exception) { }
        }
    }

    internal static void InstallHook(HarmonyLib.Harmony harmony) {
        try {
            MethodInfo target = HarmonyLib.AccessTools.Method(typeof(MusicPlayer), GameRefs.Hooks.MusicPlay, new[] { typeof(AudioClip) });
            if (target == null) throw new MissingMethodException("MusicPlayer.Play(AudioClip) not found");
            harmony.Patch(target, prefix: new HarmonyLib.HarmonyMethod(typeof(SoundPegAudio).GetMethod(nameof(PlayPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[SoundPeg] could not hook the music player ({ex.GetType().Name}: {ex.Message}); custom sound peg clips won't play");
        }
    }

    private static void PlayPrefix(MusicPlayer __instance, ref AudioClip __0) {
        if (__instance != null && customByPlayer.TryGetValue(__instance.Pointer, out AudioClip mine) && mine != null) __0 = mine;
    }

    internal static void Set(MusicPlayer player, AudioClip clip) { if (player != null && clip != null) customByPlayer[player.Pointer] = clip; }
    internal static void Unset(MusicPlayer player) { if (player != null) customByPlayer.Remove(player.Pointer); }

    internal static void Clear() { customByPlayer.Clear(); soundByProp.Clear(); vanillaSoundByProp.Clear(); }
}
public class PuzzleNoItemsGate : PuzzleVanillaCopy {
    public PuzzleNoItemsGate(IntPtr ptr) : base(ptr) { }
    internal bool oneWay = true;   // Inspector: only from front to back (the plain gate; red plaques)
    internal bool blindMask;       // Inspector: the blindfold catwalk's gate instead of the plain one (two-way)
}
// The timer ball dispenser. Picking up its ball starts the ball's countdown; when it runs out the
// ball goes back into the dispenser (the game's own logic: PeckEffectTimer, then PeckEffectPropToHome).
public class PuzzleTimerSpawner : PuzzleVanillaCopy {
    public PuzzleTimerSpawner(IntPtr ptr) : base(ptr) { }

    private const float GameTime = 10f;   // the Obby's timer ball

    // Set in the Inspector (filled in by the loader); the name must match the Unity-side field
    private float time = GameTime;   // seconds the ball lasts; when it runs out it goes back to the dispenser

    // Called while the copy is still asleep: the ball's countdown, and its countdown animation's
    // speed to match (the game's is set for 10 seconds)
    internal void ApplyTime(GameObject copy) {
        float seconds = Mathf.Max(0.5f, time);
        int timers = 0;
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckEffectTimer>(), true)) {
            PeckEffectTimer timer = c != null ? c.TryCast<PeckEffectTimer>() : null;
            if (timer == null) continue;
            try { timer.duration = seconds; timers++; }
            catch (Exception ex) { Plugin.log.LogWarning($"[PuzzleTimerSpawner] '{name}': could not set the timer: {ex.GetType().Name}: {ex.Message}"); }
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TimerAnimation>(), true)) {
            TimerAnimation anim = c != null ? c.TryCast<TimerAnimation>() : null;
            if (anim == null) continue;
            try { anim.animationSpeed = anim.animationSpeed * GameTime / seconds; } catch (Exception) { }
        }
        if (timers == 0) Plugin.log.LogWarning($"[PuzzleTimerSpawner] '{name}': no timer found in the copy, so Time does nothing.");
    }
}
// The timer ball holder. onTimerPlaced fires each time a timer ball goes in (host decides; runs
// for everyone like every event).
public class PuzzleTimerGoal : PuzzleVanillaCopy {
    public PuzzleTimerGoal(IntPtr ptr) : base(ptr) { }

    // Filled in by the loader from the UnityEvent wired up in the Inspector
    private PuzzleEvent onTimerPlaced;

    private bool hasTimer;
    private float nextCheck;

    // Called from Update a few times a second: has a ball gone in (or come out)?
    internal void Watch() {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.2f;
        bool now = false;
        try {
            foreach (PropHome home in Homes)
                if (home != null && home.pinnedProp != null) { now = true; break; }
        } catch (Exception) { }
        if (now == hasTimer) return;
        hasTimer = now;
        if (now && onTimerPlaced != null && PuzzleSDK.IsHost) onTimerPlaced.Invoke();
    }
}
// The Flare Run flare launcher. Its flare is drawn by one particle system the game shares between
// all of its flares ('FlareParticleSystem', outside the launcher). For a custom colour the copy
// gets its own copy of that particle system, recoloured, so the game's flares keep theirs.
public class PuzzleFireworkLauncher : PuzzleVanillaCopy {
    public PuzzleFireworkLauncher(IntPtr ptr) : base(ptr) { }

    // Set in the Inspector (filled in by the loader); the name must match the Unity-side field.
    // White = the game's own colours.
    private Color color = Color.white;

    private GameObject ownFlare;   // this launcher's recoloured particle system (under this object)
    private TrackedPeckState buttonState;   // the button's own state (0 ready, 1 fired, 2 off): everything the launcher does follows it

    // Called while the copy is still asleep. The launcher has no button of its own: it is fired from
    // code or from an Inspector event (Fire). The button's click target, collider and model go; its
    // logic stays, because the flare, the sound and the cool-down all run from it.
    internal void RemoveButton(GameObject copy) {
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
            TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
            if (st != null && st.name == "PokeButtonPeckLogic") { buttonState = st; break; }
        }
        int removed = 0;
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<CastableTarget>(), true)) {
            if (c == null) continue;
            UnityEngine.Object.DestroyImmediate(c);
            removed++;
        }
        Transform button = null;
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Transform>(), true)) {
            if (c != null && c.name == "BasicPokeButton") { button = c.transform; break; }
        }
        if (button != null) {
            foreach (Component c in button.GetComponentsInChildren(Il2CppType.Of<Collider>(), true)) {
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }
            // The button's model, and the effects that only animate it (they would switch parts of it back on)
            var doomed = new List<GameObject>();
            foreach (Component c in button.GetComponentsInChildren(Il2CppType.Of<Transform>(), true)) {
                if (c != null && (c.name == "BasicPokeButtonModel" || c.name == "Effects")) doomed.Add(c.gameObject);
            }
            foreach (GameObject go in doomed) UnityEngine.Object.DestroyImmediate(go);
            foreach (Component c in button.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
                Renderer r = c != null ? c.TryCast<Renderer>() : null;
                if (r != null) r.enabled = false;   // anything left over
            }
        }
        // A timer that was running in the original is copied as running, and then never starts again
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckEffectTimerNetworked>(), true)) {
            PeckEffectTimerNetworked timer = c != null ? c.TryCast<PeckEffectTimerNetworked>() : null;
            if (timer == null) continue;
            try { timer.timerActive = false; timer.endTime = 0; } catch (Exception) { }
        }
        // The clone inherits the original's current state; if that was "fired" (cooling down) its timer
        // isn't cloned, so it would never finish. Start from ready.
        if (buttonState != null) {
            try {
                PeckContext ctx = buttonState.currentPeckContext;
                int was = ctx.state;
                ctx.state = 0;
                buttonState.currentPeckContext = ctx;
                PeckContext predicted = buttonState.peckContextForPrediction;
                predicted.state = 0;
                buttonState.peckContextForPrediction = predicted;
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[PuzzleFireworkLauncher] '{name}': could not reset the launcher: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }
        if (buttonState == null || removed == 0 || button == null)
            Plugin.log.LogWarning($"[PuzzleFireworkLauncher] '{name}': the launcher's button parts weren't all found (state {(buttonState != null)}, click target {removed}, button {(button != null)}); Fire may not work or the button may still show.");
    }

    // ---- Callable from code or from an Inspector event ----

    // Fires the flare (the host fires it for everyone). Nothing happens while it is still cooling down
    public void Fire() {
        if (!PuzzleSDK.IsHost) return;
        if (buttonState == null) { Plugin.log.LogWarning($"[PuzzleFireworkLauncher] '{name}': Fire called but the launcher isn't set up."); return; }
        try {
            if (PeckValue.TryRead(buttonState, out int now) && now != 0) return;   // already fired (or switched off)
            buttonState.SetState(1);
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleFireworkLauncher] '{name}' could not fire: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Called while the copy is still asleep
    internal void ApplyColor(GameObject copy) {
        if (Approximately(color, Color.white)) return;
        var log = Plugin.log;
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckEffectParticleNetworked>(), true)) {
            PeckEffectParticleNetworked effect = c != null ? c.TryCast<PeckEffectParticleNetworked>() : null;
            if (effect == null) continue;
            try {
                ParticleSystem shared = effect.targetParticleSystem;
                if (shared == null) continue;
                GameObject source = shared.gameObject;
                ownFlare = UnityEngine.Object.Instantiate((UnityEngine.Object)source, transform).Cast<GameObject>();
                ownFlare.name = "FlareParticleSystem (" + name + ")";
                ownFlare.transform.position = source.transform.position;
                ownFlare.transform.rotation = source.transform.rotation;
                effect.targetParticleSystem = ownFlare.GetComponent<ParticleSystem>();
                Recolour(ownFlare, color);
                RepointTransforms(copy, source.transform, ownFlare.transform);
                RepointBehaviours(copy, source.transform, ownFlare.transform, name);
            } catch (Exception ex) {
                log.LogWarning($"[PuzzleFireworkLauncher] '{name}' could not colour its flare: {ex.GetType().Name}: {ex.Message}");
            }
        }
        if (ownFlare == null) log.LogWarning($"[PuzzleFireworkLauncher] '{name}': no flare particle system found, so Color does nothing.");
    }

    // Gives a copy its own recoloured flare particle system (null if it has none); shared with the Flare Gun item
    internal static GameObject MakeColouredFlare(Transform parent, GameObject copy, Color colour, string owner) {
        GameObject made = null;
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckEffectParticleNetworked>(), true)) {
            PeckEffectParticleNetworked effect = c != null ? c.TryCast<PeckEffectParticleNetworked>() : null;
            if (effect == null) continue;
            try {
                ParticleSystem shared = effect.targetParticleSystem;
                if (shared == null) continue;
                GameObject source = shared.gameObject;
                made = UnityEngine.Object.Instantiate((UnityEngine.Object)source, parent).Cast<GameObject>();
                made.name = "FlareParticleSystem (" + owner + ")";
                made.transform.position = source.transform.position;
                made.transform.rotation = source.transform.rotation;
                effect.targetParticleSystem = made.GetComponent<ParticleSystem>();
                Recolour(made, colour);
                RepointTransforms(copy, source.transform, made.transform);
                RepointBehaviours(copy, source.transform, made.transform, owner);
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[PuzzleFireworkLauncher] '{owner}' could not colour its flare: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return made;
    }

    private List<ParticleSystem> flareTargets;
    // The copy was cloned from a launcher that was reloading: its gun is shut and its timer shows, and
    // the game only redraws those when the state changes (the host's first Fire() did that). Once the
    // part that holds the timer has woken up (the host spawns it), tell the game, on this PC only,
    // that the state is "ready".
    private bool readyShown;
    private float readyAt;
    private void ShowReady() {
        if (readyShown || buttonState == null) return;
        try {
            Component timer = copy.GetComponentInChildren(Il2CppType.Of<PeckEffectTimerNetworked>(), true);
            if (timer == null || !timer.gameObject.activeInHierarchy) return;
            if (readyAt == 0f) { readyAt = Time.time + 0.5f; return; }
            if (Time.time < readyAt) return;
            readyShown = true;
            var ctx = new PeckContext();
            ctx.state = 0;
            buttonState.DoPeck(ctx, false);
        } catch (Exception ex) {
            readyShown = true;
            Plugin.log.LogWarning($"[PuzzleFireworkLauncher] '{name}': could not show the launcher as ready: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    private float nextFiredCheck;
    // Called from Update: keeps an eye on the flare system(s) this launcher plays
    internal void WatchFired() {
        if (copy == null || Time.time < nextFiredCheck) return;
        nextFiredCheck = Time.time + 0.2f;
        ShowReady();
        if (flareTargets == null) flareTargets = FlareSweep.TargetsOf(copy);
        FlareSweep.Watch(flareTargets);
    }

    private static bool Approximately(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f && Mathf.Abs(a.b - b.b) < 0.002f;

    // Every particle system starts in the colour (keeping its own transparency); lights take the
    // colour too. (The game's interop doesn't include the colour-over-lifetime module, so a fade
    // the effect has of its own still tints the colour.)
    private static void Recolour(GameObject go, Color color) {
        foreach (Component c in go.GetComponentsInChildren(Il2CppType.Of<ParticleSystem>(), true)) {
            ParticleSystem ps = c != null ? c.TryCast<ParticleSystem>() : null;
            if (ps == null) continue;
            try {
                var main = ps.main;
                float alpha = 1f;
                try { alpha = main.startColor.color.a; } catch (Exception) { }
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(color.r, color.g, color.b, alpha));
            } catch (Exception ex) { Plugin.log.LogWarning($"[PuzzleFireworkLauncher] start colour: {ex.GetType().Name}: {ex.Message}"); }
        }
        foreach (Component c in go.GetComponentsInChildren(Il2CppType.Of<Light>(), true)) {
            Light light = c != null ? c.TryCast<Light>() : null;
            if (light != null) light.color = new Color(color.r, color.g, color.b, 1f);
        }
    }

    // The copy's effects that follow the shared particle system (its light follower) follow ours instead
    private static void RepointTransforms(GameObject copy, Transform oldRoot, Transform newRoot) {
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckEffectAudio>(), true)) {
            PeckEffectAudio audio = c != null ? c.TryCast<PeckEffectAudio>() : null;
            if (audio == null) continue;
            foreach (PropertyInfo prop in typeof(PeckEffectAudio).GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if (prop.PropertyType != typeof(PeckTransformReference) || !prop.CanRead) continue;
                try {
                    PeckTransformReference reference = (PeckTransformReference)prop.GetValue(audio);
                    Transform t = reference != null ? reference.customTransform : null;
                    if (t == null || !t.IsChildOf(oldRoot)) continue;
                    string path = PathUnder(t, oldRoot);
                    Transform mine = path.Length == 0 ? newRoot : newRoot.Find(path);
                    if (mine == null) continue;
                    reference.customTransform = mine;
                    if (prop.CanWrite) prop.SetValue(audio, reference);   // a struct: write the changed copy back
                } catch (Exception) { }
            }
        }
    }

    // The copy's flare sound and light switches ('turn on' lists in its audio effect) point at the game's own
    // flare system, so with a coloured copy the sound and light played on (and stayed on at) the game's one.
    // They are pointed at the copy's matching parts instead. (Done by reflection: the lists sit inside structs.)
    private static void RepointBehaviours(GameObject copy, Transform oldRoot, Transform newRoot, string owner) {
        int changed = 0;
        try {
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckEffectAudio>(), true)) {
                PeckEffectAudio audio = c != null ? c.TryCast<PeckEffectAudio>() : null;
                if (audio == null) continue;
                PropertyInfo outcomesProp = typeof(PeckEffectAudio).GetProperty("outcomesPerState");
                object outcomes = outcomesProp != null ? outcomesProp.GetValue(audio) : null;
                var list = outcomes as System.Collections.IEnumerable;
                if (list == null) continue;
                foreach (object outcome in list) {
                    if (outcome == null) continue;
                    foreach (string field in new[] { "turnOnBehaviours", "turnOffBehaviours" }) {
                        PropertyInfo fp = outcome.GetType().GetProperty(field);
                        object arr = fp != null ? fp.GetValue(outcome) : null;
                        if (arr == null) continue;
                        PropertyInfo indexer = arr.GetType().GetProperty("Item");
                        PropertyInfo length = arr.GetType().GetProperty("Length");
                        if (indexer == null || length == null) continue;
                        int n = Convert.ToInt32(length.GetValue(arr));
                        for (int i = 0; i < n; i++) {
                            MonoBehaviour b = indexer.GetValue(arr, new object[] { i }) as MonoBehaviour;
                            if (b == null || !b.transform.IsChildOf(oldRoot)) continue;
                            string path = PathUnder(b.transform, oldRoot);
                            Transform mine = path.Length == 0 ? newRoot : newRoot.Find(path);
                            Component same = mine != null ? mine.GetComponent(b.GetIl2CppType()) : null;
                            MonoBehaviour nb = same != null ? same.TryCast<MonoBehaviour>() : null;
                            if (nb == null) continue;
                            indexer.SetValue(arr, nb, new object[] { i });
                            changed++;
                        }
                    }
                }
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleFireworkLauncher] '{owner}' could not point the flare sound at its own flare: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    private static string PathUnder(Transform t, Transform root) {
        string path = "";
        for (Transform p = t; p != null && p != root; p = p.parent) path = path.Length == 0 ? p.name : p.name + "/" + path;
        return path;
    }

    // (ownFlare sits under this object, so it goes with the puzzle)
}
// The Egg Hunt's round peg. Its colour isn't a material: the peg's PropertyBlockHelper sets
// _RColor (the colour) and _LayerTint (the camo layer's tint) on the camo material.
public class PuzzleCirclePeg : PuzzleVanillaCopy {
    public PuzzleCirclePeg(IntPtr ptr) : base(ptr) { }

    // Set in the Inspector (filled in by the loader); the name must match the Unity-side field.
    // White = the game's own colour (the red peg).
    private Color color = Color.white;

    // Called while the copy is still asleep. The game's red peg has _RColor (0.718, 0.18, 0) and
    // _LayerTint (0.867, 0.239, 0): the layer is the colour, a little brighter.
    internal void ApplyColor(GameObject copy) {
        if (Mathf.Abs(color.r - 1f) < 0.002f && Mathf.Abs(color.g - 1f) < 0.002f && Mathf.Abs(color.b - 1f) < 0.002f) return;
        Color layer = new Color(Mathf.Clamp01(color.r * 1.2f), Mathf.Clamp01(color.g * 1.2f), Mathf.Clamp01(color.b * 1.2f));
        int set = 0;
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PropertyBlockHelper>(), true)) {
            PropertyBlockHelper helper = c != null ? c.TryCast<PropertyBlockHelper>() : null;
            if (helper == null) continue;
            try {
                var colors = helper.colorSettings;
                for (int i = 0; colors != null && i < colors.Count; i++) {
                    ColorSetting cs = colors[i];
                    if (cs.propertyName == "_RColor") { cs.color = new Color(color.r, color.g, color.b, cs.color.a); colors[i] = cs; set++; }
                    else if (cs.propertyName == "_LayerTint") { cs.color = new Color(layer.r, layer.g, layer.b, cs.color.a); colors[i] = cs; set++; }
                }
                repaint.Add(helper);
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[PuzzleCirclePeg] '{name}' could not colour the peg: {ex.GetType().Name}: {ex.Message}");
            }
        }
        if (set == 0) Plugin.log.LogWarning($"[PuzzleCirclePeg] '{name}': no colour settings found on the peg, so Color does nothing.");
    }
}
// The Egg Hunt's round peg board. onFull fires when every slot has a peg; onNotFull when a peg is
// taken off a full board. Its slots are modded holders (see SetupCopy), but pegs cross over (see
// CrossesWithGame): the game's own pegs can be hung on it, and modded ones fit the game's boards.
public class PuzzleCirclePegBoard : PuzzleVanillaCopy {
    public PuzzleCirclePegBoard(IntPtr ptr) : base(ptr) { }

    // Filled in by the loader from the UnityEvents wired up in the Inspector
    private PuzzleEvent onFull;
    private PuzzleEvent onNotFull;

    private int filled = -1;   // -1 = not checked yet
    private bool full;
    private float nextCheck;

    // Read-only for scripts
    public int SlotCount => Homes.Count;
    public int FilledCount => Math.Max(0, filled);
    public bool IsFull => full;

    // Called from Update a few times a second
    internal void Watch() {
        if (Time.time < nextCheck || Homes.Count == 0) return;
        nextCheck = Time.time + 0.2f;
        int now = 0;
        try {
            foreach (PropHome home in Homes)
                if (home != null && home.pinnedProp != null) now++;
        } catch (Exception) { return; }
        bool first = filled < 0;
        if (now == filled) return;
        filled = now;
        bool nowFull = now == Homes.Count;
        if (nowFull == full) return;
        full = nowFull;
        if (first && !nowFull) return;   // starts empty: nothing to report
        if (!PuzzleSDK.IsHost) return;   // the host fires events for everyone
        if (nowFull) { if (onFull != null) onFull.Invoke(); }
        else if (onNotFull != null) onNotFull.Invoke();
    }
}
// The Black Tower's N-hold button. Every button with the same Group (in the same puzzle) must be
// held at once. The case shows how many are needed: 2, 3 or 4 dots (4 for more than four), and a
// dot lights for each button held. Copied from the 4-button set; the copy's own link to the game's
// buttons (a PeckRelay reading the Black Tower's set) is removed and the mod runs the group instead.
public class PuzzleButtonNHold : PuzzleVanillaCopy {
    public PuzzleButtonNHold(IntPtr ptr) : base(ptr) { }

    // Set in the Inspector (filled in by the loader); the name must match the Unity-side field
    private string group = "";

    // Filled in by the loader from the UnityEvents wired up in the Inspector
    private PuzzleEvent onAllHeld;      // every button in the group is held
    private PuzzleEvent onAllReleased;  // they were all held, and one was let go

    private TrackedPeckState buttonSystem;   // 1 while this button is held
    private readonly TrackedPeckState[] lights = new TrackedPeckState[4];
    private MeshRenderer caseRenderer;
    private readonly int[] dotShown = { -1, -1, -1, -1 };
    private PropertyBlockHelper lightHelper;   // draws the dot lights onto a case
    private int myIndex = -1, litShown = -1;
    private readonly List<GameObject> cases = new List<GameObject>();   // Two, Three, Four
    private List<PuzzleButtonNHold> members;   // the group, looked up again a few times a second
    private int shownCase = -1;
    private bool allHeld;

    // Read-only for scripts
    public bool IsHeld => buttonSystem != null && PeckValue.TryRead(buttonSystem, out int v) && v == 1;
    public bool AllHeld => allHeld;
    public int GroupSize => members != null ? members.Count : 0;   // switched-on buttons in the group

    internal void SetupButton(GameObject copy) {
        // The copy still reads the game's own buttons through this relay: out with it
        var relays = new List<Component>();
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckRelay>(), true)) if (c != null) relays.Add(c);
        foreach (Component c in relays) UnityEngine.Object.DestroyImmediate(c);

        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
            TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
            if (st == null) continue;
            if (st.name == "ButtonSystem") buttonSystem = st;
            else if (st.name.StartsWith("LightSystem") && int.TryParse(st.name.Substring(11), out int k) && k >= 0 && k < 4) lights[k] = st;
        }
        Component helper = copy.GetComponentInChildren(Il2CppType.Of<PropertyBlockHelper>(), true);
        lightHelper = helper != null ? helper.TryCast<PropertyBlockHelper>() : null;
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Transform>(), true)) {
            Transform t = c != null ? c.TryCast<Transform>() : null;
            if (t != null && t.name.StartsWith("Button-nHold-Case-")) cases.Add(t.gameObject);
        }
        // The clone inherits the original's current state: a button someone was holding (and its lit dots)
        // would stay "held" until pressed again. Start released.
        int held = 0;
        if (buttonSystem != null && ResetToZero(buttonSystem)) held++;
        foreach (TrackedPeckState l in lights) if (l != null) ResetToZero(l);
        // The button's two models (up / pressed in) are switched by effects that only run on a change
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Transform>(), true)) {
            Transform t = c != null ? c.TryCast<Transform>() : null;
            if (t == null) continue;
            if (t.name == "Button-nHold-Button") t.gameObject.SetActive(true);
            else if (t.name == "Button-nHold-ButtonDepressed") t.gameObject.SetActive(false);
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckSwitch>(), true)) {
            PeckSwitch sw = c != null ? c.TryCast<PeckSwitch>() : null;
            if (sw == null) continue;
            try { sw.playerHoldingThis = null; } catch (Exception) { }
        }
        if (buttonSystem != null) StateMirrors.HoldStates.Add(buttonSystem.Pointer);
        if (buttonSystem == null) Plugin.log.LogWarning($"[PuzzleButtonNHold] '{name}': the button's state wasn't found, so it can't tell when it is held.");
    }

    // Sets a state (and its predicted copy) to 0; true if it was something else
    private static bool ResetToZero(TrackedPeckState st) {
        try {
            PeckContext ctx = st.currentPeckContext;
            bool was = ctx.state != 0;
            ctx.state = 0;
            st.currentPeckContext = ctx;
            PeckContext predicted = st.peckContextForPrediction;
            predicted.state = 0;
            st.peckContextForPrediction = predicted;
            return was;
        } catch (Exception) { return false; }
    }

    private string Group => group ?? "";

    // The buttons in this one's group: same puzzle, same Group, switched on (a PuzzleHider may
    // have removed some for this lobby size). Every PC works it out the same way.
    private List<PuzzleButtonNHold> FindMembers() {
        var list = new List<PuzzleButtonNHold>();
        int root = transform.root.GetInstanceID();
        foreach (PuzzleVanillaCopy c in all)
            if (c is PuzzleButtonNHold b && b.gameObject.activeInHierarchy && b.transform.root.GetInstanceID() == root && b.Group == Group)
                list.Add(b);
        list.Sort((x, y) => string.CompareOrdinal(Templates.FullPath(x.transform), Templates.FullPath(y.transform)));
        return list;
    }

    // Shows the case with 2, 3 or 4 dots for the group's size
    private void ShowCase(int size) {
        string wanted = size >= 4 ? "Four" : size == 3 ? "Three" : "Two";
        foreach (GameObject g in cases) if (g != null) g.SetActive(g.name.EndsWith("-" + wanted));
        // The dot lights draw on the 4-dot case; point them at the case that is showing
        foreach (GameObject g in cases) {
            if (g == null || !g.activeSelf) continue;
            Component r = g.GetComponent(Il2CppType.Of<MeshRenderer>());
            MeshRenderer mr = r != null ? r.TryCast<MeshRenderer>() : null;
            if (mr == null) continue;
            if (lightHelper != null) { try { lightHelper.targetRenderer = mr; } catch (Exception) { } }
            caseRenderer = mr;
            for (int k = 0; k < 4; k++) dotShown[k] = -1;
        }
    }

    // Every PC: draws this button's dot lights straight onto the showing case (the game's light
    // effect only knows the 4-dot case). Reads the light states the host drives.
    private void ApplyDots() {
        if (caseRenderer == null) return;
        for (int k = 0; k < 4; k++) {
            if (lights[k] == null) continue;
            int v = PeckValue.TryRead(lights[k], out int r) && r == 1 ? 1 : 0;
            if (dotShown[k] == v) continue;
            dotShown[k] = v;
            try {
                var block = new MaterialPropertyBlock();
                caseRenderer.GetPropertyBlock(block);
                block.SetFloat("_EmissiveMask" + k, v == 1 ? 10f : 0f);
                caseRenderer.SetPropertyBlock(block);
            } catch (Exception) { }
        }
    }

    private float nextCheck;
    internal void Watch() {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.05f;
        // Every PC: the group as it is now. Buttons switched off (by a PuzzleHider for this lobby
        // size, or by the creator's own scripts) don't count, and the dots follow.
        members = FindMembers();
        if (members.Count != shownCase) { shownCase = members.Count; ShowCase(members.Count); }
        ApplyDots();
        if (!PuzzleSDK.IsHost) return;
        // The first button of the group runs it for all of them
        if (members.Count == 0 || members[0] != this) return;

        int held = 0;
        foreach (PuzzleButtonNHold b in members) if (b != null && b.IsHeld) held++;
        int total = members.Count;
        // Each button owns one dot (its place in the group) and lights it while it is held
        if (total <= 4) {
            for (int i = 0; i < members.Count; i++) if (members[i] != null) members[i].SetOwnLight(i);
        } else {
            // More buttons than dots: every button shows the group's progress. One dot as soon as
            // any button is held, then another past 25% and 50%; the fourth only when all are held.
            int lit = held == 0 ? 0 : held == total ? 4 : 1 + (held * 4 > total ? 1 : 0) + (held * 2 > total ? 1 : 0);
            foreach (PuzzleButtonNHold b in members) if (b != null) b.SetProgressLights(lit);
        }
        bool now = total > 0 && held == total;
        if (now == allHeld) return;
        foreach (PuzzleButtonNHold b in members) if (b != null) b.allHeld = now;
        foreach (PuzzleButtonNHold b in members) {
            if (b == null) continue;
            if (now) { if (b.onAllHeld != null) b.onAllHeld.Invoke(); }
            else if (b.onAllReleased != null) b.onAllReleased.Invoke();
        }
    }

    // Host: the first 'lit' dots are lit (groups of more than four)
    private void SetProgressLights(int lit) {
        int mask = (1 << lit) - 1;
        if (mask == litShown) return;
        litShown = mask;
        for (int k = 0; k < 4; k++) {
            if (lights[k] == null) continue;
            try { lights[k].SetState(k < lit ? 1 : 0); } catch (Exception) { }
        }
    }

    // Host: this button's own dot is lit while it is held (the game's own light states, so every player sees it)
    private void SetOwnLight(int index) {
        myIndex = index;
        bool held = IsHeld;
        for (int k = 0; k < 4; k++) {
            int want = (k == index && held) ? 1 : 0;
            int key = k == index ? want : 0;
            if (lights[k] == null) continue;
            if (((litShown >> k) & 1) == key && litShown >= 0) continue;
            try { lights[k].SetState(want); } catch (Exception) { }
        }
        int mask = 0;
        if (index >= 0 && index < 4 && held) mask = 1 << index;
        litShown = mask;
    }
}

// What PuzzleSave needs from anything that is saved while held
public interface ISavedHeld {
    bool SavesOnReload { get; }
    ulong ItemId { get; }
    MonoBehaviour Owner { get; }
    bool CountsAsHeld();
    void SendTo(Vector3 position);
}
// The game plays its flares with a few shared 'FlareParticleSystem' objects (a pool) and leaves them, and
// their looping sound, where the last flare was. For every flare system one of our guns or launchers
// uses: once it has been used and then idle for 10 seconds, it is moved far below the map so nobody
// hears it. The game puts it back where it needs it the next time a flare plays, so its own behaviour is
// untouched. Nothing is deleted, and nothing happens between shots except a cheap check of those few systems.
internal static class FlareSweep {
    private const float Delay = 10f;
    private const float ParkedHeight = -3000f;

    private sealed class Tracked { public ParticleSystem ps; public float lastUsed; public bool used, parked; }
    private static readonly Dictionary<int, Tracked> tracked = new Dictionary<int, Tracked>();
    private static float nextTick;

    // The flare particle systems a copy's flare effects play
    internal static List<ParticleSystem> TargetsOf(GameObject copy) {
        var list = new List<ParticleSystem>();
        try {
            foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckEffectParticleNetworked>(), true)) {
                PeckEffectParticleNetworked effect = c != null ? c.TryCast<PeckEffectParticleNetworked>() : null;
                ParticleSystem target = effect != null ? effect.targetParticleSystem : null;
                if (target != null && !list.Contains(target)) list.Add(target);
            }
        } catch (Exception ex) { Plugin.log.LogWarning($"[FlareSweep] could not find the flare system: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
        return list;
    }

    // Called a few times a second per item: a flare system showing particles has just been used
    internal static void Watch(List<ParticleSystem> targets) {
        if (targets == null) return;
        foreach (ParticleSystem ps in targets) {
            if (ps == null) continue;
            Tracked t;
            int id = ps.gameObject.GetInstanceID();
            if (!tracked.TryGetValue(id, out t)) { t = new Tracked { ps = ps }; tracked[id] = t; }
            bool alive = false;
            try { alive = ps.IsAlive(true) || ps.isPlaying; } catch (Exception) { }
            if (!alive) continue;
            t.lastUsed = Time.time;
            t.used = true;
            t.parked = false;   // the game has put it back to play
        }
    }

    // Once a second: park the systems that have been idle for the delay
    internal static void Tick() {
        if (tracked.Count == 0 || Time.time < nextTick) return;
        nextTick = Time.time + 1f;
        foreach (Tracked t in tracked.Values) {
            if (t.ps == null || !t.used || t.parked || Time.time - t.lastUsed < Delay) continue;
            try {
                if (t.ps.IsAlive(true)) { t.lastUsed = Time.time; continue; }
                Transform tr = t.ps.transform;
                Vector3 pos = tr.position;
                tr.position = new Vector3(pos.x, ParkedHeight, pos.z);
                t.parked = true;
            } catch (Exception ex) { Plugin.log.LogWarning($"[FlareSweep] could not move a flare system: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
        }
    }
}

} // namespace BigPuzzleSDK
