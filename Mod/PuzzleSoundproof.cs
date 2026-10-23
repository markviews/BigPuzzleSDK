using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

    // Must stay identical, in the same order, to the enum in the Unity SDK
    public enum PuzzleSoundProofMaterial { Wood, Glass }

    // Runtime (mod) version of the SDK's PuzzleSoundProof: the colliders on this object (and its
    // children) block players' voices, like the game's own soundproof rooms and doors. Open() switches
    // the colliders off (players can walk and talk through, e.g. an open doorway); Close() back on.
    //
    // How the game does it: the voice playback only treats the "Glass" layer as voice-blocking, and
    // the occlusion checks the physics material of what it hits; the game's soundproof walls are
    // solid colliders on that layer with the "audio voiceblocking wood" (or glass) material.
    public class PuzzleSoundProof : MonoBehaviour {
        public PuzzleSoundProof(IntPtr ptr) : base(ptr) { }

        private const int GlassLayer = 17;   // "Glass" in the game's layer list: the voice-blocking layer

        // Set in the Inspector; the names must match the Unity-side fields
        internal PuzzleSoundProofMaterial material;
        private bool blocking = true;       // always starts closed; false = open: colliders off (walk and talk through)

        // Read-only for scripts
        public bool IsBlocking => blocking;

        private static readonly Dictionary<string, PhysicsMaterial> materials = new Dictionary<string, PhysicsMaterial>();
        private bool set;
        private readonly List<Collider> colliders = new List<Collider>();
        private bool applied;
        private bool appliedBlocking;

        // Open or closed, from the host to every other player (late joiners included)
        private SyncedValue sync;

        // Called by the loader while the puzzle is still inactive, so every PC makes its synced values in the same order
        internal void SetupSoundproof() {
            sync = new SyncedValue(transform, "soundproof " + name);
        }

        private void Start() {
            if (sync != null) {
                sync.onChanged = v => blocking = v == 1;
                sync.Register();
            }
        }

        private void Update() {
            if (sync != null && SyncedValue.IsHost) sync.Push(blocking ? 1 : 0);
            if (applied && blocking == appliedBlocking) return;
            applied = true;
            appliedBlocking = blocking;
            foreach (Collider c in colliders) if (c != null) c.enabled = blocking;
        }

        // ---- Callable from code or from an Inspector event (host only; reaches everyone) ----
        public void Close() { if (!PuzzleSDK.IsHost) return; blocking = true; }
        public void Open() { if (!PuzzleSDK.IsHost) return; blocking = false; }

        private void Awake() {
            if (set) return;
            set = true;
            var log = Plugin.log;
            try {
                string wanted = material == PuzzleSoundProofMaterial.Glass ? "audio voiceblocking glass" : "audio voiceblocking wood";
                PhysicsMaterial mat = FindMaterial(wanted);
                if (mat == null) log.LogWarning($"[PuzzleSoundProof] the game's '{wanted}' material wasn't found; '{name}' won't block voices.");

                int count = 0;
                foreach (Collider c in GetComponentsInChildren<Collider>(true)) {
                    if (c.isTrigger) {
                        log.LogWarning($"[PuzzleSoundProof] '{c.name}' is a trigger; voices pass through triggers, so it was made solid.");
                        c.isTrigger = false;
                    }
                    c.gameObject.layer = GlassLayer;
                    if (mat != null) c.sharedMaterial = mat;
                    colliders.Add(c);
                    count++;
                }
                if (count == 0) log.LogWarning($"[PuzzleSoundProof] '{name}' has no collider, so it blocks nothing. Add colliders that cover the wall.");
            } catch (Exception ex) {
                log.LogWarning($"[PuzzleSoundProof] '{name}' could not be set up: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static PhysicsMaterial FindMaterial(string wanted) {
            if (materials.TryGetValue(wanted, out PhysicsMaterial known) && known != null) return known;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PhysicsMaterial>())) {
                PhysicsMaterial m = o.TryCast<PhysicsMaterial>();
                if (m != null && m.name == wanted) { materials[wanted] = m; return m; }
            }
            return null;
        }
    }

} // namespace BigPuzzleSDK
