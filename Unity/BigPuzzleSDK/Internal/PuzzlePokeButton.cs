// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's one-click button.
[PuzzleInfo("After a press it stays off until re-armed (Auto Enable Seconds, or SetClickable(true)).")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzlePokeButton : PuzzleButtonBase {
    [Tooltip("0 = stays off after a press. Otherwise it can be pressed again after this many seconds.")]
    [Min(0f)]
    [SerializeField] private float autoEnableSeconds;

    [SerializeField] private UnityEvent onPressed;

    // Read-only for scripts
    public bool IsClickable => true;   // starts clickable; set the starting state from PuzzleSDK On Puzzle Stop

    // Host only. true also re-arms a button that was just pressed.
    public void SetClickable(bool value) { }
}

}
