// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// The game's wall microphone. Players hold it to talk to intercom speakers.
// EXPERIMENTAL: copied from the game as it is; no events or functions yet.
[PuzzleInfo("The game's wall microphone.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleIntercom : MonoBehaviour {
}

}
