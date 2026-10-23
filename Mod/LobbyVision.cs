using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BigPuzzleSDK {

// Whether the local player is looking through a "lobby vision" telescope right now (the Invisible
// Ink challenge's telescope: the colour filter that shows camo pictures). Local to each PC.
// Two sources, either one counts:
//  - a Harmony hook on LobbyVisionToggler.SetLobbyVisionActive(bool), the game's announcement;
//  - the lobby-vision post-processing volume's weight (the filter itself). This is the backup,
//    in case the game calls the announcement in a way the hook can't see.
internal static class LobbyVision {
    private static bool hooked;            // last value seen by the hook
    private static int checkedFrame = -1;
    private static bool on;
    private static bool volumeFailed;

    internal static bool On {
        get {
            int frame = Time.frameCount;
            if (frame != checkedFrame) {
                checkedFrame = frame;
                on = hooked || FilterShowing();
            }
            return on;
        }
    }

    private static bool FilterShowing() {
        if (volumeFailed) return false;
        try {
            PostProcessingManager manager = PostProcessingManager.instance;
            if (manager == null) return false;
            Volume volume = manager.lobbyVisionPPVolume;
            return volume != null && volume.enabled && volume.gameObject.activeInHierarchy && volume.weight > 0.5f;
        } catch (Exception ex) {
            volumeFailed = true;
            Plugin.log.LogWarning($"[LobbyVision] could not read the telescope filter ({ex.GetType().Name}: {ex.Message})");
            return false;
        }
    }

    internal static void InstallHook(Harmony harmony) {
        try {
            MethodInfo target = AccessTools.Method(typeof(LobbyVisionToggler), GameRefs.Hooks.SetLobbyVisionActive);
            if (target == null) throw new MissingMethodException("LobbyVisionToggler.SetLobbyVisionActive not found");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(LobbyVision).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[LobbyVision] could not hook the telescope filter announcement ({ex.GetType().Name}: {ex.Message}); using the filter itself");
        }
    }

    private static void Postfix(bool __0) { hooked = __0; }
}

} // namespace BigPuzzleSDK
