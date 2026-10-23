// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's start knob. Flipping it starts the puzzle (PuzzleSDK.onPuzzleStart); flipping it back
// stops it. Scripts read PuzzleSDK.IsRunning for whether the puzzle is running.
[PuzzleInfo("The game's start knob. Connect door opening and whatnot to PuzzleSDK OnPuzzleStart / OnPuzzleStop")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleStartSwitch : MonoBehaviour {
}

}
