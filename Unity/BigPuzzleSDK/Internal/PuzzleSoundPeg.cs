// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// The game's sound tracks. Must stay identical, in the same order, to the mod's enum.
public enum PuzzleSoundTrack { Sound1, Sound2, Sound3, Sound4, Sound5, Sound6, Sound7, Sound8, Sound9 }

// The game's speaker peg tile: a grabbable tile that plays a tune.
[PuzzleInfo("The game's speaker peg tile. Use the same Sound on a peg and its answer speaker.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleSoundPeg : MonoBehaviour {
    [Tooltip("One of the game's nine sound tracks. They loop in sync with each other.")]
    [SerializeField] private PuzzleSoundTrack sound = PuzzleSoundTrack.Sound1;

    [Tooltip("Plays instead of the track when set. It won't stay in step with the game's tracks.")]
    [SerializeField] private AudioClip customClip;

    // Read-only for scripts
    public PuzzleSoundTrack Sound => sound;
}

}
