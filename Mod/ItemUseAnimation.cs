using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace BigPuzzleSDK {

// The animation a player plays while using a custom item. Must stay identical to the Unity SDK's enum.
// "Hold" animations play while the use button is held (the value is the game's BrandishStateId);
// the others play once when it is pressed (the value minus 100 is the game's BrandishMomentId); the cannonball's
// is one of those, but is played again for as long as the button is held, so it stays up.
public enum PuzzleItemUseAnimation {
    None = 0,
    Binoculars = 10, LaserPointer = 11, NightVisionGoggles = 12, Megaphone = 20, WalkieTalkie = 30,
    FlareGun = 110, SalonBrush = 120, Buoy = 130, Cannonball = 140, Football = 150, Torch = 160, Cowbell = 170, Radio = 180
}

// Plays those animations on the player's animator, on every PC. The host decides (it sees every press and release of
// an item, see ItemUse) and tells everyone else; the player's own PC plays it straight away.
internal static class ItemUseAnimation {
    private class Active {
        public int state;          // hold animation (BrandishStateId), or 0 for a repeated one-shot
        public int moment;         // the repeated one-shot (BrandishMomentId), or 0
        public PuzzleItem item;
        public int layer = -1;     // the animator layer the one-shot plays on, once seen
        public int hash;           // the state it is in there
        public int[] before;       // each layer's state before the trigger
        public float started;
    }
    private static readonly Dictionary<string, Active> active = new Dictionary<string, Active>();   // player id -> hold animation

    // Host: a player pressed / released the use button on an item
    internal static void Announce(PuzzlePlayer player, PuzzleItem item, bool down) {
        if (player == null || item == null || item.UseAnimation == PuzzleItemUseAnimation.None) return;
        Play(player, item, down);
        try {
            string text = "ua|" + Uri.EscapeDataString(player.id) + "|" + item.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (down ? "1" : "0");
            foreach (var pair in NetworkServer.connections) {
                if (pair.Value == null || !SdkHandshake.HasSameVersion(pair.Key)) continue;
                PlayerCommands.SendText(pair.Value, text);
            }
        } catch (Exception ex) {
            PlayerRoster.Warn("uaannounce", $"[PuzzleItem] could not tell the players about a use animation: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Everyone else: the host says a player is using an item ("ua|player|item|1")
    internal static void OnMessage(string text) {
        string[] part = text.Split('|');
        if (part.Length < 4 || !ulong.TryParse(part[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong id)) return;
        string playerId = Uri.UnescapeDataString(part[1]);
        bool down = part[3] == "1";
        PuzzlePlayer who = null;
        foreach (PuzzlePlayer player in PlayerRoster.View) if (player.id == playerId) { who = player; break; }
        if (who == null || who.isLocal) return;   // this PC's own player already played it
        foreach (PuzzleItem item in PuzzleItem.Items()) if (item.ItemId == id) { Play(who, item, down); return; }
    }

    // On the PC of the player who pressed: no waiting for the host
    internal static void PlayLocal(PuzzleItem item, bool down) {
        if (item == null || item.UseAnimation == PuzzleItemUseAnimation.None) return;
        foreach (PuzzlePlayer player in PlayerRoster.View) if (player.isLocal) { Play(player, item, down); return; }
    }

    private static void Play(PuzzlePlayer player, PuzzleItem item, bool down) {
        try {
            PlayerCharacter pc = player.Character;
            Animator animator = pc != null ? pc.myAnimator : null;
            if (animator == null) return;
            int value = (int)item.UseAnimation;
            if (value >= 100) {
                if (!down) { active.Remove(player.id); return; }
                if (item.UseAnimation == PuzzleItemUseAnimation.Cannonball) {
                    var again = new Active { moment = value - 100, item = item };
                    Fire(animator, value - 100, again);
                    active[player.id] = again;
                } else {
                    Fire(animator, value - 100, null);
                }
                return;
            }
            if (down) {
                active[player.id] = new Active { state = value, item = item };
                animator.SetInteger("BrandishStateId", value);
            } else {
                active.Remove(player.id);
                animator.SetInteger("BrandishStateId", 0);
            }
        } catch (Exception ex) {
            PlayerRoster.Warn("uaplay", $"[PuzzleItem] could not play a use animation: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Plays a one-shot; when it is to be repeated, notes what each layer was doing so its layer can be found
    private static void Fire(Animator animator, int moment, Active again) {
        animator.SetInteger("BrandishMomentId", moment);
        animator.SetTrigger("OnBrandishMoment");
        if (again == null) return;
        int layers = animator.layerCount;
        again.before = new int[layers];
        for (int i = 0; i < layers; i++) again.before[i] = animator.GetCurrentAnimatorStateInfo(i).shortNameHash;
        again.layer = -1;
        again.started = Time.time;
    }

    // Repeated one-shot: find the layer it plays on, then play it again whenever it has ended
    private static void Repeat(Animator animator, Active a) {
        if (a.layer < 0) {
            if (Time.time - a.started > 1f) { Fire(animator, a.moment, a); return; }   // never saw it start: try again
            for (int i = 0; i < a.before.Length && i < animator.layerCount; i++) {
                int now = animator.GetCurrentAnimatorStateInfo(i).shortNameHash;
                if (now != a.before[i]) { a.layer = i; a.hash = now; return; }
            }
            return;
        }
        if (animator.IsInTransition(a.layer)) return;
        if (animator.GetCurrentAnimatorStateInfo(a.layer).shortNameHash != a.hash) Fire(animator, a.moment, a);
    }

    // Keeps a hold animation on while the item is in the player's hand, and ends it when it is not
    internal static void Tick() {
        if (active.Count == 0) return;
        List<string> ended = null;
        foreach (var pair in active) {
            PuzzlePlayer player = null;
            foreach (PuzzlePlayer p in PlayerRoster.View) if (p.id == pair.Key) { player = p; break; }
            PlayerCharacter pc = player != null ? player.Character : null;
            Prop held = null;
            try { held = pc != null && pc.hands != null ? pc.hands.heldProp : null; } catch (Exception) { }
            Prop mine = pair.Value.item != null ? pair.Value.item.GameProp : null;
            bool still = held != null && mine != null && held.Pointer == mine.Pointer;
            try {
                if (pc != null && pc.myAnimator != null) {
                    if (pair.Value.moment != 0) { if (still) Repeat(pc.myAnimator, pair.Value); }
                    else pc.myAnimator.SetInteger("BrandishStateId", still ? pair.Value.state : 0);
                }
            } catch (Exception) { }
            if (!still) { if (ended == null) ended = new List<string>(); ended.Add(pair.Key); }
        }
        if (ended != null) foreach (string key in ended) active.Remove(key);
    }
}

}
