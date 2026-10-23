// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's image sets.
public enum PuzzleImageSet { A, B, C, D, Scout, Circle, Pontiff, Camo, LargeA, Custom }

// How many flaps the display has (the game's 1-, 2-, 3- and 5-slot frames)
internal enum PuzzleDisplaySize { One, Two, Three, Five }

// The game's split-flap image display.
[PuzzleInfo("The game's split-flap image display. A Peg Board can use it as its answer (Answer From). It goes blank when the puzzle stops.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleDisplay : MonoBehaviour {
    [Tooltip("How many flaps the frame has: 1, 2, 3 or 5. Each flap shows its own picture, numbered from the left (flap 1 is the far left). A Peg Board picks which flap each of its slots follows. Ignored when Copy Display is set: a copy always has the size of the display it follows.")]
    [SerializeField] private PuzzleDisplaySize size = PuzzleDisplaySize.One;

    [Tooltip("Optional. Always shows whatever the other display shows, with the same flip. It doesn't take a tile of its own.")]
    [SerializeField] private PuzzleDisplay copyDisplay;

    // Read-only for scripts: the image number being shown on flap 1 (0 = blank)
    public int Image => 0;

    // Read-only for scripts: the image number being shown on a flap (1 is the far left; 0 = blank)
    public int GetImage(int flap) { return 0; }

    // Read-only for scripts: the set being shown
    public PuzzleImageSet ImageSet => PuzzleImageSet.A;

}

}
