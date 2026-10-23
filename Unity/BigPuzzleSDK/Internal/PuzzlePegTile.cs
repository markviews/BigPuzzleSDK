// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The peg tile picture sets. The numbers match PuzzleImageSet's (A = 0, B = 1, D = 3,
// Pontiff = 6, Custom = 9), so the game's set is found by the same number and older tiles keep
// their setting.
public enum PuzzlePegSet { Shapes = 0, Peg = 1, Emotes = 3, Scribbles = 6, Custom = 9 }

// A game peg tile showing one picture. Players carry it and hang it on a PuzzlePegBoard.
[PuzzleInfo("A peg tile with one picture. Players carry it and hang it on a PuzzlePegBoard.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzlePegTile : MonoBehaviour {
    [SerializeField] private PuzzlePegSet imageSet;

    [Min(1)]
    [SerializeField] private int image = 1;

    [Tooltip("For Custom: one square texture, 4 x 4 grid, top-left cell blank, then images 1 to 8.")]
    [SerializeField] private Texture2D customSheet;

    [Tooltip("The picture only shows through a telescope with Invisible Ink View on (like the Invisible Ink challenge's); otherwise the tile looks blank.")]
    [SerializeField] private bool invisibleInkView;

    // Read-only for scripts
    public PuzzlePegSet ImageSet => imageSet;
    public int Image => image;
}

}
