// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// The game's timer ball dispenser (Obby). Picking up the ball starts its countdown; when it runs
// out the ball goes back into the dispenser, ready to be taken again.
[PuzzleInfo("The game's timer ball dispenser (Obby).")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleTimerSpawner : MonoBehaviour {
    [Tooltip("Seconds the timer ball lasts. When it runs out the ball goes back into the dispenser, so this is also the wait before a new one can be taken. The game's is 10.")]
    [Min(0.5f)]
    [SerializeField] private float time = 10f;
}

}
