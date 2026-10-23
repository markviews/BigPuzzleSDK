// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;

namespace BigPuzzleSDK {

// A button that players press by standing in it. Add a Box (or other) Collider sized to the area.
[PuzzleInfo("Presses when enough players stand inside this object's collider(s), and releases when too few are left. Make the collider at least 2 m tall, starting at the floor. It becomes an invisible trigger in-game. It can be used anywhere a button can, including Press In Order.")]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("Big Puzzle SDK/Presence Sensor")]
public class PuzzlePresenceSensor : PuzzleButtonBase {
    [Tooltip("Players needed inside at the same time to press it.")]
    [Min(1)]
    [SerializeField] private int requiredPlayers = 1;

    [Tooltip("Enough players are inside. Like a button's On Pressed.")]
    [SerializeField] private UnityEvent onPressed;

    [Tooltip("Too few players are left inside. With Required Players 1, that is when the last player leaves.")]
    [SerializeField] private UnityEvent onReleased;

    [Tooltip("Fires once for each player that steps in.")]
    [SerializeField] private UnityEvent onPlayerEntered;

    [Tooltip("Fires once for each player that steps out.")]
    [SerializeField] private UnityEvent onPlayerLeft;

    // Read-only for scripts
    public int PlayerCount => 0;

    private void OnDrawGizmos() {
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.25f);
        foreach (Collider c in GetComponents<Collider>()) Gizmos.DrawCube(c.bounds.center, c.bounds.size);
    }
}

}
