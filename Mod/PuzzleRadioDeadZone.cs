using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleRadioDeadZone: the colliders on this object become a
// radio dead zone, like the game's own (e.g. in the Centurion seance and the ring rooms). A
// walkie-talkie or megaphone inside it shows "dead zone" and can't send or receive.
//
// The game's dead zones are PropZones that call RadioVoiceAssigner.SetIsInDeadZone on radio
// props entering and leaving. A PropZone added by code throws errors (it expects set-up the game
// does in its own scenes), so this does the same job itself: its triggers find radio props and
// switch their dead-zone state. Each PC works this out on its own (the props are already synced),
// so nothing is sent over the network.
public class PuzzleRadioDeadZone : MonoBehaviour {
    public PuzzleRadioDeadZone(IntPtr ptr) : base(ptr) { }

    private const int PropZoneLayer = 21;   // "PropZone" in the game's layer list: props touch its triggers

    // Radios inside, with how many of this zone's colliders each one is touching
    private readonly Dictionary<IntPtr, (RadioVoiceAssigner radio, int touches)> inside = new Dictionary<IntPtr, (RadioVoiceAssigner, int)>();
    private bool set;

    private void Awake() {
        if (set) return;
        set = true;
        try {
            Collider[] colliders = GetComponents<Collider>();
            if (colliders.Length == 0) {
                Plugin.log.LogWarning($"[PuzzleRadioDeadZone] '{name}' has no collider, so it covers nothing. Add a Box (or other) Collider sized to the zone.");
                return;
            }
            foreach (Collider c in colliders) c.isTrigger = true;
            gameObject.layer = PropZoneLayer;

            Renderer r = GetComponent<Renderer>();
            if (r != null) r.enabled = false;   // the placeholder shape is only for the editor

            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleRadioDeadZone] '{name}' could not be set up: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnTriggerEnter(Collider other) {
        RadioVoiceAssigner radio = RadioOf(other);
        if (radio == null) return;
        if (inside.TryGetValue(radio.Pointer, out var entry)) {
            inside[radio.Pointer] = (radio, entry.touches + 1);
            return;
        }
        inside[radio.Pointer] = (radio, 1);
        SetDead(radio, true);
    }

    private void OnTriggerExit(Collider other) {
        RadioVoiceAssigner radio = RadioOf(other);
        if (radio == null || !inside.TryGetValue(radio.Pointer, out var entry)) return;
        if (entry.touches > 1) { inside[radio.Pointer] = (radio, entry.touches - 1); return; }
        inside.Remove(radio.Pointer);
        SetDead(radio, false);
    }

    // A radio that was inside and is gone (destroyed, or the zone was switched off) is let go
    private void Update() {
        if (inside.Count == 0) return;
        List<IntPtr> gone = null;
        foreach (var kv in inside)
            if (kv.Value.radio == null || !kv.Value.radio.gameObject.activeInHierarchy) (gone ??= new List<IntPtr>()).Add(kv.Key);
        if (gone != null) foreach (IntPtr p in gone) inside.Remove(p);
    }

    private void OnDisable() {
        foreach (var kv in inside) if (kv.Value.radio != null) SetDead(kv.Value.radio, false);
        inside.Clear();
    }

    // The walkie-talkie or megaphone a collider belongs to, if any
    private static RadioVoiceAssigner RadioOf(Collider other) {
        if (other == null) return null;
        Rigidbody body = other.attachedRigidbody;
        GameObject root = body != null ? body.gameObject : other.gameObject;
        if (root.GetComponent<Prop>() == null) return null;   // only carried radios, like the game's own zones
        return root.GetComponentInChildren<RadioVoiceAssigner>(true);
    }

    private void SetDead(RadioVoiceAssigner radio, bool value) {
        try { radio.SetIsInDeadZone(value); }
        catch (Exception ex) { Plugin.log.LogWarning($"[PuzzleRadioDeadZone] '{name}': could not switch '{radio.name}': {ex.GetType().Name}: {ex.Message}"); }
    }
}

} // namespace BigPuzzleSDK
