// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// The game's fixed speaker that plays the tune to match (not movable). It has no settings: when the puzzle
// starts it plays the sound of one Sound Peg from the puzzle, and it is silent while the puzzle is off.
[PuzzleInfo("The game's fixed speaker that plays the tune to match. Each time the puzzle starts it plays the sound of a different Sound Peg from the puzzle (a sound repeats only if the puzzle has more than one peg with it), and it is silent while the puzzle is off. A Peg Board can use it as its answer (Answer From).")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleSoundPegAnswer : MonoBehaviour {
    // Read-only for scripts: what it plays now ("sound_1".."sound_9", or "sound_" and a custom sound's name); "" while silent
    public string CurrentSound => "";
}

}
