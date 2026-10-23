// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// The game's round peg tile (Egg Hunt): a grabbable prop for the circle peg board.
[PuzzleInfo("The game's round peg tile (Egg Hunt).")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleCirclePeg : MonoBehaviour {
    [Tooltip("The peg's colour. White = the game's own (red).")]
    [SerializeField] private Color color = Color.white;
}

}
