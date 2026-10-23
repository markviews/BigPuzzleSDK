// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// Which of the game's items. Must stay identical, in the same order, to the mod's enum.
public enum PuzzleVanillaItemType { Blindfold, FoldingChair, Torch, FmRadio, WalkieTalkie, Binoculars, XrayGoggles, Clock, Laser, Megaphone, Kettle, CannonBall, PermoSign, SpareSign, GourdHolder, Belt, Backpack, Gps, FoldingMap, Compass, FlareGun, CowbellBig, CowbellMedium, CowbellSmall, Paintbrush, Football, Buoy, BuoyRed }

// One of the game's own items, copied as it is. Players pick it up and use it like the original.
[PuzzleInfo("One of the game's own items, copied as it is: players pick it up, carry and use it like the original.\nChoose which item with Item. It has no events or functions of its own.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleVanillaItem : MonoBehaviour {
    [Tooltip("Which of the game's items this is.")]
    [SerializeField] private PuzzleVanillaItemType itemType = PuzzleVanillaItemType.Blindfold;

    [Tooltip("Buoy and Buoy Red only: include the pedestal the buoy light sits on. Unticked, you get just the buoy, a loose item where this object is.")]
    [SerializeField] private bool includeBase = true;

    [Tooltip("Spare Sign and Permo Sign only. Spare Sign: the text until a player writes something (what a player writes is saved with the game and comes back next time). Permo Sign: its fixed text. Leave empty for a blank sign.")]
    [TextArea(2, 6)]
    [SerializeField] private string defaultText = "";

    [Tooltip("Kettle, Walkie Talkie, Backpack, Belt and Flare Gun only: the item's colour. White (#FFFFFF) keeps the game's own colour.")]
    [SerializeField] private Color itemColor = Color.white;

    [Tooltip("Flare Gun only: the colour of the flare it shoots. White (#FFFFFF) keeps the game's own colour.")]
    [SerializeField] private Color flareColor = Color.white;

    [Tooltip("Saved with the game, like gourds. If a player is holding it (hand, belt or backpack) when the game saves or the host reloads the puzzles (Ctrl+F2), it comes back at the spawn area, and stays saved as held while it lies there. Once it has been taken away and put down elsewhere it only saves while held. Otherwise it starts where you put it. Its position is never saved.")]
    [SerializeField] private bool savesOnReload = true;
}

}
