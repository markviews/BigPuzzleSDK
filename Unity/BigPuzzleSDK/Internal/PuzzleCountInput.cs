// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;

namespace BigPuzzleSDK {

// The game's counter with + and - buttons (Perspective Counting). Counts from 0 to 99.
// Point a Count Validator's Input at it to check the number, or use On Value Changed / Value
// to drive anything else.
[PuzzleInfo("The game's counter with + and - buttons. Point a Count Validator at it to check the number.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleCountInput : MonoBehaviour {
    [Tooltip("The number changed (a button, or SetValue).")]
    [SerializeField] private UnityEvent onValueChanged;

    // Read-only for scripts: the number on the counter
    public int Value => 0;

    // Host only. Sets the counter (kept between 0 and 99).
    public void SetValue(int value) { }
}

}
