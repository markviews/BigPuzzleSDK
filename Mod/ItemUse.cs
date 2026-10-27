using System;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace BigPuzzleSDK {

// OnUse / OnRelease of a custom item. A custom item has no use switch, so the game answers a press of the use button
// with its "dud" command (on the PC of the player who pressed). That is the press; the release is the use button
// coming back up (or the item leaving the hand). The player's PC tells the host, which checks that the player really
// is holding the item and then fires the events (which reach everyone, like On Picked Up).
internal static class ItemUse {
    private static PuzzleItem pressing;   // the item this PC's player is using right now
    private static ulong pressingId;

    internal static void Install(Harmony harmony) {
        try {
            MethodInfo target = AccessTools.Method(typeof(PlayerNetworking), "CmdDudUseHeld");
            if (target == null) throw new MissingMethodException("PlayerNetworking.CmdDudUseHeld not found");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(ItemUse).GetMethod(nameof(Pressed), BindingFlags.Static | BindingFlags.NonPublic)));
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleItem] could not hook the use button ({ex.GetType().Name}: {ex.Message}); On Use and On Release will not work.");
        }
    }

    // This PC's player pressed use while holding something that does nothing
    private static void Pressed(PlayerNetworking __instance) {
        try {
            if (__instance == null || !__instance.isLocalPlayer) return;
            PlayerCharacter pc = __instance.playerCharacter;
            Prop held = pc != null && pc.hands != null ? pc.hands.heldProp : null;
            PuzzleItem item = ItemOf(held);
            if (item == null) return;
            pressing = item;
            pressingId = item.ItemId;
            Deliver(item.ItemId, true, item);
        } catch (Exception ex) {
            PlayerRoster.Warn("itemuse", $"[PuzzleItem] could not read the use button: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static PuzzleItem ItemOf(Prop prop) {
        if (prop == null) return null;
        foreach (PuzzleItem item in PuzzleItem.Items()) {
            Prop mine = item.GameProp;
            if (mine != null && mine.Pointer == prop.Pointer) return item;
        }
        return null;
    }

    // Every frame: is the player still holding the use button on that item?
    internal static void Tick() {
        if (pressing == null) return;
        bool still = false;
        try {
            PlayerCharacter pc = PlayerRoster.LocalCharacter();
            Rewired.Player input = pc != null ? pc.inputPlayer : null;
            Prop held = pc != null && pc.hands != null ? pc.hands.heldProp : null;
            still = input != null && input.GetButton(RewiredConsts.Action.use) && held != null && pressing.GameProp != null && held.Pointer == pressing.GameProp.Pointer;
        } catch (Exception) { }
        if (still) return;
        PuzzleItem item = pressing;
        pressing = null;
        Deliver(pressingId, false, item);
    }

    // Host: events here. Other players: tell the host.
    private static void Deliver(ulong id, bool down, PuzzleItem item) {
        ItemUseAnimation.PlayLocal(item, down);
        if (PuzzleSDK.IsHost) {
            if (item == null) return;
            item.RaiseUse(down);
            foreach (PuzzlePlayer player in PlayerRoster.View) if (player.isLocal) { ItemUseAnimation.Announce(player, item, down); break; }
            return;
        }
        PlayerCommands.SendToHost("iu|" + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (down ? "1" : "0"));
    }

    // Host: a player's PC says it pressed / released the use button on an item ("iu|id|1")
    internal static void OnPlayerMessage(int connectionId, string text) {
        if (!SdkHandshake.HasSameVersion(connectionId)) return;
        string[] part = text.Split('|');
        if (part.Length < 3 || !ulong.TryParse(part[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong id)) return;
        bool down = part[2] == "1";
        foreach (PuzzleItem item in PuzzleItem.Items()) {
            if (item.ItemId != id) continue;
            PuzzlePlayer sender = null;
            foreach (PuzzlePlayer player in PlayerRoster.View) {
                PlayerNetworking pn = player.Networking;
                NetworkConnectionToClient conn = pn != null ? pn.connectionToClient : null;
                if (conn != null && conn.connectionId == connectionId) { sender = player; break; }
            }
            // Only if that player really is holding it (a modified game could say anything)
            if (down) {
                PlayerCharacter holder = sender != null ? sender.Character : null;
                Prop held = holder != null && holder.hands != null ? holder.hands.heldProp : null;
                if (held == null || item.GameProp == null || held.Pointer != item.GameProp.Pointer) return;
            }
            item.RaiseUse(down);
            ItemUseAnimation.Announce(sender, item, down);
            return;
        }
    }
}

}
