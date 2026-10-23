// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's "press together" lever. Pressed, it goes yellow for activeSeconds, then red for
// timeoutSeconds, then back to off. When every switch with the same group is yellow at the same
// moment, they all turn green and fire onSuccess; green also lasts timeoutSeconds, then back to off.
[PuzzleInfo("When every switch with the same Group is active at the same time, they all fire OnSuccess\nNote: ALL of switches in the group fire OnSuccess, so only wire events to one.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleSimPressSwitch : MonoBehaviour {
    [Tooltip("Yellow: the window in which the other switches must also be pressed.")]
    [Min(0f)]
    [SerializeField] private float activeSeconds = 2f;

    [Tooltip("Red (or green after a success): how long before it can be pressed again.")]
    [Min(0f)]
    [SerializeField] private float timeoutSeconds = 2f;

    [Tooltip("Switches in this puzzle with the same group are linked.")]
    [SerializeField] private string group = "";

    [Tooltip("Every switch in the group was pressed in time.")]
    [SerializeField] private UnityEvent onSuccess;
}

}
