using System;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleReverbZone: while the listener (the local player's
// ears) is inside this object's collider(s), the room's echo is set by hand instead of measured.
//
// How the game does it: AudioDynamicReverb (on the audio listener) measures the room around the
// player with rays and turns it into four values, all 0 to 1: room size, outdoorness, reverb
// time and diffusion. A ReverbZone the listener is in overrides any of them. The game has the
// class but uses none in the world, so this drives one itself: the zone object is never woken
// (so its own start-up code can't fail), and this component tells the reverb when the listener
// enters and leaves. It's local to each PC; nothing is sent over the network.
public class PuzzleReverbZone : MonoBehaviour {
    public PuzzleReverbZone(IntPtr ptr) : base(ptr) { }

    // Set in the Inspector; the names must match the Unity-side fields
    internal bool overrideRoomSize;
    internal float roomSize;          // 0 = tiny room, 1 = huge hall
    internal bool overrideOutdoorness;
    internal float outdoorness;       // 0 = fully enclosed, 1 = open air (no echo)
    internal bool overrideReverbTime;
    internal float reverbTime;        // 0 = dead, 1 = very long tail
    internal bool overrideDiffusion;
    internal float diffusion;         // 0 = distinct echoes, 1 = smooth wash

    private Collider[] colliders;
    private ReverbZone zone;
    private AudioDynamicReverb reverb;
    private bool inside;
    private static int scale;       // how the game's ReverbZone stores its values: 1 (0..1) or 100 (percent); 0 = not known yet

    private void Awake() {
        colliders = GetComponents<Collider>();
        if (colliders.Length == 0) {
            Plugin.log.LogWarning($"[PuzzleReverbZone] '{name}' has no collider, so it covers nothing. Add a Box (or other) Collider sized to the room.");
            return;
        }
        foreach (Collider c in colliders) c.isTrigger = true;   // never in anyone's way
        gameObject.layer = LayerHelper.PlayerZoneLayer;              // and never in the way of a click
        Renderer r = GetComponent<Renderer>();
        if (r != null) r.enabled = false;

        // The game's zone object, kept asleep: only its values are used
        var holder = new GameObject("ReverbZoneValues");
        holder.SetActive(false);
        holder.transform.SetParent(transform, false);
        zone = holder.AddComponent<ReverbZone>();
        ApplyValues();
    }

    private void ApplyValues() {
        if (zone == null) return;
        try {
            if (scale == 0) {
                // Find out how the game stores the values: write 50, read it back
                zone._reverbTime = 50;
                float read = zone.ReverbTime;
                scale = Mathf.Abs(read - 0.5f) < 0.01f ? 100 : 1;
            }
            zone._roomSize = Stored(overrideRoomSize, roomSize);
            zone._outdoorness = Stored(overrideOutdoorness, outdoorness);
            zone._reverbTime = Stored(overrideReverbTime, reverbTime);
            zone._diffusion = Stored(overrideDiffusion, diffusion);
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleReverbZone] '{name}' could not set its values: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // -1 = no override (the game measures it as usual)
    private static int Stored(bool use, float value) => use ? Mathf.RoundToInt(Mathf.Clamp01(value) * scale) : -1;

    private void Update() {
        if (zone == null) return;
        if (reverb == null) {
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<AudioDynamicReverb>())) {
                AudioDynamicReverb r = o.TryCast<AudioDynamicReverb>();
                if (r != null && r.gameObject.scene.IsValid() && r.isActiveAndEnabled) { reverb = r; break; }
            }
            if (reverb == null) return;
        }
        Vector3 ears = reverb.transform.position;
        bool now = false;
        foreach (Collider c in colliders)
            if (c != null && c.enabled && (c.ClosestPoint(ears) - ears).sqrMagnitude < 0.0001f) { now = true; break; }
        if (now == inside) return;
        inside = now;
        try {
            if (inside) {
                ApplyValues();   // pick up changes made while the puzzle runs
                reverb.EnterReverbZone(zone);
            } else {
                reverb.ExitReverbZone(zone);
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleReverbZone] '{name}' could not {(inside ? "enter" : "leave")} the reverb zone: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnDisable() {
        if (!inside) return;
        inside = false;
        try { if (reverb != null && zone != null) reverb.ExitReverbZone(zone); } catch (Exception) { }
    }

}

} // namespace BigPuzzleSDK
