#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace BigPuzzleSDK {

// Problems with how SDK components are set up. Shown as warnings in each component's Inspector,
// and listed (with a choice to export anyway) when a puzzle is exported.
internal static class PuzzleChecks {
    internal struct Issue {
        public string message;
        public Object context;   // the object to select in the Console
    }

    private const float LargeAudioDistance = 500f;
    private static readonly Assembly SdkAssembly = typeof(PuzzleSDK).Assembly;

    private static bool IsSdkComponent(Component c) {
        if (c == null || !(c is MonoBehaviour)) return false;
        System.Type t = c.GetType();
        return t.Assembly == SdkAssembly && t.Namespace == "BigPuzzleSDK";
    }

    // In an open scene, not a prefab asset (Project window) or a prefab being edited on its own:
    // a prefab of one component isn't meant to be a puzzle by itself
    private static bool InScene(Component c) {
        if (EditorUtility.IsPersistent(c) || !c.gameObject.scene.IsValid()) return false;
        return UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(c.gameObject) == null;
    }

    // The puzzle this object belongs to: the nearest PuzzleSDK on it or above it
    private static PuzzleSDK PuzzleOf(Component c) => c.GetComponentInParent<PuzzleSDK>(true);

    // ---- One component (Inspector and export) ----
    internal static List<string> For(Component c) {
        var list = new List<string>();
        if (c == null) return list;

        if (IsSdkComponent(c) && InScene(c) && PuzzleOf(c) == null)
            list.Add("This isn't inside any puzzle (no PuzzleSDK on it or above it), so it won't be exported with one.");

        if (c is PuzzleSDK sdk && sdk.transform.parent != null) {
            PuzzleSDK outer = sdk.transform.parent.GetComponentInParent<PuzzleSDK>(true);
            if (outer != null)
                list.Add($"This puzzle is inside another puzzle ('{outer.name}'). Use one PuzzleSDK per puzzle.");
        }

        if (c is PuzzleHider) {
            var so = new SerializedObject(c);
            bool s2 = Bool(so, "show2"), s3 = Bool(so, "show3"), s4 = Bool(so, "show4");
            if (!s2 && !s3 && !s4) list.Add("This will always be hidden. This script is not needed.");
            else if (s2 && s3 && s4) list.Add("This will always be shown. This script is not needed.");
        }

        if (c is PuzzleRadioDeadZone && c.GetComponents<Collider>().Length == 0)
            list.Add("No collider, so the dead zone covers nothing. Add a Box (or other) Collider sized to the area.");
        if (c is PuzzlePresenceSensor && c.GetComponents<Collider>().Length == 0)
            list.Add("No collider, so it never detects anyone. Add a Box (or other) Collider sized to the area.");
        if (c is PuzzlePresenceSensor) {
            foreach (MeshCollider mc in c.GetComponents<MeshCollider>())
                if (!mc.convex) list.Add("The Mesh Collider isn't Convex, so it can't tell who is inside. Tick Convex, or use a Box Collider.");
        }
        if (c is PuzzleSoundProof && c.GetComponentsInChildren<Collider>(true).Length == 0)
            list.Add("No collider on this object or its children, so it blocks nothing. Add colliders that cover the walls.");
        if (c is PuzzleGlass && c.GetComponentsInChildren<Collider>(true).Length == 0)
            list.Add("No collider, so players walk straight through. Add a Box Collider.");

        if (c is PuzzleTimer) {
            var so = new SerializedObject(c);
            SerializedProperty d = so.FindProperty("duration");
            if (d != null && d.floatValue <= 0f) list.Add("Duration is 0, so the timer finishes as soon as it starts.");
        }
        if (c is PuzzleProgressBar) {
            var so = new SerializedObject(c);
            SerializedProperty t = so.FindProperty("total");
            if (t != null && t.intValue <= 0) list.Add("Total is 0, so the bar can never fill.");
        }

        if (c is PuzzlePressInOrder) CheckPressInOrder(c, list);

        if (c is PuzzleSDK puzzleSdk) {
            CheckStartStop(puzzleSdk, list);
            CheckPuzzleType(puzzleSdk, list);
        }
        if (c is PuzzleButton button) CheckHoldButton(button, list);
        if (c is TowerButton tower) CheckTowerButton(tower, list);
        if (c is PuzzleButtonNHold nhold) {
            string g = new SerializedObject(nhold).FindProperty("group")?.stringValue ?? "";
            PuzzleSDK puzzle = PuzzleOf(nhold);
            int count = 0;
            if (puzzle != null)
                foreach (PuzzleButtonNHold other in puzzle.GetComponentsInChildren<PuzzleButtonNHold>(false))   // switched-off ones don't count
                    if ((new SerializedObject(other).FindProperty("group")?.stringValue ?? "") == g) count++;
            if (puzzle != null && count < 2) list.Add("It's the only button in its group, so holding it alone triggers it. Give other buttons the same Group.");
        }
        if (c is PuzzlePegBoard) {
            var so = new SerializedObject(c);
            SerializedProperty size = so.FindProperty("size");
            SerializedProperty from = so.FindProperty("answerFrom");
            if (size != null && from != null) {
                int slots = PuzzlePegBoardEditor.SlotsOf(size);
                SerializedProperty flapProp = so.FindProperty("answerFromFlap");
                var usedBy = new Dictionary<string, List<int>>();   // which slots use each display flap or speaker
                var usedName = new Dictionary<string, string>();
                var unset = new List<int>();                       // slots with no Answer From
                for (int i = 0; i < slots; i++) {
                    Object picked = i < from.arraySize ? from.GetArrayElementAtIndex(i).objectReferenceValue : null;
                    if (picked == null) { unset.Add(i + 1); continue; }
                    int flap = picked is PuzzleDisplay && flapProp != null && i < flapProp.arraySize ? Mathf.Max(1, flapProp.GetArrayElementAtIndex(i).intValue) : 1;
                    string key = picked.GetInstanceID() + "/" + flap;
                    if (!usedBy.TryGetValue(key, out List<int> users)) { usedBy[key] = users = new List<int>(); usedName[key] = picked is PuzzleDisplay ? picked.name + " flap " + flap : picked.name; }
                    users.Add(i + 1);
                }
                if (unset.Count > 0 && AutoAnswerDisplay(so, PuzzleOf(c)) == null)   // an empty Answer From that links itself is fine
                    list.Add($"Slot {string.Join(", ", unset)} {(unset.Count == 1 ? "has" : "have")} no Answer From, so the board can never be correct. Pick a Display or Sound Peg Answer for {(unset.Count == 1 ? "it" : "each")}.");
                foreach (var pair in usedBy)
                    if (pair.Value.Count > 1)
                        list.Add($"Slots {string.Join(", ", pair.Value)} all use '{usedName[pair.Key]}' in Answer From, so they will always need the same peg.");
            }
        }
        if (c is PuzzleCountValidator) {
            var so = new SerializedObject(c);
            SerializedProperty inp = so.FindProperty("input");
            PuzzleSDK owner = PuzzleOf(c);
            int inputs = owner != null ? owner.GetComponentsInChildren<PuzzleCountInput>(true).Length : 0;
            if (inp != null && inp.objectReferenceValue == null && inputs != 1)
                list.Add(inputs == 0 ? "No input set, and there is no Count Input in the puzzle, so it will always be incorrect."
                                     : "No input set, and the puzzle has more than one Count Input, so pick one in Input or it will always be incorrect.");
        }
        if (c is PuzzleDisplay || c is PuzzlePegBoard || c is PuzzleSoundPegAnswer) CheckPile(c, list);
        if (c is PuzzleDisplay) {
            var so = new SerializedObject(c);
            SerializedProperty copy = so.FindProperty("copyDisplay");
            var source = copy != null ? copy.objectReferenceValue as PuzzleDisplay : null;
            if (source != null) {
                if (CopyLoops((PuzzleDisplay)c)) list.Add("Copy Display leads back to this display, so it will stay blank.");
                else if (PuzzleOf(source) != PuzzleOf(c)) list.Add($"Copy Display '{source.name}' is in another puzzle, so this display can't follow it.");
            }
        }
        if (c is PuzzleSwitch toggle) CheckPair(toggle, "onTurnedOn", "On Turned On", "onTurnedOff", "On Turned Off", list, IsTwoState);

        if (c is AudioSource audio && audio.maxDistance > LargeAudioDistance)
            list.Add($"Max Distance is {audio.maxDistance:0} m, which is very large. Puzzle sounds are 3D in-game, so this carries far across the map.");

        return list;
    }

    // A Puzzle rewards one gourd (one vice or one box, one pillow); a Tower holds gourds (10+ plinths)
    private static void CheckPuzzleType(PuzzleSDK sdk, List<string> list) {
        SerializedProperty type = new SerializedObject(sdk).FindProperty("puzzleType");
        if (type == null) return;
        if (type.enumValueIndex == (int)PuzzleType.Puzzle) {
            int vices = sdk.GetComponentsInChildren<PuzzleGourdVice>(true).Length;
            int pillows = sdk.GetComponentsInChildren<PuzzlePillow>(true).Length;
            int boxes = sdk.GetComponentsInChildren<PuzzleBox>(true).Length;
            if (vices == 0 && boxes == 0) list.Add("Puzzle Type is Puzzle, so it needs a PuzzleGourdVice or a PuzzleBox to hold its gourd.");
            else if (vices > 0 && boxes > 0) list.Add("This puzzle has both a PuzzleGourdVice and a PuzzleBox. A puzzle only has one gourd, so only the vice's gourd is used and the box stays empty. Remove one of them.");
            else if (vices > 1) list.Add($"This puzzle has {vices} PuzzleGourdVices, but a puzzle only has one gourd. Keep one.");
            if (pillows != 1) list.Add($"Puzzle Type is Puzzle, so it needs exactly one PuzzlePillow (it has {pillows}).");
        } else if (type.enumValueIndex == (int)PuzzleType.Tower) {
            int plinths = sdk.GetComponentsInChildren<PuzzleGourdPlinth>(true).Length;
            if (plinths < 10) list.Add($"Puzzle Type is Tower, so it needs at least 10 PuzzleGourdPlinths (it has {plinths}).");
        }
    }

    // ---- Event wiring ----
    // The persistent calls of a UnityEvent field: (target, method name)
    private static List<(Object target, string method)> Calls(Component c, string eventField) {
        var list = new List<(Object, string)>();
        var so = new SerializedObject(c);
        SerializedProperty calls = so.FindProperty(eventField + ".m_PersistentCalls.m_Calls");
        if (calls == null || !calls.isArray) return list;
        for (int i = 0; i < calls.arraySize; i++) {
            SerializedProperty call = calls.GetArrayElementAtIndex(i);
            Object target = call.FindPropertyRelative("m_Target")?.objectReferenceValue;
            string method = call.FindPropertyRelative("m_MethodName")?.stringValue;
            if (target != null && !string.IsNullOrEmpty(method)) list.Add((target, method + ArgumentOf(call)));
        }
        return list;
    }

    // The call's argument, so SetColor(red) and SetColor(blue) count as different calls
    private static string ArgumentOf(SerializedProperty call) {
        SerializedProperty args = call.FindPropertyRelative("m_Arguments");
        SerializedProperty mode = call.FindPropertyRelative("m_Mode");
        if (args == null || mode == null) return "";
        switch (mode.intValue) {
            case 2: return "(" + args.FindPropertyRelative("m_ObjectArgument")?.objectReferenceValue + ")";   // object
            case 3: return "(" + args.FindPropertyRelative("m_IntArgument")?.intValue + ")";
            case 4: return "(" + args.FindPropertyRelative("m_FloatArgument")?.floatValue + ")";
            case 5: return "(" + args.FindPropertyRelative("m_StringArgument")?.stringValue + ")";
            case 6: return "(" + args.FindPropertyRelative("m_BoolArgument")?.boolValue + ")";
            default: return "";
        }
    }

    private static string Label(Object o) => o is Component comp ? $"'{comp.name}' ({comp.GetType().Name})" : $"'{o.name}'";

    // Follows Copy Display from this display; true if it comes back to the start (or never ends)
    private static bool CopyLoops(PuzzleDisplay start) {
        var seen = new HashSet<PuzzleDisplay> { start };
        PuzzleDisplay cur = start;
        while (true) {
            SerializedProperty copy = new SerializedObject(cur).FindProperty("copyDisplay");
            var next = copy != null ? copy.objectReferenceValue as PuzzleDisplay : null;
            if (next == null) return false;
            if (!seen.Add(next)) return true;
            cur = next;
        }
    }

    // Displays and sound answers each take a different tile from the puzzle's pile when it starts. With a Peg Board
    // that is only solvable if the puzzle has at least one tile for each (displays that copy another don't count).
    private static void CheckPile(Component c, List<string> list) {
        PuzzleSDK puzzle = PuzzleOf(c);
        if (puzzle == null || puzzle.GetComponentInChildren<PuzzlePegBoard>(true) == null) return;

        int displays = 0;   // flaps: a display with three flaps takes three tiles
        foreach (PuzzleDisplay d in puzzle.GetComponentsInChildren<PuzzleDisplay>(true)) {
            var dso = new SerializedObject(d);
            if (dso.FindProperty("copyDisplay")?.objectReferenceValue == null) displays += PuzzleDisplayEditor.FlapsOf(dso.FindProperty("size"));
        }
        int tiles = puzzle.GetComponentsInChildren<PuzzlePegTile>(true).Length;
        if (!(c is PuzzleSoundPegAnswer) && tiles < displays)
            list.Add($"{tiles} tile{(tiles == 1 ? "" : "s")} for {displays} display flap{(displays == 1 ? "" : "s")}: {displays - tiles} flap{(displays - tiles == 1 ? "" : "s")} will be blank and the puzzle can't be solved. Add more Peg Tiles, or make a display copy another.");

        int speakers = puzzle.GetComponentsInChildren<PuzzleSoundPegAnswer>(true).Length;
        int pegs = puzzle.GetComponentsInChildren<PuzzleSoundPeg>(true).Length;
        if (!(c is PuzzleDisplay) && speakers > 0 && pegs < speakers)
            list.Add($"{pegs} sound peg{(pegs == 1 ? "" : "s")} for {speakers} answer speaker{(speakers == 1 ? "" : "s")}: {speakers - pegs} speaker{(speakers - pegs == 1 ? "" : "s")} will stay silent and the puzzle can't be solved. Add more Sound Pegs.");
    }

    // A door or light set in On Puzzle Start should be put back in On Puzzle Stop (which also runs
    // at spawn), and the other way round; otherwise it keeps the other state after a restart.
    private static void CheckStartStop(PuzzleSDK sdk, List<string> list) {
        var start = new Dictionary<Object, HashSet<string>>();
        var stop = new Dictionary<Object, HashSet<string>>();
        System.Func<Object, bool> doorOrLight = o => o is PuzzleDoor || o is PuzzleLight;
        Collect(sdk, "onPuzzleStart", doorOrLight, start);
        Collect(sdk, "onPuzzleStop", doorOrLight, stop);
        foreach (var kv in start) {
            if (!stop.TryGetValue(kv.Key, out HashSet<string> other))
                list.Add($"On Puzzle Start changes {Label(kv.Key)}, but On Puzzle Stop never sets it back, so it stays that way after the puzzle stops.");
            else if (kv.Value.SetEquals(other) && !kv.Value.Contains("Toggle"))
                list.Add($"On Puzzle Start and On Puzzle Stop both call {string.Join(", ", kv.Value)} on {Label(kv.Key)}, so it never changes. One of them probably needs the opposite call.");
        }
        foreach (Object t in stop.Keys)
            if (!start.ContainsKey(t))
                list.Add($"On Puzzle Stop sets {Label(t)}, but On Puzzle Start never changes it. If it should change while the puzzle runs, add it to On Puzzle Start too.");
    }

    // Objects with an on/off (or open/closed) state that events usually set both ways
    private static bool IsTwoState(Object o) =>
        o is PuzzleDoor || o is PuzzleLight || o is PuzzleBox || o is PuzzleGourdPlinth || o is PuzzleSoundProof;

    // Two events that undo each other (on/off, pressed/released): an object changed by one of
    // them should be set back by the other
    private static void CheckPair(Component c, string eventA, string labelA, string eventB, string labelB,
                                  List<string> list, System.Func<Object, bool> include) {
        var a = new Dictionary<Object, HashSet<string>>();
        var b = new Dictionary<Object, HashSet<string>>();
        Collect(c, eventA, include, a);
        Collect(c, eventB, include, b);
        foreach (var kv in a) {
            if (!b.TryGetValue(kv.Key, out HashSet<string> other))
                list.Add($"{labelA} changes {Label(kv.Key)}, but {labelB} never sets it back.");
            else if (kv.Value.SetEquals(other) && !kv.Value.Contains("Toggle"))
                list.Add($"{labelA} and {labelB} both call {string.Join(", ", kv.Value)} on {Label(kv.Key)}, so it never changes back. One of them probably needs the opposite call.");
        }
        foreach (var kv in b)
            if (!a.ContainsKey(kv.Key)) list.Add($"{labelB} changes {Label(kv.Key)}, but {labelA} never sets it back.");
    }

    private static void Collect(Component c, string eventField, System.Func<Object, bool> include, Dictionary<Object, HashSet<string>> into) {
        foreach (var call in Calls(c, eventField)) {
            if (!include(call.target) || call.method == "HoldToOpen") continue;
            if (!into.TryGetValue(call.target, out HashSet<string> set)) into[call.target] = set = new HashSet<string>();
            set.Add(call.method);
        }
    }

    // A hold button that calls Open() leaves the door/box/plinth open after it is let go, unless
    // On Released closes it
    private static void CheckHoldButton(PuzzleButton button, List<string> list) {
        var closed = new HashSet<Object>();
        foreach (var call in Calls(button, "onReleased")) if (call.method == "Close") closed.Add(call.target);
        var warned = new HashSet<Object>();
        foreach (var call in Calls(button, "onPressed")) {
            if (call.method != "Open" || closed.Contains(call.target) || !warned.Add(call.target)) continue;
            if (call.target is PuzzleBox || call.target is PuzzleGourdPlinth)
                list.Add($"On Pressed calls Open() on {Label(call.target)}, which stays open after the button is let go. Use HoldToOpen() instead, or add Close() to On Released.");
            else if (call.target is PuzzleDoor)
                list.Add($"On Pressed calls Open() on {Label(call.target)}, which stays open after the button is let go. Add Close() to On Released if it should close again.");
        }
        // Lights and soundproof walls switched while held should switch back on release
        CheckPair(button, "onPressed", "On Pressed", "onReleased", "On Released", list,
                  o => o is PuzzleLight || o is PuzzleSoundProof);
    }

    // The plinth list: filled, no gaps or repeats, all in this puzzle, and nothing else opening them
    private static void CheckTowerButton(TowerButton tower, List<string> list) {
        SerializedProperty items = new SerializedObject(tower).FindProperty("plinths");
        if (items == null) return;
        if (items.arraySize == 0) { list.Add("No plinths in the list, so the button opens nothing."); return; }
        var seen = new HashSet<Object>();
        PuzzleSDK puzzle = PuzzleOf(tower);
        bool empty = false, repeat = false;
        for (int i = 0; i < items.arraySize; i++) {
            Object o = items.GetArrayElementAtIndex(i).objectReferenceValue;
            if (o == null) { empty = true; continue; }
            if (!seen.Add(o)) repeat = true;
            if (o is Component pc && puzzle != null && PuzzleOf(pc) != puzzle)
                list.Add($"{Label(o)} is in another puzzle. A tower button can only open plinths in its own puzzle.");
        }
        if (empty) list.Add("The plinth list has empty entries.");
        if (repeat) list.Add("A plinth is in the list more than once.");
        if (puzzle != null) {
            SerializedProperty type = new SerializedObject(puzzle).FindProperty("puzzleType");
            if (type != null && type.enumValueIndex != (int)PuzzleType.Tower)
                list.Add("This puzzle's Puzzle Type isn't Tower. Tower buttons are for the gourd tower.");
        }
        PuzzleButton hold = tower.GetComponent<PuzzleButton>();
        if (hold == null) return;
        foreach (var call in Calls(hold, "onPressed"))
            if (call.target is PuzzleGourdPlinth && seen.Contains(call.target))
                list.Add($"The PuzzleButton's On Pressed also opens {Label(call.target)}. Leave it out: the Tower Button opens the plinths.");
    }

    private static bool Bool(SerializedObject so, string name) {
        SerializedProperty p = so.FindProperty(name);
        return p != null && p.boolValue;
    }

    // ---- A whole puzzle (export) ----
    // Everything inside the puzzle, plus SDK components in the same scene that are outside every puzzle
    internal static List<Issue> ForPuzzle(PuzzleSDK puzzle) {
        var issues = new List<Issue>();
        if (puzzle == null) return issues;
        GameObject root = puzzle.gameObject;

        foreach (PuzzleSDK other in root.GetComponentsInChildren<PuzzleSDK>(true))
            if (other != puzzle)
                issues.Add(new Issue { context = other, message = $"'{other.name}' has a second PuzzleSDK inside this puzzle. Use one PuzzleSDK per puzzle." });

        foreach (Component c in root.GetComponentsInChildren<Component>(true)) {
            if (c == null || (c is PuzzleSDK && c != puzzle)) continue;   // nested puzzles are reported above
            foreach (string m in For(c)) issues.Add(new Issue { context = c, message = $"'{c.name}' ({c.GetType().Name}): {m}" });
        }

        if (root.scene.IsValid()) {
            foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (mb == null || mb.gameObject.scene != root.scene || !IsSdkComponent(mb) || PuzzleOf(mb) != null) continue;
                issues.Add(new Issue { context = mb, message = $"'{mb.name}' ({mb.GetType().Name}) is outside every puzzle in the scene, so it won't be exported." });
            }
        }
        return issues;
    }

    private static void CheckPressInOrder(Component c, List<string> list) {
        var so = new SerializedObject(c);
        SerializedProperty lights = so.FindProperty("lights"), buttons = so.FindProperty("buttons"), bars = so.FindProperty("progressBars");
        int n = Mathf.Max(lights.arraySize, buttons.arraySize);
        if (n == 0) list.Add("No pairs. Add at least one Light and Button.");
        SerializedProperty timed = so.FindProperty("holdAtEnd");
        for (int i = 0; i < n; i++) {
            Object l = i < lights.arraySize ? lights.GetArrayElementAtIndex(i).objectReferenceValue : null;
            Object b = i < buttons.arraySize ? buttons.GetArrayElementAtIndex(i).objectReferenceValue : null;
            if (l == null && b == null) list.Add($"Pair {i + 1} is empty.");
            else if (l == null) list.Add($"Pair {i + 1} has no Light.");
            else if (b == null) list.Add($"Pair {i + 1} has no Button.");
            if (timed != null && timed.boolValue && b is PuzzlePokeButton) list.Add($"Pair {i + 1} uses a Poke Button, which can't be held. Use a Hold Button or Presence Sensor in Hold At End mode.");
            for (int j = 0; j < i; j++) {
                Object l2 = j < lights.arraySize ? lights.GetArrayElementAtIndex(j).objectReferenceValue : null;
                Object b2 = j < buttons.arraySize ? buttons.GetArrayElementAtIndex(j).objectReferenceValue : null;
                if (l != null && l == l2) list.Add($"Pairs {j + 1} and {i + 1} use the same Light.");
                if (b != null && b == b2) list.Add($"Pairs {j + 1} and {i + 1} use the same Button, so one press counts for both.");
            }
        }
        bool anyBar = false;
        for (int i = 0; i < bars.arraySize; i++) if (bars.GetArrayElementAtIndex(i).objectReferenceValue != null) anyBar = true;
        if (!anyBar) list.Add("No Progress Bar, so presses fill nothing and it can never finish. Add at least one.");
    }

    // ---- Inspector ----
    // The component's [PuzzleInfo] description, as an info box
    internal static void DrawInfo(Component c) {
        if (c == null) return;
        var info = (PuzzleInfoAttribute)System.Attribute.GetCustomAttribute(c.GetType(), typeof(PuzzleInfoAttribute));
        if (info != null && !string.IsNullOrEmpty(info.text)) EditorGUILayout.HelpBox(info.text, MessageType.Info);
    }

    // ---- Links that fill themselves in ----
    // A reference left empty is filled in by the mod when the puzzle has exactly one possible target.
    // Each entry says what a field was linked to; shown with a green tick. (Keep in step with AutoLink in the mod's Plugin.cs.)
    internal static List<string> AutoLinks(Component c) {
        var list = new List<string>();
        if (c == null) return list;
        PuzzleSDK puzzle = PuzzleOf(c);
        if (puzzle == null) return list;
        var so = new SerializedObject(c);

        if (c is PuzzleCountValidator && IsEmpty(so, "input")) {
            PuzzleCountInput[] inputs = puzzle.GetComponentsInChildren<PuzzleCountInput>(true);
            if (inputs.Length == 1) list.Add($"Input is linked to Count Input '{inputs[0].name}' automatically.");
        }
        if (c is PuzzlePegBoard && IsEmpty(so, "validator")) {
            PuzzleValidator[] validators = puzzle.GetComponentsInChildren<PuzzleValidator>(true);
            if (validators.Length == 1) list.Add($"Validator is linked to '{validators[0].name}' automatically.");
        }
        if (c is PuzzlePegBoard) {
            PuzzleDisplay match = AutoAnswerDisplay(so, puzzle);
            if (match != null) list.Add($"Answer From is linked to Display '{match.name}' automatically (slot 1 follows flap 1, and so on).");
        }
        return list;
    }

    // The Display a Peg Board's empty Answer From is linked to automatically in-game (null: none).
    // Must match the rule in Plugin.cs: one Peg Board, and exactly one Display with as many flaps as slots.
    private static PuzzleDisplay AutoAnswerDisplay(SerializedObject boardSo, PuzzleSDK puzzle) {
        if (puzzle == null || !PuzzlePegBoardEditor.AnswerFromEmpty(boardSo)) return null;
        if (puzzle.GetComponentsInChildren<PuzzlePegBoard>(true).Length != 1) return null;
        int slots = PuzzlePegBoardEditor.SlotsOf(boardSo.FindProperty("size"));
        PuzzleDisplay match = null;
        int matches = 0;
        foreach (PuzzleDisplay d in puzzle.GetComponentsInChildren<PuzzleDisplay>(true))
            if (PuzzleDisplayEditor.EffectiveFlaps(d) == slots) { match = d; matches++; }
        return matches == 1 ? match : null;
    }

    private static bool IsEmpty(SerializedObject so, string field) {
        SerializedProperty p = so.FindProperty(field);
        return p != null && p.objectReferenceValue == null;
    }

    // An info box with a green tick
    internal static void DrawLinked(string message) {
        var content = new GUIContent(" " + message, EditorGUIUtility.IconContent("TestPassed").image);
        GUIStyle style = new GUIStyle(EditorStyles.helpBox) { wordWrap = true, richText = false, fontSize = EditorStyles.label.fontSize };
        EditorGUILayout.LabelField(content, style);
    }

    // Known limits and bugs of a component. Shown on the component only, never in the export list.
    internal static List<string> Notices(Component c) {
        var list = new List<string>();
        if (c == null) return list;
        const string issue = " Submit an issue on GitHub if you want this changed.";
        if (c is PuzzleIntercom || c is PuzzleIntercomSpeaker)
            list.Add("This item doesn't function yet. Display only for now.");
        if (c is PuzzleFireworkLauncher)
            list.Add("Known bug: incorrect particles spawn.");
        if (c is PuzzleSoundPeg)
            list.Add("Not accepted by vanilla peg boards.");
        if (c is PuzzleVanillaItem) {
            SerializedProperty type = new SerializedObject(c).FindProperty("itemType");
            string kind = type != null && type.enumValueIndex >= 0 && type.enumValueIndex < type.enumNames.Length ? type.enumNames[type.enumValueIndex] : "";
            if (kind == "FlareGun") list.Add("Known bug: incorrect particles spawn.");
            if (kind == "Torch") list.Add("Known bug: late joiners see an incorrect state in rare cases.");
            if (kind == "CannonBall") list.Add("Does not work in vanilla goals." + issue);
        }
        if (c is PuzzleLight) {
            var so = new SerializedObject(c);
            SerializedProperty type = so.FindProperty("type");
            SerializedProperty color = so.FindProperty("color");
            bool spotlightBlack = type != null && type.enumValueIndex >= 0 && type.enumValueIndex < type.enumNames.Length && type.enumNames[type.enumValueIndex] == "Spotlight_Black";
            if (!spotlightBlack && color != null && color.colorValue != Color.white)
                list.Add("The colour option is only optimized for Spotlight_Black.");
        }
        return list;
    }

    internal static void DrawHelpBoxes(Component c) {
        foreach (string m in AutoLinks(c)) DrawLinked(m);
        foreach (string m in For(c)) EditorGUILayout.HelpBox(m, MessageType.Warning);
        foreach (string m in Notices(c)) EditorGUILayout.HelpBox(m, MessageType.Warning);
    }
}

// Default Inspector plus the warnings above, for every SDK component without its own Inspector
internal class PuzzleComponentEditor : Editor {
    public override void OnInspectorGUI() {
        PuzzleChecks.DrawInfo((Component)target);
        DrawDefaultInspector();
        PuzzleChecks.DrawHelpBoxes((Component)target);
    }
}

[CustomEditor(typeof(PuzzleSDK))] internal class PuzzleSDKEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleStartSwitch))] internal class PuzzleStartSwitchEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleLiveSignal))] internal class PuzzleLiveSignalEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleButton))] internal class PuzzleButtonEditor : PuzzleComponentEditor { }
// Shows which screens use this button as their Poke Button (they call Check() on a press and switch it on and off)
[CustomEditor(typeof(PuzzlePokeButton))] internal class PuzzlePokeButtonEditor : PuzzleComponentEditor {
    public override void OnInspectorGUI() {
        PuzzleChecks.DrawInfo((Component)target);
        var notes = new System.Collections.Generic.List<string>();
        foreach (MonoBehaviour screen in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) {
            if (!(screen is PuzzleValidator) && !(screen is PuzzleCountValidator)) continue;
            SerializedProperty p = new SerializedObject(screen).FindProperty("pokeButton");
            if (p == null || p.objectReferenceValue != target) continue;
            notes.Add("Linked to " + screen.GetType().Name.Replace("Puzzle", "") + " '" + screen.name + "': pressing this calls its Check() (no event needed), and the button turns on and off with that screen. Auto Enable Seconds is locked at 0 because the screen re-arms the button itself.");
        }
        bool linked = notes.Count > 0;

        serializedObject.Update();
        SerializedProperty it = serializedObject.GetIterator();
        for (bool enter = true; it.NextVisible(enter); enter = false) {
            bool locked = it.propertyPath == "m_Script" || (linked && it.propertyPath == "autoEnableSeconds");
            if (linked && it.propertyPath == "autoEnableSeconds") it.floatValue = 0f;
            using (new EditorGUI.DisabledScope(locked)) EditorGUILayout.PropertyField(it, true);
        }
        serializedObject.ApplyModifiedProperties();
        foreach (string n in notes) EditorGUILayout.HelpBox(n, MessageType.Info);
        PuzzleChecks.DrawHelpBoxes((Component)target);
    }
}
[CustomEditor(typeof(PuzzleSwitch))] internal class PuzzleSwitchEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleSimPressSwitch))] internal class PuzzleSimPressSwitchEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleDoor))] internal class PuzzleDoorEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleBox))] internal class PuzzleBoxEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleGourdPlinth))] internal class PuzzleGourdPlinthEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleGourdVice))] internal class PuzzleGourdViceEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleConfetti))] internal class PuzzleConfettiEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzlePillow))] internal class PuzzlePillowEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleTelescope))] internal class PuzzleTelescopeEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleSeat))] internal class PuzzleSeatEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleItem))] internal class PuzzleItemEditor : PuzzleComponentEditor { }
// Says where a display's picture comes from
[CustomEditor(typeof(PuzzleDisplay))]
internal class PuzzleDisplayEditor : PuzzleComponentEditor {
    private static readonly int[] FlapTotals = { 1, 2, 3, 5 };
    // How many flaps a display's Size has (1, 2, 3 or 5)
    internal static int FlapsOf(SerializedProperty size) => size == null ? 1 : FlapTotals[Mathf.Clamp(size.enumValueIndex, 0, FlapTotals.Length - 1)];

    // How many flaps a display really has: its Size, or the size of the display it copies (through any chain of copies)
    internal static int EffectiveFlaps(Object o) {
        var d = o as PuzzleDisplay;
        var seen = new HashSet<PuzzleDisplay>();
        while (d != null && seen.Add(d)) {
            var so = new SerializedObject(d);
            var next = so.FindProperty("copyDisplay")?.objectReferenceValue as PuzzleDisplay;
            if (next == null) return FlapsOf(so.FindProperty("size"));
            d = next;
        }
        return 1;
    }

    public override void OnInspectorGUI() {
        PuzzleChecks.DrawInfo((Component)target);
        serializedObject.Update();
        var source = serializedObject.FindProperty("copyDisplay")?.objectReferenceValue as PuzzleDisplay;
        SerializedProperty sizeProp = serializedObject.FindProperty("size");
        if (source != null && sizeProp != null) {   // a copy has as many flaps as the display it follows
            int[] enumIndexOf = { 0, 0, 1, 2, 0, 3 };   // flaps -> Size entry (1, 2, 3, 5)
            sizeProp.enumValueIndex = enumIndexOf[Mathf.Clamp(EffectiveFlaps(source), 1, 5)];
        }
        SerializedProperty it = serializedObject.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter)) {
            enter = false;
            if (it.propertyPath == "m_Script" || (it.propertyPath == "size" && source != null)) {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(it);
            } else EditorGUILayout.PropertyField(it, true);
        }
        serializedObject.ApplyModifiedProperties();
        EditorGUILayout.HelpBox(source != null
            ? $"This display will always display whatever's on {source.name}. Its Size follows {source.name}, so it can copy every flap."
            : "Each flap takes a different tile from this puzzle each time the puzzle starts. A picture only repeats if the puzzle has more than one tile of it.", MessageType.Info);
        PuzzleChecks.DrawHelpBoxes((Component)target);
    }
}
[CustomEditor(typeof(PuzzlePegTile))] internal class PuzzlePegTileEditor : PuzzleComponentEditor { }

// The peg board shows Answer From as one dropdown per slot, listing the displays and speakers in the puzzle
[CustomEditor(typeof(PuzzlePegBoard))]
internal class PuzzlePegBoardEditor : PuzzleComponentEditor {
    private static readonly int[] SlotTotals = { 1, 2, 3, 4, 5, 6, 12 };
    internal static int SlotsOf(SerializedProperty size) => SlotTotals[Mathf.Clamp(size.enumValueIndex, 0, SlotTotals.Length - 1)];

    // True if no slot has an Answer From yet
    internal static bool AnswerFromEmpty(SerializedObject so) {
        SerializedProperty from = so.FindProperty("answerFrom");
        if (from == null) return true;
        for (int i = 0; i < from.arraySize; i++) if (from.GetArrayElementAtIndex(i).objectReferenceValue != null) return false;
        return true;
    }

    public override void OnInspectorGUI() {
        PuzzleChecks.DrawInfo((Component)target);
        serializedObject.Update();
        SerializedProperty it = serializedObject.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter)) {
            enter = false;
            if (it.propertyPath == "m_Script") { using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(it); }
            else if (it.propertyPath == "answerFrom") DrawAnswerFrom(it);
            else EditorGUILayout.PropertyField(it, true);
        }
        serializedObject.ApplyModifiedProperties();
        PuzzleChecks.DrawHelpBoxes((Component)target);
    }

    private void DrawAnswerFrom(SerializedProperty list) {
        int slots = SlotsOf(serializedObject.FindProperty("size"));

        // Every display and speaker in this puzzle (or in the scene, outside a puzzle), plus anything already picked
        var sources = new List<MonoBehaviour>();
        Component board = (Component)target;
        PuzzleSDK puzzle = board.GetComponentInParent<PuzzleSDK>(true);
        if (puzzle != null) {
            sources.AddRange(puzzle.GetComponentsInChildren<PuzzleDisplay>(true));
            sources.AddRange(puzzle.GetComponentsInChildren<PuzzleSoundPegAnswer>(true));
        } else foreach (GameObject root in board.gameObject.scene.GetRootGameObjects()) {
            sources.AddRange(root.GetComponentsInChildren<PuzzleDisplay>(true));
            sources.AddRange(root.GetComponentsInChildren<PuzzleSoundPegAnswer>(true));
        }
        for (int i = 0; i < slots && i < list.arraySize; i++) {
            var picked = list.GetArrayElementAtIndex(i).objectReferenceValue as MonoBehaviour;
            if (picked != null && !sources.Contains(picked)) sources.Add(picked);
        }

        // Names are shown as "Name (Display)"; the path is added when two would look the same
        var names = new List<string>();
        foreach (MonoBehaviour source in sources) {
            string kind = source is PuzzleDisplay ? "Display" : source is PuzzleSoundPegAnswer ? "Sound Peg Answer" : source.GetType().Name;
            int flapsOf = source is PuzzleDisplay ? PuzzleDisplayEditor.EffectiveFlaps(source) : 1;
            names.Add(source.name + " (" + kind + (flapsOf > 1 ? ", " + flapsOf + " flaps" : "") + ")");
        }
        var labels = new string[sources.Count + 1];
        labels[0] = "None";
        for (int k = 0; k < sources.Count; k++) {
            labels[k + 1] = names[k];
            if (names.FindAll(n => n == names[k]).Count > 1) labels[k + 1] += "  " + PathOf(sources[k].transform).Replace('/', '>');
        }

        list.isExpanded = EditorGUILayout.Foldout(list.isExpanded, new GUIContent("Answer From", "Per slot: a Display (and which of its flaps) or Sound Peg Answer whose picture or sound is that slot's answer. A slot left on None can never be correct."), true);
        if (!list.isExpanded) return;
        EditorGUI.indentLevel++;
        SerializedProperty flapList = serializedObject.FindProperty("answerFromFlap");
        for (int i = 0; i < slots; i++) {
            var current = i < list.arraySize ? list.GetArrayElementAtIndex(i).objectReferenceValue as MonoBehaviour : null;
            int index = current == null ? 0 : sources.IndexOf(current) + 1;
            EditorGUILayout.BeginHorizontal();
            int chosen = EditorGUILayout.Popup("Slot " + (i + 1), index, labels);
            if (chosen != index) {
                if (list.arraySize <= i) list.arraySize = i + 1;
                list.GetArrayElementAtIndex(i).objectReferenceValue = chosen == 0 ? null : sources[chosen - 1];
                current = chosen == 0 ? null : sources[chosen - 1];
            }
            // A display with several flaps: which flap (1 is the far left)
            int flaps = current is PuzzleDisplay ? PuzzleDisplayEditor.EffectiveFlaps(current) : 1;
            if (flapList != null) {
                int stored = i < flapList.arraySize ? Mathf.Max(1, flapList.GetArrayElementAtIndex(i).intValue) : 1;
                int shown = Mathf.Clamp(stored, 1, flaps);
                if (flaps > 1) {
                    var flapLabels = new string[flaps];
                    for (int f = 0; f < flaps; f++) flapLabels[f] = "Flap " + (f + 1);
                    shown = EditorGUILayout.Popup(shown - 1, flapLabels, GUILayout.Width(80)) + 1;
                }
                if (shown != stored || flapList.arraySize <= i) {
                    if (flapList.arraySize <= i) flapList.arraySize = i + 1;
                    flapList.GetArrayElementAtIndex(i).intValue = shown;
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUI.indentLevel--;
    }

    private static string PathOf(Transform t) {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }
}
[CustomEditor(typeof(PuzzlePressInOrder))]
internal class PuzzlePressInOrderEditor : PuzzleComponentEditor {
    public override void OnInspectorGUI() {
        PuzzleChecks.DrawInfo((Component)target);
        serializedObject.Update();
        SerializedProperty it = serializedObject.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter)) {
            enter = false;
            if (it.propertyPath == "m_Script") { using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(it); }
            else if (it.propertyPath == "lights") DrawPairs(it, serializedObject.FindProperty("buttons"));
            else if (it.propertyPath == "buttons") continue;
            else if ((it.propertyPath == "phaseSeconds" || it.propertyPath == "phaseBars") && !serializedObject.FindProperty("holdAtEnd").boolValue) continue;
            else {
                if (it.propertyPath == "onEnd") EditorGUILayout.HelpBox("Runs when the game ends, however it ended: after the last right press, on End(), and after On Fail.", MessageType.Info);
                EditorGUILayout.PropertyField(it, true);
            }
        }
        serializedObject.ApplyModifiedProperties();
        PuzzleChecks.DrawHelpBoxes((Component)target);
    }

    // One row per pair: a light and a button, both drag and drop
    private void DrawPairs(SerializedProperty lights, SerializedProperty buttons) {
        int n = Mathf.Max(lights.arraySize, buttons.arraySize);
        if (lights.arraySize != n) lights.arraySize = n;
        if (buttons.arraySize != n) buttons.arraySize = n;

        EditorGUILayout.LabelField("Pairs", EditorStyles.boldLabel);
        if (n > 0) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(24);
            EditorGUILayout.LabelField("Light", GUILayout.MinWidth(40));
            EditorGUILayout.LabelField("Button", GUILayout.MinWidth(40));
            EditorGUILayout.EndHorizontal();
        }
        for (int i = 0; i < n; i++) {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField((i + 1).ToString(), GUILayout.Width(20));
            SerializedProperty lp = lights.GetArrayElementAtIndex(i);
            lp.objectReferenceValue = EditorGUILayout.ObjectField(lp.objectReferenceValue, typeof(PuzzleLight), true, GUILayout.MinWidth(40));
            SerializedProperty bp = buttons.GetArrayElementAtIndex(i);
            bp.objectReferenceValue = EditorGUILayout.ObjectField(bp.objectReferenceValue, typeof(PuzzleButtonBase), true, GUILayout.MinWidth(40));
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Pair")) { lights.arraySize = n + 1; buttons.arraySize = n + 1; }
        using (new EditorGUI.DisabledScope(n == 0))
            if (GUILayout.Button("Remove Last")) { lights.arraySize = n - 1; buttons.arraySize = n - 1; }
        EditorGUILayout.EndHorizontal();
    }
}
[CustomEditor(typeof(PuzzlePresenceSensor))] internal class PuzzlePresenceSensorEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleValidator))] internal class PuzzleValidatorEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleLight))] internal class PuzzleLightEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleTimer))] internal class PuzzleTimerEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleProgressBar))] internal class PuzzleProgressBarEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(TowerButton))] internal class TowerButtonEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleButtonNHold))] internal class PuzzleButtonNHoldEditor : PuzzleComponentEditor { }
// Batch scan
[CustomEditor(typeof(PuzzleVanillaItem))] internal class PuzzleVanillaItemEditor : Editor {
    public override void OnInspectorGUI() {
        PuzzleChecks.DrawInfo((Component)target);
        serializedObject.Update();
        SerializedProperty type = serializedObject.FindProperty("itemType");
        EditorGUILayout.PropertyField(type);
        // Default Text only means something for the two signs
        string kind = type.enumValueIndex >= 0 && type.enumValueIndex < type.enumNames.Length ? type.enumNames[type.enumValueIndex] : "";
        if (kind == "SpareSign" || kind == "PermoSign") EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultText"));
        if (kind == "Kettle" || kind == "WalkieTalkie" || kind == "Backpack" || kind == "Belt" || kind == "FlareGun") EditorGUILayout.PropertyField(serializedObject.FindProperty("itemColor"));
        if (kind == "FlareGun") EditorGUILayout.PropertyField(serializedObject.FindProperty("flareColor"));
        if (kind == "Buoy" || kind == "BuoyRed") EditorGUILayout.PropertyField(serializedObject.FindProperty("includeBase"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("savesOnReload"));
        serializedObject.ApplyModifiedProperties();
        PuzzleChecks.DrawHelpBoxes((Component)target);
    }
}
[CustomEditor(typeof(PuzzleCountInput))] internal class PuzzleCountInputEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleCountValidator))] internal class PuzzleCountValidatorEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleIntercom))] internal class PuzzleIntercomEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleIntercomSpeaker))] internal class PuzzleIntercomSpeakerEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleSoundPeg))] internal class PuzzleSoundPegEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleSoundPegAnswer))] internal class PuzzleSoundPegAnswerEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleNoItemsGate))] internal class PuzzleNoItemsGateEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleTimerSpawner))] internal class PuzzleTimerSpawnerEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleTimerGoal))] internal class PuzzleTimerGoalEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleFireworkLauncher))] internal class PuzzleFireworkLauncherEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleCirclePeg))] internal class PuzzleCirclePegEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleCirclePegBoard))] internal class PuzzleCirclePegBoardEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleRadioDeadZone))] internal class PuzzleRadioDeadZoneEditor : PuzzleComponentEditor { }
[CustomEditor(typeof(PuzzleReverbZone))] internal class PuzzleReverbZoneEditor : PuzzleComponentEditor { }

}
#endif
