// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// The game's turnstile: players pass through one at a time. With Blind Mask on it is the
// blindfold catwalk's turnstile instead of the plain one.
[PuzzleInfo("The game's turnstile.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleNoItemsGate : MonoBehaviour {
    [Tooltip("On: players can only pass from the front to the back (red plaques). Off: both ways (green plaques).")]
    [SerializeField] private bool oneWay = true;

    [Tooltip("Off: the plain turnstile (Poet and Priest). On: the blindfold catwalk's turnstile.")]
    [SerializeField] private bool blindMask;
}

}
