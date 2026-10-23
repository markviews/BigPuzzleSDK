using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzlePegBoard: copies a vanilla five-slot peg board into
// place. Players hang peg tiles on it; the board reports which picture is in each slot and can
// check the slots against an answer.
// The board's size. Must stay identical, in the same order, to the Unity SDK's enum.
internal enum PuzzlePegBoardSize { Slots1, Slots2, Slots3, Slots4, Slots5, Slots6, Slots12 }

// What a peg board slot holds. Must stay identical, in the same order, to the Unity SDK's types.
public enum PuzzlePegType { Empty, Image, Sound }

// What is in one slot of a peg board (see PuzzlePegBoard.GetSlot)
public struct PegSlot {
    public PuzzlePegType Type;     // Empty, Image (a picture tile) or Sound (a sound peg)
    public PuzzlePegSet ImageSet;  // Image: the set the picture is from
    public int Image;              // Image: its number in that set (-1 if the SDK doesn't recognise it); 0 otherwise
    public string Sound;           // Sound: "sound_1".."sound_9", or "sound_" and a custom sound's name; "" otherwise
}

public class PuzzlePegBoard : MonoBehaviour {
    public PuzzlePegBoard(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.PegBoard.ExactPath;

    private const string TemplateName = GameRefs.PegBoard.TemplateName;
    private const string PreferredPath = GameRefs.PegBoard.PreferredPath;
    private const string CopyName = "VanillaPegBoard";

    // Set in the Inspector; the names must match the Unity-side fields.
    internal PuzzlePegBoardSize size = PuzzlePegBoardSize.Slots5;   // how many slots (the game's board models)
    internal bool startMarker;            // the little marker at the first slot's end (looks only)
    internal List<MonoBehaviour> answerFrom = new List<MonoBehaviour>();   // per slot: a display or answer speaker the
                                          // answer follows; a slot with none can never be correct
    internal List<int> answerFromFlap = new List<int>();   // per slot: which flap of a multi-flap display it follows (1 = far left)

    // Set in the Inspector (optional; the name must match the Unity-side field): kept up to date by the
    // board, silently. Correct: SetCorrect(true) and TurnOn(). Wrong: SetCorrect(false) and TurnOn().
    // Pegs missing: SetCorrect(false) and TurnOff().
    private PuzzleValidator validator;

    // The puzzle's only Validator, when the creator left Validator empty
    internal void AutoLink(PuzzleValidator only) { if (validator == null) validator = only; }

    // How many slots the creator chose (the board's size), before it is built
    internal int ConfiguredSlots => SlotTotal(size);

    // True if no slot has an Answer From yet
    internal bool AnswerFromEmpty {
        get {
            foreach (MonoBehaviour m in answerFrom) if (m != null) return false;
            return true;
        }
    }

    // The puzzle's only display with as many flaps as this board has slots, when Answer From was left empty:
    // slot 1 follows flap 1, slot 2 follows flap 2, and so on
    internal void AutoLinkAnswers(PuzzleDisplay display) {
        if (display == null || !AnswerFromEmpty) return;
        answerFrom = new List<MonoBehaviour>();
        answerFromFlap = new List<int>();
        for (int i = 0; i < ConfiguredSlots; i++) { answerFrom.Add(display); answerFromFlap.Add(i + 1); }
    }

    // Filled in by the loader from the UnityEvents wired up in the Inspector
    // Every time a peg is placed or removed, exactly one of these fires:
    private PuzzleEvent onCorrect;      // the board matches the answer
    private PuzzleEvent onIncorrect;    // the board is full but does not match
    private PuzzleEvent onMissingPegs;  // the board still has empty slots that need a peg

    private readonly List<PropHome> slots = new List<PropHome>();   // in order, PegSlot0 first
    private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
    private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
    private int[] current = new int[0];   // per slot: 0 = empty, 1 = something hangs there
    private string[] currentSound = new string[0];   // per slot: the sound peg's sound, or null
    private PropGroup[] currentGroup = new PropGroup[0];       // per slot: the game's picture of the tile (default = none)
    private string lastSourceKey;                              // what the Answer From sources showed at the last check
    private bool wasCorrect;
    private float nextCheck;
    private bool synced;
    private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)

    // Called by the loader while the puzzle is still inactive
    internal void SetupBoard(int index) {
        var log = Plugin.log;

        GameObject template = Templates.Find(ExactPath);
        if (template == null) {
            // Not at its known path: search every loaded object instead (slow)
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Transform>())) {
                Transform candidate = obj.TryCast<Transform>();
                if (candidate == null || candidate.name != TemplateName) continue;
                string path = PathOf(candidate);
                if (path.Contains(CopyName)) continue;
                if (template == null) template = candidate.gameObject;
                if (path.EndsWith(PreferredPath)) { template = candidate.gameObject; break; }
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzlePegBoard] No vanilla '{TemplateName}' is loaded, so '{name}' has nothing to copy.");
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
        Resize(copy);

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
            if (sw != null) switches.Add(sw);
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
            TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
            if (st != null) states.Add(st);
        }
        // The slots. Each holder gets its own network id (tiles find their holder by id) and is
        // cut off from the save file, so tiles hung here are not saved as the vanilla board's.
        // Holders are numbered after the board's switches and states, so the two never share
        // a number (a shared number made the game mistake a state for a holder)
        var found = new List<KeyValuePair<string, PropHome>>();
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PropHome>(), true)) {
            PropHome home = c != null ? c.TryCast<PropHome>() : null;
            if (home == null) continue;
            string slotName = home.transform.parent != null ? home.transform.parent.name : home.name;
            found.Add(new KeyValuePair<string, PropHome>(slotName, home));
        }
        found.Sort((a, b) => SlotNumber(a.Key).CompareTo(SlotNumber(b.Key)));   // PegSlot2 before PegSlot10
        ticketIds = Tickets.Allocate(switches.Count + states.Count + found.Count);
        int homeSlot = switches.Count + states.Count;
        foreach (var pair in found) {
            PropHome home = pair.Value;
            try {
                ushort homeTicket = ticketIds[homeSlot++];
                home._ticket_k__BackingField = homeTicket;
                home.shellReference = new SeaShell.ShellReference(homeTicket);
                home.pinnedProp = null;
                home.saveableHomeName = default(SaveableHomeName);
                home.keepSavedWhenLeavingHome = false;
                ModGourd.MakeCrossHome(home);   // takes modded and the game's own pegs
                slots.Add(home);
            } catch (Exception ex) { log.LogWarning($"[PuzzlePegBoard]   slot setup failed: {ex.GetType().Name}: {ex.Message}"); }
        }
        current = new int[slots.Count];

        AssignTickets(false);
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

    private static int SlotNumber(string slotName) {
        string digits = "";
        for (int i = slotName.Length - 1; i >= 0 && char.IsDigit(slotName[i]); i--) digits = slotName[i] + digits;
        return int.TryParse(digits, out int n) ? n : int.MaxValue;
    }

    // ---- Size ----
    // The game's board has a model for 1-6 slots and for 12 (two rows of six), but holders for five
    // slots: PegSlots/PegSlot0..4, 0.45 apart (each slot further along -x). The right model is shown
    // and the slots are added or removed to match; 12 slots hang in two rows, the second 0.45 lower.
    private const float SlotSpacing = 0.45f;

    private static int SlotTotal(PuzzlePegBoardSize s) {
        switch (s) {
            case PuzzlePegBoardSize.Slots1: return 1;
            case PuzzlePegBoardSize.Slots2: return 2;
            case PuzzlePegBoardSize.Slots3: return 3;
            case PuzzlePegBoardSize.Slots4: return 4;
            case PuzzlePegBoardSize.Slots6: return 6;
            case PuzzlePegBoardSize.Slots12: return 12;
            default: return 5;
        }
    }

    private void Resize(GameObject copy) {
        int total = SlotTotal(size);
        Transform models = copy.transform.Find("PegBoard");
        if (models != null) {
            for (int i = 0; i < models.childCount; i++) {
                Transform m = models.GetChild(i);
                if (m.name == "PegBoard_startmarker") m.gameObject.SetActive(startMarker);
                else if (m.name.StartsWith("PegBoard_") && m.name.EndsWith("slot")) m.gameObject.SetActive(m.name == "PegBoard_" + total + "slot");
            }
        }
        Transform slotRoot = copy.transform.Find("PegSlots");
        Transform first = slotRoot != null ? slotRoot.Find("PegSlot0") : null;
        if (slotRoot == null || first == null) {
            Plugin.log.LogWarning($"[PuzzlePegBoard] '{name}': the board's slots weren't found, so it keeps 5 slots.");
            return;
        }
        // Remove slots past the size; add copies of the first one up to it
        var existing = new List<Transform>();
        for (int i = 0; i < slotRoot.childCount; i++) existing.Add(slotRoot.GetChild(i));
        foreach (Transform t in existing)
            if (t.name.StartsWith("PegSlot") && SlotNumber(t.name) >= total) UnityEngine.Object.DestroyImmediate(t.gameObject);
        for (int n = 0; n < total; n++) {
            Transform slot = slotRoot.Find("PegSlot" + n);
            if (slot == null) {
                slot = UnityEngine.Object.Instantiate((UnityEngine.Object)first.gameObject, slotRoot).Cast<GameObject>().transform;
                slot.name = "PegSlot" + n;
            }
            int column = total == 12 ? n % 6 : n;
            int row = total == 12 ? n / 6 : 0;
            slot.localPosition = new Vector3(-SlotSpacing * column, -SlotSpacing * row, 0f);
            slot.localRotation = first.localRotation;
        }
    }

    // Watches the slots a few times a second
    private void Update() {
        if (slots.Count == 0 || Time.time < nextCheck) return;
        nextCheck = Time.time + 0.2f;

        // A display or speaker the answer follows changed: IsCorrect updates (no events)
        if (answerFrom.Count > 0) {
            string key = SourceKey();
            if (key != lastSourceKey) { lastSourceKey = key; CheckAnswer(false); }
        }

        if (currentSound.Length != current.Length) currentSound = new string[current.Length];
        if (currentGroup.Length != current.Length) currentGroup = new PropGroup[current.Length];
        bool changed = false;
        for (int i = 0; i < slots.Count; i++) {
            int now = OccupiedIn(slots[i]);
            string sound = SoundIn(slots[i]);
            PropGroup group = GroupIn(slots[i]);
            if (now == current[i] && sound == currentSound[i] && group.Equals(currentGroup[i])) continue;
            current[i] = now;
            currentGroup[i] = group;
            currentSound[i] = sound;
            changed = true;
        }
        if (!synced) {
            synced = true;
            if (!changed) { CheckAnswer(false); return; }   // the starting state, for the validator
        }
        if (!changed) return;

        CheckAnswer(true);
    }

    // 1 if something hangs in a slot, 0 if it is empty
    private static int OccupiedIn(PropHome slot) {
        try { return slot != null && slot.pinnedProp != null ? 1 : 0; }
        catch (Exception) { return 0; }
    }

    // Which of the game's image sets a picture is from, and its number there (0 = no known picture).
    // Looked up once per picture and remembered.
    private static readonly Dictionary<PropGroup, (PuzzlePegSet set, int image)> pictures = new Dictionary<PropGroup, (PuzzlePegSet, int)>();
    private static readonly PuzzlePegSet[] searchedSets = { PuzzlePegSet.Shapes, PuzzlePegSet.Peg, PuzzlePegSet.Emotes, PuzzlePegSet.Scribbles };
    private static (PuzzlePegSet set, int image) PictureOf(PropGroup group) {
        var none = (PuzzlePegSet.Shapes, 0);
        if (group.Equals(default(PropGroup))) return none;
        if (pictures.TryGetValue(group, out var known)) return known;
        var found = none;
        bool lookedUp = false;   // false while the game's pictures can't be looked up yet: don't remember "unknown" then
        try {
            foreach (PuzzlePegSet candidate in searchedSets)
                for (int n = 1; n <= 15 && found.Item2 == 0; n++) {
                    PropGroup g = PuzzleDisplay.GroupFor((PuzzleImageSet)(int)candidate, n);
                    if (!g.Equals(default(PropGroup))) lookedUp = true;
                    if (g.Equals(group)) found = (candidate, n);
                }
        } catch (Exception) { }
        if (lookedUp) pictures[group] = found;
        return found;
    }

    // The game's picture of the tile hanging in a slot (default if empty, or not a picture tile)
    private static PropGroup GroupIn(PropHome slot) {
        try {
            Prop prop = slot != null ? slot.pinnedProp : null;
            PegTileRenderer tile = prop != null ? prop.gameObject.GetComponent<PegTileRenderer>() : null;
            return tile != null ? tile.propGroup : default(PropGroup);
        } catch (Exception) { return default(PropGroup); }
    }

    // The sound of the sound peg hanging in a slot, or null (empty, or not a sound peg)
    private static string SoundIn(PropHome slot) {
        try { return SoundPegAudio.SoundOf(slot != null ? slot.pinnedProp : null); }
        catch (Exception) { return null; }
    }

    private int AnswerFromSet() {
        int n = 0;
        for (int i = 0; i < current.Length; i++) if (SourceAt(i) != null) n++;
        return n;
    }

    // Which flap (0 is the first) of its display a slot follows
    private int FlapAt(int slot) {
        int flap = slot >= 0 && slot < answerFromFlap.Count ? answerFromFlap[slot] : 1;
        return flap >= 1 ? flap - 1 : 0;
    }

    // The display or speaker that gives a slot's answer (Answer From), or null
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    private MonoBehaviour SourceAt(int slot) {
        MonoBehaviour source = slot >= 0 && slot < answerFrom.Count ? answerFrom[slot] : null;
        return source != null ? source : null;   // a destroyed one counts as none
    }

    // What the Answer From sources show right now, as one string, to notice when one changes
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    private string SourceKey() {
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < slots.Count && i < answerFrom.Count; i++) {
            MonoBehaviour source = SourceAt(i);
            if (source != null) {
                PuzzleDisplay display = source.TryCast<PuzzleDisplay>();
                if (display != null) text.Append('d').Append(display.ImageAt(FlapAt(i))).Append('/').Append((int)display.ImageSetAt(FlapAt(i)));
                else {
                    PuzzleSoundPegAnswer speaker = source.TryCast<PuzzleSoundPegAnswer>();
                    if (speaker != null) text.Append('s').Append(speaker.CurrentSound);
                }
            }
            text.Append(';');
        }
        return text.ToString();
    }

    // Works out whether the board is correct, full but wrong, or still missing pegs.
    // A slot with an Answer From follows that display (its picture, 0 = blank = empty) or speaker
    // (its sound). A slot with no Answer From can never be correct.
    // With no answer at all, the board only reports missing pegs (empty slots).
    private void CheckAnswer(bool fire) {
        bool hasSource = false;
        for (int i = 0; i < current.Length && !hasSource; i++) hasSource = SourceAt(i) != null;
        bool hasAnswer = hasSource;
        bool correct = hasAnswer;
        bool missing = false;
        for (int i = 0; i < current.Length; i++) {
            bool wantEmpty = false;          // the slot must stay empty
            string wantSound = null;         // that sound peg
            PropGroup wantPicture = default(PropGroup);   // that picture (the game's own name for it)
            bool readable = true;
            MonoBehaviour source = SourceAt(i);
            if (source != null) {
                PuzzleDisplay display = source.TryCast<PuzzleDisplay>();
                PuzzleSoundPegAnswer speaker = display == null ? source.TryCast<PuzzleSoundPegAnswer>() : null;
                if (display != null) {
                    int flap = FlapAt(i);
                    int shown = display.ImageAt(flap);
                    PuzzleImageSet shownSet = display.ImageSetAt(flap);
                    if (flap >= display.FlapCount) readable = false;   // the display has no such flap
                    else if (shown <= 0) wantEmpty = true;
                    else { wantPicture = PuzzleDisplay.GroupFor(shownSet, shown); readable = !wantPicture.Equals(default(PropGroup)); }
                } else if (speaker != null) wantSound = SoundPegAudio.Normalize(speaker.CurrentSound);
                else readable = false;
            } else readable = false;   // no source answers this slot

            if (current[i] == 0 && !wantEmpty) missing = true;
            if (!readable) correct = false;
            if (!correct) continue;
            if (wantEmpty) { if (current[i] != 0) correct = false; }
            else if (wantSound != null) {
                string have = i < currentSound.Length ? currentSound[i] : null;
                if (have == null || !string.Equals(SoundPegAudio.Normalize(have), wantSound, StringComparison.OrdinalIgnoreCase)) correct = false;
            } else if (current[i] == 0 || i >= currentGroup.Length || !currentGroup[i].Equals(wantPicture)) correct = false;   // an empty slot or another picture
        }
        wasCorrect = correct;
        if (validator != null && PuzzleSDK.IsHost) {
            validator.SetCorrect(correct);
            if (missing) validator.TurnOff(); else validator.TurnOn();
        }
        if (!fire) return;

        if (correct) {
            if (onCorrect != null) onCorrect.Invoke();
        } else if (missing) {
            if (onMissingPegs != null) onMissingPegs.Invoke();
        } else if (hasAnswer) {
            if (onIncorrect != null) onIncorrect.Invoke();
        }
    }

    // ---- For scripts ----
    // Read-only
    public int SlotCount => current.Length;
    // Read-only: the board matches the answer
    public bool IsCorrect => wasCorrect;
    // What is in a slot (0 is the first). A slot that doesn't exist counts as empty.
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    public PegSlot GetSlot(int slot) {
        var result = new PegSlot { Type = PuzzlePegType.Empty, ImageSet = PuzzlePegSet.Shapes, Image = 0, Sound = "" };
        if (slot < 0 || slot >= current.Length || slot >= currentSound.Length || slot >= currentGroup.Length) return result;
        if (currentSound[slot] != null) { result.Type = PuzzlePegType.Sound; result.Sound = currentSound[slot]; return result; }
        if (current[slot] == 0) return result;
        result.Type = PuzzlePegType.Image;
        var picture = PictureOf(currentGroup[slot]);
        result.ImageSet = picture.set;
        result.Image = picture.image > 0 ? picture.image : -1;   // -1: a tile the SDK doesn't recognise
        return result;
    }
}

} // namespace BigPuzzleSDK
