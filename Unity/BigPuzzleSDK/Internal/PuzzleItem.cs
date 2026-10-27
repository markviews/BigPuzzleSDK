// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;

namespace BigPuzzleSDK {

// The sound set. Must stay identical, in the same order, to the mod's enum.
public enum PuzzleItemSounds { Peg, Cowbell, Buoy, Football, Cannonball, Torch, FoldingChair, WalkieTalkie, Binoculars, Kettle, NightVisionGoggles, LaserPointer, Clock, Pomodoro, Sign, FlareGun, Blindfold, Megaphone, SalonBrush, Laptop, FoldingMap, CoordinatesTracker, GourdHolder, GourdCradle, Key, PermoSign }

// The animation played while a player uses a Custom Item. Must stay identical to the mod's enum.
public enum PuzzleItemUseAnimation {
    None = 0,
    Binoculars = 10, LaserPointer = 11, NightVisionGoggles = 12, Megaphone = 20, WalkieTalkie = 30,
    FlareGun = 110, SalonBrush = 120, Buoy = 130, Cannonball = 140, Football = 150, Torch = 160, Cowbell = 170, Radio = 180
}

// "Custom Item": your own model as an item players can pick up, carry and throw like the game's items.
// Put it on an object with a mesh (or with meshes under it). The game fits one box collider around
// all the meshes; colliders on the model itself are not used.
[PuzzleInfo("Custom Item: your model as an item players can pick up, carry, throw and keep in their backpack.\nPut it on an object with a mesh (or meshes under it); the game fits one box collider around them.")]
[AddComponentMenu("Big Puzzle SDK/Custom Item")]
public class PuzzleItem : MonoBehaviour {
    [Tooltip("The animation the player plays while using the item, like the game's own items. None: the game's default response to using an item that does nothing. Binoculars, Laser Pointer, Night Vision Goggles, Megaphone, Walkie Talkie and Cannonball stay on while the use button is held; the rest play once when it is pressed.")]
    [SerializeField] private PuzzleItemUseAnimation useAnimation = PuzzleItemUseAnimation.None;

    [Tooltip("The sounds it makes when grabbed, let go of and bumped, taken from one of the game's items. Football, Buoy and Cannonball also roll with a sound.")]
    [SerializeField] private PuzzleItemSounds sounds = PuzzleItemSounds.Peg;

    [Tooltip("Optional. An empty object (usually a child) that marks the item's pivot: where players hold it, and the point it spins around when thrown. Its forward direction is the way the item points. Move and rotate it in the Scene view. Leave empty to use this object's own origin.")]
    [SerializeField] private Transform pivot;

    [Tooltip("How heavy it is when it bumps into things (1 is about the weight of a cowbell).")]
    [SerializeField] private float mass = 1f;

    [Tooltip("Players can put it on their belt.")]
    [SerializeField] private bool canBelt = true;

    [Tooltip("Players can put it in their backpack.")]
    [SerializeField] private bool canBackpack = true;

    [Tooltip("Saved with the game, like gourds. If a player is holding it (hand, belt or backpack) when the game saves or the host reloads the puzzles (Ctrl+F2), it comes back at the spawn area, and stays saved as held while it lies there. Once it has been taken away and put down elsewhere it only saves while held. Otherwise it starts where you put it. Its position is never saved.")]
    [SerializeField] private bool savesOnReload = true;

    [Tooltip("A player picked it up or put it in their backpack (the host fires it).")]
    [SerializeField] private UnityEvent onPickedUp;

    [Tooltip("The player let go of it.")]
    [SerializeField] private UnityEvent onDropped;

    [Tooltip("A player holding it pressed the use button (the host fires it). Nothing else happens when it is used.")]
    [SerializeField] private UnityEvent onUse;

    [Tooltip("The player let go of the use button, or put the item away while pressing it (the host fires it).")]
    [SerializeField] private UnityEvent onRelease;

#if UNITY_EDITOR
    // Shows where the pivot is while the item is selected: a small cross and the forward arrow
    private void OnDrawGizmosSelected() {
        Transform t = pivot != null ? pivot : transform;
        Gizmos.matrix = t.localToWorldMatrix;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(Vector3.zero, 0.03f);
        Gizmos.DrawLine(new Vector3(-0.06f, 0, 0), new Vector3(0.06f, 0, 0));
        Gizmos.DrawLine(new Vector3(0, -0.06f, 0), new Vector3(0, 0.06f, 0));
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(Vector3.zero, new Vector3(0, 0, 0.15f));
    }
#endif

    // Read-only in the game
    public bool IsHeld => false;
}

}
