using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzlePressInOrder. It copies nothing: it watches the buttons
// the creator paired with lights. Begin() lights one pair at random; pressing its button fills the
// Progress Bars by one and lights the next. A wrong button turns the lights off and fires
// On Fail. After Rounds right presses the lights turn off; On End fires however it ends (success, a wrong press after On Fail, or End()). It stops until Begin() again.
// Everything is decided on the host; the lights and bars reach other players from there.
public class PuzzlePressInOrder : MonoBehaviour {
    public PuzzlePressInOrder(IntPtr ptr) : base(ptr) { }

    // Set in the Inspector (filled in by the loader). The names must match the Unity-side fields.
    private List<PuzzleLight> lights;
    private List<PuzzleButtonBase> buttons;     // each a Hold Button or a Poke Button
    private List<PuzzleProgressBar> progressBars;
    private List<PuzzleProgressBar> phaseBars;   // timed mode: full at the start of a phase, emptying as it ticks
    private int rounds = 5;
    private bool holdAtEnd;                     // timed mode: hold the lit button when the phase ends
    private float phaseSeconds = 5f;
    private PuzzleEvent onBegin;
    private PuzzleEvent onFail;
    private PuzzleEvent onEnd;

    private const float NextLightDelay = 0.5f;   // a short dark moment, so the same light twice in a row still blinks

    private readonly List<int> usable = new List<int>();                                 // pairs with both a light and a button
    private readonly Dictionary<int, PuzzlePokeButton> pokeAt = new Dictionary<int, PuzzlePokeButton>();
    private bool running;
    private int target = -1;                      // the pair that is lit right now; -1 = none
    private float nextAt;
    private float phaseEnd;                       // timed mode: when the lit phase ends
    private float phaseLength;

    // Read-only for scripts
    public bool IsRunning => running;

    private void Start() {
        var log = Plugin.log;
        int count = Math.Max(lights != null ? lights.Count : 0, buttons != null ? buttons.Count : 0);
        for (int i = 0; i < count; i++) {
            PuzzleLight light = lights != null && i < lights.Count ? lights[i] : null;
            PuzzleButtonBase b = buttons != null && i < buttons.Count ? buttons[i] : null;
            if (light == null || b == null) {
                log.LogWarning($"[PuzzlePressInOrder] '{name}': pair {i + 1} is missing its {(light == null ? "light" : "button")}, so it is skipped.");
                continue;
            }
            int pair = i;
            b.pressedHook += () => OnPairPressed(pair);
            PuzzlePokeButton poke = b.TryCast<PuzzlePokeButton>();
            if (poke != null) pokeAt[pair] = poke;
            usable.Add(i);
        }
        if (usable.Count == 0) log.LogWarning($"[PuzzlePressInOrder] '{name}' has no complete light and button pair, so Begin() does nothing.");
    }

    private void Update() {
        if (!running || !PuzzleSDK.IsHost) return;
        if (target >= 0) {
            if (!holdAtEnd) return;
            if (Time.time >= phaseEnd) EndPhase();
            else DrainPhaseBars((phaseEnd - Time.time) / phaseLength);
            return;
        }
        if (Time.time < nextAt) return;
        target = usable[UnityEngine.Random.Range(0, usable.Count)];   // the same pair twice in a row is allowed
        lights[target].TurnOn();
        phaseLength = Math.Max(0.5f, phaseSeconds);
        phaseEnd = Time.time + phaseLength;
        if (holdAtEnd) DrainPhaseBars(1f);
    }

    private void DrainPhaseBars(float fraction) {
        if (phaseBars != null) foreach (PuzzleProgressBar bar in phaseBars) if (bar != null) bar.ShowDrain(fraction);
    }

    // Is this pair's button held down (Hold Button) or occupied (Presence Sensor) right now? A Poke Button never is.
    private bool IsHeld(int pair) {
        PuzzleButtonBase b = buttons[pair];
        if (b == null) return false;
        PuzzleButton hold = b.TryCast<PuzzleButton>();
        if (hold != null) return hold.IsHeld;
        PuzzlePresenceSensor sensor = b.TryCast<PuzzlePresenceSensor>();
        return sensor != null && sensor.IsHeld;
    }

    // Timed mode: the phase has ended. Only now does it matter what is held: the lit button must be, and no other.
    private void EndPhase() {
        DrainPhaseBars(0f);
        bool right = IsHeld(target);
        foreach (int i in usable) if (i != target && IsHeld(i)) right = false;
        if (!right) {
            Finish();
            FailBars();
            if (onFail != null) onFail.Invoke();
            if (onEnd != null) onEnd.Invoke();
            return;
        }
        Succeed();
    }

    // The lit pair was done right: light off, bars up, next phase or the end
    private void Succeed() {
        lights[target].TurnOff();
        target = -1;
        bool full = false;
        if (progressBars != null) foreach (PuzzleProgressBar bar in progressBars) {
            if (bar == null) continue;
            bar.Increment();
            if (bar.IsFull) full = true;
        }
        if (full) {
            Finish();
            if (onEnd != null) onEnd.Invoke();
        } else nextAt = Time.time + NextLightDelay;
    }

    private void OnPairPressed(int pair) {
        if (!running || !PuzzleSDK.IsHost || holdAtEnd) return;   // timed mode ignores presses; only the end of the phase counts
        // A poke button stays off after a press; keep it ready for the next one
        if (pokeAt.TryGetValue(pair, out PuzzlePokeButton poke) && poke != null) poke.SetClickable(true);
        if (target < 0) return;   // between lights: ignored

        if (pair != target) {
            Finish();
            FailBars();
            if (onFail != null) onFail.Invoke();
            if (onEnd != null) onEnd.Invoke();
            return;
        }
        Succeed();
    }

    // Shows the failure look on every bar, the round counters and the phase timers (each empties itself after a second)
    private void FailBars() {
        if (progressBars != null) foreach (PuzzleProgressBar bar in progressBars) if (bar != null) bar.Fail();
        if (phaseBars != null) foreach (PuzzleProgressBar bar in phaseBars) if (bar != null) bar.Fail();
    }

    private void Finish() {
        running = false;
        target = -1;
        foreach (int i in usable) lights[i].TurnOff();
        if (phaseBars != null) foreach (PuzzleProgressBar bar in phaseBars) if (bar != null) bar.Reset();
    }

    // ---- Callable from code or from an Inspector event ----
    // Host only. Sets the Progress Bars' Total to Rounds, empties them, fires On Begin, then lights a random pair.
    public void Begin() {
        if (!PuzzleSDK.IsHost) return;
        if (usable.Count == 0) return;
        Finish();
        if (progressBars != null) foreach (PuzzleProgressBar bar in progressBars) if (bar != null) { bar.SetTotal(Math.Max(1, rounds)); bar.Reset(); }
        running = true;
        nextAt = Time.time;
        if (onBegin != null) onBegin.Invoke();
    }

    // Host only. Turns the lights off, stops, fails the Progress Bars and fires On End; presses are ignored until Begin(). Does nothing if it isn't running.
    public void End() {
        if (!PuzzleSDK.IsHost || !running) return;
        Finish();
        FailBars();
        if (onEnd != null) onEnd.Invoke();
    }
}

} // namespace BigPuzzleSDK
