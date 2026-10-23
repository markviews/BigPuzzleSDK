// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// The game's flare launcher (Flare Run), without its button: call Fire() to launch a flare.
[PuzzleInfo("The game's flare launcher. Call Fire() to launch a flare.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleFireworkLauncher : MonoBehaviour {
    [Tooltip("The flare's colour (its sparks and its light). White = the game's own colours.")]
    [SerializeField] private Color color = Color.white;

    // Host only. Launches the flare for everyone (ignored while it is still cooling down).
    public void Fire() { }
}

}
