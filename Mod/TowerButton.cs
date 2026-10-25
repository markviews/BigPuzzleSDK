using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigPuzzleSDK {

    // Runtime (mod) version of the SDK's TowerButton, for a modded gourd tower. Goes next to a
    // PuzzleButton (the hold button players press). While the button is held, the first plinth in
    // the list without a gourd is open; letting go closes it again. Placed gourds stack up the list.
    //
    // The tower only shows as many plinths as there are modded puzzles to win gourds from: with N
    // puzzles (PuzzleSDK Puzzle Type = Puzzle) loaded, the first N plinths in the list stay and the
    // rest are switched off. Every PC counts for itself; every player builds the host's puzzles, so
    // they agree.
    public class TowerButton : MonoBehaviour {
        public TowerButton(IntPtr ptr) : base(ptr) { }

        // Set in the Inspector (filled in by the loader); the names must match the Unity-side fields
        private List<PuzzleGourdPlinth> plinths = new List<PuzzleGourdPlinth>();   // bottom first

        // Read-only for scripts
        public int SlotCount { get { int n = 0; foreach (PuzzleGourdPlinth p in plinths) if (Usable(p)) n++; return n; } }
        public int FilledCount { get { int n = 0; foreach (PuzzleGourdPlinth p in plinths) if (Usable(p) && p.HasGourd) n++; return n; } }
        public bool IsFull => SlotCount > 0 && FilledCount == SlotCount;

        internal static readonly List<TowerButton> all = new List<TowerButton>();
        private void OnDestroy() { all.Remove(this); }

        private global::PuzzleButton button;
        private bool wasHeld;
        private bool trimmed;

        private void Start() {
            plinths.RemoveAll(p => p == null);
            all.Add(this);
            button = GetComponent<global::PuzzleButton>();
            if (button == null)
                Plugin.log.LogWarning($"[TowerButton] '{name}' has no PuzzleButton next to it, so nothing can be pressed.");
        }

        private void Update() {
            // First frame: every puzzle has been built by now (they are all built in the same frame)
            if (!trimmed) { trimmed = true; TrimToPuzzleCount(); }

            // Host only: only the host's game knows who holds the button
            if (button == null || !PuzzleSDK.IsHost) return;
            bool held = button.IsHeld;
            if (held == wasHeld) return;
            wasHeld = held;
            if (!held) return;   // the plinth closes itself when the button is let go

            foreach (PuzzleGourdPlinth p in plinths) {
                if (!Usable(p) || p.HasGourd) continue;
                p.HoldOpenFor(button);
                return;
            }
        }

        // The plinths in use, in fill order (a save fills them from the first)
        internal List<PuzzleGourdPlinth> Slots {
            [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] get {
                if (!trimmed) { trimmed = true; TrimToPuzzleCount(); }
                return plinths.FindAll(Usable);
            }
        }

        private static bool Usable(PuzzleGourdPlinth p) => p != null && p.gameObject.activeInHierarchy;

        // Keeps one plinth per loaded modded puzzle, in list order; switches off the rest
        private void TrimToPuzzleCount() {
            int puzzles = 0;
            foreach (PuzzleSDK sdk in PuzzleSDK.all)
                if (sdk != null && sdk.puzzleType == PuzzleType.Puzzle && sdk.gameObject.activeInHierarchy) puzzles++;

            for (int i = 0; i < plinths.Count; i++)
                if (plinths[i] != null) plinths[i].gameObject.SetActive(i < puzzles);

            if (puzzles > plinths.Count)
                Plugin.log.LogWarning($"[TowerButton] '{name}': {puzzles} modded puzzles are loaded but the tower has only {plinths.Count} plinths; the extra gourds have nowhere to go.");
        }
    }

} // namespace BigPuzzleSDK
