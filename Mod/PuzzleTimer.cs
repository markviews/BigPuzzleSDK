using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;

// Needs a project reference to BepInEx/interop/Unity.TextMeshPro.dll

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleTimer: copies a vanilla countdown clock into place.
// The game's own timer logic is networked and tied to its puzzle, so it is switched off and
// the mod counts down and writes the digits itself.
public class PuzzleTimer : MonoBehaviour {
    public PuzzleTimer(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.Timer.ExactPath;
    // The same object in 3- and 2-player lobbies (the 4-player challenges aren't loaded there)
    private const string ExactPath3 = GameRefs.Timer.ExactPath3;
    private const string ExactPath2 = GameRefs.Timer.ExactPath2;

    private const string CopyName = "VanillaTimer";
    private const string PreferredPath = GameRefs.Timer.PreferredPath;

    internal static readonly List<PuzzleTimer> all = new List<PuzzleTimer>();

    // Set in the Inspector (filled in by the loader); change it with SetDuration
    internal float duration = 60f; // seconds the countdown starts from

    // Optional, set in the Inspector (filled in by the loader): bars that drain from full to empty as the time runs out
    private List<PuzzleProgressBar> progressBars;

    private float timeLeft;       // seconds remaining
    private bool running;         // is it counting down right now
    private bool idle = true;     // reset or never started: shows 00:00, StartTimer begins from the full duration

    // Read-only for scripts
    public float Duration => duration;
    public float TimeLeft => timeLeft;
    public bool IsRunning => running;

    // Called once when the countdown reaches zero. Wired up in the Inspector.
    private PuzzleEvent onFinished;

    private readonly List<TMP_Text> texts = new List<TMP_Text>(); // the text(s) that show the time
    private int shownSeconds = -1;
    private bool writeFailed;

    // The host's seconds left (as hundreds and the rest, to keep each number small), sent to
    // every other player (see SyncedValue in Plugin.cs)
    private SyncedValue syncHundreds, syncRest;
    private int hostHundreds, hostRest;

    // Called by the loader while the puzzle is still inactive
    internal void SetupTimer() {
        var log = Plugin.log;

        GameObject template = Templates.Find(ExactPath, ExactPath3, ExactPath2);
        if (template == null) {
            // Not at its known path: search every loaded object instead (slow)
            TimerTextController source = null;
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<TimerTextController>())) {
                TimerTextController candidate = obj.TryCast<TimerTextController>();
                if (candidate == null) continue;
                string path = PathOf(candidate.transform);
                if (path.Contains(CopyName)) continue;            // one of our own copies
                if (source == null) source = candidate;
                if (path.Contains(PreferredPath)) { source = candidate; break; }
            }
            if (source == null) {
                log.LogWarning($"[PuzzleTimer] No vanilla timer is loaded, so '{name}' has nothing to copy.");
                return;
            }
            template = source.gameObject;
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        Vector3 templateScale = template.transform.lossyScale;

        GameObject copy = Templates.Clone(template, transform);
        copy.name = CopyName;
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.SetActive(true);

        // Keep the vanilla timer's own world size whatever the creator's objects are scaled to
        // Unlike most components, the timer follows the creator's scale: its object's scale
        // multiplies the vanilla size (1,1,1 = the game's own size)
        copy.transform.localScale = templateScale;

        // Switch off the game's controller before the copy wakes up, so it never connects to
        // the vanilla puzzle's networked timer or overwrites the digits.
        // Its 'textField' is the one text the game writes the time to (textFront). The other
        // text (textBack) is a fixed row of bars that draws the unlit grey dots and must be left alone.
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TimerTextController>(), true)) {
            TimerTextController controller = c != null ? c.TryCast<TimerTextController>() : null;
            if (controller == null) continue;
            TMP_Text digits = controller.textField;
            if (digits != null) texts.Add(digits);
            controller.enabled = false;
        }

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

        // Fall back to the text named textFront if the controller did not point at one
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TMP_Text>(), true)) {
            TMP_Text text = c != null ? c.TryCast<TMP_Text>() : null;
            if (text == null) continue;
            if (texts.Count == 0 && text.name == "textFront") texts.Add(text);
        }
        if (texts.Count == 0) log.LogWarning($"[PuzzleTimer] The copy for '{name}' has no text to write the time on.");

        // Made here, during setup, so every PC hands out the same network ids in order
        syncHundreds = new SyncedValue(transform, "timer hundreds " + name);
        syncRest = new SyncedValue(transform, "timer rest " + name);
    }

    private static string PathOf(Transform t) {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    private void Start() {
        all.Add(this);
        if (syncHundreds != null) {
            syncHundreds.onChanged = v => { hostHundreds = v; timeLeft = hostHundreds * 100 + hostRest; };
            syncRest.onChanged = v => { hostRest = v; timeLeft = hostHundreds * 100 + hostRest; };
            syncHundreds.Register(); syncRest.Register();
        }
    }

    private void Update() {
        // Other players: the countdown runs on the host only; its time arrives through OnHostTime
        if (syncHundreds != null && !SyncedValue.IsHost) running = false;
        if (running) {
            timeLeft -= Time.deltaTime;
            if (timeLeft <= 0f) {
                timeLeft = 0f;
                running = false;
                if (onFinished != null) onFinished.Invoke();
            }
        }
        if (timeLeft < 0f) timeLeft = 0f;

        // The bar follows the time left (the host drives it; other players get it from the host's bar)
        if (progressBars != null && SyncedValue.IsHost) {
            float fraction = duration > 0f ? timeLeft / duration : 0f;
            foreach (PuzzleProgressBar bar in progressBars) if (bar != null) bar.ShowDrain(fraction);
        }

        // Rewrite the digits only when the whole-second value changes
        int seconds = Mathf.CeilToInt(timeLeft);
        if (syncHundreds != null && SyncedValue.IsHost) {   // host: send it
            syncHundreds.Push(seconds / 100);
            syncRest.Push(seconds % 100);
        }
        if (seconds == shownSeconds || writeFailed) return;
        shownSeconds = seconds;
        string label = (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00"); // 01:05, like the game's clock
        try {
            foreach (TMP_Text text in texts) text.text = label;
        } catch (Exception ex) {
            writeFailed = true; // log once, not every second
            Plugin.log.LogWarning($"[PuzzleTimer] '{name}' could not write the time: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- Callable from code or from an Inspector event ----
    // Host only: on other players' PCs these do nothing (the time reaches them from the host)

    // Starts the countdown from the full duration, or continues it if paused. A timer that was
    // reset or ran out starts again from the full duration.
    public void StartTimer() {
        if (!PuzzleSDK.IsHost) return;
        if (idle || timeLeft <= 0f) timeLeft = duration;
        idle = false;
        running = timeLeft > 0f;
    }

    // Pauses the countdown where it is; StartTimer continues it
    public void PauseTimer() { if (!PuzzleSDK.IsHost) return; running = false; }

    // Stops and shows 00:00; the next StartTimer begins from the full duration
    public void ResetTimer() { if (!PuzzleSDK.IsHost) return; running = false; timeLeft = 0f; idle = true; }

    // Changes the full duration, used from the next StartTimer after a reset (or after it runs
    // out). A running or paused countdown keeps its time left.
    public void SetDuration(float seconds) {
        if (!PuzzleSDK.IsHost) return;
        duration = Mathf.Max(0f, seconds);
    }

    private void OnDestroy() { all.Remove(this); }
}

} // namespace BigPuzzleSDK
