// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// The game's gourd valet pillow: a model only, no settings. Hidden in-game unless the gourd is lying on it.
[PuzzleInfo("Hidden in the game, so it is not there on a fresh puzzle. It appears, with the gourd on it, only if players won this puzzle but lost the gourd last time they played (it wasn't held or in the tower).")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzlePillow : MonoBehaviour {
}

}
