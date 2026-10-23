// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's glass box with a lid. Starts closed.
[PuzzleInfo("HoldToOpen() from a hold button stays open only while it's held. In a Puzzle with no Gourd Vice, the box holds the puzzle's gourd: picking it up completes the puzzle (there is no win event).")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleBox : MonoBehaviour {
    // Read-only for scripts
    public bool IsOpen => false;

    // Host only. Opens and stays open until Close().
    public void Open() { }

    // Host only. For a hold button's onPressed: open while the button is held, closed again when let go.
    public void HoldToOpen() { }

    // Host only
    public void Close() { }
}

}
