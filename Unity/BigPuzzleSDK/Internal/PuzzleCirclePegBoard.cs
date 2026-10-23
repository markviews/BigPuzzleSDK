// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;

namespace BigPuzzleSDK {

// The game's round peg board (Egg Hunt).
[PuzzleInfo("The game's round peg board (Egg Hunt).")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleCirclePegBoard : MonoBehaviour {
    [Tooltip("Every slot has a peg.")]
    [SerializeField] private UnityEvent onFull;

    [Tooltip("A peg was taken off a full board.")]
    [SerializeField] private UnityEvent onNotFull;

    // Read-only for scripts
    public int SlotCount => 0;
    public int FilledCount => 0;
    public bool IsFull => false;
}

}
