// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// What a peg board slot holds. What a peg board slot holds.
public enum PuzzlePegType { Empty, Image, Sound }

// What is in one slot of a peg board (see PuzzlePegBoard.GetSlot)
public struct PegSlot {
    public PuzzlePegType Type;     // Empty, Image (a picture tile) or Sound (a sound peg)
    public PuzzlePegSet ImageSet;  // Image: the set the picture is from
    public int Image;              // Image: its number in that set (-1 if the SDK doesn't recognise it); 0 otherwise
    public string Sound;           // Sound: "sound_1".."sound_9", or "sound_" and a custom sound's name; "" otherwise
}

// How many slots the board has (the game's board models)
internal enum PuzzlePegBoardSize { Slots1, Slots2, Slots3, Slots4, Slots5, Slots6, Slots12 }

// The game's peg board, 1 to 6 slots or 12 (two rows of six). Checks the tiles hung on it against 'answer'.
[PuzzleInfo("Checks the tiles hung on it against the answer whenever a peg changes.\nPick a Display flap or Sound Peg Answer per slot in Answer From.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzlePegBoard : MonoBehaviour {
    [Tooltip("How many slots. 12 = two rows of six (first row left to right, then the second).")]
    [SerializeField] private PuzzlePegBoardSize size = PuzzlePegBoardSize.Slots5;

    [Tooltip("The little marker at the first slot's end. Looks only.")]
    [SerializeField] private bool startMarker;

    [Tooltip("Per slot (the first is slot 1): a Display or Sound Peg Answer whose picture or sound is that slot's answer. It follows them when they change. A slot left on None can never be correct.")]
    [SerializeField] private List<MonoBehaviour> answerFrom = new List<MonoBehaviour>();

    [Tooltip("Per slot: which flap of a multi-flap Display that slot follows (1 is the far left). Set from the Answer From dropdowns.")]
    [SerializeField] private List<int> answerFromFlap = new List<int>();

    [Tooltip("Optional. Left empty, the puzzle's only Validator is used. Kept up to date for you: correct = SetCorrect(true) + TurnOn(), wrong = SetCorrect(false) + TurnOn(), pegs missing = SetCorrect(false) + TurnOff(). The screen only lights up while the puzzle is on.")]
    [SerializeField] private PuzzleValidator validator;

    [Tooltip("A peg changed and the board matches the answer.")]
    [SerializeField] private UnityEvent onCorrect;
    [Tooltip("A peg changed; the board is full but wrong.")]
    [SerializeField] private UnityEvent onIncorrect;
    [Tooltip("A peg changed; some slots are still empty.")]
    [SerializeField] private UnityEvent onMissingPegs;

    // For scripts (read-only)
    public int SlotCount {
        get {
            switch (size) {
                case PuzzlePegBoardSize.Slots1: return 1;
                case PuzzlePegBoardSize.Slots2: return 2;
                case PuzzlePegBoardSize.Slots3: return 3;
                case PuzzlePegBoardSize.Slots4: return 4;
                case PuzzlePegBoardSize.Slots6: return 6;
                case PuzzlePegBoardSize.Slots12: return 12;
                default: return 5;
            }
        }
    }
    public bool IsCorrect => false;
    // What is in a slot (0 is the first). A slot that doesn't exist counts as empty.
    public PegSlot GetSlot(int slot) { return default(PegSlot); }
}

}
