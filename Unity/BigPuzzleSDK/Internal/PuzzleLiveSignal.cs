// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's "live" lamp. Lights by itself while the puzzle is running.
[PuzzleInfo("Lights up when a Start Switch is turned on.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleLiveSignal : MonoBehaviour {
}

}
