using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Before a puzzle is destroyed (hot reload, rebuilding), stands up everyone sitting or standing in
// one of its poses (seats, telescopes...). Otherwise the game keeps pointing at a pose that is gone
// and fills the console with null errors.
internal static class PoseRelease {
    private static readonly string[] LeaveNames = {
        "Evict", "Leave", "LeavePose", "ExitPose", "Exit", "ForceLeave", "ForceExit", "Eject", "KickOut", "Release", "Stand", "StandUp", "Unpose",
    };
    private static bool reportedMissing, dumped;

    internal static void EmptyPosesIn(IEnumerable<GameObject> roots) {
        foreach (GameObject root in roots) {
            if (root == null) continue;
            try {
                foreach (Component c in root.GetComponentsInChildren(Il2CppType.Of<PlayerPose>(), true)) {
                    PlayerPose pose = c != null ? c.TryCast<PlayerPose>() : null;
                    if (pose == null) continue;
                    object occupant = null;
                    try { occupant = pose.occupant; } catch (Exception) { }
                    if (occupant == null) continue;
                    if (!dumped) {
                        dumped = true;
                    }
                    if (Release(pose, occupant)) continue;
                    if (!reportedMissing) {
                        reportedMissing = true;
                        Plugin.log.LogWarning("[PoseRelease] Could not find a way to stand a player up. Methods that might do it: "
                            + Candidates(pose) + " | on the occupant (" + occupant.GetType().Name + "): " + Candidates(occupant));
                    }
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[PoseRelease] {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static bool Release(PlayerPose pose, object occupant) {
        // Try the pose first (no parameters, or the occupant), then the occupant itself
        if (TryCall(pose, null)) return true;
        if (TryCall(pose, occupant)) return true;
        if (TryCall(occupant, null)) return true;
        return false;
    }

    private static bool TryCall(object target, object arg) {
        const BindingFlags f = BindingFlags.Public | BindingFlags.Instance;
        Type t = target.GetType();
        foreach (string name in LeaveNames) {
            foreach (MethodInfo m in t.GetMethods(f)) {
                if (m.Name != name && !(m.Name.StartsWith(name) && !m.Name.StartsWith("On"))) continue;
                ParameterInfo[] ps = m.GetParameters();
                try {
                    if (arg == null && ps.Length == 0) { m.Invoke(target, null); return true; }
                    if (arg != null && ps.Length == 1 && ps[0].ParameterType.IsInstanceOfType(arg)) { m.Invoke(target, new[] { arg }); return true; }
                } catch (Exception) { }
            }
        }
        return false;
    }

    private static string AllMethods(object target) {
        var names = new List<string>();
        foreach (MethodInfo m in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            if (!m.IsSpecialName && !m.Name.StartsWith("NativeMethodInfoPtr") ) names.Add(m.Name + "(" + m.GetParameters().Length + ")");
        return string.Join(", ", names);
    }

    private static string Candidates(object target) {
        var names = new List<string>();
        foreach (MethodInfo m in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
            string n = m.Name.ToLowerInvariant();
            if (n.Contains("leave") || n.Contains("exit") || n.Contains("eject") || n.Contains("stand") || n.Contains("release") || n.Contains("pose"))
                names.Add(m.Name + "(" + m.GetParameters().Length + ")");
        }
        return string.Join(", ", names);
    }
}

} // namespace BigPuzzleSDK
