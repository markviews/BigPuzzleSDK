// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;

namespace BigPuzzleSDK {

// The game's counter validator screen (Perspective Counting). Shows 00 until Check(); then its
// dials count up to the answer a script sets with SetAnswer(), and it shows whether the Input's number matched (tick or cross).
// Changing the counter afterwards puts it back to 00.
[PuzzleInfo("Shows 00 until Check(). Then it counts up to the answer a script sets with SetAnswer() (never right until it is set) and shows whether its Input's number matched, then a right number wins the puzzle (PuzzleSDK.Win()) and a wrong one fires On Incorrect.\nTurnOn() / TurnOff() turn the screen on and off; Check() does nothing while off. It is always off while the puzzle is switched off; TurnOn() / TurnOff() are remembered and applied when the puzzle starts.\nSet Poke Button and pressing it calls Check() for you; it turns on and off with the screen and re-arms after each result.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleCountValidator : MonoBehaviour {
    [Tooltip("The counter it checks. Left empty, the puzzle's only Count Input is used.")]
    [SerializeField] private PuzzleCountInput input;

    [Tooltip("Check() found a wrong number.")]
    [SerializeField] private UnityEvent onIncorrect;

    [Tooltip("Optional. Pressing it calls Check(), and it is switched on and off along with this screen.")]
    [SerializeField] private PuzzlePokeButton pokeButton;

    // Read-only for scripts
    public int Answer => -1;   // -1 until SetAnswer() is called: it can never be right without a script setting it
    public bool IsCorrect => false;
    public bool IsChecking => false;   // counting up, result not shown yet
    public bool IsOn => true;

    // Host only. Counts up to the answer, then shows whether the counter matched.
    public void Check() { }
    // Host only. Sets the answer (0 to 99); back to 00 (an off screen stays off).
    public void SetAnswer(int value) { }
    // Host only. Turns the screen on: 00, ready to check.
    public void TurnOn() { }
    // Host only. Turns the screen off (shows 00, cancels a check). Check() does nothing while off.
    public void TurnOff() { }
}

}
