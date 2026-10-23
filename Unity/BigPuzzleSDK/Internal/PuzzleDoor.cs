// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// What a door does when the puzzle starts and stops
public enum PuzzleDoorMode { OpenWhenPuzzleActive, CloseWhenPuzzleActive, ControlWithFunctionsOnly }

// The game's double door.
[PuzzleInfo("Opens or closes with the puzzle (see Mode), or only when you call Open() and Close() using events. Soundproof blocks voices while it's closed.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleDoor : MonoBehaviour {
    [Tooltip("Open when puzzle active: open while the puzzle is on, closed while it's off.\nClose when puzzle active: closed while the puzzle is on, open while it's off.\nControl with functions only: starts closed and only Open() and Close() move it.\nOpen() and Close() still work in every mode.")]
    [SerializeField] private PuzzleDoorMode mode = PuzzleDoorMode.CloseWhenPuzzleActive;

    [Tooltip("Blocks players' voices while closed, like the game's soundproof doors. Voices pass once it opens.")]
    [SerializeField] private bool soundproof;

    // Host only
    public void Open() { }
    public void Close() { }
}

}
