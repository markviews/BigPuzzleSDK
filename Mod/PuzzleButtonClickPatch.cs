//using HarmonyLib;

//namespace BigWalkPuzzleSDK {

//    [HarmonyPatch(typeof(PlayerDecisions), nameof(PlayerDecisions.UseCastableTarget))]
//    static class PuzzleButtonClickPatch {
//        static void Prefix(PlayerDecisions __instance, CastableTarget castableTarget) {
//            if (castableTarget != null && PuzzleButton.byTarget.TryGetValue(castableTarget.GetInstanceID(), out PuzzleButton button))
//                button.Press(__instance.playerCharacter);
//            // no return value: the game carries on and plays the hand animation
//        }
//    }

//}