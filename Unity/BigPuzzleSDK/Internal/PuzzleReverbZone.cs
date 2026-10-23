// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// Sets a room's echo by hand while a player's ears are inside the collider(s) on this object.
// Normally the game measures every space with rays and works out its echo itself; anything not
// overridden here is still measured. The colliders become invisible triggers in-game.
//
// Note: the game's measurement only "sees" its own world layers, which includes walls with
// PuzzleSoundProof on them. Ordinary puzzle walls don't count, so a room only echoes from its
// soundproof walls.
[PuzzleInfo("Sets a room's echo by hand while a player is inside its collider(s). Values you don't override are measured by the game.")]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleReverbZone : MonoBehaviour {
    [Tooltip("Override how big the room sounds.")]
    [SerializeField] private bool overrideRoomSize;
    [Range(0f, 1f), Tooltip("0 = tiny room, 1 = huge hall.")]
    [SerializeField] private float roomSize = 0.5f;

    [Tooltip("Override how open the space sounds.")]
    [SerializeField] private bool overrideOutdoorness = true;
    [Range(0f, 1f), Tooltip("0 = fully enclosed, 1 = open air (no echo).")]
    [SerializeField] private float outdoorness = 1f;

    [Tooltip("Override how long the echo lasts.")]
    [SerializeField] private bool overrideReverbTime;
    [Range(0f, 1f), Tooltip("0 = no tail, 1 = very long tail.")]
    [SerializeField] private float reverbTime = 0.2f;

    [Tooltip("Override how smooth the echo is.")]
    [SerializeField] private bool overrideDiffusion;
    [Range(0f, 1f), Tooltip("0 = distinct echoes, 1 = smooth wash.")]
    [SerializeField] private float diffusion = 1f;

    private void OnDrawGizmos() {
        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.2f);
        foreach (Collider c in GetComponents<Collider>()) Gizmos.DrawCube(c.bounds.center, c.bounds.size);
    }
}

}
