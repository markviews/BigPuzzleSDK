using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Before a puzzle is destroyed (hot reload, rebuilding), stands up everyone sitting or standing in
// one of its poses (seats, telescopes...). Otherwise the game keeps pointing at a pose that is gone
// and fills the console with null errors.
internal static class PoseRelease {
    internal static void EmptyPosesIn(IEnumerable<GameObject> roots) {
        foreach (GameObject root in roots) {
            if (root == null) continue;
            try {
                foreach (Component c in root.GetComponentsInChildren(Il2CppType.Of<PlayerPose>(), true)) {
                    PlayerPose pose = c != null ? c.TryCast<PlayerPose>() : null;
                    if (pose == null) continue;
                    bool occupied = false;
                    try { occupied = pose.occupant != null; } catch (Exception) { }
                    if (occupied) pose.Evict();
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[PoseRelease] {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}

} // namespace BigPuzzleSDK
