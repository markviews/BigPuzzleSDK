// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// One slot of the game's gourd tower. Open its flap and players can place a gourd in it
// (a placed gourd can't be taken back out). Starts closed.
[PuzzleInfo("HoldToOpen() from a hold button stays open only while it's held.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleGourdPlinth : MonoBehaviour {
    [Tooltip("A gourd was placed in the slot.")]
    [SerializeField] private UnityEvent onGourdPlaced;

    // Read-only for scripts
    public bool IsOpen => false;
    public bool HasGourd => false;

    // Host only. Opens and stays open until Close().
    public void Open() { }

    // Host only. For a hold button's onPressed: open while the button is held, closed again when let go.
    public void HoldToOpen() { }

    // Host only
    public void Close() { }
}

}
