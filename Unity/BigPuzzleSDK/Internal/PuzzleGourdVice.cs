// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's gourd vice. PuzzleSDK.Win() releases it.
[PuzzleInfo("Fires on PuzzleSDK.Win()")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleGourdVice : MonoBehaviour {
}

}
