// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;

namespace BigPuzzleSDK {

// The game's holder for a timer ball (Obby). On Timer Placed fires each time a timer ball goes in.
[PuzzleInfo("The game's holder for a timer ball (Obby). On Timer Placed fires when a ball goes in.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleTimerGoal : MonoBehaviour {
    [Tooltip("A timer ball was put in the holder.")]
    [SerializeField] private UnityEvent onTimerPlaced;
}

}
