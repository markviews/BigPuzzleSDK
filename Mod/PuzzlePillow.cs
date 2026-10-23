using System;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzlePillow: copies the gourd valet's pillow model into
// place. The game keeps its own pillow switched off most of the time, and so does this one: it
// only appears when the puzzle's gourd is put on it (a won puzzle whose gourd was lost). Any game
// scripts on the copy are removed so nothing else turns it on or off.
public class PuzzlePillow : MonoBehaviour {
    public PuzzlePillow(IntPtr ptr) : base(ptr) { }

    // Where the vanilla model sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.Pillow.ExactPath;
    private const string TemplateName = GameRefs.Pillow.TemplateName;
    private const string PreferredPath = GameRefs.Pillow.PreferredPath;
    private const string CopyName = "VanillaPillow";

    // The model is hidden until the host puts the puzzle's gourd on it (a won puzzle whose gourd was
    // lost last time, see PuzzleSave). Shown to every player, late joiners included.
    private SyncedValue syncShown;
    private GameObject copyObject;

    private void Start() {
        if (syncShown == null) return;
        syncShown.onChanged = v => SetShown(v == 1);
        syncShown.Register();
    }

    private void SetShown(bool value) {
        try { if (copyObject != null) copyObject.SetActive(value); } catch (Exception) { }
    }

    // Host only: shows the pillow for everyone
    internal void Show() {
        if (!SyncedValue.IsHost) return;
        SetShown(true);
        if (syncShown != null) syncShown.Push(1);
    }

    // Called by the loader while the puzzle is still inactive
    internal void SetupPillow() {
        var log = Plugin.log;
        // Made here, during setup, so every PC hands out the same network ids in order
        syncShown = new SyncedValue(transform, "pillow " + name);

        GameObject template = Templates.Find(ExactPath);
        if (template == null) {
            // Not at its known path (e.g. a 2- or 3-player lobby): search every loaded object by name (slow)
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Transform>())) {
                Transform t = obj.TryCast<Transform>();
                if (t == null || t.name != TemplateName || !t.gameObject.scene.IsValid()) continue;
                string path = Templates.FullPath(t);
                if (path.Contains(CopyName)) continue;   // one of our own copies
                if (template == null) template = t.gameObject;
                if (path.Contains(PreferredPath)) { template = t.gameObject; break; }
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzlePillow] No vanilla '{TemplateName}' is loaded, so '{name}' has nothing to copy.");
            return;
        }

        Vector3 templateScale = template.transform.lossyScale;
        GameObject copy = Templates.Clone(template, transform);
        copy.name = CopyName;
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = template.transform.localRotation;   // the model's own upright turn

        // Keep the game's own size whatever the creator's objects are scaled to
        Vector3 parentScale = transform.lossyScale;
        copy.transform.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? templateScale.x : templateScale.x / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? templateScale.y : templateScale.y / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? templateScale.z : templateScale.z / parentScale.z);

        // Just the model: the game's scripts on it would belong to the vanilla valet
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<MonoBehaviour>(), true))
            if (c != null) UnityEngine.Object.DestroyImmediate(c);

        // A loose physics body would fall or get pushed: keep it where the creator put it
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Rigidbody>(), true)) {
            Rigidbody body = c != null ? c.TryCast<Rigidbody>() : null;
            if (body != null) body.isKinematic = true;
        }

        // The game's pillow is usually off: switch its renderers on, but leave the copy hidden
        // until Show() (the gourd is lying on it)
        copy.SetActive(true);
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
            Renderer r = c != null ? c.TryCast<Renderer>() : null;
            if (r != null) r.enabled = true;
        }
        copy.SetActive(false);
        copyObject = copy;

        // Hide the creator's placeholder
        Renderer placeholder = GetComponent<Renderer>();
        if (placeholder != null) placeholder.enabled = false;
        Collider placeholderCollider = GetComponent<Collider>();
        if (placeholderCollider != null) placeholderCollider.enabled = false;
    }
}

} // namespace BigPuzzleSDK
