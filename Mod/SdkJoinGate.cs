using System;
using System.Reflection;
using HarmonyLib;

namespace BigPuzzleSDK {

    // Lets a host that runs custom puzzles tell, at the join request, whether the joining player has the SDK.
    //  - A player with the SDK, joining a lobby that carries SDK data (see LobbyData), puts "sdk" on the end of the
    //    version string the game sends. (Only for such lobbies: a vanilla host would refuse the changed version.)
    //  - The host takes the "sdk" off again before the game compares versions, so the game's own check passes. A join
    //    without it, to a host that has custom puzzles, is refused with the game's own DenyEntry and the host is told.
    // Only methods that take plain objects and text are hooked (never ones that take structs by reference).
    internal static class SdkJoinGate {
        private const string Suffix = "sdk";

        private const byte RefusalCode = 2;   // the game's own "version mismatch" code: the closest it has
        private static MethodInfo denyEntry;
        private static PropertyInfo versionProp, nameProp;
        private static int clientLogs, hostLogs;

        // Set by LobbyData: the lobby this PC is joining carries SDK data
        internal static bool JoiningSdkLobby;

        private static Harmony harmonyInstance;
        private static bool clientHooked;
        private static int clientTries;

        internal static void Install(Harmony harmony) {
            harmonyInstance = harmony;
            const BindingFlags own = BindingFlags.Static | BindingFlags.NonPublic;
            bool host = false;
            InstallClient();
            try {
                Type auth = FindType("Mirror.Authenticators.HouseAuthenticator");
                MethodInfo receive = auth != null ? AccessTools.Method(auth, "OnInitialAuthRequestMessage") : null;
                denyEntry = auth != null ? AccessTools.Method(auth, "DenyEntry") : null;
                if (receive != null && versionProp == null) {
                    versionProp = receive.GetParameters()[1].ParameterType.GetProperty("versionNumber");
                    nameProp = receive.GetParameters()[1].ParameterType.GetProperty("displayName");
                }
                if (receive != null && versionProp != null && denyEntry != null) {
                    harmony.Patch(receive, prefix: new HarmonyMethod(typeof(SdkJoinGate).GetMethod(nameof(ReceivePrefix), own)));
                    host = true;
                }
                if (denyEntry != null)
                    harmony.Patch(denyEntry, postfix: new HarmonyMethod(typeof(SdkJoinGate).GetMethod(nameof(DenyPostfix), own)));
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[SdkGate] could not hook the game's join check ({ex.GetType().Name}: {ex.Message}); this PC can't check joining players.");
            }
            Plugin.log.LogInfo($"[SdkGate] hooks: marking my join request {(clientHooked ? "ready" : "NOT available yet")}, checking joining players {(host ? "ready" : "NOT available")}.");
        }


        // Looks in every loaded assembly (loading the game's own if needed), without relying on one scan of all types
        private static Type FindType(string name) {
            for (int pass = 0; pass < 2; pass++) {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
                    try { Type t = asm.GetType(name, false); if (t != null) return t; } catch (Exception) { }
                }
                if (pass == 0) { try { Assembly.Load("Assembly-CSharp"); } catch (Exception) { } }
            }
            return null;
        }

        // The client side: the game's writer for the join request (found by its parameters, its name is generated).
        // Tried again each time a lobby is read, until it works.
        internal static void InstallClient() {
            if (clientHooked || harmonyInstance == null) return;
            string why = "";
            try {
                Type generated = FindType("Mirror.GeneratedNetworkCode");
                MethodInfo writer = null;
                if (generated == null) why = "Mirror.GeneratedNetworkCode not found";
                else {
                    foreach (MethodInfo m in generated.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                        ParameterInfo[] ps = m.GetParameters();
                        if (m.ReturnType == typeof(void) && ps.Length == 2 && ps[0].ParameterType.Name == "NetworkWriter" && ps[1].ParameterType.Name == "InitialialAuthRequestMessage") { writer = m; break; }
                    }
                    if (writer == null) why = "no writer for the join request among " + generated.GetMethods(BindingFlags.Public | BindingFlags.Static).Length + " methods";
                }
                if (writer != null) {
                    versionProp = writer.GetParameters()[1].ParameterType.GetProperty("versionNumber");
                    nameProp = writer.GetParameters()[1].ParameterType.GetProperty("displayName");
                    harmonyInstance.Patch(writer, prefix: new HarmonyMethod(typeof(SdkJoinGate).GetMethod(nameof(WriterPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                    clientHooked = true;
                    if (clientTries > 0) Plugin.log.LogInfo("[SdkGate] join request hook ready (on attempt " + (clientTries + 1) + ").");
                }
            } catch (Exception ex) {
                why = ex.GetType().Name + ": " + (ex.InnerException ?? ex).Message;
            }
            if (!clientHooked && clientTries++ < 3) Plugin.log.LogWarning($"[SdkGate] could not hook the join request writer ({why}); this PC won't tell hosts it has the SDK.");
        }

        // Client: just before the join request is written
        private static void WriterPrefix(object __1) {
            try {
                if (__1 == null || versionProp == null || !JoiningSdkLobby) return;
                string version = versionProp.GetValue(__1) as string;
                if (version == null || version.EndsWith(Suffix, StringComparison.Ordinal)) return;
                versionProp.SetValue(__1, version + Suffix);
                if (clientLogs++ < 5) Plugin.log.LogInfo($"[SdkGate] join request sent with the version '{version}{Suffix}' (the lobby has SDK data).");
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[SdkGate] could not mark the join request: {(ex.InnerException ?? ex).Message}");
            }
        }

        // Host: the game is about to check a join request. Returns false to skip the game's handling (we refused it).
        private static bool ReceivePrefix(object __instance, object __0, object __1) {
            try {
                if (__1 == null || versionProp == null) return true;
                // The host's own player joins through connection 0: never check that one
                if (__0 != null) {
                    PropertyInfo connId = __0.GetType().GetProperty("connectionId");
                    if (connId != null && connId.GetValue(__0) is int id && id == 0) return true;
                }
                string version = versionProp.GetValue(__1) as string ?? "";
                string who = nameProp != null ? (nameProp.GetValue(__1) as string ?? "?") : "?";
                bool hasSdk = version.EndsWith(Suffix, StringComparison.Ordinal);
                if (hasSdk) versionProp.SetValue(__1, version.Substring(0, version.Length - Suffix.Length));   // the game compares the real version
                bool hostHasPuzzles = PuzzleLibrary.packs != null && PuzzleLibrary.packs.Count > 0;
                bool refuse = !hasSdk && hostHasPuzzles;
                if (hostLogs++ < 20)
                    Plugin.log.LogInfo($"[SdkGate] join request from '{who}': version '{version}', {(hasSdk ? "has the SDK" : "no SDK")}{(refuse ? " -> refused" : "")}.");
                if (!refuse) return true;

                denyEntry.Invoke(__instance, new object[] { __0, RefusalCode, "This world uses custom puzzles. Install the Big Puzzle SDK to join." });
                HostNotice.Show(who + " tried to join without the SDK", "They were turned away: they would not see the custom puzzles.");
                return false;
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[SdkGate] could not check a join request, letting the game handle it: {(ex.InnerException ?? ex).Message}");
                return true;
            }
        }

        // Host: every refusal the game makes, with its code (to pick NoSdkDenyCode)
        private static void DenyPostfix(object __0, byte __1, string __2) {
            try { Plugin.log.LogInfo($"[SdkGate] the game refused a join: code {__1}, message '{__2}'."); } catch (Exception) { }
        }
    }
}