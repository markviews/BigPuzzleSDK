// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// The game's fixed telescope. Players step up and look through it. With Invisible Ink View on it is the
// Invisible Ink challenge's telescope, whose colour filter shows invisible peg tiles; off, it is a
// plain telescope (the Optical Telegraph's).
[PuzzleInfo("A telescope players look through. With Invisible Ink View on, looking through it shows peg tiles that have Invisible Ink View on.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleTelescope : MonoBehaviour {
    [Tooltip("On: the Invisible Ink challenge's colour filter, which shows peg tiles that have Invisible Ink View on. Off: a plain telescope.")]
    [SerializeField] private bool invisibleInkView;
}

}
