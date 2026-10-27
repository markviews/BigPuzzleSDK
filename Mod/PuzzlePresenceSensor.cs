using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Mirror;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzlePresenceSensor: a button that "presses" when enough players
// stand inside the collider(s) on this object, and "releases" when too few are left. Being a button
// (PuzzleButtonBase), it also works in Press In Order. Everything is decided on the host (it knows where
// every player is); the events then reach every player (see PuzzleEvent in Plugin.cs).
public class PuzzlePresenceSensor : PuzzleButtonBase {
    public PuzzlePresenceSensor(IntPtr ptr) : base(ptr) { }

    // Set in the Inspector (filled in by the loader). The names must match the Unity-side fields.
    private int requiredPlayers = 1;
    private PuzzleEvent onPressed;
    private PuzzleEvent onReleased;
    private PuzzleEvent onPlayerEntered;
    private PuzzleEvent onPlayerLeft;

    private const float CheckSeconds = 0.1f;
    private const float PlayerListSeconds = 1f;   // how often the list of players is looked up again

    private readonly List<Collider> volumes = new List<Collider>();
    private readonly List<PlayerCharacter> players = new List<PlayerCharacter>();
    private HashSet<uint> inside = new HashSet<uint>();
    private float nextCheck;
    private float nextPlayerList;
    private bool pressed;
    internal bool IsHeld { get { return pressed; } }   // enough players are inside right now
    private bool set;

    // Read-only for scripts
    public int PlayerCount => inside.Count;

    private void Awake() {
        if (set) return;
        set = true;
        try {
            foreach (Collider c in GetComponents<Collider>()) {
                c.isTrigger = true;   // players walk through it
                volumes.Add(c);
                MeshCollider mesh = c.TryCast<MeshCollider>();
                if (mesh != null && !mesh.convex)
                    Plugin.log.LogWarning($"[PuzzlePresenceSensor] '{name}' has a Mesh Collider that isn't Convex, so it can't tell who is inside. Tick Convex, or use a Box Collider.");
            }
            if (volumes.Count == 0) Plugin.log.LogWarning($"[PuzzlePresenceSensor] '{name}' has no collider, so it never detects anyone. Add a Box (or other) Collider sized to the area.");
            gameObject.layer = LayerHelper.PlayerZoneLayer;   // so it doesn't catch clicks aimed at things inside it
            Renderer r = GetComponent<Renderer>();
            if (r != null) r.enabled = false;   // the placeholder shape is only for the editor
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzlePresenceSensor] '{name}' could not be set up: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Update() {
        if (!PuzzleSDK.IsHost || volumes.Count == 0 || Time.time < nextCheck) return;
        nextCheck = Time.time + CheckSeconds;

        if (Time.time >= nextPlayerList) {
            nextPlayerList = Time.time + PlayerListSeconds;
            players.Clear();
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PlayerCharacter>())) {
                PlayerCharacter pc = o != null ? o.TryCast<PlayerCharacter>() : null;
                if (pc != null && pc.gameObject.scene.IsValid()) players.Add(pc);
            }
        }

        var now = new HashSet<uint>();
        foreach (PlayerCharacter pc in players) {
            if (pc == null) continue;
            NetworkIdentity id = pc.GetComponent<NetworkIdentity>();
            if (id != null && IsInside(pc.transform.position)) now.Add(id.netId);
        }

        foreach (uint id in now) if (!inside.Contains(id) && onPlayerEntered != null) onPlayerEntered.Invoke();
        foreach (uint id in inside) if (!now.Contains(id) && onPlayerLeft != null) onPlayerLeft.Invoke();
        inside = now;

        bool shouldBePressed = inside.Count >= Math.Max(1, requiredPlayers);
        if (shouldBePressed == pressed) return;
        pressed = shouldBePressed;
        if (pressed) {
            if (onPressed != null) onPressed.Invoke();
            if (pressedHook != null) pressedHook();
        } else if (onReleased != null) onReleased.Invoke();
    }

    // A player counts when its position, or a point half a metre or a metre above it, is inside the volume
    private bool IsInside(Vector3 position) {
        foreach (Collider c in volumes) {
            if (c == null || !c.enabled) continue;
            for (int i = 0; i < 3; i++) {
                Vector3 p = position + Vector3.up * (i * 0.5f);
                if ((c.ClosestPoint(p) - p).sqrMagnitude < 0.0001f) return true;
            }
        }
        return false;
    }
}

} // namespace BigPuzzleSDK
