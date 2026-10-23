// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// For a modded gourd tower. Goes next to the PuzzleButton players hold. While the button is held,
// the first plinth in the list without a gourd is open; letting go closes it. In-game, only as
// many plinths stay as there are modded puzzles loaded (the first ones in the list); the rest
// are switched off.
[PuzzleInfo("While the hold button is held, opens the first plinth in the list without a gourd.\nIn-game, only the first plinths stay: one per modded puzzle loaded. The rest are switched off.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
[RequireComponent(typeof(PuzzleButton))]
public class TowerButton : MonoBehaviour {
    [Tooltip("The tower's plinths in the order they fill, bottom first.")]
    [SerializeField] private List<PuzzleGourdPlinth> plinths = new List<PuzzleGourdPlinth>();

    // Read-only for scripts
    public int SlotCount => plinths.Count;   // in-game: plinths in use
    public int FilledCount => 0;             // in-game: plinths with a gourd
    public bool IsFull => false;
}

}
