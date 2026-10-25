using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mirror;
using UnityEngine;
using static PropertyBlockHelper;

// Project references needed in addition to the existing ones:
//   BepInEx/interop/UnityEngine.AssetBundleModule.dll

namespace BigPuzzleSDK {
    [BepInPlugin("markviews.BigPuzzleSDK", "Big Puzzle SDK", Version)]
    public class Plugin : BasePlugin {
        internal const string Version = "0.1.2";

        internal static ManualLogSource log;

        // Puzzle creators put the puzzles they are testing in BepInEx/plugins/BigPuzzleSDK. Packs there are
        // "dev puzzles": the creator keys (Ctrl+F1 teleport, Ctrl+F2 reload) work on them and only on them.
        // A puzzle installed by a mod manager is never in this folder, so players never get the keys.
        internal static string DevFolder { get { return Path.Combine(Paths.PluginPath, "BigPuzzleSDK"); } }
        private static readonly HashSet<Type> registered = new HashSet<Type>();

        public override void Load() {
            log = base.Log;
            try { Directory.CreateDirectory(DevFolder); }   // so a creator can just pick it as their export folder
            catch (Exception ex) { log.LogWarning($"[Puzzle] Could not create {DevFolder}: {ex.Message}"); }
            var harmony = new Harmony("markviews.BigPuzzleSDK");
            harmony.PatchAll();
            SyncedValue.InstallHook(harmony);
            LobbyVision.InstallHook(harmony);
            PuzzleAudio.InstallHooks(harmony);
            ModGourd.InstallHooks(harmony);
            PuzzleSave.InstallHooks(harmony);
            SoundPegAudio.InstallHook(harmony);
            HeldJoinFix.InstallHook(harmony);
            LobbyData.Install(harmony);
            SdkJoinGate.Install(harmony);

            // Register every MonoBehaviour in this assembly (BigPuzzleEditor + all puzzle scripts)
            // so the loader can AddComponent them by name.
            foreach (Type t in Assembly.GetExecutingAssembly().GetTypes()) {
                if (!t.IsAbstract && typeof(MonoBehaviour).IsAssignableFrom(t)) RegisterType(t);
            }

            // Read every installed puzzle (and load its DLLs) now, not when a world loads
            PuzzleLibrary.Scan();

            var mono = new GameObject("BigPuzzleSDK");
            UnityEngine.Object.DontDestroyOnLoad(mono);
            mono.AddComponent<BigPuzzleEditor>();
        }

        // Registers base classes from this assembly first, then the type itself.
        private static void RegisterType(Type t) {
            if (!registered.Add(t)) return;
            if (t.BaseType != null && t.BaseType.Assembly == t.Assembly) RegisterType(t.BaseType);
            ClassInjector.RegisterTypeInIl2Cpp(t);
        }

    }

    public class BigPuzzleEditor : MonoBehaviour {
        private const float LoadDelaySeconds = 2f;   // let the world finish loading before copying from it

        private readonly List<GameObject> activePuzzles = new List<GameObject>();   // every loaded puzzle
        private int teleportIndex;   // F1 cycles through the puzzles

        public BigPuzzleEditor(IntPtr ptr) : base(ptr) { }

        internal static BigPuzzleEditor instance;
        private bool reloadRequested;   // the host reloaded; do the same on the next frame

        private void Start() {
            instance = this;
            // Other players: the host reloaded. Start over (its new list follows), even if this PC
            // hasn't built anything yet.
            ReloadChannel.onReload = () => { reloadRequested = true; };
            WorldManager.add_OnWorldManagerStart(new Action(() => {
                InvokeRepeating(nameof(WaitForPlayer), 0f, 0.5f);
            }));
        }

        // Once the local player exists the world is loaded: load the puzzles a moment later
        private void WaitForPlayer() {
            if (NetworkClient.localPlayer == null) return;
            CancelInvoke(nameof(WaitForPlayer));
            Invoke(nameof(LoadPuzzle), LoadDelaySeconds);
        }

        // Every .puzzlepack anywhere under BepInEx/plugins (each Thunderstore puzzle installs into
        // its own folder there). The load order comes from the puzzles' ids (see PuzzleLibrary),
        // not from these file names.
        internal static List<string> FindPuzzlePacks() {
            var packs = new List<string>();
            try {
                foreach (string path in Directory.GetFiles(Paths.PluginPath, "*.puzzlepack", SearchOption.AllDirectories))
                    packs.Add(path);
            } catch (Exception ex) {
                Plugin.log.LogError($"[Puzzle] Could not search for puzzle packs: {ex.GetType().Name}: {ex.Message}");
            }
            packs.Sort((a, b) => {
                int byName = string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
            return packs;
        }

        // Builds (or rebuilds) every installed puzzle, each at the position set on its PuzzleSDK
        private void LoadPuzzle() {
            CancelInvoke(nameof(ApplySavedGourds));
            CancelInvoke(nameof(CleanRegistries));
            PuzzleSave.BeforeBuild();
            NetItems.UnspawnAll();   // host: remove the old puzzles' items for every player
            PuzzleVanillaCopy.DestroyAllProps();   // now, not at the end of the frame (see there)
            PoseRelease.EmptyPosesIn(activePuzzles);   // stand players up before their seats are destroyed
            foreach (GameObject old in activePuzzles)
                if (old != null) GameObject.DestroyImmediate(old);
            activePuzzles.Clear();
            builtFor = null;
            HostPuzzleQueue.puzzles = null;

            List<PuzzlePack> packs = PuzzleLibrary.packs;
            foreach (PuzzlePack pack in packs) pack.firstTicket = -1;
            PuzzleLoader.BeginLoadAll();   // also when there are none: the SDK's own sync still runs

            // Another player's PC: build nothing yet. The puzzles are built when the host's list
            // arrives (BuildHostPuzzles); a host without the SDK never sends one, so nothing is built.
            if (NetworkClient.active && !NetworkServer.active) {
                SdkHandshake.AnswerAgain();
                Plugin.log.LogInfo("[Puzzle] waiting for the host's list of puzzles");
                return;
            }

            if (packs.Count == 0) {
                Plugin.log.LogInfo($"[Puzzle] No .puzzlepack files found under {Paths.PluginPath}");
                return;
            }
            var names = new List<string>();
            foreach (PuzzlePack pack in packs) {
                GameObject root = PuzzleLoader.LoadPuzzlePackage(pack);
                if (root == null) continue;
                activePuzzles.Add(root);
                names.Add(root.name);
            }
            Plugin.log.LogInfo($"[Puzzle] {activePuzzles.Count} of {packs.Count} puzzle(s) loaded: {string.Join(", ", names)}; {Tickets.Summary()}");

            // Host: the saved progress. Won puzzles start won now (before their first frame); their
            // gourds are put back a moment later, once they can be woken and the tower is ready.
            PuzzleSave.ApplyBuilt();
            Invoke(nameof(ApplySavedGourds), 1.5f);
            Invoke(nameof(CleanRegistries), 0.5f);
        }

        // (no parameters: called by name with Invoke) After a rebuild, once the old puzzles'
        // objects are really gone: drop them from the game's registries, so nothing finds a
        // destroyed object by its network id (see RegistryCleanup)
        private void CleanRegistries() { RegistryCleanup.Run(); }

        // (no parameters: called by name with Invoke) A player who was holding a switch before this PC joined
        // sent the switch's id along with the rest of their state, when our copy of the switch didn't
        // exist yet, so their hand never went onto it. Now that the copies exist, the game is told again.
        private void RefreshHeldSwitches() { HeldSwitchRefresh.Run(); }

        // (no parameters: called by name with Invoke)
        private void ApplySavedGourds() { PuzzleSave.ApplyGourds(); }

        private void Update() {
            // Host: make sure players who joined late get the puzzle's grabbable items
            NetItems.Tick();
            // Joining players: show held items that had not arrived yet
            HeldJoinFix.Tick();
            // Looping sounds: keep them in step between players
            PuzzleAudio.Tick();
            // Synced values: hands new values to their owners once per frame
            SyncedValue.Tick();
            // Synced variables made in code
            SyncedVars.Tick();
            // Who has the SDK and the same puzzles
            SdkHandshake.Tick();
            // Other players: build the host's puzzles once its list has arrived
            if (HostPuzzleQueue.puzzles != null) BuildHostPuzzles();
            HostNotice.Tick();
            // Over the password screen: what doesn't match the host's lobby
            JoinPopup.Tick();

            // Creator keys (see Plugin.DevFolder): only when a dev puzzle is installed, and only with Ctrl held,
            // so they can't be hit by accident while playing
            bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
            bool dev = ctrl && PuzzleLibrary.HasDevPuzzles;

            // Ctrl+F2: reload the puzzles, for every player. Only the host can do this.
            if (dev && UnityEngine.Input.GetKeyDown(KeyCode.F2)) {
                if (SyncedValue.IsHost || !NetworkClient.active) {
                    PuzzleSave.SnapshotForReload();   // the rebuilt puzzles keep their progress
                    PuzzleLibrary.Scan();   // pick up changed, added or removed packs
                    LoadPuzzle();
                    ReloadChannel.Announce();
                } else {
                    Plugin.log.LogInfo("[Puzzle] Ctrl+F2: only the host can reload the puzzles.");
                }
            }
            // Other players: the host reloaded
            if (reloadRequested) {
                reloadRequested = false;
                Plugin.log.LogInfo("[Puzzle] the host reloaded the puzzles; reloading here too");
                PuzzleLibrary.Scan();
                LoadPuzzle();
            }


            // Ctrl+F1: teleport the local player to a puzzle's debugTeleport
            if (dev && UnityEngine.Input.GetKeyDown(KeyCode.F1)) TeleportToPuzzle();

        }

        // Other players: the host's list of puzzles arrived. Build the ones this PC has in the same
        // version, each starting at the network id the host's copy starts at; skip the rest.
        private string builtFor;   // the host list the current puzzles were built from
        // A new connection: the puzzles of an earlier session don't count as built for the new host's list
        internal void ForgetBuilt() { builtFor = "(new connection)"; }   // not null: the next list starts from a clean slate

        // Builds the host list waiting in HostPuzzleQueue (no parameters: this class is registered
        // with the game, which can't take list parameters)
        private void BuildHostPuzzles() {
            string listKey = HostPuzzleQueue.key;
            List<KeyValuePair<string, int>> hostPuzzles = HostPuzzleQueue.puzzles;
            HostPuzzleQueue.puzzles = null;
            if (hostPuzzles == null) return;
            if (builtFor == listKey) return;   // already built from this list
            if (builtFor != null) {
                // Built from an older list: start over
                PuzzleVanillaCopy.DestroyAllProps();
                PoseRelease.EmptyPosesIn(activePuzzles);
                foreach (GameObject old in activePuzzles)
                    if (old != null) GameObject.DestroyImmediate(old);
                activePuzzles.Clear();
                PuzzleLoader.BeginLoadAll();
            }
            builtFor = listKey;

            var names = new List<string>();
            int skipped = 0;
            foreach (var wanted in hostPuzzles) {   // key: "<id>:<fingerprint>", value: first network id
                PuzzlePack pack = PuzzleLibrary.FindHostPack(wanted.Key);
                if (pack == null || wanted.Value < 0) { skipped++; continue; }
                GameObject root = PuzzleLoader.LoadPuzzlePackage(pack, wanted.Value);
                if (root == null) continue;
                activePuzzles.Add(root);
                names.Add(root.name);
            }
            CancelInvoke(nameof(CleanRegistries));
            Invoke(nameof(CleanRegistries), 0.5f);
            // Players already holding one of our buttons when this PC joined: their hands
            CancelInvoke(nameof(RefreshHeldSwitches));
            Invoke(nameof(RefreshHeldSwitches), 1f);
            Invoke(nameof(RefreshHeldSwitches), 3f);
            Plugin.log.LogInfo($"[Puzzle] built {activePuzzles.Count} of the host's {hostPuzzles.Count} puzzle(s): {string.Join(", ", names)}"
                + (skipped > 0 ? $"; {skipped} missing or a different version here" : "") + $"; {Tickets.Summary()}");
        }

        // Each press goes to the next loaded puzzle that has a debugTeleport
        private void TeleportToPuzzle() {
            PuzzleSDK main = null;
            activePuzzles.RemoveAll(p => p == null);
            for (int tries = 0; tries < activePuzzles.Count && main == null; tries++) {
                GameObject puzzle = activePuzzles[teleportIndex % activePuzzles.Count];
                teleportIndex = (teleportIndex + 1) % activePuzzles.Count;
                PuzzleSDK sdk = puzzle.GetComponentInChildren<PuzzleSDK>();
                if (sdk != null && sdk.debugTeleport != null && PuzzleLibrary.IsDevPuzzle(sdk.packId)) main = sdk;
            }
            if (main == null) {
                Plugin.log.LogWarning("[Puzzle] Ctrl+F1: no puzzle in the BigPuzzleSDK folder has a debugTeleport set on its PuzzleSDK.");
                return;
            }
            Plugin.log.LogInfo($"[Puzzle] Ctrl+F1: teleporting to '{main.transform.root.name}'");
            NetworkIdentity player = NetworkClient.localPlayer;
            if (player == null) return;

            Vector3 target = main.debugTeleport.position;
            PlayerCharacter character = player.GetComponent<PlayerCharacter>();
            Rigidbody body = character != null ? character.rb : player.GetComponent<Rigidbody>();
            player.transform.position = target;
            if (body != null) {
                body.position = target;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
    }

    // ----------------------------------------------------------------------
    // The mod's stand-in for a UnityEvent: a list of "call this function on
    // that object" entries, built by the loader from what the creator wired up
    // in the Inspector. Invoke() calls each one and logs failures instead of
    // letting one broken entry stop the rest.
    // ----------------------------------------------------------------------
    public class PuzzleEvent {
        private class Call {
            public object target;
            public MethodInfo method;
            public object[] args;
            public string description;
        }

        private readonly List<Call> calls = new List<Call>();

        public int Count { get { return calls.Count; } }

        public void Add(object target, MethodInfo method, object[] args, string description) {
            calls.Add(new Call { target = target, method = method, args = args, description = description });
        }

        // Events run for everyone. The host decides when an event happens: it runs the calls,
        // then bumps this event's hidden counter, and every other player runs the same calls
        // when the bump arrives (once per bump, so quick repeats aren't lost). SDK functions that
        // change the puzzle ignore calls on other players' PCs (see PuzzleSDK.IsHost), so the
        // host's choices (Random included) are the ones everyone sees; anything else (animations,
        // sounds, the creator's own objects) happens on every PC.
        // A player who joins later doesn't get events from before they arrived.
        internal static bool RunsHere { get { return NetworkServer.active || !NetworkClient.active; } }

        private const int Cycle = 100;      // the counter goes 1..100 and round again
        private const int MaxCatchUp = 5;   // at most this many runs for one arrival
        private SyncedValue channel;
        private int fired;                  // host: the counter
        private int lastSeen = -1;          // other players: the last counter that arrived

        // Called by the loader for every event that has something wired to it. Every PC builds
        // the puzzle the same way, so the counters get the same network ids everywhere.
        internal void AttachChannel(Transform parent, string label) {
            channel = new SyncedValue(parent, "event " + label, oneShot: true);
            if (!channel.Ready) { channel = null; return; }
            channel.onChanged = OnHostFired;
            channel.RegisterSoon();
        }

        public void Invoke() {
            if (!RunsHere) return;   // other players run it when the host's bump arrives
            RunCalls(1);
            if (channel != null && NetworkServer.active) {
                fired = fired % Cycle + 1;
                channel.Push(fired);
            }
        }

        // For an event that sets up the puzzle's starting look (fired once when the puzzle spawns):
        // every PC runs it for itself, at its own spawn, so players who join later get it too.
        // SDK functions that change the puzzle still only count on the host.
        public void InvokeLocal() {
            RunCalls(1);
        }

        // Other players: the host fired this event (maybe several times since the last arrival)
        private void OnHostFired(int value) {
            int times = 1;
            if (lastSeen >= 0) times = Mathf.Clamp(((value - lastSeen) % Cycle + Cycle) % Cycle, 1, MaxCatchUp);
            lastSeen = value;
            RunCalls(times);
        }

        private void RunCalls(int times) {
            for (int run = 0; run < times; run++) {
                foreach (Call call in calls) {
                    try {
                        call.method.Invoke(call.target, call.args);
                    } catch (Exception ex) {
                        Exception inner = ex.InnerException ?? ex;
                        Plugin.log.LogWarning($"[PuzzleEvent] {call.description} failed: {inner.GetType().Name}: {inner.Message}");
                    }
                }
            }
        }
    }

    // ----------------------------------------------------------------------
    // AssetBundle access. Which of Unity's AssetBundle entry points survive in
    // an IL2CPP build differs per game, so each step tries several routes and
    // uses the first one that works. Every route is a separate lambda so that a
    // MissingMethodException from one cannot take the others down with it.
    // ----------------------------------------------------------------------
    internal static class BundleAccess {
        private const string AssetName = "puzzle";

        public static UnityEngine.AssetBundle LoadBundle(string path) {
            return TryEach("load the AssetBundle",
                new[] { "Direct icall LoadFromFile", "AssetBundle.LoadFromFile", "AssetBundle.LoadFromFileAsync" },
                new Func<UnityEngine.AssetBundle>[] {
                    () => NativeBundle.LoadFromFile(path),
                    () => UnityEngine.AssetBundle.LoadFromFile(path),
                    () => {
                        // Reading assetBundle before the request finishes makes Unity complete it synchronously
                        var request = UnityEngine.AssetBundle.LoadFromFileAsync(path);
                        return request != null ? request.assetBundle : null;
                    }
                });
        }

        public static GameObject LoadPrefab(UnityEngine.AssetBundle bundle) {
            return TryEach("load the puzzle prefab",
                new[] { "Direct icall LoadAsset", "AssetBundle.LoadAsset", "AssetBundle.LoadAssetAsync", "AssetBundle.LoadAllAssets" },
                new Func<GameObject>[] {
                    () => NativeBundle.LoadGameObject(bundle, AssetName),
                    () => {
                        UnityEngine.Object asset = bundle.LoadAsset(AssetName, Il2CppType.Of<GameObject>());
                        return asset != null ? asset.TryCast<GameObject>() : null;
                    },
                    () => {
                        var request = bundle.LoadAssetAsync(AssetName, Il2CppType.Of<GameObject>());
                        UnityEngine.Object asset = request != null ? request.asset : null;
                        return asset != null ? asset.TryCast<GameObject>() : null;
                    },
                    () => {
                        foreach (UnityEngine.Object asset in bundle.LoadAllAssets()) {
                            GameObject go = asset != null ? asset.TryCast<GameObject>() : null;
                            if (go != null) return go;
                        }
                        return null;
                    }
                });
        }

        // Loads a texture that the exporter added to the bundle (named by its Unity project path).
        public static Texture2D LoadTexture(UnityEngine.AssetBundle bundle, string assetPath) {
            if (bundle == null || string.IsNullOrEmpty(assetPath)) return null;
            try {
                IntPtr pointer = NativeBundle.LoadAssetPointer(bundle, assetPath, Il2CppType.Of<Texture2D>());
                if (pointer != IntPtr.Zero) return new Texture2D(pointer);
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[BundleAccess] Loading texture '{assetPath}' failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
            Plugin.log.LogWarning($"[BundleAccess] Texture '{assetPath}' is not in the pack.");
            return null;
        }

        // A sound a script field points at (shipped in the pack by the exporter)
        public static AudioClip LoadAudioClip(UnityEngine.AssetBundle bundle, string assetPath) {
            if (bundle == null || string.IsNullOrEmpty(assetPath)) return null;
            try {
                IntPtr pointer = NativeBundle.LoadAssetPointer(bundle, assetPath, Il2CppType.Of<AudioClip>());
                if (pointer != IntPtr.Zero) {
                    // Its sound data must be read now: the pack is closed once the puzzle is built
                    var clip = new AudioClip(pointer);
                    try { clip.LoadAudioData(); } catch (Exception) { }
                    return clip;
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[BundleAccess] Loading sound '{assetPath}' failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
            Plugin.log.LogWarning($"[BundleAccess] Sound '{assetPath}' is not in the pack.");
            return null;
        }

        // Releases the bundle file but keeps everything that was loaded from it alive.
        public static void Unload(UnityEngine.AssetBundle bundle) {
            try {
                Action viaUnity = () => bundle.Unload(false);
                viaUnity();
                return;
            } catch (Exception ex) {
                Plugin.log.LogWarning($"AssetBundle.Unload failed ({ex.GetType().Name}: {ex.Message}), trying the direct call.");
            }
            try {
                NativeBundle.Unload(bundle, false);
            } catch (Exception ex) {
                Plugin.log.LogError($"Could not unload the AssetBundle; loading the same pack again will fail until restart. {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static T TryEach<T>(string what, string[] names, Func<T>[] attempts) where T : UnityEngine.Object {
            for (int i = 0; i < attempts.Length; i++) {
                try {
                    T result = attempts[i]();
                    if (result != null) return result;
                    Plugin.log.LogWarning($"[BundleAccess] {names[i]} returned null.");
                } catch (Exception ex) {
                    Plugin.log.LogWarning($"[BundleAccess] {names[i]} failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
            Plugin.log.LogError($"[BundleAccess] Every way to {what} failed.");
            return null;
        }
    }

    // ----------------------------------------------------------------------
    // Calls Unity 6's native AssetBundle functions directly, skipping the C#
    // wrappers that Il2CppInterop cannot translate (the ReadOnlySpan errors).
    // In Unity 6 strings go to the engine as a pointer + length struct, and
    // objects come back as a GC handle that has to be turned into an object.
    // ----------------------------------------------------------------------
    internal static class NativeBundle {
        [StructLayout(LayoutKind.Sequential)]
        private struct ManagedSpanWrapper {
            public IntPtr begin; // pointer to UTF-16 characters
            public int length;   // number of characters
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr LoadFromFileDelegate(ref ManagedSpanWrapper path, uint crc, ulong offset);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr LoadAssetDelegate(IntPtr self, ref ManagedSpanWrapper name, IntPtr type);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void UnloadDelegate(IntPtr self, [MarshalAs(UnmanagedType.U1)] bool unloadAllLoadedObjects);

        [DllImport("GameAssembly", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr il2cpp_gchandle_get_target(IntPtr gchandle);

        private static T Resolve<T>(string name) where T : Delegate {
            IntPtr fn = IL2CPP.il2cpp_resolve_icall(name);
            if (fn == IntPtr.Zero) throw new MissingMethodException($"icall '{name}' does not exist in this game build");
            return Marshal.GetDelegateForFunctionPointer<T>(fn);
        }

        public static UnityEngine.AssetBundle LoadFromFile(string path) {
            var icall = Resolve<LoadFromFileDelegate>("UnityEngine.AssetBundle::LoadFromFile_Internal_Injected");
            IntPtr chars = Marshal.StringToHGlobalUni(path);
            try {
                var span = new ManagedSpanWrapper { begin = chars, length = path.Length };
                IntPtr handle = icall(ref span, 0u, 0UL);
                if (handle == IntPtr.Zero) return null;
                IntPtr obj = il2cpp_gchandle_get_target(handle);
                return obj == IntPtr.Zero ? null : new UnityEngine.AssetBundle(obj);
            } finally {
                Marshal.FreeHGlobal(chars);
            }
        }

        public static GameObject LoadGameObject(UnityEngine.AssetBundle bundle, string name) {
            IntPtr obj = LoadAssetPointer(bundle, name, Il2CppType.Of<GameObject>());
            return obj == IntPtr.Zero ? null : new GameObject(obj);
        }

        // Loads any asset from the bundle by name and returns the raw object pointer (zero if missing)
        public static IntPtr LoadAssetPointer(UnityEngine.AssetBundle bundle, string name, Il2CppSystem.Type type) {
            var icall = Resolve<LoadAssetDelegate>("UnityEngine.AssetBundle::LoadAsset_Internal_Injected");
            IntPtr chars = Marshal.StringToHGlobalUni(name);
            try {
                var span = new ManagedSpanWrapper { begin = chars, length = name.Length };
                IntPtr handle = icall(bundle.m_CachedPtr, ref span, type.Pointer);
                if (handle == IntPtr.Zero) return IntPtr.Zero;
                return il2cpp_gchandle_get_target(handle);
            } finally {
                Marshal.FreeHGlobal(chars);
            }
        }

        public static void Unload(UnityEngine.AssetBundle bundle, bool unloadAllLoadedObjects) {
            var icall = Resolve<UnloadDelegate>("UnityEngine.AssetBundle::Unload_Injected");
            icall(bundle.m_CachedPtr, unloadAllLoadedObjects);
        }
    }

    // ----------------------------------------------------------------------
    // Finds the vanilla objects the SDK components copy. Each component knows the
    // exact path of its template ("RootObject/child/child"), which is looked up
    // directly: no search through every loaded object. Works for inactive objects
    // too. Results are remembered between loads. If a path stops working (say,
    // after a game update), the component falls back to searching and reports
    // the path it found instead, so the hard-coded one can be updated.
    // ----------------------------------------------------------------------
    internal static class Templates {
        private static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();

        // The first of these paths that exists (one per lobby size: only the challenges for the
        // lobby's player count are loaded), or null. Null or empty paths are skipped.
        internal static GameObject Find(params string[] paths) {
            if (paths == null) return null;
            foreach (string path in paths) {
                GameObject found = Find(path);
                if (found != null) return found;
            }
            return null;
        }

        // The object at this exact path in any loaded scene, or null
        internal static GameObject Find(string path) {
            if (string.IsNullOrEmpty(path)) return null;
            if (cache.TryGetValue(path, out GameObject known)) {
                if (known != null) return known;
                cache.Remove(path);   // destroyed since (scene reloaded)
            }

            int slash = path.IndexOf('/');
            string rootName = slash < 0 ? path : path.Substring(0, slash);
            string rest = slash < 0 ? "" : path.Substring(slash + 1);
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++) {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects()) {
                    if (root == null || root.name != rootName) continue;
                    Transform found = rest.Length == 0 ? root.transform : root.transform.Find(rest);
                    if (found == null) continue;
                    cache[path] = found.gameObject;
                    return found.gameObject;
                }
            }
            return null;
        }

        // Called when the exact path failed but a search found a template: remember it for
        // the next load and report where it really is
        internal static void FoundBySearch(Component who, string variant, string path, GameObject found) {
            if (found == null) return;
            if (!string.IsNullOrEmpty(path)) cache[path] = found;
            string label = (who != null ? who.GetIl2CppType().Name : "?") + (string.IsNullOrEmpty(variant) ? "" : " " + variant);
            Plugin.log.LogWarning($"[Template] {label}: not at \"{path}\"; found by searching at \"{FullPath(found.transform)}\"");
        }

        // Copies a vanilla template under the given parent. The parent sits inside the puzzle's
        // switched-off holder, so the copy is created asleep and gets its new ids before it wakes.
        // (The vanilla original is left alone: switching it off made props such as the gourd in
        // the gourd vice drop out of it before it could be copied.)
        internal static GameObject Clone(GameObject template, Transform parent) {
            GameObject copy = UnityEngine.Object.Instantiate((UnityEngine.Object)template, parent).Cast<GameObject>();
            UsePlayerCount4Look(copy);
            RemoveInheritedPlayers(copy);
            return copy;
        }

        // A copy made while someone sits or stands in the original (chair, telescope...) would also
        // contain that player (the game parents the whole player, named "PlayerCharacter <id>", under the
        // pose) and think the pose is taken. Take the player out of every copy.
        private static void RemoveInheritedPlayers(GameObject copy) {
            try {
                var doomed = new List<GameObject>();
                foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Transform>(), true)) {
                    if (c != null && c.transform != copy.transform && c.name.StartsWith("PlayerCharacter")) doomed.Add(c.gameObject);
                }
                foreach (GameObject go in doomed) { if (go != null) UnityEngine.Object.DestroyImmediate(go); }
                foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PlayerPose>(), true)) {
                    PlayerPose pose = c != null ? c.TryCast<PlayerPose>() : null;
                    if (pose == null) continue;
                    try { pose.occupant = null; pose.chosenPlayer = null; } catch (Exception) { }
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Template] could not remove inherited players from '{copy.name}': {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }

        // Some vanilla objects carry a PlayerCountSwapper: when it wakes it switches on the part
        // made for the lobby's player count (target2 / target3 / target4) and switches off the
        // others. The templates are taken from the 4-player versions, so in a 2- or 3-player lobby
        // the copy would turn off the very parts it needs (a poke button's clickable part, a
        // progress bar's display), and the original may already have done the same before it was
        // copied. Every copy gets the 4-player look, and its swappers are removed before they wake.
        private static void UsePlayerCount4Look(GameObject copy) {
            try {
                foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PlayerCountSwapper>(), true)) {
                    PlayerCountSwapper swapper = c != null ? c.TryCast<PlayerCountSwapper>() : null;
                    if (swapper == null) continue;
                    GameObject four = swapper.target4;
                    SetInside(copy, swapper.target2, four, false);
                    SetInside(copy, swapper.target3, four, false);
                    SetInside(copy, four, null, true);
                    UnityEngine.Object.DestroyImmediate(swapper);
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Template] could not set up the 4-player look of '{copy.name}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void SetInside(GameObject copy, GameObject target, GameObject keep, bool on) {
            if (target == null || target == keep) return;
            if (!target.transform.IsChildOf(copy.transform)) return;   // never touch the vanilla level
            target.SetActive(on);
        }

        internal static string FullPath(Transform t) {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }

    // ----------------------------------------------------------------------
    // Makes a puzzle's own sounds follow the game's Master and Sound Effects sliders.
    //
    // The game applies its sliders in SettingsWardrobe.ApplyVolume / ApplyVolumeSfx (called
    // when a slider moves, and once at start-up). Two Harmony hooks note the new values; every
    // puzzle sound is then set to: its Inspector volume x master x sound effects, using the
    // game's own GetNormalizedVolume so it fades the same way the game's sounds do.
    // Only sounds with no mixer group are managed: the creator's own and the ones the SDK adds.
    // Copied vanilla objects keep the group (and volume handling) the game gave them.
    // ----------------------------------------------------------------------
    internal static class PuzzleAudio {
        private class Entry {
            public AudioSource source;
            public float maxVolume;   // the volume set in the Unity Inspector
        }

        private static readonly List<Entry> entries = new List<Entry>();
        private static float master = 1f, sfx = 1f;   // full until the game applies its settings

        internal static void InstallHooks(Harmony harmony) {
            Hook(harmony, GameRefs.Hooks.ApplyVolume, nameof(MasterChanged));
            Hook(harmony, GameRefs.Hooks.ApplyVolumeSfx, nameof(SfxChanged));
        }

        private static void Hook(Harmony harmony, string method, string postfixName) {
            try {
                MethodInfo target = AccessTools.Method(typeof(SettingsWardrobe), method);
                if (target == null) throw new MissingMethodException("SettingsWardrobe." + method + " not found");
                MethodInfo postfix = typeof(PuzzleAudio).GetMethod(postfixName, BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Audio] could not hook {method} ({ex.GetType().Name}: {ex.Message}); puzzle sounds will not follow that slider");
            }
        }

        // Harmony postfixes: 'value' is the slider position the game was given
        private static void MasterChanged(int value) {
            master = Normalize(value);
            ApplyAll();
        }
        private static void SfxChanged(int value) {
            sfx = Normalize(value);
            ApplyAll();
        }

        private static bool normalizeFailed;
        private static float Normalize(int value) {
            try { return Mathf.Clamp01(SettingsWardrobe.GetNormalizedVolume(value)); }
            catch (Exception ex) {
                if (!normalizeFailed) {
                    normalizeFailed = true;
                    Plugin.log.LogWarning($"[Audio] GetNormalizedVolume failed ({ex.GetType().Name}: {ex.Message}); using full volume");
                }
                return 1f;
            }
        }

        // Called by the loader once each puzzle is built: remember every managed sound and its
        // Inspector volume, then apply the current sliders (Clear runs once before all puzzles)
        internal static void Clear() { entries.Clear(); loops.Clear(); pending.Clear(); }

        internal static void Collect(GameObject root) {
            try {
                foreach (Component c in root.GetComponentsInChildren(Il2CppType.Of<AudioSource>(), true)) {
                    AudioSource source = c != null ? c.TryCast<AudioSource>() : null;
                    if (source == null) continue;
                    // Every puzzle sound plays from where it is in the world (fully 3D)
                    source.spatialBlend = 1f;
                    if (source.outputAudioMixerGroup != null) continue;
                    entries.Add(new Entry { source = source, maxVolume = source.volume });
                    if (source.loop) loops.Add(new Loop { source = source });
                    // Play On Awake: held back, then started together with the other sounds that are ready (see StartPending)
                    if (source.playOnAwake) {
                        source.playOnAwake = false;
                        try { source.Stop(); } catch (Exception) { }
                        pending.Add(new Pending { source = source });
                    }
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Audio] could not collect the puzzle's sounds: {ex.GetType().Name}: {ex.Message}");
            }
            ApplyAll();
        }

        // ---- Looping sounds stay in step for everyone, late joiners included ----
        // A looping sound's place in its loop is taken from the network's shared clock (Mirror's
        // NetworkTime, the host's time on every PC): the time modulo the clip's length. So a sound that
        // starts playing is put at the right place at once, and each one is checked once a second and
        // moved back if it has drifted. No messages are sent; everyone works out the same place.
        private class Loop {
            public AudioSource source;
            public bool wasPlaying;
            public float nextCheck;
        }
        private static readonly List<Loop> loops = new List<Loop>();
        private const float LoopCheckSeconds = 1f;
        private const float LoopMaxDrift = 0.1f;
        private static bool clockFailed;

        private static double SharedTime() {
            try { return NetworkTime.time; }
            catch (Exception ex) {
                if (!clockFailed) {
                    clockFailed = true;
                    Plugin.log.LogWarning($"[Audio] the network clock is not available ({ex.GetType().Name}: {ex.Message}); looping sounds use this PC's time and will not line up between players");
                }
                return Time.unscaledTimeAsDouble;
            }
        }

        // ---- Play On Awake sounds start together ----
        // Unity starts each one the moment its object wakes, and a clip that has to load first starts
        // late, so speakers that should play the same music came out of step. Now they are held back
        // and started in one go, once every sound that is about to start has its clip loaded
        // (waiting at most MaxLoadWait for a slow one). A sound on an object that is switched off
        // starts when the object is switched on, like Play On Awake does.
        private class Pending {
            public AudioSource source;
            public float since = -1f;
        }
        private static readonly List<Pending> pending = new List<Pending>();
        private const float MaxLoadWait = 5f;

        private static void StartPending() {
            if (pending.Count == 0) return;
            float now = Time.unscaledTime;
            bool allLoaded = true;
            var ready = new List<Pending>();
            for (int i = pending.Count - 1; i >= 0; i--) {
                Pending p = pending[i];
                try {
                    if (p.source == null) { pending.RemoveAt(i); continue; }
                    if (!p.source.enabled || !p.source.gameObject.activeInHierarchy) continue;   // not awake yet
                    if (p.since < 0f) p.since = now;
                    AudioClip clip = p.source.clip;
                    if (clip != null) {
                        AudioDataLoadState load = clip.loadState;
                        if (load == AudioDataLoadState.Unloaded) { clip.LoadAudioData(); load = clip.loadState; }
                        if (load == AudioDataLoadState.Loading || load == AudioDataLoadState.Unloaded) {
                            if (now - p.since < MaxLoadWait) { allLoaded = false; continue; }
                        }
                    }
                    ready.Add(p);
                } catch (Exception) { pending.RemoveAt(i); }
            }
            if (!allLoaded) return;
            foreach (Pending p in ready) {
                try { p.source.Play(); } catch (Exception) { }
                pending.Remove(p);
            }
        }

        internal static void Tick() {
            StartPending();
            if (loops.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = loops.Count - 1; i >= 0; i--) {
                Loop l = loops[i];
                try {
                    if (l.source == null) { loops.RemoveAt(i); continue; }   // destroyed with an old puzzle
                    if (!l.source.isPlaying) { l.wasPlaying = false; continue; }
                    if (l.wasPlaying && now < l.nextCheck) continue;
                    l.nextCheck = now + LoopCheckSeconds;
                    bool justStarted = !l.wasPlaying;
                    l.wasPlaying = true;
                    AudioClip clip = l.source.clip;
                    if (clip == null || clip.length < 0.5f) continue;
                    if (Mathf.Abs(l.source.pitch - 1f) > 0.001f) continue;   // a changed pitch moves the loop at another speed
                    float length = clip.length;
                    float expected = (float)(SharedTime() % length);
                    float diff = Mathf.Abs(l.source.time - expected);
                    diff = Mathf.Min(diff, length - diff);
                    if (justStarted || diff > LoopMaxDrift) l.source.time = Mathf.Min(expected, length - 0.01f);
                } catch (Exception) { loops.RemoveAt(i); }
            }
        }

        private static void ApplyAll() {
            float factor = master * sfx;
            for (int i = entries.Count - 1; i >= 0; i--) {
                Entry e = entries[i];
                if (e.source == null) { entries.RemoveAt(i); continue; }   // destroyed with an old puzzle
                try { e.source.volume = e.maxVolume * factor; }
                catch (Exception) { entries.RemoveAt(i); }
            }
        }
    }

    // ----------------------------------------------------------------------
    // Hands out network ids ("tickets") for the SDK components' copies. The game
    // spreads its own ids all over the number range, so fixed ranges clashed with
    // vanilla objects. Instead, every id used by a vanilla object in the loaded
    // world is collected once, and puzzle copies get the next free ids in load
    // order. Every player has the same world and loads the same puzzles in the
    // same order, so everyone hands out the same ids.
    // ----------------------------------------------------------------------
    // Grabbable items (peg tiles, gourds) are networked objects: the host creates them and tells
    // every player "spawn object X". Our items are copies, so the other players' games had
    // nothing called X and dropped the message. Now every item gets a fixed id worked out from
    // its place in the puzzle (the same on every PC). Each player's game registers its own
    // sleeping copy under that id, so the host's message finds it.
    //
    // A player who joins gets the host's spawn messages while still joining, before their puzzle
    // has loaded, so those would still be missed. The host therefore sends the spawn message for
    // each item again a few times after a player joins. A repeat for an item the player already
    // has just refreshes its position.
    // Reads the current value of a game switch state: the last value it received (from this
    // PC or over the network)
    internal static class PeckValue {
        internal static bool TryRead(TrackedPeckState st, out int value) {
            value = 0;
            if (st == null) return false;
            try {
                value = st.currentPeckContext.state;
                return true;
            } catch (Exception) { return false; }
        }
    }

    // A whole number the host sets and every other player
    // reads. It rides on the game's own switch-state sync: a hidden TrackedPeckState with its own
    // network id, the same thing that keeps doors and buttons in step between players.
    internal class SyncedValue {
        internal static bool IsHost { get { return NetworkServer.active; } }

        // ---- Being told about new values (other players) ----
        //
        // When a value arrives from the host, the game applies it to the state with
        // TrackedPeckState.DoPeck. One Harmony hook on that method looks the state up in 'byState'
        // and calls the owner's onChanged. Nothing is checked while nothing changes. If the hook
        // cannot be installed, one central check (Tick, 10 times a second) does the same job.

        // Called with the host's new value, on other players' PCs only
        internal Action<int> onChanged;

        private static readonly Dictionary<IntPtr, SyncedValue> byState = new Dictionary<IntPtr, SyncedValue>();
        private static bool hooked;

        internal static void InstallHook(Harmony harmony) {
            try {
                MethodInfo target = AccessTools.Method(typeof(TrackedPeckState), GameRefs.Hooks.DoPeck);
                MethodInfo postfix = typeof(SyncedValue).GetMethod(nameof(DoPeckPostfix), BindingFlags.Static | BindingFlags.NonPublic);
                if (target == null) throw new MissingMethodException("TrackedPeckState.DoPeck not found");
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                hooked = true;
            } catch (Exception ex) {
                hooked = false;
                Plugin.log.LogWarning($"[Sync] could not hook state changes ({ex.GetType().Name}: {ex.Message}); checking 10 times a second instead");
            }
        }

        // Runs after every DoPeck in the game: one lookup, and nothing else unless the state is ours.
        // Deliberately does not take DoPeck's PeckContext parameter.
        private static void DoPeckPostfix(TrackedPeckState __instance) {
            if (__instance == null || byState.Count == 0) return;
            IntPtr ptr = __instance.Pointer;
            SyncedValue v;
            // Other players: one of our hidden states received a value
            if (byState.TryGetValue(ptr, out v)) { dirty.Add(v); return; }
            if (!IsHost) return;
            // Host: a copied game state changed; its mirror sends the new value (see StateMirrors)
            if (mirrorBySource.TryGetValue(ptr, out v)) dirty.Add(v);
            // Host: a component asked to hear about this state (see WatchOnHost)
            if (hostWatches.ContainsKey(ptr)) pendingWatches.Add(ptr);
        }

        // ---- Host: hearing about a game state changing ----
        // For components whose logic runs on the host and reacts to a game state that any
        // player can change (e.g. a press on a sim-press switch). The callback gets the state's
        // new value once per change, after the game has finished applying it.
        private class HostWatch {
            public TrackedPeckState state;
            public Action<int> onChange;
            public int lastPolled = int.MinValue;
        }
        private static readonly Dictionary<IntPtr, HostWatch> hostWatches = new Dictionary<IntPtr, HostWatch>();
        private static readonly List<IntPtr> pendingWatches = new List<IntPtr>();

        internal static void WatchOnHost(TrackedPeckState st, Action<int> onChange) {
            if (st == null || onChange == null) return;
            var watch = new HostWatch { state = st, onChange = onChange };
            int now;
            if (PeckValue.TryRead(st, out now)) watch.lastPolled = now;
            hostWatches[st.Pointer] = watch;
        }

        private static void RunHostWatches() {
            if (!IsHost || hostWatches.Count == 0) return;
            if (!hooked) {
                // No hook: notice changes by comparing (10 times a second, see Tick)
                foreach (var pair in hostWatches) {
                    int value;
                    if (PeckValue.TryRead(pair.Value.state, out value) && value != pair.Value.lastPolled) pendingWatches.Add(pair.Key);
                }
            }
            if (pendingWatches.Count == 0) return;
            var work = new List<IntPtr>(pendingWatches);
            pendingWatches.Clear();
            foreach (IntPtr ptr in work) {
                HostWatch watch;
                if (!hostWatches.TryGetValue(ptr, out watch) || watch.state == null) continue;
                int value;
                if (!PeckValue.TryRead(watch.state, out value)) continue;
                watch.lastPolled = value;
                try { watch.onChange(value); }
                catch (Exception ex) { Plugin.log.LogWarning($"[Sync] watch on '{watch.state.name}': {ex.GetType().Name}: {ex.Message}"); }
            }
        }

        // Work found by the hook is done once per frame in Tick, never inside the game's own
        // state code: the owner gets only the latest value (a re-send nudges a value to a
        // neighbour and back within one update; only the real value is passed on)
        private static readonly HashSet<SyncedValue> dirty = new HashSet<SyncedValue>();
        private static readonly List<SyncedValue> toRegister = new List<SyncedValue>();
        internal static readonly Dictionary<IntPtr, SyncedValue> mirrorBySource = new Dictionary<IntPtr, SyncedValue>();
        private static float nextPoll;

        internal static void Tick() {
            if (toRegister.Count > 0) {
                foreach (SyncedValue v in toRegister.ToArray()) v.Register();
                toRegister.Clear();
            }
            // Fallback when the hook is missing: one loop for the whole puzzle, not one per component
            bool poll = !hooked && Time.unscaledTime >= nextPoll;
            if (poll) {
                nextPoll = Time.unscaledTime + 0.1f;
                foreach (SyncedValue v in all) if (IsHost ? v.hostSource != null : true) dirty.Add(v);
            }
            if (hooked || poll) RunHostWatches();
            if (dirty.Count == 0) return;
            var work = new List<SyncedValue>(dirty);
            dirty.Clear();
            foreach (SyncedValue v in work) {
                if (IsHost) { if (v.hostSource != null) v.PushFromSource(); }
                else v.Notify();
            }
        }

        // Registers the value with the game's id registry on the next frame (for values made
        // outside a component, e.g. the state mirrors)
        internal void RegisterSoon() { toRegister.Add(this); }

        // Host: where a mirror reads the value it sends
        internal TrackedPeckState hostSource;
        // Hold buttons: a second value says which player did it (sent first, so it is there when the state arrives)
        internal SyncedValue holderCompanion;
        private void PushFromSource() {
            int value;
            if (!PeckValue.TryRead(hostSource, out value)) return;
            if (holderCompanion != null) holderCompanion.Push(StateMirrors.HolderRank(hostSource));
            Push(value);
        }

        // Other players: if a new value has arrived, pass it to the owner
        private int lastNotified = int.MinValue;
        private void Notify() {
            if (IsHost || onChanged == null) return;
            int value;
            if (!TryGet(out value) || value == lastNotified) return;
            lastNotified = value;
            try { onChanged(value); }
            catch (Exception ex) { Plugin.log.LogWarning($"[Sync] '{label}': {ex.GetType().Name}: {ex.Message}"); }
        }

        // One-shot values (e.g. confetti fired) are not re-sent to players who join later
        private readonly bool oneShot;
        // Persistent values outlive puzzle reloads (e.g. the reload signal itself)
        private readonly bool persistent;

        private readonly string label;
        private readonly TrackedPeckState state;
        private readonly ushort ticket;

        // Called during setup, while the puzzle is still asleep
        internal SyncedValue(Transform parent, string label, bool oneShot = false, bool persistent = false) {
            this.label = label;
            this.oneShot = oneShot;
            this.persistent = persistent;
            try {
                // Copy a real game state (made from scratch, the game's code finds parts missing
                // and crashes), then strip it down to just the state, linked to nothing
                GameObject go = null;
                Transform moveTo = null;
                GameObject prototype = Prototype();
                if (prototype != null) {
                    // Made under the prototype's inactive holder and moved to 'parent' once the id is set:
                    // under an awake parent the copy would wake up with the template's id and log "Duplicate ticket"
                    go = UnityEngine.Object.Instantiate((UnityEngine.Object)prototype, prototype.transform.parent).Cast<GameObject>();
                    moveTo = parent;
                    state = go.GetComponent<TrackedPeckState>();
                } else {
                    Plugin.log.LogWarning($"[Sync] '{label}': no game state to copy; making one from scratch");
                    go = new GameObject();
                    go.transform.SetParent(parent, false);
                    state = go.AddComponent(Il2CppType.Of<TrackedPeckState>()).Cast<TrackedPeckState>();
                }
                go.name = "Sync_" + label;
                go.transform.localPosition = Vector3.zero;
                ticket = Tickets.Allocate(1, "synced value '" + label + "'")[0];
                state.ticket = ticket;
                state.shellReference = new SeaShell.ShellReference(ticket);
                state.systemRefences = new Il2CppSystem.Collections.Generic.List<PeckSystemReference>();
                state.savableSystem = SavableSystem.NotSavable;
                state.saveIdentity = null;
                state.hasInitialState = false;
                state.initialState = 0;
                state.ignoreStateRepeats = false;   // so the same value can be sent again (see ResendAll)
                startAction = state.currentPeckContext.actionNumber;   // see TryGet
                startValue = state.currentPeckContext.state;
                if (moveTo != null) go.transform.SetParent(moveTo, false);
                all.Add(this);
                byState[state.Pointer] = this;
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Sync] '{label}': could not create: {ex.GetType().Name}: {ex.Message}");
                state = null;
            }
        }

        // A stripped-down copy of a game state, made once and copied for every synced value
        // (stripping a fresh copy each time made puzzles with many events slow to build). Kept
        // under an inactive holder, so the prototype itself never wakes up.
        private static GameObject prototype;
        private static GameObject Prototype() {
            if (prototype != null) return prototype;
            TrackedPeckState template = Template();
            if (template == null) return null;
            var holder = new GameObject("BigPuzzleSDK Sync Prototype");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);
            GameObject go = UnityEngine.Object.Instantiate((UnityEngine.Object)template.gameObject, holder.transform).Cast<GameObject>();
            for (int i = go.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            foreach (Component c in go.GetComponents(Il2CppType.Of<Component>())) {
                if (c == null) continue;
                string type = c.GetIl2CppType().Name;
                if (type != "Transform" && type != "TrackedPeckState") UnityEngine.Object.DestroyImmediate(c);
            }
            prototype = go;
            return prototype;
        }

        // A plain game state to copy: the first one in the vanilla push button
        private static TrackedPeckState cachedTemplate;
        private static TrackedPeckState Template() {
            if (cachedTemplate != null) return cachedTemplate;
            GameObject button = Templates.Find(ButtonPath);
            if (button == null) return null;
            Component c = button.GetComponentInChildren(Il2CppType.Of<TrackedPeckState>(), true);
            cachedTemplate = c != null ? c.TryCast<TrackedPeckState>() : null;
            return cachedTemplate;
        }
        private const string ButtonPath = GameRefs.SyncPrototype.ButtonPath;

        // Every synced value of the loaded puzzle (cleared on each load)
        internal static readonly List<SyncedValue> all = new List<SyncedValue>();
        internal static void Clear() {
            all.RemoveAll(v => !v.persistent);
            var keep = new List<KeyValuePair<IntPtr, SyncedValue>>();
            foreach (var pair in byState) if (pair.Value.persistent) keep.Add(pair);
            byState.Clear();
            foreach (var pair in keep) byState[pair.Key] = pair.Value;
            dirty.Clear(); toRegister.Clear(); mirrorBySource.Clear();
            hostWatches.Clear(); pendingWatches.Clear();
        }

        // Host: send every value again. A player who joins does not get values that were set
        // before they arrived (only later changes), so this runs a few times after each join.
        internal static void ResendAll() {
            if (!IsHost) return;
            // A repeat of the same value may not be sent at all, so nudge it to a neighbour
            // first; both changes go out together and the player ends on the real value
            foreach (SyncedValue v in all) {
                if (v.lastSent == int.MinValue || v.oneShot) continue;
                v.Set(v.lastSent >= 127 ? v.lastSent - 1 : v.lastSent + 1);
                v.Set(v.lastSent);
            }
        }

        // Host: send this value again now (nudged to a neighbour and back, like ResendAll)
        internal void Resend() {
            if (!IsHost || lastSent == int.MinValue) return;
            Set(lastSent >= 127 ? lastSent - 1 : lastSent + 1);
            Set(lastSent);
        }

        internal bool Ready { get { return state != null; } }
        internal bool Alive { get { try { return state != null; } catch (Exception) { return false; } } }

        // True if the game's current id list points at this value's state. The list can be rebuilt
        // after we registered (a second lobby, a rejoin), leaving the value deaf to the host.
        internal bool IsRegistered() {
            if (state == null) return true;   // nothing to register
            var office = LobbyNetworking.TicketOffice.instance;
            if (office == null) return true;  // can't tell yet; Register would only complain
            try {
                LobbyNetworking.ITicketed found;
                if (!office.tickets.TryGetValue(ticket, out found) || found == null) return false;
                UnityEngine.Object owner = found.TryCast<UnityEngine.Object>();
                return owner != null && owner.Pointer == state.Pointer;
            } catch (Exception) { return false; }
        }

        // For the log: what the state has been told so far
        internal string DebugText() {
            try {
                PeckContext ctx = state.currentPeckContext;
                return $"'{label}' ticket {ticket}: action {ctx.actionNumber} state {ctx.state} (made with {startAction}/{startValue}), received {received}, registered {IsRegistered()}";
            } catch (Exception ex) { return $"'{label}': unreadable ({ex.GetType().Name})"; }
        }

        // Called from Start, once the puzzle is awake: tells the game's id registry about it
        internal void Register() {
            if (state == null) return;
            // Under a prop that is still asleep (spawned by the host later): the game registers the
            // id itself when the state wakes up, and doing it here first only makes it log "Duplicate ticket"
            try { if (!state.gameObject.activeInHierarchy) return; } catch (Exception) { }
            var office = LobbyNetworking.TicketOffice.instance;
            if (office == null) { Plugin.log.LogWarning($"[Sync] '{label}': no ticket office"); return; }
            try { office.tickets[ticket] = state.Cast<LobbyNetworking.ITicketed>(); }
            catch (Exception ex) { Plugin.log.LogWarning($"[Sync] '{label}': registration failed: {ex.GetType().Name}: {ex.Message}"); }
            Notify();   // in case a value arrived before this point
        }

        // Host only. Values travel as small numbers: keep them between -128 and 127.
        internal void Set(int value) {
            if (state == null || !IsHost) return;
            if (value < -128 || value > 127) {
                Plugin.log.LogWarning($"[Sync] '{label}': {value} is out of range (-128 to 127); clamped");
                value = Mathf.Clamp(value, -128, 127);
            }
            try {
                state.SetState(value);
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Sync] '{label}': SetState({value}) failed: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }

        // Host: send a value if it changed since the last send (nothing is sent otherwise)
        private int lastSent = int.MinValue;
        internal void Push(int value) {
            if (!IsHost || value == lastSent) return;
            lastSent = value;
            Set(value);
        }

        // Other players: forget what was received, so only a value sent from now on counts
        // (used when joining a different host)
        internal void ForgetReceived() {
            received = false;
            lastNotified = int.MinValue;
            if (state == null) return;
            try {
                PeckContext ctx = state.currentPeckContext;
                startAction = ctx.actionNumber;
                startValue = ctx.state;
            } catch (Exception) { }
        }

        // False until the host has actually sent a value (the state's last change differs from
        // the one it was made with), so other players keep their own starting value (e.g. the bar
        // total from the Inspector) instead of reading a 0
        private int startAction, startValue;
        private bool received;
        internal bool TryGet(out int value) {
            value = 0;
            if (state == null) return false;
            try {
                PeckContext ctx = state.currentPeckContext;
                if (!received && ctx.actionNumber == startAction && ctx.state == startValue) return false;
                received = true;
                value = ctx.state;
                return true;
            } catch (Exception) { return false; }
        }
    }

    // ----------------------------------------------------------------------
    // Late join for the copied game states (light switches, lamps, doors, boxes, ...).
    // These sync through the game itself, but a player who joins only gets changes made after
    // they arrived. Each copied state therefore gets a hidden mirror (a SyncedValue): the host
    // sends the state's value through it whenever the state changes, and re-sends it when a
    // player joins (see SyncedValue.ResendAll). Another player's PC applies a mirrored value to
    // its own copy of the state only if it differs, so in normal play nothing happens twice.
    // ----------------------------------------------------------------------
    internal static class StateMirrors {
        // Called by the loader after every component's setup (so every PC hands out the same
        // network ids in the same order)
        internal static void Setup(Transform root) {
            foreach (Component c in root.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
                TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
                if (st == null || st.gameObject.name.StartsWith("Sync_")) continue;   // our own hidden states
                // A hold button's state also carries who is holding it (created first: it is sent first)
                SyncedValue holder = null;
                if (HoldStates.Contains(st.Pointer)) {
                    holder = new SyncedValue(st.transform, "holder " + st.name);
                    if (!holder.Ready) holder = null; else holder.RegisterSoon();
                }
                var mirror = new SyncedValue(st.transform, "mirror " + st.name);
                if (!mirror.Ready) continue;
                mirror.hostSource = st;
                mirror.holderCompanion = holder;
                TrackedPeckState target = st;
                mirror.onChanged = v => ApplyHere(target, v, holder);
                SyncedValue.mirrorBySource[st.Pointer] = mirror;
                mirror.RegisterSoon();
            }
        }

        // Other players: put this PC's copy of the state on the host's value, locally (the same
        // step the game takes when a change arrives from the host)
        // The states of hold buttons (PuzzleButton, PuzzleButtonNHold add theirs), whose peck should show the
        // player who is holding, also for a player who joins while it is held
        internal static readonly HashSet<IntPtr> HoldStates = new HashSet<IntPtr>();

        // Every PC lists the players the same way (by network id), so a number names one of them
        private static List<PlayerCharacter> PlayersByNetId() {
            var list = new List<PlayerCharacter>();
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PlayerCharacter>())) {
                PlayerCharacter pc = o != null ? o.TryCast<PlayerCharacter>() : null;
                if (pc != null && pc.gameObject.scene.IsValid()) list.Add(pc);
            }
            list.Sort((a, b) => a.GetComponent<NetworkIdentity>().netId.CompareTo(b.GetComponent<NetworkIdentity>().netId));
            return list;
        }

        // Host: 0 = nobody, otherwise 1 + the holder's place in the list
        internal static int HolderRank(TrackedPeckState st) {
            try {
                NetworkIdentity who = st.currentPeckContext.playerIdentity;
                if (who == null) return 0;
                var list = PlayersByNetId();
                for (int i = 0; i < list.Count; i++)
                    if (list[i].GetComponent<NetworkIdentity>().netId == who.netId) return i + 1;
            } catch (Exception) { }
            return 0;
        }

        private static void ApplyHere(TrackedPeckState st, int value, SyncedValue holder) {
            if (st == null) return;
            int current;
            if (PeckValue.TryRead(st, out current) && current == value) return;
            try {
                var ctx = new PeckContext();
                ctx.state = value;
                int rank;
                if (holder != null && holder.TryGet(out rank) && rank > 0) {
                    var list = PlayersByNetId();
                    if (rank <= list.Count) ctx.playerIdentity = list[rank - 1].GetComponent<NetworkIdentity>();
                }
                st.DoPeck(ctx, false);
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Sync] could not catch up '{st.name}': {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }
    }

    // ----------------------------------------------------------------------
    // Which players have the SDK, and the same puzzles as the host.
    //
    // A player's game disconnects on a network message it doesn't know, so the host never
    // messages players directly. Instead:
    //   1. The host keeps a hidden "SDK here" beacon state (a synced value; a player without the
    //      SDK simply ignores it).
    //   2. A player with the SDK who sees the beacon sends the host one message: its SDK version
    //      and every installed puzzle with its fingerprint.
    //   3. The host compares that with its own list and logs the result. A player who hasn't
    //      answered a while after joining doesn't have the SDK (or has a very old one).
    // Every load (F2) bumps the beacon, so players answer again with their new lists.
    // ----------------------------------------------------------------------
    internal static class SdkHandshake {
        private const ushort MessageId = 0xB15D;   // our own network message (checked for clashes)
        private const string Protocol = "BWSDK1";
        private const float AnswerWithin = 45f;    // seconds after the player is fully in the game
        private const float AnswerDelay = 0.5f;    // players: let the value settle before answering

        private static SyncedValue beacon;
        private static int loads;

        // Called by the loader right after the reload channel, before every puzzle
        internal static void Prepare() {
            // Its own id, taken on every load in the same order (made the first time)
            Transform parent = BigPuzzleEditor.instance != null ? BigPuzzleEditor.instance.transform : null;
            if (beacon == null && parent != null) beacon = new SyncedValue(parent, "sdk beacon", oneShot: false, persistent: true);
            else Tickets.Allocate(1);   // keep its id out of the puzzles'
            if (beacon == null) return;
            // Each lobby has its own id registry, so register again on every load (a player who
            // joins a second lobby would otherwise never hear the beacon)
            beacon.Register();

            // Host: a new value every load, so players answer again with their current puzzles
            loads = loads % 100 + 1;
            beacon.Push(loads);
            foreach (Player player in players.Values) { player.answer = null; player.since = Time.unscaledTime; player.reported = false; }
        }

        internal static void Tick() {
            if (NetworkServer.active) HostTick();
            else ClientTick();
        }

        // What this PC has: "BWSDK1|1.0.0|<id>:<fingerprint>:<first id>:<name>;..."
        // (one entry per puzzle; <first id> is where the host's copy starts its network ids,
        // -1 if the host didn't build it, and means nothing in a player's answer)
        private static string Describe() {
            var parts = new List<string>();
            foreach (PuzzlePack pack in PuzzleLibrary.packs) {
                if (NetworkServer.active && pack.firstTicket < 0) continue;   // host: failed to build, so not part of the game
                parts.Add(Clean(pack.id) + ":" + pack.hash + ":" + pack.firstTicket + ":" + Clean(pack.name));
            }
            // A player also has the built-in tower, even when it loads an installed tower itself
            PuzzlePack builtIn = PuzzleLibrary.builtInTower;
            if (!NetworkServer.active && builtIn != null && !PuzzleLibrary.packs.Contains(builtIn))
                parts.Add(Clean(builtIn.id) + ":" + builtIn.hash + ":-1:" + Clean(builtIn.name));
            return Protocol + "|" + Plugin.Version + "|" + string.Join(";", parts);
        }

        // Keeps a name from breaking the format above
        private static string Clean(string text) {
            return (text ?? "").Replace("|", "/").Replace(";", ",").Replace(":", "-");
        }

        private class Listed { public string hash, name; public int first = -1; }

        // ---- Players ----
        private static IntPtr answeredOn;     // the connection we last answered on
        private static int answeredValue;     // the beacon value we last answered
        private static float answerAt;        // when to send the pending answer (0 = none)
        private static int pendingValue;
        private static bool wasConnected;
        private static float nextClientCheck;
        private static float connectedAt;
        private static bool reportedRegister, reportedWaiting, reportedWaiting5;
        private static bool gotHostList;      // the host's list has arrived on this connection
        private static bool worldLoaded;      // this PC's puzzles were cleared for this connection (the world finished loading)
        private static int fallbackTries;
        private static float nextFallback;

        private static void ClientTick() {
            if (beacon == null || Time.unscaledTime < nextClientCheck) return;
            nextClientCheck = Time.unscaledTime + 0.25f;
            NetworkConnection conn = NetworkClient.active ? NetworkClient.connection : null;
            if (conn == null || !NetworkClient.isConnected) {
                // Left (or not joined yet): whatever the beacon says is from the old host
                if (wasConnected) { beacon.ForgetReceived(); answeredOn = IntPtr.Zero; answerAt = 0f; }
                wasConnected = false;
                return;
            }
            if (!wasConnected) {
                beacon.ForgetReceived();
                wasConnected = true; connectedAt = Time.unscaledTime; reportedRegister = false; reportedWaiting = false; reportedWaiting5 = false;
                gotHostList = false; fallbackTries = 0; nextFallback = 0f; worldLoaded = false;
                if (BigPuzzleEditor.instance != null) BigPuzzleEditor.instance.ForgetBuilt();
            }

            // The game rebuilds its id list when a lobby is joined, which may be after the puzzles
            // were (re)loaded here; a value that isn't in the current list never hears the host.
            // Checked all the time, so a rejoin without restarting the game works too.
            if (!beacon.IsRegistered()) {
                beacon.Register();
                if (!reportedRegister) { reportedRegister = true; Plugin.log.LogInfo("[SDK] the game's id list didn't have the host beacon (rebuilt after joining); registered it again."); }
            }

            int value;
            if (!beacon.TryGet(out value)) {   // the host hasn't said it has the SDK (yet)
                if (!reportedWaiting5 && Time.unscaledTime - connectedAt > 8f) {
                    reportedWaiting5 = true;
                    Plugin.log.LogInfo("[SDK] 8 s since connecting and still no word from the host: " + beacon.DebugText());
                }
                if (!reportedWaiting && Time.unscaledTime - connectedAt > 20f) {
                    reportedWaiting = true;
                    Plugin.log.LogWarning("[SDK] still no word from the host after 20 s: " + beacon.DebugText());
                }
                // Joining a second lobby in one game session: the beacon can stay deaf even though the
                // lobby itself says the host has the SDK. Then answer without it (the host replies to
                // any answer with its list), a few times until the list arrives.
                if (SdkJoinGate.JoiningSdkLobby && worldLoaded && !gotHostList && fallbackTries < 6 && NetworkClient.localPlayer != null
                        && Time.unscaledTime - connectedAt > 6f && Time.unscaledTime >= nextFallback) {
                    nextFallback = Time.unscaledTime + 5f;
                    fallbackTries++;
                    EnsureHandler(NetworkClient.handlers, ref clientHandler, OnHostList);
                    Send(conn, Describe());
                    Plugin.log.LogInfo($"[SDK] no beacon from the host, but the lobby has the SDK: told the host which puzzles this PC has anyway (try {fallbackTries}).");
                }
                return;
            }
            EnsureHandler(NetworkClient.handlers, ref clientHandler, OnHostList);   // for the host's reply
            if (conn.Pointer != answeredOn || value != answeredValue) {
                // Wait until the value has held still for a moment (a re-send briefly nudges it)
                if (answerAt == 0f || value != pendingValue) { pendingValue = value; answerAt = Time.unscaledTime + AnswerDelay; }
                if (Time.unscaledTime < answerAt) return;
                answerAt = 0f;
                answeredOn = conn.Pointer;
                answeredValue = value;
                Send(conn, Describe());
                Plugin.log.LogInfo($"[SDK] told the host which puzzles this PC has ({PuzzleLibrary.packs.Count})");
            }
        }

        // Host: has this player answered since the last load (so they're building the puzzles)?
        internal static bool HasAnswered(int connectionId) {
            return players.TryGetValue(connectionId, out Player player) && player.answer != null;
        }

        // Players: answer the host again (after this PC reloaded, so the reply rebuilds the puzzles)
        internal static void AnswerAgain() {
            answeredOn = IntPtr.Zero;
            answerAt = 0f;
            // The puzzles were just cleared (the world finished loading after the host's list had already
            // arrived): ask for the list again, also when the beacon is deaf
            // (not before: a list that arrives while the world is still loading would be built, and then
            // wiped by the world's own load, which stands seated players up and puts them back badly)
            worldLoaded = true; gotHostList = false; fallbackTries = 0; nextFallback = Time.unscaledTime + 1f;
        }

        // Players: the host's own list arrived (its reply to ours); show what doesn't match
        private static NetworkMessageDelegate clientHandler;
        private static void OnHostList(NetworkConnection conn, NetworkReader reader, int channelId) {
            try {
                string hostText = ReadText(reader);
                if (hostText == null) return;
                gotHostList = true;
                if (!Differences(hostText, Describe(), out List<string> problems, out List<string> lines)) {
                    Plugin.log.LogInfo("[SDK] this PC has the same SDK and puzzles as the host.");
                } else {
                    Plugin.log.LogWarning($"[SDK] this PC doesn't match the host: you {string.Join("; ", problems)}. Those puzzles won't be built here.");
                }
                // Build the host's puzzles (the ones this PC has in the same version)
                if (Parse(hostText, out string hostVersion, out var hostPuzzles) && BigPuzzleEditor.instance != null) {
                    var wanted = new List<KeyValuePair<string, int>>();
                    foreach (var puzzle in hostPuzzles)
                        wanted.Add(new KeyValuePair<string, int>(puzzle.Key + ":" + puzzle.Value.hash, puzzle.Value.first));
                    HostPuzzleQueue.key = hostText.Split('|')[2];
                    HostPuzzleQueue.puzzles = wanted;   // built on the next frame, outside network code
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[SDK] could not read the host's list: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void Send(NetworkConnection conn, string text) {
            try {
                byte[] payload = System.Text.Encoding.UTF8.GetBytes(text);
                var writer = new NetworkWriter();
                writer.WriteByte((byte)(MessageId & 0xFF));
                writer.WriteByte((byte)(MessageId >> 8));
                int length = payload.Length;
                for (int i = 0; i < 4; i++) writer.WriteByte((byte)(length >> (8 * i)));
                writer.WriteBytes((Il2CppStructArray<byte>)payload, 0, payload.Length);
                conn.Send(writer.ToArraySegment(), 0);   // channel 0: reliable
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[SDK] could not send the list of puzzles: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Reads a message written by Send; null if it doesn't look like one
        private static string ReadText(NetworkReader reader) {
            int length = 0;
            for (int i = 0; i < 4; i++) length |= reader.ReadByte() << (8 * i);
            if (length < 0 || length > 65536 || length > reader.Remaining) return null;
            var data = new Il2CppStructArray<byte>(length);
            reader.ReadBytes(data, length);
            byte[] bytes = new byte[length];
            for (int i = 0; i < length; i++) bytes[i] = data[i];
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        // ---- Host ----
        private class Player {
            public string name = "A player";
            public float since;     // joined (or last reload)
            public string answer;   // null until they answer
            public float nextBeacon;   // when to send the beacon again (until they answer)
            public int beaconsSent;
            public bool reported;
        }
        private static readonly Dictionary<int, Player> players = new Dictionary<int, Player>();
        private static NetworkMessageDelegate hostHandler;   // kept so it isn't collected
        private static float nextHostCheck;
        private static bool clashReported;

        private static void HostTick() {
            if (Time.unscaledTime < nextHostCheck) return;
            nextHostCheck = Time.unscaledTime + 0.5f;
            try {
                EnsureHandler(NetworkServer.handlers, ref hostHandler, OnMessage);
                var seen = new HashSet<int>();
                var local = NetworkServer.localConnection;
                foreach (var pair in NetworkServer.connections) {
                    NetworkConnectionToClient conn = pair.Value;
                    if (conn == null || (local != null && conn.Pointer == local.Pointer)) continue;
                    int cid = conn.connectionId;
                    seen.Add(cid);
                    if (!conn.isReady) continue;   // still loading in: the clock starts once they're in
                    if (!players.TryGetValue(cid, out Player player)) {
                        player = new Player { since = Time.unscaledTime, name = NameOf(conn) };
                        players[cid] = player;
                    }
                    // Until they answer, send the beacon again every second: a player only builds the
                    // puzzles once they've heard it and got the host's list back
                    if (player.answer == null && Time.unscaledTime >= player.nextBeacon && Time.unscaledTime - player.since > 2.5f) {
                        player.nextBeacon = Time.unscaledTime + 1f;
                        if (beacon != null) beacon.Resend();
                        player.beaconsSent++;
                        if (player.beaconsSent == 1) Plugin.log.LogInfo($"[SDK] {player.name} (connection {cid}) is in the game; sending them the SDK beacon until they answer.");
                        if (player.beaconsSent == 15) Plugin.log.LogWarning($"[SDK] {player.name} (connection {cid}): sent the beacon 15 times, no answer yet ({(beacon != null ? beacon.DebugText() : "no beacon")}).");
                    }
                    if (player.answer == null && !player.reported && Time.unscaledTime - player.since > AnswerWithin) {
                        player.reported = true;
                        Plugin.log.LogWarning($"[SDK] {player.name} (connection {cid}) hasn't answered: they probably don't have the Big Walk Puzzle SDK installed. Puzzles won't work for them.");
                    }
                }
                var gone = new List<int>();
                foreach (int cid in players.Keys) if (!seen.Contains(cid)) gone.Add(cid);
                foreach (int cid in gone) players.Remove(cid);
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[SDK] {ex.GetType().Name}: {ex.Message}");
                nextHostCheck = Time.unscaledTime + 5f;
            }
        }

        // Puts our message in the game's message table (host's or player's). The tables are
        // rebuilt when hosting or joining starts, so this is checked regularly.
        private static void EnsureHandler(Il2CppSystem.Collections.Generic.Dictionary<ushort, NetworkMessageDelegate> handlers,
                ref NetworkMessageDelegate handler, Action<NetworkConnection, NetworkReader, int> method) {
            if (handlers == null) return;
            if (handler == null)
                handler = DelegateSupport.ConvertDelegate<NetworkMessageDelegate>(method);
            if (handlers.ContainsKey(MessageId)) {
                if (handlers[MessageId].Pointer != handler.Pointer && !clashReported) {
                    clashReported = true;
                    Plugin.log.LogError($"[SDK] network message id {MessageId} is already used by the game; the SDK check is off.");
                }
                return;
            }
            handlers[MessageId] = handler;
        }

        // Host: a player's list arrived
        private static void OnMessage(NetworkConnection conn, NetworkReader reader, int channelId) {
            try {
                string text = ReadText(reader);
                if (text == null) return;

                int cid = conn.connectionId;
                if (!players.TryGetValue(cid, out Player player)) {
                    player = new Player { since = Time.unscaledTime, name = NameOf(conn) };
                    players[cid] = player;
                }
                bool late = player.answer == null && player.reported;   // warned before it arrived
                player.answer = text;
                player.reported = true;
                if (late) Plugin.log.LogInfo($"[SDK] {player.name} (connection {cid}) answered after all (ignore the warning above).");
                Compare(player, cid, text, late);
                // Reply with the host's own list, so the player sees what doesn't match too. Only
                // a player who answered gets this, so their game knows the message.
                if (text.StartsWith(Protocol + "|")) Send(conn, Describe());
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[SDK] could not read a player's answer: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Is version a older than b? ("1.0.0" style; anything unreadable counts as older)
        private static bool IsOlder(string a, string b) {
            if (!Version.TryParse(a, out Version va)) return true;
            if (!Version.TryParse(b, out Version vb)) return false;
            return va < vb;
        }

        // The name the player logged in with (their Steam/Epic name), if the game has it
        private static string NameOf(NetworkConnection conn) {
            try {
                NetworkConnectionToClient client = conn != null ? conn.TryCast<NetworkConnectionToClient>() : null;
                if (client != null) {
                    string name = NetworkConnectionToClientExtension.GetAuthedCredentials(client).displayName;
                    if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
                }
            } catch (Exception) { }
            return "A player";
        }

        // Logs (and shows the host) whether a player has the same SDK and puzzles as the host
        private static void Compare(Player player, int cid, string text, bool late) {
            if (!Differences(Describe(), text, out List<string> problems, out List<string> lines)) {
                Plugin.log.LogInfo($"[SDK] {player.name} (connection {cid}) has the same SDK and puzzles as the host.");
                return;
            }
            Plugin.log.LogWarning($"[SDK] {player.name} (connection {cid}) doesn't match the host: they {string.Join("; ", problems)}. Puzzles won't sync for them.");
        }

        // Compares a player's list with the host's. False if they match. Otherwise "problems"
        // (for the log, read as "they/you ...") and "lines" (for the popup), both from the
        // player's side: "missing" = the host has it, the player doesn't.
        private static bool Differences(string hostText, string playerText, out List<string> problems, out List<string> lines) {
            problems = new List<string>();
            lines = new List<string>();
            if (!Parse(hostText, out string hostVersion, out var hostPuzzles) || !Parse(playerText, out string playerVersion, out var playerPuzzles)) {
                problems.Add("have an SDK that can't be compared");
                lines.Add("SDK (incompatible)");
                return true;
            }
            if (playerVersion != hostVersion) {
                problems.Add($"have SDK {playerVersion} (the host has {hostVersion})");
                lines.Add($"SDK {playerVersion} ({(IsOlder(playerVersion, hostVersion) ? "outdated" : "newer")})");
            }
            foreach (var puzzle in hostPuzzles) {
                string name = puzzle.Value.name;
                if (!playerPuzzles.TryGetValue(puzzle.Key, out Listed theirs)) {
                    problems.Add($"don't have '{name}'");
                    lines.Add($"{name} (missing)");
                } else if (!string.Equals(theirs.hash, puzzle.Value.hash, StringComparison.OrdinalIgnoreCase)) {
                    problems.Add($"have a different version of '{name}'");
                    lines.Add($"{name} (incorrect version)");
                }
            }
            // Puzzles only the player has don't matter: they aren't built in the host's game
            return problems.Count > 0;
        }

        // Reads what Describe wrote: puzzle id -> fingerprint and name
        private static bool Parse(string text, out string version, out Dictionary<string, Listed> puzzles) {
            version = null;
            puzzles = null;
            string[] fields = (text ?? "").Split('|');
            if (fields.Length < 3 || fields[0] != Protocol) return false;
            version = fields[1];
            puzzles = new Dictionary<string, Listed>(StringComparer.Ordinal);
            foreach (string entry in fields[2].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) {
                string[] parts = entry.Split(new[] { ':' }, 4);
                if (parts.Length < 4) continue;
                if (!int.TryParse(parts[2], out int first)) first = -1;
                puzzles[parts[0]] = new Listed { hash = parts[1], first = first, name = parts[3] != "" ? parts[3] : parts[0] };
            }
            return true;
        }
    }

    // Other players: the host's list of puzzles, waiting to be built by BigPuzzleEditor
    internal static class HostPuzzleQueue {
        internal static string key;   // identifies the list (puzzles and their first ids)
        internal static List<KeyValuePair<string, int>> puzzles;   // "<id>:<fingerprint>" -> first network id
    }

    // ----------------------------------------------------------------------
    // F2 for everyone: the host's reload is announced through one synced value that outlives
    // reloads. Its network id is always the first one handed out in a load, so it is the same on
    // every PC; later loads skip that id. It is never re-sent to players who join later.
    // ----------------------------------------------------------------------
    internal static class ReloadChannel {
        private static SyncedValue channel;
        private static int count;
        internal static Action onReload;   // other players: the host reloaded

        // Called by the loader right after the id allocator restarts, before anything else
        internal static void Prepare() {
            if (channel != null) {
                Tickets.Allocate(1);   // keep its id out of the puzzle's
                channel.Register();    // each lobby has its own id registry: register again
                return;
            }
            Transform parent = BigPuzzleEditor.instance != null ? BigPuzzleEditor.instance.transform : null;
            if (parent == null) { Tickets.Allocate(1); return; }
            channel = new SyncedValue(parent, "reload", oneShot: true, persistent: true);
            channel.onChanged = v => { if (onReload != null) onReload(); };
            channel.Register();
        }

        // Host: tell everyone a reload just happened
        internal static void Announce() {
            if (channel == null || !SyncedValue.IsHost) return;
            count = count % 100 + 1;
            channel.Push(count);
        }
    }

    // ----------------------------------------------------------------------
    // Every gourd the SDK spawns is teal, so modded gourds are never mistaken for the game's own.
    // A gourd's colours are not on its material: a PropertyBlockHelper on the gourd applies its
    // own list of colour settings to the renderer. Those settings are changed (every colour teal,
    // any emission off) and re-applied. Done on every PC, so nothing needs syncing.
    // ----------------------------------------------------------------------
    internal static class ModGourd {
        internal static readonly Color Teal = new Color(0.15f, 0.75f, 0.7f, 1f);

        // Modded gourds and modded holders are kept apart from the game's own: a vanilla gourd
        // never goes into a modded holder (its saved location would point at a modded id, gone
        // next load) and a modded gourd never goes into a vanilla holder like the tower (modded
        // rewards stay out of real progress). Groups could not do this (the game's holders also
        // check the gourd's usual groups), so placing is blocked where the game pins a prop.
        private static readonly HashSet<IntPtr> moddedProps = new HashSet<IntPtr>();
        private static readonly HashSet<IntPtr> moddedHomes = new HashSet<IntPtr>();

        // Pegs and peg boards are the exception: modded pegs also fit the game's own boards, and the
        // game's own pegs fit modded boards (these are also in the sets above, so they are still
        // recognised as ours). Gourds stay apart.
        private static readonly HashSet<IntPtr> crossProps = new HashSet<IntPtr>();
        private static readonly HashSet<IntPtr> crossHomes = new HashSet<IntPtr>();

        internal static void MakeCrossProp(GameObject go) {
            Prop prop = go != null ? go.GetComponent<Prop>() : null;
            if (prop != null) { moddedProps.Add(prop.Pointer); crossProps.Add(prop.Pointer); }
        }

        internal static void MakeCrossHome(PropHome home) {
            if (home != null) { moddedHomes.Add(home.Pointer); crossHomes.Add(home.Pointer); }
        }

        internal static void MakeModdedProp(GameObject go) {
            Prop prop = go != null ? go.GetComponent<Prop>() : null;
            if (prop != null) moddedProps.Add(prop.Pointer);
        }

        internal static void MakeModdedHome(PropHome home) {
            if (home != null) moddedHomes.Add(home.Pointer);
        }

        internal static void InstallHooks(Harmony harmony) {
            // The game asks a holder "can this prop go here?" before it offers to place it. Saying
            // no there means the place option never appears: the player keeps holding the gourd,
            // just as with any prop that does not fit.
            try {
                MethodInfo target = AccessTools.Method(typeof(PropHome), GameRefs.Hooks.IsSafeToPlace);
                if (target == null) throw new MissingMethodException("PropHome.IsSafeToPlace not found");
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(ModGourd).GetMethod(nameof(SafeToPlacePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Gourd] could not hook PropHome.IsSafeToPlace ({ex.GetType().Name}: {ex.Message}); a refused gourd is dropped instead");
            }
            // What actually offers the place option: the holder's "castable outcome" (what happens
            // when a player points at it while holding something). It names the holder and the
            // group the held prop needs; this adds "and modded fits modded only".
            try {
                MethodInfo target = AccessTools.Method(typeof(CastableOutcome), GameRefs.Hooks.MatchesConditions);
                if (target == null) throw new MissingMethodException("CastableOutcome.MatchesConditions not found");
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(ModGourd).GetMethod(nameof(OutcomePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Gourd] could not hook CastableOutcome.MatchesConditions ({ex.GetType().Name}: {ex.Message}); a refused gourd is dropped instead");
            }

            // Safety net, in case a placement does not go through that check
            Block(harmony, GameRefs.Hooks.ServerSetPinned);          // the host's decision
            Block(harmony, GameRefs.Hooks.PredictivelyPlaceInHome);  // a player's own prediction, so it does not snap in for a moment
        }

        private static void OutcomePostfix(CastableOutcome __instance, PlayerCharacter playerCharacter, ref bool __result) {
            if (!__result || __instance == null || playerCharacter == null) return;
            if (moddedProps.Count == 0 && moddedHomes.Count == 0) return;
            try {
                PropHome home = __instance.propHome;
                if (home == null) return;
                PlayerHands hands = playerCharacter.hands;
                Prop held = hands != null ? hands.heldProp : null;
                if (held != null && !Allowed(held, home)) __result = false;
            } catch (Exception) { }
        }

        private static void SafeToPlacePostfix(PropHome __instance, Prop prop, ref bool __result) {
            if (__result && !Allowed(prop, __instance)) __result = false;
        }

        private static bool Allowed(Prop prop, PropHome home) {
            if (prop == null || home == null) return true;
            if (moddedProps.Count == 0 && moddedHomes.Count == 0) return true;
            // A player's belt, backpack and head (the blindfold) take modded items too (not gourds, which are kept with their own holders)
            try {
                bool pocket = home.isInventory || home.name.StartsWith("GoesIn") || home.pinGroup.ToString().StartsWith("GoesIn")
                    || home.pinGroup == PropGroup.Blindfold || home.parentCharacter != null;
                if (pocket && prop.GetComponent<RewardGourd>() == null) return true;
            } catch (Exception) { }
            bool propIsModded = moddedProps.Contains(prop.Pointer), homeIsModded = moddedHomes.Contains(home.Pointer);
            if (propIsModded == homeIsModded) return true;
            // the pegs' exception: a modded peg in one of the game's boards, or one of the game's pegs in a modded board
            if (propIsModded && crossProps.Contains(prop.Pointer)) return true;
            if (homeIsModded && crossHomes.Contains(home.Pointer)) return true;
            return false;
        }

        private static void Block(Harmony harmony, string method) {
            try {
                MethodInfo target = AccessTools.Method(typeof(Prop), method);
                if (target == null) throw new MissingMethodException("Prop." + method + " not found");
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(ModGourd).GetMethod(nameof(AllowPin), BindingFlags.Static | BindingFlags.NonPublic)));
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Gourd] could not hook Prop.{method} ({ex.GetType().Name}: {ex.Message}); modded and vanilla gourds are not kept apart");
            }
        }

        // Harmony prefix: false skips the game's pinning
        private static bool AllowPin(Prop __instance, PropHome propHome) {
            return Allowed(__instance, propHome);
        }

        // Called on every load: the old puzzle's objects are gone
        internal static void Clear() { moddedProps.Clear(); moddedHomes.Clear(); crossProps.Clear(); crossHomes.Clear(); }

        // Is this one of the SDK's gourds (from any loaded puzzle)?
        internal static bool IsModded(GameObject go) {
            Prop prop = go != null ? go.GetComponent<Prop>() : null;
            return prop != null && moddedProps.Contains(prop.Pointer);
        }

        internal static void Tint(GameObject go) {
            if (go == null) return;
            foreach (Component c in go.GetComponentsInChildren(Il2CppType.Of<PropertyBlockHelper>(), true)) {
                PropertyBlockHelper helper = c != null ? c.TryCast<PropertyBlockHelper>() : null;
                if (helper == null) continue;
                try {
                    var colors = helper.colorSettings;
                    for (int i = 0; colors != null && i < colors.Count; i++) {
                        ColorSetting cs = colors[i];
                        cs.color = (cs.propertyName ?? "").ToLowerInvariant().Contains("emission") ? Color.black : Teal;
                        colors[i] = cs;
                    }
                    var floats = helper.floatSettings;
                    for (int i = 0; floats != null && i < floats.Count; i++) {
                        FloatSetting fs = floats[i];
                        if (!(fs.propertyName ?? "").ToLowerInvariant().Contains("emission")) continue;
                        fs.floatValue = 0f;
                        floats[i] = fs;
                    }
                    helper.Refresh();
                } catch (Exception ex) {
                    Plugin.log.LogWarning($"[Gourd] could not colour '{helper.name}': {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }

    internal static class NetItems {
        private static readonly float[] ResendAfter = { 3f, 6f, 10f, 16f, 25f, 40f, 60f };

        private static readonly Dictionary<int, ulong> idByObject = new Dictionary<int, ulong>();
        private static readonly HashSet<ulong> usedIds = new HashSet<ulong>();
        private static readonly List<NetworkIdentity> spawned = new List<NetworkIdentity>();
        private static readonly Dictionary<int, float> joinedAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, int> resendsDone = new Dictionary<int, int>();
        private static float nextScan;

        // Host: before a reload, remove the old puzzle's items for every player (not just here),
        // and give everyone connected the "just joined" re-sends again, since their own reload
        // happens a moment after the host's and misses the first spawn messages and values
        internal static void UnspawnAll() {
            if (!NetworkServer.active) return;
            foreach (NetworkIdentity identity in spawned) {
                if (identity == null) continue;
                try { NetworkServer.Destroy(identity.gameObject); }
                catch (Exception ex) { Plugin.log.LogWarning($"[Items] could not remove '{identity.name}': {ex.GetType().Name}: {ex.Message}"); }
            }
            spawned.Clear();
            foreach (int cid in new List<int>(joinedAt.Keys)) { joinedAt[cid] = Time.unscaledTime; resendsDone[cid] = 0; }
        }

        internal static void BeginLoad() {
            ModGourd.Clear();
            SoundPegAudio.Clear();
            SyncedValue.Clear();
            idByObject.Clear();
            usedIds.Clear();
            spawned.Clear();
        }

        // A fixed, non-zero id for an item, from its place in the puzzle and a slot name
        internal static ulong IdFor(Transform owner, string slot) {
            ulong hash = 14695981039346656037UL;   // FNV-1a
            foreach (char ch in Templates.FullPath(owner) + "#" + slot) {
                hash ^= ch;
                hash *= 1099511628211UL;
            }
            hash = (hash & 0x00FFFFFFFFFFFFFFUL) | 0xB100000000000000UL;   // keep clear of the game's own ids
            while (!usedIds.Add(hash)) hash++;                              // same path twice: next one
            return hash;
        }

        // Gives a sleeping item its id (on every PC). On a joining player's PC it is also
        // registered, so the host's spawn message can find it. Safe to call again.
        internal static void Prepare(GameObject go, ulong id) {
            if (go == null) return;
            NetworkIdentity identity = go.GetComponent<NetworkIdentity>();
            if (identity == null) { Plugin.log.LogWarning($"[Items] '{go.name}' has no network identity"); return; }
            idByObject[go.GetInstanceID()] = id;
            identity.sceneId = id;
            if (NetworkServer.active || !NetworkClient.active) return;
            try {
                NetworkClient.spawnableObjects[id] = identity;
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Items] could not register '{go.name}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Host only: wakes the item and spawns it for every player
        internal static bool Spawn(GameObject go) {
            var log = Plugin.log;
            if (go == null || !NetworkServer.active) return false;
            NetworkIdentity identity = go.GetComponent<NetworkIdentity>();
            if (identity == null) return false;
            if (idByObject.TryGetValue(go.GetInstanceID(), out ulong id)) identity.sceneId = id;
            else log.LogWarning($"[Items] '{go.name}' has no fixed id; other players will not get it");
            go.SetActive(true);
            try {
                NetworkServer.Spawn(go, (NetworkConnection)null);
            } catch (Exception ex) {
                log.LogWarning($"[Items] could not spawn '{go.name}': {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            spawned.Add(identity);
            return true;
        }

        // Host: send one item's current place and state to every other player right now
        internal static void ResendNow(GameObject go) {
            if (go == null || !NetworkServer.active) return;
            NetworkIdentity identity = go.GetComponent<NetworkIdentity>();
            if (identity == null || identity.netId == 0) return;
            try {
                var local = NetworkServer.localConnection;
                foreach (var pair in NetworkServer.connections) {
                    NetworkConnectionToClient conn = pair.Value;
                    if (conn == null || !conn.isReady || (local != null && conn.Pointer == local.Pointer)) continue;
                    NetworkServer.SendSpawnMessage(identity, conn);
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Items] could not resend '{go.name}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Host: resend the items' spawn messages to players who joined recently
        internal static void Tick() {
            if (!NetworkServer.active || Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;
            spawned.RemoveAll(i => i == null);
            try {
                var seen = new HashSet<int>();
                var local = NetworkServer.localConnection;
                foreach (var pair in NetworkServer.connections) {
                    NetworkConnectionToClient conn = pair.Value;
                    if (conn == null || (local != null && conn.Pointer == local.Pointer)) continue;
                    int cid = conn.connectionId;
                    seen.Add(cid);
                    if (!conn.isReady) continue;
                    // Only once the player has answered the SDK check: before that they have no
                    // puzzles built (a player without the SDK never answers, and gets nothing)
                    if (!SdkHandshake.HasAnswered(cid)) { joinedAt.Remove(cid); resendsDone.Remove(cid); continue; }
                    if (!joinedAt.ContainsKey(cid)) { joinedAt[cid] = Time.unscaledTime; resendsDone[cid] = 0; }
                    int done = resendsDone[cid];
                    if (done >= ResendAfter.Length || Time.unscaledTime - joinedAt[cid] < ResendAfter[done]) continue;
                    resendsDone[cid] = done + 1;
                    SyncedValue.ResendAll();   // values set before this player joined
                    foreach (NetworkIdentity identity in spawned) {
                        if (identity.netId == 0) continue;
                        try { NetworkServer.SendSpawnMessage(identity, conn); }
                        catch (Exception ex) { Plugin.log.LogWarning($"[Items] resend failed: {ex.GetType().Name}: {ex.Message}"); }
                    }
                    HeldJoinFix.NudgeHolders();   // the held item exists there now: send who holds it again
                }
                // Players who left: forget them, so a rejoin starts the rounds again
                var gone = new List<int>();
                foreach (int cid in joinedAt.Keys) if (!seen.Contains(cid)) gone.Add(cid);
                foreach (int cid in gone) { joinedAt.Remove(cid); resendsDone.Remove(cid); }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Items] {ex.GetType().Name}: {ex.Message}");
                nextScan = Time.unscaledTime + 5f;
            }
        }
    }

    // Network ids for everything the SDK adds, handed out in one sequence from 1000: the SDK's
    // own first (the reload channel), then each puzzle in file-name order. Every PC must load the
    // same puzzles with the same DLL so the sequence matches. The range is kept low because the
    // game's own ids (which are skipped) are spread over the rest of the range, and which of them
    // a PC knows about can differ between PCs; skipping different ids would shift every id after.
    // After the puzzles are rebuilt (Ctrl+F2), the old puzzles' objects are gone, but the game's
    // own lists of props, prop holders and network ids may still point at them: they reuse the same
    // network ids as the new copies, and the game then finds a destroyed object (picking up a
    // reloaded timer ball failed this way). Drops the destroyed entries, then has the copies put
    // their own entries back.
    internal static class RegistryCleanup {
        internal static void Run() {
            int tickets = 0, props = 0, homes = 0;
            try {
                var office = LobbyNetworking.TicketOffice.instance;
                if (office != null) {
                    var dead = new List<ushort>();
                    foreach (var pair in office.tickets) {
                        UnityEngine.Object obj = null;
                        try { obj = pair.Value != null ? pair.Value.TryCast<UnityEngine.Object>() : null; } catch (Exception) { }
                        if (pair.Value == null || (obj is not null && obj == null)) dead.Add(pair.Key);
                    }
                    foreach (ushort key in dead) office.tickets.Remove(key);
                    tickets = dead.Count;
                }
            } catch (Exception ex) { Plugin.log.LogWarning($"[Puzzle] could not tidy the network id list: {ex.GetType().Name}: {ex.Message}"); }
            try {
                var list = Prop.allProps;
                for (int i = list != null ? list.Count - 1 : -1; i >= 0; i--)
                    if (list[i] == null) { list.RemoveAt(i); props++; }
            } catch (Exception ex) { Plugin.log.LogWarning($"[Puzzle] could not tidy the prop list: {ex.GetType().Name}: {ex.Message}"); }
            try {
                var list = PropHome.allPropHomes;
                for (int i = list != null ? list.Count - 1 : -1; i >= 0; i--)
                    if (list[i] == null) { list.RemoveAt(i); homes++; }
            } catch (Exception ex) { Plugin.log.LogWarning($"[Puzzle] could not tidy the holder list: {ex.GetType().Name}: {ex.Message}"); }
            PuzzleVanillaCopy.RegisterAgain();
        }
    }

    internal static class Tickets {
        private const int First = 1000;   // never hand out the very low ids
        private static HashSet<ushort> vanilla;
        private static int next = First;
        private static int skipped;
        private static string current = "the SDK";

        // Called once at the start of every load of the puzzles
        internal static void BeginLoad() {
            next = First;
            highest = First - 1;
            skipped = 0;
            current = "the SDK";
            if (vanilla == null) vanilla = CollectVanilla();
        }

        // Called at the start of each puzzle. The host carries on from the previous puzzle and
        // returns where this one starts; another player starts exactly where the host's did
        // (startAt, from the host's list), so a puzzle they don't have can't shift the others.
        internal static int BeginPuzzle(string puzzleName, int startAt = -1) {
            current = "'" + puzzleName + "'";
            if (startAt >= First) next = startAt;
            return next;
        }

        // For the log after loading: compare this line between PCs if things don't sync
        internal static string Summary() {
            return $"network ids {First}-{highest}" + (skipped > 0 ? $", {skipped} skipped as the game's own (if this differs between PCs, nothing will sync)" : "");
        }
        private static int highest = First - 1;

        internal static List<ushort> Allocate(int count, string owner = null, [CallerFilePath] string file = "") {
            var ids = new List<ushort>();
            if (vanilla == null) vanilla = CollectVanilla();
            while (ids.Count < count) {
                if (next > ushort.MaxValue) {
                    Plugin.log.LogError($"[Tickets] Ran out of network ids while loading {current}; the rest won't sync.");
                    break;
                }
                ushort id = (ushort)next++;
                if (!vanilla.Contains(id)) { ids.Add(id); if (id > highest) highest = id; }
                else skipped++;
            }
            return ids;
        }

        // Every id a vanilla object in the loaded world uses, active or not
        private static HashSet<ushort> CollectVanilla() {
            var used = new HashSet<ushort>();
            foreach (Il2CppSystem.Type type in new[] { Il2CppType.Of<PeckSwitch>(), Il2CppType.Of<TrackedPeckState>(), Il2CppType.Of<PropHome>() }) {
                foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(type)) {
                    try {
                        var ticketed = obj.TryCast<LobbyNetworking.ITicketed>();
                        if (ticketed != null) used.Add(ticketed.ticket);
                    } catch (Exception) { }
                }
            }
            try {
                var office = LobbyNetworking.TicketOffice.instance;
                if (office != null) foreach (var pair in office.tickets) used.Add(pair.Key);
            } catch (Exception) { }
            return used;
        }
    }

    // ----------------------------------------------------------------------
    // Every installed puzzle, read into memory once at start-up (and again on F2): the file's
    // fingerprint, its puzzle.json, and its asset bundle. Building a
    // puzzle in a world then only copies what's already here. The asset bundle is the one part
    // Unity can only open from a file, so it's written to BepInEx/cache/BigPuzzleSDK under the
    // pack's fingerprint (reused across launches), and opened only while its puzzle is built.
    // ----------------------------------------------------------------------
    public class PuzzlePack {
        public string path;        // where the .puzzlepack was found
        public string fileName;    // its name without .puzzlepack (for messages only; renaming is fine)
        public string id;          // the puzzle's permanent id from puzzle.json: what tells puzzles apart
        public string name;        // the puzzle's name (its root object), for messages
        public int firstTicket = -1;   // host: where this puzzle's network ids start (-1 = not built)
        public string hash;        // SHA-256 of the whole file (for the multiplayer check)
        public bool dev;           // found in BepInEx/plugins/BigPuzzleSDK: a puzzle being tested (creator keys work on it)
        public PuzzlePackData data;
        public string bundlePath;
    }

    internal static class PuzzleLibrary {
        internal static readonly List<PuzzlePack> packs = new List<PuzzlePack>();

        private static string CacheDir { get { return Path.Combine(Paths.CachePath, "BigPuzzleSDK"); } }

        private static bool reportedDev;
        internal static bool HasDevPuzzles { get { return packs.Exists(p => p.dev); } }

        // Is the puzzle with this id one being tested (see Plugin.DevFolder)?
        internal static bool IsDevPuzzle(string id) { return id != null && packs.Exists(p => p.dev && p.id == id); }

        private static bool IsInDevFolder(string path) {
            try {
                string root = Path.GetFullPath(Plugin.DevFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
            } catch (Exception) { return false; }
        }

        // Reads every installed pack. An unchanged pack (same fingerprint) keeps what was read
        // before; changed packs are read again.
        internal static void Scan() {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var previous = new Dictionary<string, PuzzlePack>();
            foreach (PuzzlePack old in packs) previous[old.hash] = old;
            packs.Clear();

            foreach (string path in BigPuzzleEditor.FindPuzzlePacks()) {
                try {
                    byte[] bytes = File.ReadAllBytes(path);
                    string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                    if (previous.TryGetValue(hash, out PuzzlePack same)) {
                        previous.Remove(hash);
                        same.path = path;
                        same.dev = IsInDevFolder(path);
                        same.fileName = Path.GetFileNameWithoutExtension(path);
                        packs.Add(same);
                        continue;
                    }
                    PuzzlePack pack = Read(path, bytes, hash);
                    if (pack != null) { pack.dev = IsInDevFolder(path); packs.Add(pack); }
                } catch (Exception ex) {
                    Plugin.log.LogError($"[Puzzle] Could not read {Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // The same puzzle installed twice (same id): keep one. Then order by id, so every PC
            // loads the same puzzles in the same order whatever their files are called.
            var byId = new Dictionary<string, PuzzlePack>();
            foreach (PuzzlePack pack in packs.ToArray()) {
                if (byId.TryGetValue(pack.id, out PuzzlePack first)) {
                    // A puzzle being tested wins over an installed copy of it, quietly
                    PuzzlePack keep = pack.dev && !first.dev ? pack : first;
                    PuzzlePack drop = keep == pack ? first : pack;
                    if (!(keep.dev && !drop.dev))
                        Plugin.log.LogWarning($"[Puzzle] '{pack.name}' is installed more than once; only loading {keep.path}. Skipped: {drop.path}");
                    packs.Remove(drop);
                    byId[pack.id] = keep;
                    continue;
                }
                byId[pack.id] = pack;
            }
            packs.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

            ChooseTower(previous);
            packs.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

            CleanCache();
            Plugin.log.LogInfo($"[Puzzle] Read {packs.Count} puzzle pack(s) in {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
            if (!reportedDev && HasDevPuzzles) {
                reportedDev = true;
                Plugin.log.LogInfo("[Puzzle] Creator keys on (a puzzle is in the BigPuzzleSDK folder): Ctrl+F1 teleports to it, Ctrl+F2 reloads.");
            }
        }

        // ---- The gourd tower: exactly one ----
        // Installed packs with Puzzle Type = Tower come first: only one is loaded (the first by id),
        // with a warning if there are more. With none installed, the default tower built into this
        // DLL (embedded resource BuiltInTowerResource) is loaded instead.
        private const string BuiltInTowerResource = "BigPuzzleSDK.DefaultTower.puzzlepack";
        internal const string BuiltInTowerPath = "(built into BigPuzzleSDK.dll)";

        // The built-in tower, read even when an installed tower is used instead: a host may be using
        // it while this PC has another tower installed (see FindHostPack)
        internal static PuzzlePack builtInTower;

        private static void ChooseTower(Dictionary<string, PuzzlePack> previous) {
            builtInTower = ReadBuiltInTower(previous);

            var towers = packs.FindAll(p => TypeOf(p) == PuzzleType.Tower);
            if (towers.Count > 0) {
                for (int i = 1; i < towers.Count; i++) {
                    Plugin.log.LogWarning($"[Puzzle] Only one gourd tower can be loaded: using '{towers[0].name}' ({towers[0].path}), skipping '{towers[i].name}' ({towers[i].path}).");
                    packs.Remove(towers[i]);
                }
                return;
            }
            if (builtInTower == null) return;
            if (packs.Exists(p => p.id == builtInTower.id)) {
                Plugin.log.LogWarning("[Puzzle] A pack with the built-in tower's id is installed but isn't a Tower; not loading the built-in tower.");
                return;
            }
            packs.Add(builtInTower);
            Plugin.log.LogInfo("[Puzzle] No gourd tower installed: using the built-in one.");
        }

        private static PuzzlePack ReadBuiltInTower(Dictionary<string, PuzzlePack> previous) {
            try {
                byte[] bytes;
                using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(BuiltInTowerResource)) {
                    if (input == null) return null;   // this build has no default tower
                    using (var buffer = new MemoryStream()) { input.CopyTo(buffer); bytes = buffer.ToArray(); }
                }
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                PuzzlePack tower;
                if (builtInTower != null && builtInTower.hash == hash) tower = builtInTower;   // unchanged since the last scan
                else if (previous.TryGetValue(hash, out PuzzlePack same)) { previous.Remove(hash); tower = same; }
                else tower = Read(BuiltInTowerPath, bytes, hash);
                if (tower == null) return null;
                tower.path = BuiltInTowerPath;
                tower.fileName = "DefaultTower";
                if (TypeOf(tower) != PuzzleType.Tower)
                    Plugin.log.LogWarning("[Puzzle] The built-in tower's PuzzleSDK isn't set to Puzzle Type Tower.");
                return tower;
            } catch (Exception ex) {
                Plugin.log.LogError($"[Puzzle] Could not read the built-in tower: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        // Other players: the pack the host's list names ("<id>:<fingerprint>"), including the
        // built-in tower when this PC loads an installed tower instead
        internal static PuzzlePack FindHostPack(string key) {
            PuzzlePack pack = packs.Find(p => p.id + ":" + p.hash == key);
            if (pack == null && builtInTower != null && builtInTower.id + ":" + builtInTower.hash == key) pack = builtInTower;
            return pack;
        }

        // The Puzzle Type set on a pack's PuzzleSDK (Puzzle when it has none)
        internal static PuzzleType TypeOf(PuzzlePack pack) {
            if (pack?.data?.scripts == null) return PuzzleType.Puzzle;
            foreach (PuzzleScriptData script in pack.data.scripts) {
                string type = script.typeName ?? "";
                if (!(type == "BigPuzzleSDK" || type == "PuzzleSDK" || type.EndsWith(".PuzzleSDK"))) continue;
                if (script.fields == null) return PuzzleType.Puzzle;
                foreach (PuzzleFieldData field in script.fields)
                    if (field.name == "puzzleType" && int.TryParse(field.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                        && Enum.IsDefined(typeof(PuzzleType), value))
                        return (PuzzleType)value;
                return PuzzleType.Puzzle;
            }
            return PuzzleType.Puzzle;
        }

        private static PuzzlePack Read(string path, byte[] bytes, string hash) {
            var pack = new PuzzlePack {
                path = path,
                fileName = Path.GetFileNameWithoutExtension(path),
                hash = hash,
                bundlePath = Path.Combine(CacheDir, hash + ".bundle"),
            };
            bool hasBundle = false;
            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read)) {
                foreach (ZipArchiveEntry entry in zip.Entries) {
                    string name = entry.FullName.Replace('\\', '/');
                    if (name == "puzzle.bundle") {
                        hasBundle = true;
                        if (!File.Exists(pack.bundlePath) || new FileInfo(pack.bundlePath).Length != entry.Length) {
                            Directory.CreateDirectory(CacheDir);
                            using (Stream input = entry.Open())
                            using (FileStream output = File.Create(pack.bundlePath)) input.CopyTo(output);
                        }
                    } else if (name == "puzzle.json") {
                        using (var reader = new StreamReader(entry.Open())) pack.data = JsonSerializer.Deserialize<PuzzlePackData>(reader.ReadToEnd());
                    }
                }
            }
            if (!hasBundle) {
                Plugin.log.LogError($"[Puzzle] Invalid puzzle pack {Path.GetFileName(path)}: puzzle.bundle is missing (was it exported with the old exporter?).");
                return null;
            }
            // Which exporter made it: a newer one than this mod knows may use things it can't build
            int format = pack.data != null ? pack.data.formatVersion : 0;
            if (format > PuzzlePackData.SupportedFormat) {
                Plugin.log.LogError($"[Puzzle] {Path.GetFileName(path)} was exported with a newer SDK (format {format}; this mod reads up to {PuzzlePackData.SupportedFormat}). Update BigPuzzleSDK to load it.");
                return null;
            }
            if (format < PuzzlePackData.SupportedFormat)
                Plugin.log.LogWarning($"[Puzzle] {Path.GetFileName(path)} was exported with an older SDK (format {format}); some settings or event calls may be missing. Re-export it with the current SDK.");
            pack.name = pack.data != null && !string.IsNullOrEmpty(pack.data.rootName) ? pack.data.rootName : pack.fileName;
            if (pack.data != null && !string.IsNullOrEmpty(pack.data.puzzleId)) {
                pack.id = pack.data.puzzleId;
            } else {
                // Exported before puzzles had ids: fall back on the file name
                pack.id = "file:" + pack.fileName.ToLowerInvariant();
                Plugin.log.LogWarning($"[Puzzle] {Path.GetFileName(path)} has no puzzle id (exported with an older exporter). Re-export it, or renaming the file will break multiplayer.");
            }
            return pack;
        }

        // Opens a pack's asset bundle for building its puzzle; the caller closes it again. Bundles
        // can't stay open: every exported bundle has the same internal name, and Unity refuses
        // to open a second one with the same name while the first is open.
        internal static UnityEngine.AssetBundle OpenBundle(PuzzlePack pack) {
            UnityEngine.AssetBundle bundle = BundleAccess.LoadBundle(pack.bundlePath);
            if (bundle == null) Plugin.log.LogError($"[Puzzle] Could not open the asset bundle of '{pack.name}' ({pack.fileName}).");
            return bundle;
        }

        // Removes cached bundles no installed pack uses any more
        private static void CleanCache() {
            try {
                if (!Directory.Exists(CacheDir)) return;
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (PuzzlePack pack in packs) keep.Add(Path.GetFileName(pack.bundlePath));
                if (builtInTower != null) keep.Add(Path.GetFileName(builtInTower.bundlePath));
                foreach (string file in Directory.GetFiles(CacheDir))
                    if (!keep.Contains(Path.GetFileName(file))) {
                        try { File.Delete(file); } catch (Exception) { }   // still open: next time
                    }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Puzzle] Could not tidy the bundle cache: {ex.Message}");
            }
        }
    }

    public class PuzzleLoader {
        // Runs one component type's setup on every instance of it, numbering them 0, 1, 2...
        // Objects a PuzzleHider switched off for this lobby size are skipped (nothing is copied
        // or spawned for them). Every player is in the same lobby, so every PC skips the same ones.
        private static void Setup<T>(BuildContext ctx, Action<T, int> setup) where T : class {
            int index = 0;
            foreach (PendingScript p in ctx.pending) {
                if (!(p.instance is T item)) continue;
                if (item is Component c && PuzzleHider.IsHidden(c.transform)) continue;
                setup(item, index++);
            }
        }

        // Links left empty are filled in when there is exactly one possible target in the puzzle (the Inspector shows
        // the same links with a green tick):
        //   Count Validator -> Count Input, Peg Board -> Validator, Peg Board -> Answer From (the only display with as many flaps as it has slots)
        private static void AutoLink(BuildContext ctx) {
            var inputs = new List<PuzzleCountInput>();
            var validators = new List<PuzzleValidator>();
            var boards = new List<PuzzlePegBoard>();
            var displays = new List<PuzzleDisplay>();
            foreach (PendingScript p in ctx.pending) {
                if (p.instance is PuzzleCountInput i) inputs.Add(i);
                else if (p.instance is PuzzleValidator v) validators.Add(v);
                else if (p.instance is PuzzlePegBoard b) boards.Add(b);
                else if (p.instance is PuzzleDisplay d) displays.Add(d);
            }
            // The only Peg Board's Answer From: the only display with as many flaps as the board has slots
            if (boards.Count == 1 && boards[0].AnswerFromEmpty) {
                PuzzleDisplay match = null;
                int matches = 0;
                foreach (PuzzleDisplay d in displays) if (d.ConfiguredFlaps == boards[0].ConfiguredSlots) { match = d; matches++; }
                if (matches == 1) boards[0].AutoLinkAnswers(match);
            }
            foreach (PendingScript p in ctx.pending) {
                if (p.instance is PuzzleCountValidator cv && inputs.Count == 1) cv.AutoLink(inputs[0]);
                else if (p.instance is PuzzlePegBoard board && validators.Count == 1) board.AutoLink(validators[0]);
            }
        }

        // State shared while one package is being built
        private class BuildContext {
            public Transform root;
            public UnityEngine.AssetBundle bundle; // still open while fields are assigned, for textures
            public readonly List<PendingScript> pending = new List<PendingScript>();
            // GameObject instance id -> scripts added to it, in export order
            public readonly Dictionary<int, List<LoadedScript>> scriptsByObject = new Dictionary<int, List<LoadedScript>>();
        }

        private class PendingScript {
            public object instance;
            public PuzzleScriptData data;
            public bool detached; // creator script: a plain object owned by the mod, not a real component
        }

        private class LoadedScript {
            public string typeName;
            public object instance; // null if the script could not be created
        }

        private static readonly Dictionary<string, Type> typeCache = new Dictionary<string, Type>();
        private static Dictionary<string, Type> scriptsBySimpleName;   // this plugin's scripts by class name, built on first use

        // Called once before the packs are loaded: everything that belongs to the whole set
        public static void BeginLoadAll() {
            Tickets.BeginLoad();
            NetItems.BeginLoad();
            PuzzleAudio.Clear();
            ReloadChannel.Prepare();   // the SDK's own ids come first, before every puzzle's
            SdkHandshake.Prepare();
        }

        // Builds one puzzle from a pack already read at start-up (see PuzzleLibrary)
        // The puzzle being built right now (where synced variables made in code put their hidden states)
        internal static Transform BuildRoot;

        public static GameObject LoadPuzzlePackage(PuzzlePack puzzlePack, int startTicket = -1) {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            UnityEngine.AssetBundle bundle = null;
            GameObject holder = null;
            GameObject root = null;
            try {
                PuzzlePackData pack = puzzlePack.data;

                bundle = PuzzleLibrary.OpenBundle(puzzlePack);
                if (bundle == null) return null;

                GameObject prefab = BundleAccess.LoadPrefab(bundle);
                if (prefab == null) return null;

                // Instantiate under an inactive holder so nothing runs Awake/OnEnable/Start until
                // the custom scripts have been added and every field has been assigned.
                holder = new GameObject("PuzzleLoadHolder");
                holder.SetActive(false);
                root = UnityEngine.Object.Instantiate((UnityEngine.Object)prefab, holder.transform).Cast<GameObject>();
                if (pack != null && !string.IsNullOrEmpty(pack.rootName)) root.name = pack.rootName;

                var ctx = new BuildContext { root = root.transform, bundle = bundle };
                BuildRoot = root.transform;
                int firstTicket = Tickets.BeginPuzzle(puzzlePack.name, startTicket);

                // Only the creator's own objects need their shaders swapped, so do it before any
                // vanilla copies are added to the puzzle
                FixShaders(root);

                if (pack != null && pack.scripts != null) {
                    // Pass 1: create the scripts (fields still empty)
                    foreach (PuzzleScriptData scriptData in pack.scripts) {
                        Transform target = FindByPath(ctx.root, scriptData.path);
                        if (target == null) {
                            Plugin.log.LogWarning($"Script '{scriptData.typeName}': object path '{scriptData.path}' not found, skipped.");
                            continue;
                        }
                        AddScript(target.gameObject, scriptData, ctx);
                    }

                    // Pass 2: assign fields and events now that every script exists
                    foreach (PendingScript p in ctx.pending) ApplyScriptData(p, ctx);

                    // Links the creator left empty are filled in when the puzzle has only one possible target
                    AutoLink(ctx);

                    // First: switch off what this lobby size shouldn't have, so nothing below sets it up
                    Setup<PuzzleHider>(ctx, (x, i) => x.SetupHider());
                    Setup<PuzzleButton>(ctx, (x, i) => x.SetupInteraction(i));
                    Setup<PuzzleDisplay>(ctx, (x, i) => x.SetupDisplay());
                    Setup<PuzzleProgressBar>(ctx, (x, i) => x.SetupBar());
                    Setup<PuzzleTimer>(ctx, (x, i) => x.SetupTimer());
                    Setup<PuzzleBox>(ctx, (x, i) => x.SetupBox(i));
                    Setup<PuzzleGourdPlinth>(ctx, (x, i) => x.SetupPlinth(i));
                    Setup<PuzzleGourdVice>(ctx, (x, i) => x.SetupVice(i));
                    Setup<PuzzleConfetti>(ctx, (x, i) => x.SetupConfetti());
                    Setup<PuzzlePillow>(ctx, (x, i) => x.SetupPillow());
                    Setup<PuzzleStartSwitch>(ctx, (x, i) => x.SetupSwitch(i));
                    Setup<PuzzleLiveSignal>(ctx, (x, i) => x.SetupSignal(i));
                    Setup<PuzzleDoor>(ctx, (x, i) => x.SetupDoor(i));
                    Setup<PuzzleLight>(ctx, (x, i) => x.SetupLight(i));
                    Setup<PuzzleSwitch>(ctx, (x, i) => x.SetupSwitch(i));
                    Setup<PuzzlePegTile>(ctx, (x, i) => x.SetupTile());
                    Setup<PuzzlePegBoard>(ctx, (x, i) => x.SetupBoard(i));
                    Setup<PuzzlePokeButton>(ctx, (x, i) => x.SetupButton(i));
                    Setup<PuzzleSimPressSwitch>(ctx, (x, i) => x.SetupSwitch(i));
                    Setup<PuzzleValidator>(ctx, (x, i) => x.SetupValidator(i));
                    Setup<PuzzleSoundProof>(ctx, (x, i) => x.SetupSoundproof());
                    Setup<PuzzleTelescope>(ctx, (x, i) => x.SetupTelescope(i));
                    Setup<PuzzleSeat>(ctx, (x, i) => x.SetupSeat(i));
                    Setup<PuzzleVanillaCopy>(ctx, (x, i) => x.SetupCopy(i));   // the batch-scan components
                }

                // Every sound in the puzzle without a mixer group of its own (the creator's, and the
                // ones the SDK components add) follows the game's Master and Sound Effects sliders
                PuzzleAudio.Collect(root);

                // Copied game states catch up for players who join late
                StateMirrors.Setup(root.transform);

                // Where the puzzle goes: the spawn location and rotation set on its PuzzleSDK
                PuzzleSDK main = null;
                foreach (PendingScript p in ctx.pending)
                    if (p.instance is PuzzleSDK sdk) { main = sdk; break; }

                // Release from the holder; this is the moment the puzzle wakes up
                root.transform.SetParent(null, false);
                if (main != null) main.packId = puzzlePack.id;   // what the save file knows it by
                // The puzzle spawns where its root object sits in Unity. (Packs made before that carry a
                // spawn location and rotation on the PuzzleSDK instead; those still win.)
                if (main != null && (main.spawnPuzzleLocation != Vector3.zero || main.spawnPuzzleRotation != Vector3.zero)) {
                    root.transform.position = main.spawnPuzzleLocation;
                    root.transform.rotation = Quaternion.Euler(main.spawnPuzzleRotation);
                }
                else if (main == null) Plugin.log.LogWarning("[Puzzle] The puzzle has no PuzzleSDK, so it spawns at its own exported position.");

                loadedName = root.name;
                puzzlePack.firstTicket = firstTicket;
                return root;
            } catch (System.Exception ex) {
                Plugin.log.LogError($"[Puzzle] Failed to load '{puzzlePack.name}' ({puzzlePack.fileName}): {ex}");
                if (root != null) GameObject.Destroy(root); // don't leave a half-built puzzle behind
                return null;
            } finally {
                BuildRoot = null;
                if (holder != null) GameObject.Destroy(holder);
                if (bundle != null) BundleAccess.Unload(bundle);   // the built puzzle keeps what it uses
                if (loadedName != null) Plugin.log.LogInfo($"[Puzzle] Built '{loadedName}' from {puzzlePack.fileName} in {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
                loadedName = null;
            }
        }

        private static string loadedName;   // set once a load succeeds, for the summary line

        // The game's copy of each shader name, looked up once and remembered between loads.
        // null = the game has no shader with that name.
        private static readonly Dictionary<string, Shader> gameShaders = new Dictionary<string, Shader>();

        private static Shader GameShader(string shaderName) {
            if (gameShaders.TryGetValue(shaderName, out Shader known)) {
                if (ReferenceEquals(known, null)) return null;   // looked up before: the game has none
                if (known != null) return known;                  // still loaded
            }
            Shader found = null;
            try { found = Shader.Find(shaderName); } catch (Exception) { }
            gameShaders[shaderName] = found;
            if (found == null) Plugin.log.LogWarning($"[Puzzle] The game has no shader named '{shaderName}', so materials using it may draw wrong.");
            return found;
        }

        // Swaps each material's shader for the game's own copy of the same shader when one exists
        // (shaders compiled into a bundle by a different project often draw nothing in the game).
        private static void FixShaders(GameObject root) {
            try {
                var done = new HashSet<IntPtr>();   // materials shared by several renderers are fixed once
                foreach (Component c in root.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
                    Renderer r = c != null ? c.TryCast<Renderer>() : null;
                    if (r == null) continue;

                    int emptySlots = 0;
                    foreach (Material mat in r.sharedMaterials) {
                        if (mat == null) { emptySlots++; continue; }
                        if (!done.Add(mat.Pointer)) continue;
                        Shader bundled = mat.shader;
                        if (bundled == null) continue;
                        Shader fromGame = GameShader(bundled.name);
                        if (fromGame != null && fromGame.Pointer != bundled.Pointer) mat.shader = fromGame;
                    }
                    if (emptySlots > 0) Plugin.log.LogWarning($"[Puzzle] '{r.name}' has {emptySlots} empty material slot(s).");
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Puzzle] Shader fix failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Path format: "/" is the root, "/0/2" is root -> child 0 -> child 2
        private static Transform FindByPath(Transform root, string path) {
            Transform t = root;
            if (string.IsNullOrEmpty(path)) return t;
            foreach (string segment in path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)) {
                if (!int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out int childIndex)) return null;
                if (childIndex < 0 || childIndex >= t.childCount) return null;
                t = t.GetChild(childIndex);
            }
            return t;
        }

        // ------------------------------------------------------------------
        // Scripts
        // ------------------------------------------------------------------

        private static void AddScript(GameObject go, PuzzleScriptData scriptData, BuildContext ctx) {
            object instance = null;
            try {
                if (!string.IsNullOrEmpty(scriptData.assembly)) {
                    // Creator-written script: its code is in the puzzle's own BepInEx plugin, which registered its assembly
                    // First choice: a real component on the object, like any other script.
                    instance = AttachCreatorScript(go, scriptData, ctx);
                    if (instance != null) {
                        ctx.pending.Add(new PendingScript { instance = instance, data = scriptData });
                    } else {
                        // Fallback: a plain object owned by the mod (no transform, no Update)
                        instance = CreateCreatorScript(go, scriptData, ctx);
                        if (instance != null) ctx.pending.Add(new PendingScript { instance = instance, data = scriptData, detached = true });
                    }
                } else {
                    // SDK script (or one of the game's): its code is in the mod or the game
                    Type type = ResolveType(scriptData.typeName);
                    if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type)) {
                        Plugin.log.LogWarning($"Script '{scriptData.typeName}' on '{go.name}' was not found in the mod or the game, skipped.");
                    } else {
                        Component comp = go.AddComponent(Il2CppType.From(type));
                        instance = CastTo(comp, type); // managed instance of the script
                        if (instance != null) ctx.pending.Add(new PendingScript { instance = instance, data = scriptData });
                    }
                }
            } catch (Exception ex) {
                Plugin.log.LogError($"Failed to add script '{scriptData.typeName}' to '{go.name}': {ex}");
                instance = null;
            }

            // Always record an entry (even a failed one) so component indices used by references stay aligned
            int id = go.GetInstanceID();
            if (!ctx.scriptsByObject.TryGetValue(id, out List<LoadedScript> list)) {
                list = new List<LoadedScript>();
                ctx.scriptsByObject[id] = list;
            }
            list.Add(new LoadedScript { typeName = scriptData.typeName, instance = instance });
        }

        // Code plugins that have registered with PuzzleSDK.RegisterAssemblyOf (called by the plugin class the exporter adds): assembly name -> assembly.
        // They are ordinary BepInEx plugins, loaded by BepInEx; this mod never loads code itself.
        private static readonly Dictionary<string, Assembly> registeredAssemblies = new Dictionary<string, Assembly>();

        // Creator classes we tried to register with the game: type -> did it work
        private static readonly Dictionary<Type, bool> injectedCreatorTypes = new Dictionary<Type, bool>();

        internal static void RegisterAssembly(Assembly assembly) {
            if (assembly == null) { Plugin.log.LogWarning("[Creator] RegisterAssembly was called with no assembly."); return; }
            string name = assembly.GetName().Name;
            if (name == "Assembly-CSharp" || assembly == typeof(PuzzleLoader).Assembly) {
                Plugin.log.LogWarning($"[Creator] An assembly named '{name}' cannot register as a puzzle's code. Give the puzzle's scripts their own assembly (an .asmdef) with a unique name.");
                return;
            }
            if (registeredAssemblies.TryGetValue(name, out Assembly known)) {
                if (known != assembly) Plugin.log.LogWarning($"[Creator] Two different assemblies are both named '{name}'; keeping the first.");
                return;
            }
            registeredAssemblies[name] = assembly;
            Plugin.log.LogInfo($"[Creator] Registered puzzle code '{name}'.");
        }

        // Registers a creator's MonoBehaviour class with the game and attaches it to the object as a
        // real component, so transform, gameObject, Update, Start and the rest work as in Unity.
        // Returns null if that is not possible; the caller then falls back to a detached object.
        private static object AttachCreatorScript(GameObject go, PuzzleScriptData scriptData, BuildContext ctx) {
            if (!registeredAssemblies.TryGetValue(scriptData.assembly, out Assembly assembly)) return null;

            Type type = assembly.GetType(scriptData.typeName, false);
            if (type == null || type.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(type)) return null;
            if (!EnsureInjected(type)) return null;

            try {
                Component comp = go.AddComponent(Il2CppType.From(type));
                object attached = comp != null ? CastTo(comp, type) : null;
                if (attached == null) {
                    Plugin.log.LogWarning($"[Creator] '{scriptData.typeName}' could not be attached to '{go.name}'; using a detached copy instead.");
                    return null;
                }
                return attached;
            } catch (Exception ex) {
                Exception inner = ex.InnerException ?? ex;
                Plugin.log.LogWarning($"[Creator] Attaching '{scriptData.typeName}' to '{go.name}' failed ({inner.GetType().Name}: {inner.Message}); using a detached copy instead.");
                return null;
            }
        }

        // Registers a creator class (and its base classes from the same DLL) with the game, once.
        private static bool EnsureInjected(Type type) {
            if (injectedCreatorTypes.TryGetValue(type, out bool known)) return known;

            bool ok = false;
            try {
                Type baseType = type.BaseType;
                if (baseType != null && baseType.Assembly == type.Assembly && !EnsureInjected(baseType))
                    throw new InvalidOperationException($"its base class {baseType.Name} could not be registered");
                ClassInjector.RegisterTypeInIl2Cpp(type);
                ok = true;
            } catch (Exception ex) {
                Exception inner = ex.InnerException ?? ex;
                Plugin.log.LogWarning($"[Creator] Could not register class '{type.FullName}' with the game: {inner.GetType().Name}: {inner.Message}");
            }

            injectedCreatorTypes[type] = ok;
            return ok;
        }

        // Creates a creator's script as a plain object owned by the mod. It is not attached to the
        // game object as a component, so inside the script 'transform', 'gameObject', GetComponent
        // and Unity messages such as Update do not work; fields and public functions do.
        private static object CreateCreatorScript(GameObject go, PuzzleScriptData scriptData, BuildContext ctx) {
            if (!registeredAssemblies.TryGetValue(scriptData.assembly, out Assembly assembly)) {
                Plugin.log.LogWarning($"[Creator] Script '{scriptData.typeName}' on '{go.name}' needs the puzzle's code plugin '{scriptData.assembly}', which is not installed or did not register. Skipped.");
                return null;
            }

            Type type = assembly.GetType(scriptData.typeName, false);
            if (type == null) {
                Plugin.log.LogWarning($"[Creator] The plugin '{scriptData.assembly}' has no class '{scriptData.typeName}'. Skipped.");
                return null;
            }

            try {
                return Activator.CreateInstance(type, true); // runs the script's field initializers
            } catch (Exception ex) {
                Exception inner = ex.InnerException ?? ex;
                Plugin.log.LogWarning($"[Creator] Normal construction of '{scriptData.typeName}' failed ({inner.GetType().Name}: {inner.Message}); creating it without running its constructor.");
            }

            return RuntimeHelpers.GetUninitializedObject(type);
        }

        private static void ApplyScriptData(PendingScript pending, BuildContext ctx) {
            if (pending.data.fields != null) {
                foreach (PuzzleFieldData field in pending.data.fields) {
                    try {
                        ApplyField(pending.instance, field, ctx);
                    } catch (Exception ex) {
                        Plugin.log.LogWarning($"Could not set {pending.data.typeName}.{field.name}: {ex.Message}");
                    }
                }
            }

            // Detached creator scripts are not real components, so 'enabled' must not be touched on them
            if (!pending.detached && pending.instance is Behaviour behaviour) behaviour.enabled = pending.data.enabled;
        }

        // The settable member (a plain field, or for the game's own scripts the property Il2CppInterop
        // exposes) a serialized field name refers to. Looked up once per type and name, not per script.
        private struct SettableMember { public FieldInfo field; public PropertyInfo property; }
        private static readonly Dictionary<(Type, string), SettableMember> settableCache = new Dictionary<(Type, string), SettableMember>();

        private static SettableMember FindSettable(Type type, string name) {
            if (settableCache.TryGetValue((type, name), out SettableMember known)) return known;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var found = new SettableMember();
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType) {
                FieldInfo fi = t.GetField(name, flags);
                if (fi != null) { found.field = fi; break; }
                PropertyInfo pi = t.GetProperty(name, flags);
                if (pi != null && pi.CanWrite) { found.property = pi; break; }
            }
            settableCache[(type, name)] = found;
            return found;
        }

        private static void ApplyField(object instance, PuzzleFieldData field, BuildContext ctx) {
            SettableMember member = FindSettable(instance.GetType(), field.name);
            if (member.field != null) {
                member.field.SetValue(instance, ConvertField(field, member.field.FieldType, ctx));   // mod-defined and creator scripts
                return;
            }
            if (member.property != null) {
                member.property.SetValue(instance, ConvertField(field, member.property.PropertyType, ctx));   // the game's own scripts
                return;
            }

            // Fields that packs exported earlier may still carry (removed from the SDK): ignored quietly
            if (field.name == "cooldownSeconds" || field.name == "hideVanillaObjects" || field.name == "cowbellSize" || field.name == "answer" || field.name == "imageSet") return;
            Plugin.log.LogWarning($"Field '{field.name}' does not exist on {instance.GetType().Name}, skipped.");
        }

        private static object ConvertField(PuzzleFieldData field, Type targetType, BuildContext ctx) {
            // A UnityEvent from the Inspector becomes the mod's PuzzleEvent
            if (field.kind == "event") {
                if (!targetType.IsAssignableFrom(typeof(PuzzleEvent)))
                    throw new NotSupportedException($"an event can only be stored in a PuzzleEvent field, not {targetType.Name}");
                return BuildEvent(field, ctx);
            }

            if (!field.isArray) return ConvertValue(field.kind, field.value, targetType, ctx);

            int count = field.items != null ? field.items.Count : 0;

            if (targetType.IsArray) {
                Type elementType = targetType.GetElementType();
                Array array = Array.CreateInstance(elementType, count);
                for (int i = 0; i < count; i++) array.SetValue(ConvertValue(field.kind, field.items[i], elementType, ctx), i);
                return array;
            }

            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>)) {
                Type elementType = targetType.GetGenericArguments()[0];
                IList list = (IList)Activator.CreateInstance(targetType);
                for (int i = 0; i < count; i++) list.Add(ConvertValue(field.kind, field.items[i], elementType, ctx));
                return list;
            }

            throw new NotSupportedException($"collection type {targetType} is not supported (use T[] or List<T>)");
        }

        private static object ConvertValue(string kind, string value, Type targetType, BuildContext ctx) {
            CultureInfo inv = CultureInfo.InvariantCulture;
            switch (kind) {
                case "string":
                    return value ?? "";
                case "bool":
                    return value == "1";
                case "int":
                case "enum": {
                        long l = long.Parse(value, inv);
                        if (targetType.IsEnum) return Enum.ToObject(targetType, l);
                        return Convert.ChangeType(l, targetType, inv);
                    }
                case "float":
                    return Convert.ChangeType(double.Parse(value, inv), targetType, inv);
                case "vector2": {
                        float[] f = ParseFloats(value, 2);
                        return new Vector2(f[0], f[1]);
                    }
                case "vector3": {
                        float[] f = ParseFloats(value, 3);
                        return new Vector3(f[0], f[1], f[2]);
                    }
                case "vector4": {
                        float[] f = ParseFloats(value, 4);
                        return new Vector4(f[0], f[1], f[2], f[3]);
                    }
                case "quaternion": {
                        float[] f = ParseFloats(value, 4);
                        return new Quaternion(f[0], f[1], f[2], f[3]);
                    }
                case "color": {
                        float[] f = ParseFloats(value, 4);
                        if (targetType == typeof(Color32)) return new Color32(ToByte(f[0]), ToByte(f[1]), ToByte(f[2]), ToByte(f[3]));
                        return new Color(f[0], f[1], f[2], f[3]);
                    }
                case "ref":
                    return ResolveReference(value, targetType, ctx);
                case "texture":
                    return BundleAccess.LoadTexture(ctx.bundle, value);
                case "audio":
                    return BundleAccess.LoadAudioClip(ctx.bundle, value);
                default:
                    throw new NotSupportedException($"unknown value kind '{kind}'");
            }
        }

        private static float[] ParseFloats(string value, int count) {
            string[] parts = value.Split(',');
            if (parts.Length != count) throw new FormatException($"expected {count} numbers but got '{value}'");
            float[] result = new float[count];
            for (int i = 0; i < count; i++) result[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
            return result;
        }

        private static byte ToByte(float channel) {
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(channel * 255f)));
        }

        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------

        // Turns the exported entries of a UnityEvent into a PuzzleEvent. Each entry is one JSON
        // string: which object, which function, and at most one argument.
        private static PuzzleEvent BuildEvent(PuzzleFieldData field, BuildContext ctx) {
            var result = new PuzzleEvent();
            if (field.items == null) return result;

            foreach (string json in field.items) {
                PuzzleEventCall call;
                try {
                    call = JsonSerializer.Deserialize<PuzzleEventCall>(json);
                } catch (Exception ex) {
                    Plugin.log.LogWarning($"[PuzzleEvent] Unreadable entry in '{field.name}': {ex.Message}");
                    continue;
                }
                if (call == null || string.IsNullOrEmpty(call.method)) continue;

                // "Any object" target: a script, a built-in component or a GameObject
                object target = ResolveReference(call.target, typeof(Il2CppObjectBase), ctx);
                if (target == null) {
                    Plugin.log.LogWarning($"[PuzzleEvent] '{field.name}': target for '{call.method}' was not found (is its script missing in game?). Entry skipped.");
                    continue;
                }

                if (!TryBindMethod(target, call, ctx, out MethodInfo method, out object[] args)) {
                    Plugin.log.LogWarning($"[PuzzleEvent] '{field.name}': {target.GetType().Name} has no public function '{call.method}' taking {DescribeArgument(call.argKind)}. Entry skipped.");
                    continue;
                }

                result.Add(target, method, args, $"{target.GetType().Name}.{call.method}");
            }
            // Something is wired to it: give it its counter, so it runs for everyone
            if (result.Count > 0) result.AttachChannel(ctx.root, field.name);
            return result;
        }

        private static string DescribeArgument(string argKind) {
            switch (argKind) {
                case "none": return "no argument";
                case "ref": return "an object";
                default: return "one " + argKind;
            }
        }

        // Finds the public function the entry names, with a parameter list that fits its argument.
        private static bool TryBindMethod(object target, PuzzleEventCall call, BuildContext ctx, out MethodInfo method, out object[] args) {
            method = null;
            args = null;

            MethodInfo[] candidates;
            try {
                candidates = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
            } catch (Exception) {
                return false;
            }

            foreach (MethodInfo candidate in candidates) {
                if (candidate.Name != call.method) continue;
                ParameterInfo[] parameters = candidate.GetParameters();

                if (call.argKind == "none") {
                    if (parameters.Length != 0) continue;
                    method = candidate;
                    args = new object[0];
                    return true;
                }

                if (parameters.Length != 1) continue;
                Type parameterType = parameters[0].ParameterType;

                bool fits;
                switch (call.argKind) {
                    case "int": fits = parameterType == typeof(int); break;
                    case "float": fits = parameterType == typeof(float); break;
                    case "string": fits = parameterType == typeof(string); break;
                    case "bool": fits = parameterType == typeof(bool); break;
                    case "ref": fits = typeof(Il2CppObjectBase).IsAssignableFrom(parameterType); break;
                    default: fits = false; break;
                }
                if (!fits) continue;

                object argument;
                try {
                    argument = ConvertValue(call.argKind, call.arg, parameterType, ctx);
                } catch (Exception) {
                    continue;
                }
                // An object argument that was assigned but could not be resolved to this parameter type: try the next overload
                if (call.argKind == "ref" && argument == null && !string.IsNullOrEmpty(call.arg)) continue;

                method = candidate;
                args = new[] { argument };
                return true;
            }
            return false;
        }

        // Reference format written by the exporter: "<path>|<type full name>|<index>".
        // "" means null (unassigned, or it pointed outside the exported hierarchy).
        // Passing Il2CppObjectBase as targetType means "whatever kind of object it is".
        private static object ResolveReference(string encoded, Type targetType, BuildContext ctx) {
            if (string.IsNullOrEmpty(encoded)) return null;

            string[] parts = encoded.Split('|');
            if (parts.Length != 3) return null;

            Transform t = FindByPath(ctx.root, parts[0]);
            if (t == null) return null;

            string typeName = parts[1];
            int index = int.Parse(parts[2], CultureInfo.InvariantCulture);
            object result = null;

            if (typeName == "UnityEngine.GameObject") {
                result = t.gameObject;
            } else {
                // A custom script we created during this load?
                bool isLoadedScript = false;
                if (ctx.scriptsByObject.TryGetValue(t.gameObject.GetInstanceID(), out List<LoadedScript> scripts)) {
                    int n = 0;
                    foreach (LoadedScript s in scripts) {
                        if (s.typeName != typeName) continue;
                        isLoadedScript = true;
                        if (n++ == index) { result = s.instance; break; }
                    }
                }

                // Otherwise a component that came out of the bundle (Transform, Collider, Rigidbody, ...)
                if (!isLoadedScript && typeof(Il2CppObjectBase).IsAssignableFrom(targetType)) {
                    Type castType = targetType == typeof(Il2CppObjectBase) ? ResolveType(typeName) : targetType;
                    if (castType != null && typeof(Il2CppObjectBase).IsAssignableFrom(castType)) {
                        int n = 0;
                        foreach (Component c in t.gameObject.GetComponents(Il2CppType.Of<Component>())) {
                            if (c == null || c.GetIl2CppType().FullName != typeName) continue;
                            if (n++ == index) { result = CastTo(c, castType); break; }
                        }
                    }
                }
            }

            if (result != null && !targetType.IsInstanceOfType(result)) {
                Plugin.log.LogWarning($"Reference of type {typeName} cannot be assigned to a {targetType.Name} field, using null.");
                return null;
            }
            return result;
        }

        // Finds a type by the full name the Unity project used. Falls back to a simple-name match
        // inside this plugin so mod scripts may live in a namespace (e.g. BigPuzzleSDK.PuzzleDoor).
        private static Type ResolveType(string fullName) {
            if (string.IsNullOrEmpty(fullName)) return null;
            if (typeCache.TryGetValue(fullName, out Type cached)) return cached;

            Type found = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
                try { found = asm.GetType(fullName, false); } catch { found = null; }
                if (found != null) break;
            }

            if (found == null) {
                string simpleName = fullName.Substring(fullName.LastIndexOf('.') + 1);
                if (simpleName == "BigPuzzleSDK") simpleName = "PuzzleSDK";   // the main class's old name (older packs)
                if (scriptsBySimpleName == null) {
                    scriptsBySimpleName = new Dictionary<string, Type>();
                    foreach (Type t in Assembly.GetExecutingAssembly().GetTypes())
                        if (typeof(MonoBehaviour).IsAssignableFrom(t) && !scriptsBySimpleName.ContainsKey(t.Name)) scriptsBySimpleName[t.Name] = t;
                }
                scriptsBySimpleName.TryGetValue(simpleName, out found);
            }

            typeCache[fullName] = found;
            return found;
        }

        // Il2CppObjectBase.TryCast<T>() with a runtime Type. For injected classes this returns
        // the managed instance, which is what reflection needs to set fields on.
        private static readonly MethodInfo tryCastDefinition = typeof(Il2CppObjectBase).GetMethod(nameof(Il2CppObjectBase.TryCast));
        private static readonly Dictionary<Type, MethodInfo> tryCastByType = new Dictionary<Type, MethodInfo>();

        private static object CastTo(Il2CppObjectBase obj, Type type) {
            if (!tryCastByType.TryGetValue(type, out MethodInfo cast)) {
                cast = tryCastDefinition.MakeGenericMethod(type);
                tryCastByType[type] = cast;
            }
            return cast.Invoke(obj, null);
        }
    }

    public class PuzzleFieldData {
        public string name { get; set; }
        public string kind { get; set; }
        public bool isArray { get; set; }
        public string value { get; set; }
        public List<string> items { get; set; }
    }

    public class PuzzleEventCall {
        public string target { get; set; }
        public string method { get; set; }
        public string argKind { get; set; }
        public string arg { get; set; }
    }

    public class PuzzleScriptData {
        public string path { get; set; }
        public string typeName { get; set; }
        public string assembly { get; set; }
        public bool enabled { get; set; }
        public List<PuzzleFieldData> fields { get; set; }
    }

    public class PuzzlePackData {
        // The newest pack format this mod can build. Raise it (and the exporter's) whenever a
        // component's fields or functions change. 5 = the pre-release API clean-up.
        internal const int SupportedFormat = 5;

        public int formatVersion { get; set; }
        public string puzzleId { get; set; }   // permanent id given by the exporter (format 4+)
        public string rootName { get; set; }
        public List<PuzzleScriptData> scripts { get; set; }
    }

}
namespace BigPuzzleSDK {
    internal static class HeldSwitchRefresh {
        internal static void Run() {
            try {
                int refreshed = 0;
                foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PlayerNetworking>())) {
                    PlayerNetworking pn = o != null ? o.TryCast<PlayerNetworking>() : null;
                    if (pn == null || !pn.gameObject.scene.IsValid() || pn.isLocalPlayer) continue;
                    ShellReferenceWithActionNumber now = pn.heldSwitch;
                    if (now.shellReference.isEmpty) continue;
                    pn.OnSetHeldSwitch(new ShellReferenceWithActionNumber(), now);
                    refreshed++;
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Hands] could not refresh held switches: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
