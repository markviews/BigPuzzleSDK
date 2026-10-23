// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;

namespace BigPuzzleSDK {

// The seat's look. Must stay identical, in the same order, to the mod's enum.
public enum PuzzleSeatPose { MidcenturyChair, FoldingChair, WoodenChair, ListeningChair, CouchLeft, CouchMiddle, CouchRight, BenchA, BenchB, BenchC, Stump, CarouselPony, ChairliftLeft, ChairliftRight, TrainSeatA, TrainSeatB, TrainSeatC, TrainSeatD, TrainSeatE }

// A seat players click to sit in (Jump to stand). Put it where a player's bottom should go. Players
// click anywhere in the blue box. Your own model stays as it is; the editor only draws the box.
[PuzzleInfo("A seat players click to sit in; they stand with Jump.\nSet where the player sits (green marker) and which way they face, and size the blue click box around your model.")]
[AddComponentMenu("Big Puzzle SDK/Seat")]
public class PuzzleSeat : MonoBehaviour {
    [Tooltip("How the player sits (the game's own seats: chairs, couch, benches, stump, carousel pony, chairlift, train seats).")]
    [SerializeField] private PuzzleSeatPose pose = PuzzleSeatPose.MidcenturyChair;

    [Tooltip("Where a player's bottom goes, measured from this object (the green ball). Separate from the click box.")]
    [SerializeField] private Vector3 seatOffset = Vector3.zero;

    [Tooltip("Which way the player faces, in degrees around the up axis (the green arrow).")]
    [SerializeField] private float seatTurn = 0f;

    [Tooltip("The click box players aim at, centred this far from the object.")]
    [SerializeField] private Vector3 clickCenter = new Vector3(0f, 0.3f, 0f);

    [Tooltip("The click box's size.")]
    [SerializeField] private Vector3 clickSize = new Vector3(0.8f, 0.6f, 0.8f);

    [Tooltip("A player sat down (host fires it).")]
    [SerializeField] private UnityEvent onSit;

    [Tooltip("The player got up.")]
    [SerializeField] private UnityEvent onStand;

    // Read-only in the game
    public bool IsOccupied => false;

    private void OnDrawGizmosSelected() {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.35f);
        Gizmos.DrawCube(clickCenter, clickSize);
        Gizmos.color = new Color(0.2f, 0.5f, 1f, 1f);
        Gizmos.DrawWireCube(clickCenter, clickSize);
        // A ball where the player's bottom goes, with a small arrow out of it showing which way they face
        Gizmos.color = Color.green;
        Quaternion turn = Quaternion.Euler(0f, seatTurn, 0f);
        Vector3 tail = seatOffset;
        Gizmos.DrawSphere(tail, 0.05f);
        Vector3 tip = tail + turn * Vector3.forward * 0.3f;
        Vector3 left = tip + turn * new Vector3(-0.06f, 0f, -0.1f);
        Vector3 right = tip + turn * new Vector3(0.06f, 0f, -0.1f);
        Gizmos.DrawLine(tail, tip);
        Gizmos.DrawLine(tip, left);
        Gizmos.DrawLine(tip, right);
        Gizmos.DrawLine(left, right);
    }
}

}
