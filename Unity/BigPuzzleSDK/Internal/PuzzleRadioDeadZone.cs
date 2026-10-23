// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// A radio dead zone, like the game's own: walkie-talkies and megaphones inside the collider(s) on
// this object show "dead zone" and can't send or receive. Add a Box (or other) Collider sized to
// the area; it becomes an invisible trigger in-game, and any renderer here is hidden.
[PuzzleInfo("Walkie-talkies and megaphones stop working inside this object's collider(s).")]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleRadioDeadZone : MonoBehaviour {
    private void OnDrawGizmos() {
        Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.25f);
        foreach (Collider c in GetComponents<Collider>()) Gizmos.DrawCube(c.bounds.center, c.bounds.size);
    }
}

}
