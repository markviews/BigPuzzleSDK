using UnityEngine;

namespace BigPuzzleSDK {

// One player of the lobby, from PuzzleSDK.players. Everything is read-only, except that the host can move a player
// (Teleport) and make them put something down (DropItem, DropHat). These are stand-ins: the real ones run inside the game.
// A player who disconnects stays in the list, in the same place, with isOnline false; the values then keep what they
// were when the player left (position, colors, name), and seat / hat / held are empty. If the same player joins again
// they get their old entry back.
public sealed class PuzzlePlayer {
    private PuzzlePlayer() { }

    // The player's platform id (their Steam id, ...). Stays the same when they rejoin.
    public string id => "";
    public string name => "";
    public bool isHost => false;
    public bool isLocal => false;
    public bool isOnline => false;
    public Color colorHead => Color.white;
    public Color colorBody => Color.white;
    public Color colorFeet => Color.white;

    public Vector3 position => Vector3.zero;
    public Quaternion headRotation => Quaternion.identity;

    // The Seat (from your puzzle) the player is sitting in, or null
    public PuzzleSeat seat => null;
    // The name of what the player wears on their head (the blindfold slot), or null
    public string hat => null;
    // The name of what the player holds in their hands, or null
    public string held => null;

    // Host only. Stands the player up if they sit, then moves them. Keeps the way they face unless you pass a rotation.
    public void Teleport(Transform target) { }
    public void Teleport(Vector3 position, Quaternion? rotation = null) { }
    // Host only. Stands the player up if they are sitting (in a seat, at a telescope...).
    public void EjectFromSeat() { }
    // Host only. Makes the player put down what they hold in their hands.
    public void DropItem() { }
    // Host only. Makes the player take off what they wear on their head.
    public void DropHat() { }
}

}
