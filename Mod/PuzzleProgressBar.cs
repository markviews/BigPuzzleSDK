using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// The bar's looks; the names match the game's ProgressDisplay.ProgressBarState.
// Chosen automatically (not set by creators): Increment briefly after Increment(), Victory when
// full, Failure after Fail(), Standard otherwise.
internal enum PuzzleBarState { Standard, Failure, Increment, Victory }

// Which of the game's bars to copy. Must stay identical, in the same order, to the Unity SDK's enum.
public enum PuzzleBarStyle { Straight, Spiral }

// Runtime (mod) version of the SDK's PuzzleProgressBar: copies a vanilla dot-matrix progress
// bar into place and drives it from its progress and total.
public class PuzzleProgressBar : MonoBehaviour {
    public PuzzleProgressBar(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.ProgressBar.ExactPath;
    // The same object in 3- and 2-player lobbies (the 4-player challenges aren't loaded there)
    private const string ExactPath3 = GameRefs.ProgressBar.ExactPath3;
    private const string ExactPath2 = GameRefs.ProgressBar.ExactPath2;

    // Style Spiral: the Memory Bombs spiral bar (the same script as the straight one)
    private const string SpiralPath = GameRefs.ProgressBar.SpiralPath;

    private const string CopyName = "VanillaProgressBar";
    private const string PreferredPath = GameRefs.ProgressBar.PreferredPath;

    internal static readonly List<PuzzleProgressBar> all = new List<PuzzleProgressBar>();

    // Set in the Inspector (filled in by the loader); changed only through the functions below
    private PuzzleBarStyle style;  // which of the game's bars it looks like
    private int total = 10;        // progress value at which the bar is full
    private int current;           // progress, 0 to total; always starts at 0 (not set in the Inspector)
    private PuzzleBarState state;  // which look the bar is in (automatic)

    // Fires once each time the bar becomes full
    private PuzzleEvent onFull;

    private const float IncrementLookSeconds = 0.6f;   // how long the "Increment" look shows
    private float backToStandardAt = -1f;               // host: when the Increment look ends
    private float failResetAt = -1f;                    // host: when the failure look ends and the bar empties
    private const float FailLookSeconds = 1f;

    // Read-only for scripts
    public int Total => total;
    public int Progress => current;
    public bool IsFull => current >= total;

    private ProgressDisplay bar;
    private bool applied;
    private int appliedProgress, appliedTotal;
    private PuzzleBarState appliedState;
    private float target, shown, velocity;
    private bool drawFailed;

    // The host's progress, total and look, sent to every other player (see SyncedValue in Plugin.cs)
    private SyncedValue syncProgress, syncTotal, syncState;

    // Called by the loader while the puzzle is still inactive
    internal void SetupBar() {
        var log = Plugin.log;

        GameObject template = style == PuzzleBarStyle.Spiral ? Templates.Find(SpiralPath) : null;
        if (style == PuzzleBarStyle.Spiral && template == null)
            log.LogWarning($"[PuzzleProgressBar] '{name}': the spiral bar isn't loaded; using the straight one.");
        if (template == null) template = Templates.Find(ExactPath, ExactPath3, ExactPath2);
        if (template == null) {
            // Not at its known path: search every loaded object instead (slow)
            // (the 4-player challenges aren't loaded in a 2- or 3-player lobby). A plain
            // "BasicProgressBar" is wanted; others (such as the seance's ProgressBar_inside)
            // can be built differently.
            ProgressDisplay source = null;
            int bestScore = int.MinValue;
            var seen = new List<string>();
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<ProgressDisplay>())) {
                ProgressDisplay candidate = obj.TryCast<ProgressDisplay>();
                if (candidate == null || !candidate.gameObject.scene.IsValid()) continue;
                string path = PathOf(candidate.transform);
                if (path.Contains(CopyName)) continue;            // one of our own copies
                if (seen.Count < 20) seen.Add(path);
                int score = 0;
                if (candidate.name.StartsWith("BasicProgressBar")) score += 10;
                if (path.Contains(PreferredPath)) score += 2;
                if (candidate.name.Contains("_inside")) score -= 5;
                if (score > bestScore) { bestScore = score; source = candidate; }
            }
            if (source != null && bestScore < 10)
                log.LogWarning("[PuzzleProgressBar] No BasicProgressBar is loaded; candidates were:\n  " + string.Join("\n  ", seen));
            if (source == null) {
                log.LogWarning($"[PuzzleProgressBar] No vanilla progress bar is loaded, so '{name}' has nothing to copy.");
                return;
            }

            // Copy the bar's parent if that is a small self-contained object (bar + frame);
            // otherwise just the bar itself, so we never drag a whole puzzle room along.
            template = source.gameObject;
            Transform parent = source.transform.parent;
            if (parent != null && !source.name.StartsWith("BasicProgressBar")) {   // a BasicProgressBar is complete on its own
                int size = parent.GetComponentsInChildren(Il2CppType.Of<Transform>(), true).Length;
                bool hasSwitches = parent.GetComponentInChildren(Il2CppType.Of<PeckSwitch>(), true) != null;
                if (size <= 12 && !hasSwitches) template = parent.gameObject;
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        Vector3 templateScale = template.transform.lossyScale;

        GameObject copy = Templates.Clone(template, transform);
        copy.name = CopyName;
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.SetActive(true);

        // Unlike most components, the bar follows the creator's scale: its object's scale
        // multiplies the vanilla size (1,1,1 = the game's own size)
        copy.transform.localScale = templateScale;

        // The game switches off renderers of far-away objects; the copy inherits that
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
            Renderer r = c != null ? c.TryCast<Renderer>() : null;
            if (r != null) r.enabled = true;
        }

        // Hide the creator's placeholder
        Renderer placeholder = GetComponent<Renderer>();
        if (placeholder != null) placeholder.enabled = false;
        Collider placeholderCollider = GetComponent<Collider>();
        if (placeholderCollider != null) placeholderCollider.enabled = false;

        Component found = copy.GetComponentInChildren(Il2CppType.Of<ProgressDisplay>(), true);
        bar = found != null ? found.TryCast<ProgressDisplay>() : null;
        if (bar == null) { log.LogWarning($"[PuzzleProgressBar] The copy for '{name}' has no ProgressDisplay."); return; }

        // Made here, during setup, so every PC hands out the same network ids in order
        syncProgress = new SyncedValue(transform, "bar progress " + name);
        syncTotal = new SyncedValue(transform, "bar total " + name);
        syncState = new SyncedValue(transform, "bar look " + name);
    }

    private static string PathOf(Transform t) {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    private void Start() {
        all.Add(this);
        if (bar == null) return;
        var log = Plugin.log;
        if (syncProgress != null) {
            // Other players: take the host's values as they arrive (the bar redraws itself in Update)
            syncTotal.onChanged = v => total = v;
            syncProgress.onChanged = v => current = v;
            syncState.onChanged = v => { if (Enum.IsDefined(typeof(PuzzleBarState), v)) state = (PuzzleBarState)v; };
            syncProgress.Register(); syncTotal.Register(); syncState.Register();
        }

        // The copy can be missing its material instance, which makes the game's draw code fail
        // (NullReference inside SetMaterialProperties); give it one
        try {
            if (bar._materialInstance == null && bar.targetRenderer != null)
                bar._materialInstance = bar.targetRenderer.material;
        } catch (Exception ex) {
            log.LogWarning($"[PuzzleProgressBar] '{name}': could not set up the bar's material: {ex.GetType().Name}: {ex.Message}");
        }

        try {
            OccasionalPartner partner = bar.occasionalPartner;

            // The partner is what makes the game update the bar from its own puzzle. Switch it
            // off so only the mod decides what the bar shows.
            Behaviour partnerBehaviour = partner != null ? partner.TryCast<Behaviour>() : null;
            if (partnerBehaviour != null) {
                partnerBehaviour.enabled = false;
            }
        } catch (Exception ex) {
            log.LogWarning($"[PuzzleProgressBar] '{name}' could not switch off the game's own updates: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Update() {
        if (bar == null) return;

        if (total < 1) total = 1;
        current = Mathf.Clamp(current, 0, total);

        // Host: the short Increment look settles back to standard
        if (failResetAt >= 0f && Time.time >= failResetAt && PuzzleSDK.IsHost) {
            failResetAt = -1f;
            Reset();
        }
        if (backToStandardAt >= 0f && Time.time >= backToStandardAt && PuzzleSDK.IsHost) {
            backToStandardAt = -1f;
            if (state == PuzzleBarState.Increment) state = PuzzleBarState.Standard;
        }

        // Host: send it
        if (syncProgress != null && SyncedValue.IsHost) {
            syncTotal.Push(total);
            syncProgress.Push(current);
            syncState.Push((int)state);
        }

        if (!applied || current != appliedProgress || total != appliedTotal || state != appliedState) Apply();

        // Slide the bar towards its target and redraw it
        shown = Mathf.SmoothDamp(shown, target, ref velocity, 0.25f);
        if (drawFailed) return;
        try {
            bar.dampedProgress = shown;
            bar.SetMaterialProperties(shown, GameState());
        } catch (Exception ex) {
            drawFailed = true; // log once, not every frame
            Plugin.log.LogWarning($"[PuzzleProgressBar] '{name}' could not draw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private ProgressDisplay.ProgressBarState GameState() {
        return Enum.TryParse(state.ToString(), out ProgressDisplay.ProgressBarState parsed) ? parsed : ProgressDisplay.ProgressBarState.Standard;
    }

    // Tells the game's bar about a new value or state (this is what plays its sound and effects)
    private void Apply() {
        applied = true;
        appliedProgress = current;
        appliedTotal = total;
        appliedState = state;
        target = current / (float)total;

        try {
            bar.DoTransition(target, GameState());
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleProgressBar] '{name}' transition to {current}/{total} ({state}) failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- Callable from code or from an Inspector event ----
    // Host only: on other players' PCs these do nothing (the bar reaches them from the host)

    // Changes the value at which the bar is full. Progress above the new total is cut down to
    // it; a bar that becomes full this way shows the victory look and fires onFull.
    public void SetTotal(int value) {
        if (!PuzzleSDK.IsHost) return;
        bool wasFull = current >= total;
        total = Mathf.Max(1, value);
        if (current > total) current = total;
        bool full = current >= total;
        if (full && state != PuzzleBarState.Failure) state = PuzzleBarState.Victory;
        else if (!full && state == PuzzleBarState.Victory) state = PuzzleBarState.Standard;
        if (full && !wasFull && onFull != null) onFull.Invoke();
    }

    // Adds one. Shows the "Increment" look briefly, or the victory look (and fires onFull) when
    // that fills the bar. Does nothing once full; Reset() empties it.
    public void Increment() {
        if (!PuzzleSDK.IsHost) return;
        if (current >= total) return;
        failResetAt = -1f;
        current++;
        if (current >= total) {
            backToStandardAt = -1f;
            state = PuzzleBarState.Victory;
            if (onFull != null) onFull.Invoke();
        } else {
            state = PuzzleBarState.Increment;
            backToStandardAt = Time.time + IncrementLookSeconds;
        }
    }

    // Shows the failure look for one second, then empties the bar (as Reset()). An Increment() or
    // Reset() in that second cancels the automatic reset.
    public void Fail() { if (!PuzzleSDK.IsHost) return; backToStandardAt = -1f; state = PuzzleBarState.Failure; failResetAt = Time.time + FailLookSeconds; }

    // For Press In Order's phase timer: shows a fraction (1 = full, 0 = empty) in the standard look, in
    // steps of 1/DrainSteps. No Increment look, no victory, no On Full. Host only; others get it from the host.
    private const int DrainSteps = 20;
    internal void ShowDrain(float fraction) {
        if (!PuzzleSDK.IsHost) return;
        backToStandardAt = -1f;
        failResetAt = -1f;
        total = DrainSteps;
        current = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(fraction) * DrainSteps), 0, DrainSteps);
        state = PuzzleBarState.Standard;
    }

    // Empties the bar and returns it to the standard look
    public void Reset() { if (!PuzzleSDK.IsHost) return; backToStandardAt = -1f; failResetAt = -1f; current = 0; state = PuzzleBarState.Standard; }

    private void OnDestroy() { all.Remove(this); }
}

} // namespace BigPuzzleSDK
