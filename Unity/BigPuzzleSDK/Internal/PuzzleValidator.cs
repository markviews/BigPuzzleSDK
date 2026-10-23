// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's check / cross screen. Tell it whether the answer is right with SetCorrect, then
// Check() plays the 5-second "checking" animation and shows the result. Starts on.
[PuzzleInfo("A right answer wins the puzzle (PuzzleSDK.Win()); a wrong one fires On Incorrect.\nSet Poke Button and pressing it calls Check() for you. The button turns on and off with this screen, and re-arms after each result unless the screen was turned off.\nOr call Check() yourself from any event.\nThe screen is always off while the puzzle is switched off. TurnOn() / TurnOff() are remembered and applied when the puzzle starts.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleValidator : MonoBehaviour {
    [Tooltip("Check() found the answer wrong.")]
    [SerializeField] private UnityEvent onIncorrect;

    [Tooltip("Optional. Pressing it calls Check(), and it is switched on and off along with this screen.")]
    [SerializeField] private PuzzlePokeButton pokeButton;

    // Read-only for scripts
    public bool IsCorrect => false;
    public bool IsOn => true;

    // Host only. Nothing shows until Check().
    public void SetCorrect(bool value) { }
    // Host only. Checking animation, then the result: right calls PuzzleSDK.Win(), wrong fires onIncorrect. Does nothing while off.
    public void Check() { }
    // Host only. Screen on: blank, ready to check.
    public void TurnOn() { }
    // Host only. Screen off; cancels a check in progress.
    public void TurnOff() { }
}

}
