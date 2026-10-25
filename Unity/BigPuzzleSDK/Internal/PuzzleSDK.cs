// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// What a puzzle is, for the modded gourd tower. Must match the mod's enum, in the same order.
public enum PuzzleType { Puzzle, Tower, None }

// The puzzle's main component: one on the puzzle's root object.
// Inspector fields set where the puzzle spawns; the events tell you when it starts, stops and is won.
[PuzzleInfo("The puzzle's main component: one on the puzzle's root object. The puzzle spawns in-game exactly where this object sits in your scene (position and rotation). OnPuzzleStop fires once when the puzzle spawns, so set up the starting look there.")]
[AddComponentMenu("Big Puzzle SDK/Puzzle SDK")]
public class PuzzleSDK : MonoBehaviour {
    [Tooltip("Ctrl+F1 (creator keys on) teleports you here. For testing.")]
    [SerializeField] private Transform debugTeleport;

    [Tooltip("Puzzle: a challenge that rewards a gourd (needs one PuzzleGourdVice or one PuzzleBox to hold the gourd, and one PuzzlePillow). Tower: a gourd tower (needs at least 10 PuzzleGourdPlinths). None: anything else.")]
    [SerializeField] private PuzzleType puzzleType = PuzzleType.Puzzle;

    [Tooltip("A start switch was flipped on.")]
    [SerializeField] private UnityEvent onPuzzleStart;

    [Tooltip("The puzzle stopped. Also fires once when the puzzle spawns: set up its starting look here.")]
    [SerializeField] private UnityEvent onPuzzleStop;

    [SerializeField] private UnityEvent onWin;

    // Read-only for scripts
    public bool IsWon => false;
    public bool IsRunning => false;

    // True on the host (and in single player). SDK functions only take effect on the host.
    public static bool IsHost => true;

    // Called by the plugin class the exporter adds to your scripts DLL. Not for your own code.
    public static void RegisterAssemblyOf(object anyObject) { }

    // Host only. Fires every PuzzleConfetti and releases every PuzzleGourdVice, then onWin. Does not stop the puzzle: players flip the Start Switch back for that.
    public void Win() { }
}

}
