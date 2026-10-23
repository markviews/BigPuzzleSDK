// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's hold button: onPressed when pushed in, onReleased when let go.
// Wire onPressed to PuzzleBox/PuzzleGourdPlinth.HoldToOpen() for "open while held".
[PuzzleInfo("On Pressed fires when it's pushed in, On Released when it's let go.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleButton : PuzzleButtonBase {
    [SerializeField] private UnityEvent onPressed;
    [SerializeField] private UnityEvent onReleased;
}

}
