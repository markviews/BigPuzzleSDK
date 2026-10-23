// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;

namespace BigPuzzleSDK {

// The game's N-hold button (Black Tower). Every button with the same Group must be held at once.
// The case shows how many are needed (2, 3 or 4 dots; 4 for more than four) and a dot lights for
// each button held.
[PuzzleInfo("Every button with the same Group must be held at the same time.\nThe dots show how many: 2, 3 or 4 (4 for more than four), counting only switched-on buttons, so a PuzzleHider can remove buttons per lobby size.\nNote: ALL buttons in the group fire their events, so only wire events to one.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleButtonNHold : MonoBehaviour {
    [Tooltip("Buttons in this puzzle with the same group must all be held together.")]
    [SerializeField] private string group = "";

    [Tooltip("Every button in the group is held.")]
    [SerializeField] private UnityEvent onAllHeld;

    [Tooltip("They were all held, and one was let go.")]
    [SerializeField] private UnityEvent onAllReleased;

    // Read-only for scripts
    public bool IsHeld => false;     // this button is held
    public bool AllHeld => false;    // the whole group is held
    public int GroupSize => 0;       // switched-on buttons in the group
}

}
