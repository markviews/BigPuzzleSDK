using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigPuzzleSDK {

    // Runtime (mod) version of the SDK's PuzzleHider: keeps this object (and everything under it)
    // only in the lobby sizes ticked in the Inspector. In the others it is switched off before the
    // puzzle wakes up, and no SDK component under it is set up (no game copies, nothing spawned).
    //
    // The lobby size is the game's own: PlayerCountSwapper.playerCount, the static value every
    // PlayerCountSwapper in the game reads to pick its 2/3/4-player version of a challenge.
    public class PuzzleHider : MonoBehaviour {
        public PuzzleHider(IntPtr ptr) : base(ptr) { }

        // Set in the Inspector (filled in by the loader); the names must match the Unity-side fields
        internal bool show2 = true;   // 2-player lobbies
        internal bool show3 = true;   // 3-player lobbies
        internal bool show4 = true;   // 4-player lobbies (and larger)

        // Hidden objects of the puzzles loaded so far
        private static readonly List<Transform> hidden = new List<Transform>();

        // Called by the loader while the puzzle is still inactive, before any other setup
        internal void SetupHider() {
            int players = LobbySize();
            bool show = players == 2 ? show2 : players == 3 ? show3 : show4;
            if (show) return;
            hidden.RemoveAll(t => t == null);
            hidden.Add(transform);
            gameObject.SetActive(false);
            Plugin.log.LogInfo($"[PuzzleHider] '{name}' is hidden in {players}-player lobbies.");
        }

        // True if this object is, or is inside, an object a PuzzleHider switched off
        internal static bool IsHidden(Transform t) {
            if (t == null || hidden.Count == 0) return false;
            foreach (Transform h in hidden)
                if (h != null && (t == h || t.IsChildOf(h))) return true;
            return false;
        }

        // 2, 3 or 4: the lobby size the game is using for its own challenges. Unknown counts as 4.
        internal static int LobbySize() {
            int fromGame = 0;
            try { fromGame = (int)PlayerCountSwapper.playerCount; }   // 0 = NotSet, else 2 / 3 / 4
            catch (Exception ex) {
                if (!warned) Plugin.log.LogWarning($"[PuzzleHider] couldn't read the game's player count: {ex.GetType().Name}: {ex.Message}");
            }
            int players = fromGame >= 2 && fromGame <= 4 ? fromGame : 0;
            if (players == 0) {
                int connected = -1;
                try { connected = PlayerNetworking.playerCount; } catch (Exception) { }
                if (!warned) {
                    warned = true;
                    Plugin.log.LogWarning($"[PuzzleHider] the game's player count isn't set yet (PlayerCountSwapper.playerCount = {fromGame}, PlayerNetworking.playerCount = {connected}); treating the lobby as 4 players.");
                }
                return 4;
            }
            return players;
        }
        private static bool warned;
    }

} // namespace BigPuzzleSDK
