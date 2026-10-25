using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Mirror;

namespace BigPuzzleSDK {

    // Puts the SDK's puzzle list into the game's EOS lobby, so a player can read it BEFORE connecting.
    //
    //   Host:   when the game fills in the lobby's attributes, the SDK adds its own:
    //             bpsdk       "BWSDK1|<SDK version>|<hash of the whole list>|<puzzle count>"
    //             bpsdk_0..   the list itself, 9 characters per puzzle (6 = its id, 3 = its version),
    //                         100 puzzles per attribute (an EOS text attribute holds about 1000 characters)
    //   Player: when the game finds the lobby, the SDK reads those attributes and logs what it found
    //           and whether this PC has the host's puzzles.
    //
    // This step only publishes and reports; joining is not blocked and nothing is built from it yet.
    internal static class LobbyData {
        private const string Protocol = "BWSDK1";
        private const string KeyMain = "bpsdk";
        private const string KeyList = "bpsdk_";      // + 0, 1, 2...
        private const string KeyNames = "bpsdk_n";     // + 0, 1, 2...: id hash + name of each puzzle, so a player can be told what's missing
        private const int NameChars = 24, NameChunkChars = 900;
        private const int IdChars = 6, VersionChars = 3, EntryChars = IdChars + VersionChars;
        private const int EntriesPerChunk = 100, MaxChunks = 8;

        private static MethodInfo updateAttribute, tryGetString;
        private static bool reportedPublishFail, reportedReadFail;
        private static int publishLogs, readLogs;
        private static bool adding;   // true while we add our own attributes (so the hook on the game's method ignores them)
        private static readonly HashSet<long> alreadyAdded = new HashSet<long>();      // lobby changes that already carry our attributes

        internal static void Install(Harmony harmony) {
            Type manager = null, extensions = null;
            try {
                // Both are public types in the game's Mirror.Transports assembly; typeof makes the runtime load it
                manager = typeof(EOSPrivateLobbyManager);
                extensions = typeof(LobbyDetailsExtensions);
                updateAttribute = AccessTools.Method(manager, GameRefs.Lobby.UpdateAttribute);
                if (extensions != null) tryGetString = AccessTools.Method(extensions, GameRefs.Lobby.TryGetStringAttribute);
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[LobbyData] could not find the game's lobby code ({ex.GetType().Name}: {ex.Message}); the lobby won't carry the puzzle list.");
                return;
            }

            // Only methods of the game's own manager are hooked, and only ones that take plain objects and text.
            // (Epic's own calls take structs by reference; hooking those corrupts them and breaks the lobby.)
            const BindingFlags own = BindingFlags.Static | BindingFlags.NonPublic;
            bool host = false, read = false;
            try {
                // The game adds its attributes one at a time through this; ours are added right after the first
                if (updateAttribute != null) {
                    harmony.Patch(updateAttribute, postfix: new HarmonyMethod(typeof(LobbyData).GetMethod(nameof(UpdateAttributePostfix), own)));
                    host = true;
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[LobbyData] could not hook {GameRefs.Lobby.ManagerType}.{GameRefs.Lobby.UpdateAttribute} ({ex.GetType().Name}: {ex.Message}); the lobby won't carry the puzzle list.");
            }
            try {
                MethodInfo convert = AccessTools.Method(manager, GameRefs.Lobby.ConvertToLobbyInfo);
                if (convert != null && tryGetString != null) {
                    harmony.Patch(convert, postfix: new HarmonyMethod(typeof(LobbyData).GetMethod(nameof(ConvertPostfix), own)));
                    read = true;
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[LobbyData] could not hook {GameRefs.Lobby.ManagerType}.{GameRefs.Lobby.ConvertToLobbyInfo} ({ex.GetType().Name}: {ex.Message}); this PC can't read a lobby's puzzle list.");
            }
            Plugin.log.LogInfo($"[LobbyData] hooks: writing the list {(host ? "ready" : "NOT available")}, reading it {(read ? "ready" : "NOT available")}.");
        }


        // ---- What this PC has ----
        private class Entry {
            public string key;    // 9 characters: puzzle id hash + version hash
            public string full;   // id:SHA-256 of the whole file, for the overall hash
            public string name;
        }

        private static string Hex(byte[] bytes) { return Convert.ToHexString(bytes).ToLowerInvariant(); }

        private static void Add(List<Entry> list, HashSet<string> seen, PuzzlePack pack) {
            if (pack == null || string.IsNullOrEmpty(pack.id) || string.IsNullOrEmpty(pack.hash) || pack.hash.Length < VersionChars) return;
            string full = pack.id + ":" + pack.hash;
            if (!seen.Add(full)) return;
            string idHash = Hex(SHA256.HashData(Encoding.UTF8.GetBytes(pack.id))).Substring(0, IdChars);
            list.Add(new Entry { key = idHash + pack.hash.Substring(0, VersionChars).ToLowerInvariant(), full = full, name = string.IsNullOrEmpty(pack.name) ? pack.id : pack.name });
        }

        // host = what the host offers; otherwise what a player has (which also counts the built-in tower, as the join check does)
        private static List<Entry> Local(bool host) {
            var list = new List<Entry>();
            var seen = new HashSet<string>();
            foreach (PuzzlePack pack in PuzzleLibrary.packs) Add(list, seen, pack);
            if (!host) Add(list, seen, PuzzleLibrary.builtInTower);
            list.Sort((a, b) => string.CompareOrdinal(a.full, b.full));
            return list;
        }

        private static string OverallHash(List<Entry> entries) {
            var text = new StringBuilder();
            foreach (Entry e in entries) text.Append(e.full).Append('\n');
            return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).Substring(0, 8);
        }

        // ---- Host: add our attributes to every lobby change the game builds ----
        // The game's UpdateAttribute(LobbyModification update, string attributeKey, string attributeValue) runs once per
        // attribute it sets. After the first one for a lobby change, ours are added to the same change (once).
        private static void UpdateAttributePostfix(object __instance, object update) {
            if (adding) return;
            AddTo(__instance, update, "the game adding an attribute");
        }

        private static void AddTo(object manager, object modification, string via) {
            try {
                if (manager == null || modification == null || updateAttribute == null) return;
                // The same change can reach us twice (through both ways in): add once
                long id = PointerOf(modification);
                if (id != 0 && !alreadyAdded.Add(id)) return;
                if (alreadyAdded.Count > 64) alreadyAdded.Clear();
                adding = true;

                List<Entry> entries = Local(true);
                int count = entries.Count;
                if (count > EntriesPerChunk * MaxChunks) {
                    Plugin.log.LogWarning($"[LobbyData] {count} puzzles are installed but the lobby list only holds {EntriesPerChunk * MaxChunks}; the rest aren't listed.");
                    entries = entries.GetRange(0, EntriesPerChunk * MaxChunks);
                    count = entries.Count;
                }
                string overall = OverallHash(entries);
                Set(manager, modification, KeyMain, Protocol + "|" + Plugin.Version + "|" + overall + "|" + count);

                int chunks = 0;
                for (int start = 0; start < count; start += EntriesPerChunk, chunks++) {
                    var chunk = new StringBuilder();
                    for (int i = start; i < Math.Min(count, start + EntriesPerChunk); i++) chunk.Append(entries[i].key);
                    Set(manager, modification, KeyList + chunks, chunk.ToString());
                }
                // Names, in chunks that stay under the value limit (an entry is never split)
                var names = new StringBuilder();
                int nameChunks = 0;
                foreach (Entry e in entries) {
                    string clean = e.name.Replace('|', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim();
                    if (clean.Length > NameChars) clean = clean.Substring(0, NameChars).TrimEnd();
                    string piece = e.key.Substring(0, IdChars) + clean + "|";
                    if (names.Length + piece.Length > NameChunkChars && names.Length > 0) {
                        if (nameChunks >= MaxChunks) { names.Clear(); break; }
                        Set(manager, modification, KeyNames + nameChunks++, names.ToString());
                        names.Clear();
                    }
                    names.Append(piece);
                }
                if (names.Length > 0 && nameChunks < MaxChunks) Set(manager, modification, KeyNames + nameChunks++, names.ToString());
                if (publishLogs++ < 10)
                    Plugin.log.LogInfo($"[LobbyData] added the SDK's attributes to the lobby (via {via}): {count} puzzle(s) in {chunks} list attribute(s), list hash {overall}.");
            } catch (Exception ex) {
                if (reportedPublishFail) return;
                reportedPublishFail = true;
                Exception inner = ex.InnerException ?? ex;
                Plugin.log.LogWarning($"[LobbyData] could not add the SDK's attributes to the lobby: {inner.GetType().Name}: {inner.Message}");
            } finally {
                adding = false;
            }
        }

        // The address of an interop object (0 if it can't be read), to tell one lobby change from another
        private static long PointerOf(object wrapper) {
            return wrapper is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj ? obj.Pointer.ToInt64() : 0;
        }

        private static void Set(object manager, object modification, string key, string value) {
            updateAttribute.Invoke(manager, new object[] { modification, key, value });
        }

        // ---- Anyone: read the attributes of a lobby the game just found or created ----
        private static void ConvertPostfix(object[] __args) {
            try {
                if (__args == null || __args.Length < 1 || __args[0] == null || tryGetString == null) return;
                object details = __args[0];
                bool host = NetworkServer.active;
                string who = host ? "host, read back from the lobby" : "read before joining";

                string main = Get(details, KeyMain);
                if (!host) { SdkJoinGate.JoiningSdkLobby = main != null; if (main != null) SdkJoinGate.InstallClient(); }   // the join request carries "sdk" only for SDK lobbies
                if (main == null) {
                    JoinPopup.Clear();   // not an SDK lobby: nothing to compare
                    if (readLogs++ < 20) Plugin.log.LogInfo($"[LobbyData] ({who}) this lobby has no SDK data: the host doesn't have the SDK, or it didn't publish its list.");
                    return;
                }
                string[] parts = main.Split('|');
                if (parts.Length < 4 || !int.TryParse(parts[3], out int count)) {
                    JoinPopup.Clear();
                    Plugin.log.LogWarning($"[LobbyData] ({who}) the lobby's SDK data isn't in a format this PC understands: '{main}'");
                    return;
                }
                bool log = readLogs++ < 20;   // the popup below is worked out every time; only the log lines are limited
                if (log) Plugin.log.LogInfo($"[LobbyData] ({who}) SDK data found in the lobby: {parts[0]}, SDK {parts[1]}, {count} puzzle(s), list hash {parts[2]}.");
                bool sdkDiffers = parts[0] != Protocol || parts[1] != Plugin.Version;
                if (sdkDiffers && log)
                    Plugin.log.LogWarning($"[LobbyData] the host's SDK ({parts[0]} {parts[1]}) is not this PC's ({Protocol} {Plugin.Version}).");

                var list = new StringBuilder();
                for (int chunk = 0; chunk * EntriesPerChunk < count && chunk < MaxChunks; chunk++) {
                    string piece = Get(details, KeyList + chunk);
                    if (piece == null) break;
                    list.Append(piece);
                }
                string hostList = list.ToString();
                int listed = hostList.Length / EntryChars;
                if (listed != count) Plugin.log.LogWarning($"[LobbyData] the lobby says {count} puzzle(s) but its list holds {listed} ({hostList.Length} characters).");

                // Does this PC have every puzzle the host lists? (Extra puzzles here don't matter.)
                List<Entry> mine = Local(host);
                var haveKey = new HashSet<string>();
                var haveId = new Dictionary<string, Entry>();
                foreach (Entry e in mine) { haveKey.Add(e.key); haveId[e.key.Substring(0, IdChars)] = e; }
                int missing = 0, outdated = 0;
                var outdatedNames = new List<string>();
                var missingIds = new List<string>();
                for (int i = 0; i < listed; i++) {
                    string token = hostList.Substring(i * EntryChars, EntryChars);
                    if (haveKey.Contains(token)) continue;
                    if (haveId.TryGetValue(token.Substring(0, IdChars), out Entry old)) { outdated++; outdatedNames.Add(old.name); } else { missing++; missingIds.Add(token.Substring(0, IdChars)); }
                }
                if (log) {
                    if (missing == 0 && outdated == 0)
                        Plugin.log.LogInfo($"[LobbyData] this PC has every puzzle the host lists ({listed}); it has {mine.Count} installed.");
                    else
                        Plugin.log.LogInfo($"[LobbyData] this PC is missing {missing} of the host's puzzle(s) and has {outdated} out of date" + (outdated > 0 ? " (" + string.Join(", ", outdatedNames) + ")" : "") + ".");
                }

                // A player who doesn't match is told on the password screen (see JoinPopup); the host never is
                var problems = new List<string>();
                if (sdkDiffers) problems.Add($"The host has SDK {parts[1]}, you have {Plugin.Version}.");
                if (missing > 0) {
                    List<string> missingNames = missing > 0 && !host ? MissingNames(details, missingIds) : null;
                    if (missingNames != null && missingNames.Count > 0) {
                        const int show = 8;
                        string shown = string.Join("\n", missingNames.GetRange(0, Math.Min(show, missingNames.Count)));
                        if (missingNames.Count > show) shown += $"\nand {missingNames.Count - show} more";
                        problems.Add("Missing puzzle(s):\n" + shown);
                    } else problems.Add($"Missing {missing} puzzle(s) the host has.");
                }
                if (outdated > 0) problems.Add("Out of date:\n" + string.Join("\n", outdatedNames));
                if (host || problems.Count == 0) JoinPopup.Clear();
                else JoinPopup.Set(problems.ToArray());

            } catch (Exception ex) {
                if (reportedReadFail) return;
                reportedReadFail = true;
                Exception inner = ex.InnerException ?? ex;
                Plugin.log.LogWarning($"[LobbyData] could not read the lobby's SDK data: {inner.GetType().Name}: {inner.Message}");
            }
        }

        // The names the host published for the puzzles this PC is missing (null if it published none)
        private static List<string> MissingNames(object details, List<string> ids) {
            try {
                var byId = new Dictionary<string, string>();
                for (int chunk = 0; chunk < MaxChunks; chunk++) {
                    string piece = Get(details, KeyNames + chunk);
                    if (piece == null) break;
                    foreach (string item in piece.Split('|')) if (item.Length > IdChars) byId[item.Substring(0, IdChars)] = item.Substring(IdChars);
                }
                if (byId.Count == 0) return null;
                var result = new List<string>();
                foreach (string id in ids) result.Add(byId.TryGetValue(id, out string name) ? name : "an unnamed puzzle");
                return result;
            } catch (Exception) { return null; }
        }

        private static string Get(object details, string key) {
            return tryGetString.Invoke(null, new object[] { details, key }) as string;
        }
    }

} // namespace BigPuzzleSDK