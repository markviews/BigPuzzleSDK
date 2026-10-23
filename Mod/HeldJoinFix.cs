using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using UnityEngine;
using static PlayerHeldInformation;

namespace BigPuzzleSDK {

    // Fixes joining while a player is holding an item the puzzle spawned.
    //
    // A joining PC is told what everyone holds before it has built the puzzles, so the held item
    // doesn't exist there yet. The game's own handler (PlayerNetworking.OnSetHeld) then throws, which
    // abandons reading that player and everything after it, and the join fails.
    //   Client: skips the handler for a held item that can't be found yet, so the join carries on.
    //   Host:   once the item has been re-sent to a joining player (see NetItems.Tick), marks the
    //           holders' synced data as changed so the held item is sent again, now that it exists.
    internal static class HeldJoinFix {
        private static bool reportedSkip, reportedNudge, reportedNoNudge;
        private static int nudges;
        private static MethodInfo setDirty;
        private static bool setDirtyLooked;

        internal static void InstallHook(Harmony harmony) {
            try {
                MethodInfo target = AccessTools.Method(typeof(PlayerNetworking), GameRefs.Hooks.OnSetHeld);
                if (target == null) throw new MissingMethodException("PlayerNetworking.OnSetHeld not found");
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(HeldJoinFix).GetMethod(nameof(HeldPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[HeldJoinFix] could not hook PlayerNetworking.OnSetHeld ({ex.GetType().Name}: {ex.Message}); joining while a player holds a puzzle item may fail");
            }
        }

        // Players whose held item we couldn't show yet. This PC applies it itself once the item exists
        // and has settled (see Tick); the game's handler throws if it runs the moment the item wakes.
        private class Pending { public PlayerNetworking player; public float since, readyAt, nextTry; public int tries; }
        private static readonly Dictionary<IntPtr, Pending> pending = new Dictionary<IntPtr, Pending>();
        private static bool applying;

        // false = skip the game's handler
        private static bool HeldPrefix(PlayerNetworking __instance, ref PlayerHeldInformation oldValue, PlayerHeldInformation newValue) {
            try {
                if (applying) return true;   // our own call from Tick
                IntPtr key = __instance.Pointer;

                // Put down (or nothing held): the game can handle that, as long as it isn't asked about the missing item
                if (newValue.heldType == HeldType.Nothing) {
                    pending.Remove(key);
                    if (Unresolved(oldValue)) oldValue = default(PlayerHeldInformation);
                    return true;
                }
                // Holding something this PC doesn't have yet: skip, and show it once it exists
                if (Unresolved(newValue)) {
                    if (!pending.ContainsKey(key)) pending[key] = new Pending { player = __instance, since = Time.unscaledTime };
                    if (!reportedSkip) {
                        reportedSkip = true;
                        Plugin.log.LogInfo("[HeldJoinFix] a player is holding an item this PC doesn't have yet; skipped the game's held-item handler (it is shown once the item has arrived).");
                    }
                    return false;
                }
                // The item is here, but it was skipped before: Tick shows it after it has settled
                if (pending.ContainsKey(key)) return false;
                if (Unresolved(oldValue)) oldValue = default(PlayerHeldInformation);
                return true;
            } catch (Exception) {
                return true;
            }
        }

        // Every frame (cheap when nothing is waiting): shows held items that were skipped at join
        internal static void Tick() {
            if (pending.Count == 0) return;
            float now = Time.unscaledTime;
            List<IntPtr> done = null;
            foreach (KeyValuePair<IntPtr, Pending> pair in pending) {
                Pending p = pair.Value;
                if (done == null) done = new List<IntPtr>();
                bool gone = false;
                try { gone = p.player == null; } catch (Exception) { gone = true; }
                if (gone) { done.Add(pair.Key); continue; }
                if (now - p.since > 90f) {
                    Plugin.log.LogWarning("[HeldJoinFix] gave up showing a held item after 90 s.");
                    done.Add(pair.Key); continue;
                }
                if (now < p.nextTry) continue;
                p.nextTry = now + 0.25f;
                PlayerHeldInformation current;
                try { current = p.player.NetworkplayerHeldInformation; } catch (Exception) { done.Add(pair.Key); continue; }
                if (current.heldType == HeldType.Nothing) { done.Add(pair.Key); continue; }   // put down meanwhile
                if (Unresolved(current)) { p.readyAt = 0f; continue; }                         // not here yet
                if (p.readyAt == 0f) p.readyAt = now + 1f;                                     // let it wake up and settle
                if (now < p.readyAt) continue;
                try {
                    applying = true;
                    p.player.OnSetHeld(default(PlayerHeldInformation), current);
                    Plugin.log.LogInfo("[HeldJoinFix] showed a held item that arrived after joining" + (p.tries > 0 ? $" (after {p.tries} failed tries)" : "") + ".");
                    done.Add(pair.Key);
                } catch (Exception ex) {
                    p.tries++;
                    p.nextTry = now + 1.5f;
                    Exception inner = ex.InnerException ?? ex;
                    if (p.tries == 1) {
                        Plugin.log.LogWarning($"[HeldJoinFix] showing the held item failed, will retry: {inner.GetType().Name}: {inner.Message}");
                        string st = inner.StackTrace;
                        if (!string.IsNullOrEmpty(st)) Plugin.log.LogWarning("[HeldJoinFix] stack: " + (st.Length > 900 ? st.Substring(0, 900) : st));
                    }
                    if (TryDirect(p.player, current)) { done.Add(pair.Key); continue; }
                    if (p.tries >= 8) {
                        Plugin.log.LogWarning("[HeldJoinFix] gave up showing a held item after 8 tries; it appears once the player puts it down and picks it up.");
                        done.Add(pair.Key);
                    }
                } finally {
                    applying = false;
                }
            }
            if (done != null) foreach (IntPtr key in done) pending.Remove(key);
        }

        // Last resort: attach the item to the holder ourselves
        private static bool TryDirect(PlayerNetworking player, PlayerHeldInformation held) {
            try {
                Prop prop = held.GetProp();
                PlayerCharacter who = player.playerCharacter;
                if (prop == null || who == null) { Plugin.log.LogWarning($"[HeldJoinFix] direct attach not possible (prop {(prop == null ? "null" : "ok")}, character {(who == null ? "null" : "ok")})."); return false; }
                prop.SetHeld(who);
                Plugin.log.LogInfo("[HeldJoinFix] attached the held item to the player directly.");
                return true;
            } catch (Exception ex) {
                Exception inner = ex.InnerException ?? ex;
                Plugin.log.LogWarning($"[HeldJoinFix] direct attach failed: {inner.GetType().Name}: {inner.Message}");
                return false;
            }
        }

        private static string NullMembers(object o) {
            if (o == null) return "(object is null)";
            var names = new List<string>();
            foreach (PropertyInfo pi in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
                if (pi.GetIndexParameters().Length != 0 || !pi.CanRead || pi.PropertyType.IsValueType || pi.PropertyType == typeof(string)) continue;
                try { if (pi.GetValue(o) == null) names.Add(pi.Name); } catch (Exception) { }
            }
            return names.Count == 0 ? "(none)" : string.Join(", ", names);
        }

        // Something is held, but this PC has no such object
        private static bool Unresolved(PlayerHeldInformation held) {
            try {
                if (held.heldType == HeldType.Nothing) return false;
                return held.identity == null || held.GetProp() == null;
            } catch (Exception) {
                return true;
            }
        }

        // Host: has every player who is holding a puzzle-spawned item send it to everyone again
        internal static void NudgeHolders() {
            if (!NetworkServer.active) return;
            try {
                foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PlayerNetworking>())) {
                    PlayerNetworking player = obj != null ? obj.TryCast<PlayerNetworking>() : null;
                    if (player == null) continue;
                    PlayerHeldInformation held = player.NetworkplayerHeldInformation;
                    if (held.heldType == HeldType.Nothing || held.identity == null) continue;
                    if ((held.identity.sceneId & 0xFF00000000000000UL) != 0xB100000000000000UL) continue;   // not one of ours
                    MarkChanged(player);
                    if (nudges++ < 5) Plugin.log.LogInfo("[HeldJoinFix] host: sent a player's held puzzle item again for a joining player.");
                }
            } catch (Exception ex) {
                if (!reportedNudge) {
                    reportedNudge = true;
                    Plugin.log.LogWarning($"[HeldJoinFix] could not check who is holding what: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static void MarkChanged(PlayerNetworking player) {
            if (!setDirtyLooked) {
                setDirtyLooked = true;
                const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                setDirty = typeof(NetworkBehaviour).GetMethod(GameRefs.Hooks.SetDirty, f, null, Type.EmptyTypes, null);
            }
            if (setDirty == null) {
                if (!reportedNoNudge) {
                    reportedNoNudge = true;
                    Plugin.log.LogWarning("[HeldJoinFix] NetworkBehaviour.SetDirty wasn't found, so a joining player only sees a held puzzle item after it is next picked up.");
                }
                return;
            }
            setDirty.Invoke(player, null);
        }
    }

} // namespace BigPuzzleSDK