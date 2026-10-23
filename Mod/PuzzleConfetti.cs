using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleConfetti: copies the vanilla reward confetti into
// place. It has no functions for creators; the puzzle's main class fires every one of these
// in the puzzle from Win().
public class PuzzleConfetti : MonoBehaviour {
    public PuzzleConfetti(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.Confetti.ExactPath;

    private const string CopyName = "VanillaConfetti";
    private const string TemplateName = GameRefs.Confetti.TemplateName;
    private const string PreferredPath = GameRefs.Confetti.PreferredPath;

    internal static readonly List<PuzzleConfetti> all = new List<PuzzleConfetti>();

    private readonly List<ParticleSystem> particles = new List<ParticleSystem>();
    private bool fired;

    // 1 once the host has fired it, so every other player fires theirs too (see SyncedValue in Plugin.cs)
    private SyncedValue syncFired;
    private readonly List<AudioClip> clips = new List<AudioClip>();   // the vanilla confetti sound(s)

    // The confetti clips found the first time. Audio clips are assets, not scene objects, so
    // they have no path to look up; instead they are found once and remembered for every
    // later confetti and every reload.
    private static readonly List<AudioClip> knownClips = new List<AudioClip>();
    private Transform audioPoint;
    private AudioSource source;

    // Called by the loader while the puzzle is still inactive
    internal void SetupConfetti() {
        var log = Plugin.log;

        GameObject template = Templates.Find(ExactPath);
        if (template == null) {
            // Not at its known path: search every loaded object instead (slow)
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PeckEffectParticle>())) {
                PeckEffectParticle candidate = obj.TryCast<PeckEffectParticle>();
                if (candidate == null || candidate.name != TemplateName) continue;
                string path = PathOf(candidate.transform);
                if (path.Contains(CopyName)) continue;   // skip our own copies
                if (template == null) template = candidate.gameObject;
                if (path.Contains(PreferredPath)) { template = candidate.gameObject; break; }
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzleConfetti] No vanilla '{TemplateName}' is loaded, so '{name}' has nothing to copy.");
            return;
        }

        Vector3 templateScale = template.transform.lossyScale;
        GameObject copy = Templates.Clone(template, transform);
        copy.name = CopyName;
        copy.transform.localPosition = Vector3.zero;
        // Keep the vanilla confetti's own facing (it is flipped to pour downwards), turned by
        // however the creator rotated this object
        copy.transform.rotation = transform.rotation * template.transform.rotation;
        copy.SetActive(true);

        Vector3 parentScale = transform.lossyScale;
        copy.transform.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? templateScale.x : templateScale.x / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? templateScale.y : templateScale.y / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? templateScale.z : templateScale.z / parentScale.z);

        // The vanilla trigger listens to its own challenge's victory. Remove it from the copy
        // so the copy only fires from this puzzle's Win().
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckEffectParticle>(), true)) {
            PeckEffectParticle effect = c != null ? c.TryCast<PeckEffectParticle>() : null;
            if (effect == null) continue;
            try {
                if (effect.customAudioTransform != null) audioPoint = effect.customAudioTransform;
                if (effect.sound != null) FindClips(effect.sound, effect.sound.name);
            } catch (Exception ex) { }
            UnityEngine.Object.DestroyImmediate(effect);
        }

        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<ParticleSystem>(), true)) {
            ParticleSystem ps = c != null ? c.TryCast<ParticleSystem>() : null;
            if (ps != null) particles.Add(ps);
        }
        // The game switches off renderers of far-away objects; the copy inherits that
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
            Renderer r = c != null ? c.TryCast<Renderer>() : null;
            if (r != null) r.enabled = true;
        }

        RebuildBatchedMeshes(template.transform, copy.transform);

        // Hide the creator's placeholder
        Renderer placeholder = GetComponent<Renderer>();
        if (placeholder != null) placeholder.enabled = false;
        Collider placeholderCollider = GetComponent<Collider>();
        if (placeholderCollider != null) placeholderCollider.enabled = false;

        // A plain 3D audio source at the vanilla audio point, playing the vanilla clip
        if (audioPoint == null) audioPoint = copy.transform;
        source = audioPoint.gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;      // fully 3D
        source.minDistance = 1f;
        source.maxDistance = 30f;

        // Made here, during setup, so every PC hands out the same network ids in order
        syncFired = new SyncedValue(transform, "confetti " + name, oneShot: true);
    }

    // The game merges still scenery into one big "combined" mesh whose points sit at their
    // original world positions, so a copy of such an object is drawn where the original is.
    // For each of those in the copy, make a replacement that is drawn in the right place.
    private void RebuildBatchedMeshes(Transform template, Transform copy) {
        var log = Plugin.log;
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<MeshRenderer>(), true)) {
            MeshRenderer renderer = c != null ? c.TryCast<MeshRenderer>() : null;
            if (renderer == null) continue;
            try {
                MeshFilter filter = renderer.gameObject.GetComponent<MeshFilter>();
                Mesh combined = filter != null ? filter.sharedMesh : null;
                if (combined == null || !combined.name.StartsWith("Combined Mesh")) continue;

                Transform original = renderer.transform == copy ? template : template.Find(PathOf(renderer.transform, copy));
                if (original == null) { log.LogWarning($"[PuzzleConfetti]   no original found for '{renderer.name}'"); continue; }
                Matrix4x4 toLocal = original.worldToLocalMatrix;   // combined-mesh space -> the object's own space

                var materials = renderer.sharedMaterials;
                int first = renderer.subMeshStartIndex;
                int count = Mathf.Max(1, materials.Length);

                GameObject visual = new GameObject("DispenserVisual");
                visual.layer = renderer.gameObject.layer;
                visual.transform.SetParent(renderer.transform, false);
                MeshFilter newFilter = visual.AddComponent<MeshFilter>();
                MeshRenderer newRenderer = visual.AddComponent<MeshRenderer>();

                if (combined.isReadable) {
                    // Cut this object's part out of the combined mesh into a small mesh of its own
                    var allVertices = combined.vertices;
                    var allNormals = combined.normals;
                    var allColors = combined.colors32;
                    var allUv = combined.uv;
                    var map = new Dictionary<int, int>();
                    var vertices = new List<Vector3>();
                    var normals = new List<Vector3>();
                    var colors = new List<Color32>();
                    var uv = new List<Vector2>();
                    var parts = new List<int[]>();
                    for (int part = 0; part < count; part++) {
                        var source = combined.GetIndices(first + part);
                        var indices = new int[source.Length];
                        for (int i = 0; i < source.Length; i++) {
                            int old = source[i];
                            if (!map.TryGetValue(old, out int now)) {
                                now = vertices.Count;
                                map[old] = now;
                                vertices.Add(toLocal.MultiplyPoint3x4(allVertices[old]));
                                if (allNormals != null && allNormals.Length > old) normals.Add(toLocal.MultiplyVector(allNormals[old]).normalized);
                                if (allColors != null && allColors.Length > old) colors.Add(allColors[old]);
                                if (allUv != null && allUv.Length > old) uv.Add(allUv[old]);
                            }
                            indices[i] = now;
                        }
                        parts.Add(indices);
                    }
                    Mesh mesh = new Mesh();
                    mesh.name = "PuzzleDispenser";
                    mesh.vertices = vertices.ToArray();
                    if (normals.Count == vertices.Count) mesh.normals = normals.ToArray();
                    if (colors.Count == vertices.Count) mesh.colors32 = colors.ToArray();
                    if (uv.Count == vertices.Count) mesh.uv = uv.ToArray();
                    mesh.subMeshCount = parts.Count;
                    for (int part = 0; part < parts.Count; part++) mesh.SetTriangles(parts[part], part);
                    mesh.RecalculateBounds();
                    newFilter.sharedMesh = mesh;
                    newRenderer.sharedMaterials = materials;
                } else {
                    // The combined mesh cannot be read, so it can't be cut up. Drawing all of it as a copy
                    // would also draw every other object merged into it (the loose pink dots in the sky).
                    // Instead, draw only this object's sub-meshes, each frame (see Update).
                    UnityEngine.Object.Destroy(visual);
                    for (int part = 0; part < count; part++) {
                        if (part >= materials.Length || materials[part] == null) continue;
                        var draw = new DrawPart();
                        draw.mesh = combined; draw.sub = first + part; draw.material = materials[part];
                        draw.anchor = renderer.transform; draw.toLocal = toLocal; draw.layer = renderer.gameObject.layer;
                        draws.Add(draw);
                    }
                }

                renderer.enabled = false;   // the copy's own renderer draws at the original's place
                LODGroup lod = renderer.gameObject.GetComponent<LODGroup>();
                if (lod != null) lod.enabled = false;
                batchedOriginals.Add(renderer);
            } catch (Exception ex) {
                log.LogWarning($"[PuzzleConfetti]   could not rebuild '{renderer.name}': {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private readonly List<MeshRenderer> batchedOriginals = new List<MeshRenderer>();

    // A piece of an unreadable combined mesh, drawn every frame in the right place
    private class DrawPart {
        public Mesh mesh; public int sub; public Material material;
        public Transform anchor; public Matrix4x4 toLocal; public int layer;
    }
    private readonly List<DrawPart> draws = new List<DrawPart>();

    private void Update() {
        foreach (DrawPart d in draws) {
            if (d.anchor == null || d.mesh == null || d.material == null || !d.anchor.gameObject.activeInHierarchy) continue;
            Graphics.DrawMesh(d.mesh, d.anchor.localToWorldMatrix * d.toLocal, d.material, d.layer, null, d.sub);
        }
    }

    private static string PathOf(Transform t, Transform stopAt = null) {
        string path = t.name;
        for (Transform p = t.parent; p != null && p != stopAt; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    // Uses the remembered clips if they are still loaded; otherwise looks them up once
    private void FindClips(Il2CppSystem.Object asset, string assetName) {
        knownClips.RemoveAll(c => c == null);   // unloaded since
        if (knownClips.Count == 0) {
            CollectClips(asset, assetName);
            knownClips.AddRange(clips);
            return;
        }
        clips.AddRange(knownClips);
    }

    // Finds the audio clips behind the game's sound asset. First looks inside the asset itself;
    // if that finds nothing, falls back to loaded clips whose name matches the asset's name.
    private void CollectClips(Il2CppSystem.Object asset, string assetName) {
        var seen = new List<string>();
        foreach (PropertyInfo prop in asset.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
            Type type = prop.PropertyType;
            bool single = type == typeof(AudioClip);
            bool many = type.IsGenericType && type.GetGenericArguments().Length == 1 && type.GetGenericArguments()[0] == typeof(AudioClip);
            if (prop.DeclaringType == asset.GetType()) seen.Add($"{prop.Name}:{type.Name}");
            if (!single && !many) continue;
            try {
                object value = prop.GetValue(asset);
                if (value == null) continue;
                if (value is AudioClip one) { clips.Add(one); continue; }
                if (value is Il2CppReferenceArray<AudioClip> array) {
                    foreach (AudioClip clip in array) if (clip != null) clips.Add(clip);
                    continue;
                }
                // Il2Cpp List<AudioClip>
                PropertyInfo countProp = value.GetType().GetProperty("Count");
                PropertyInfo item = value.GetType().GetProperty("Item");
                if (countProp == null || item == null) continue;
                int count = (int)countProp.GetValue(value);
                for (int i = 0; i < count; i++)
                    if (item.GetValue(value, new object[] { i }) is AudioClip clip && clip != null) clips.Add(clip);
            } catch (Exception) { }
        }
        if (clips.Count > 0) return;

        string key = assetName.ToLowerInvariant();
        if (key.StartsWith("sfx_")) key = key.Substring(4);
        if (key.EndsWith("_random")) key = key.Substring(0, key.Length - 7);
        foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<AudioClip>())) {
            AudioClip clip = obj.TryCast<AudioClip>();
            if (clip == null) continue;
            string clipName = clip.name.ToLowerInvariant();
            if (clipName.Contains(key) || clipName.Contains("confetti")) clips.Add(clip);
        }
    }

    private void Start() {
        all.Add(this);
        if (syncFired != null) {
            syncFired.onChanged = v => { if (v == 1) Fire(); };   // other players: the host fired it
            syncFired.Register();
        }
    }

    private void OnDestroy() { all.Remove(this); }

    // Fires the confetti. Called by the puzzle's main class from Win().
    internal void Fire() {
        if (fired) return;
        fired = true;
        if (syncFired != null) syncFired.Push(1);
        int played = 0;
        foreach (ParticleSystem ps in particles) {
            if (ps == null) continue;
            try { ps.Play(true); played++; }
            catch (Exception ex) { Plugin.log.LogWarning($"[PuzzleConfetti] '{name}' could not play '{ps.name}': {ex.GetType().Name}: {ex.Message}"); }
        }
        try {
            if (source != null && clips.Count > 0) {
                AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Count)];
                source.clip = clip;
                source.Play();
            }
        } catch (Exception ex) { Plugin.log.LogWarning($"[PuzzleConfetti] '{name}' could not play its sound: {ex.GetType().Name}: {ex.Message}"); }
    }
}

} // namespace BigPuzzleSDK
