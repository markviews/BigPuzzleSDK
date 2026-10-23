// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's light switch.
[PuzzleInfo("Starts off, and remembers its position when the lobby is restarted. The matching event fires when the puzzle first loads.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleSwitch : MonoBehaviour {
    [SerializeField] private UnityEvent onTurnedOn;
    [SerializeField] private UnityEvent onTurnedOff;

    // Read-only for scripts
    public bool IsOn => false;
}

}
