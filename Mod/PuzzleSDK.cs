using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// What a puzzle is, for the modded gourd tower. Must stay identical, in the same order, to the
// enum in the Unity SDK.
public enum PuzzleType { Puzzle, Tower, None }

// Runtime (mod) version of the puzzle's main class. Goes on the puzzle's root object.
// Runs the puzzle's start/stop (driven by the start switches), Win(), and the creator's Log().
public class PuzzleSDK : MonoBehaviour {
    public PuzzleSDK(IntPtr ptr) : base(ptr) { }

    internal bool won;     // true once Win() has been called
    internal bool running; // true between StartPuzzle() and StopPuzzle()

    // Read-only for scripts
    public bool IsWon => won;
    public bool IsRunning => running;

    // Set in the Inspector; the names must match the Unity-side fields.
    // Packs made before the root object's own position was used only; new puzzles spawn where the root sits in Unity
    internal Vector3 spawnPuzzleLocation;   // world position the puzzle's root is placed at
    internal Vector3 spawnPuzzleRotation;   // world rotation of the puzzle's root, in degrees (x, y, z)
    internal Transform debugTeleport;    // F1 teleports the local player here (testing only)

    // Set in the Inspector: what this puzzle is (not used yet: for the modded gourd tower)
    internal PuzzleType puzzleType = PuzzleType.Puzzle;

    // The puzzle's permanent id (from its pack), set by the loader: what the save file is keyed by
    internal string packId;

    // A save says this puzzle was won before: IsWon is true from the start, with no events
    internal void RestoreWon() { won = true; }

    // Filled in by the loader from the UnityEvents wired up in the Inspector (optional)
    private PuzzleEvent onPuzzleStart;
    private PuzzleEvent onPuzzleStop;
    private PuzzleEvent onWin;

    internal static readonly List<PuzzleSDK> all = new List<PuzzleSDK>();
    private void Awake() { all.Add(this); }

    // At spawn the puzzle is stopped, so onPuzzleStop fires once on the first frame: everything
    // wired to it starts in its "stopped" state. (Switches fire their own starting events too.)
    // Every PC fires this one for itself, so players who join later start the same way.
    private bool spawnEventFired;
    private bool dealt;   // a puzzle with no start switch has dealt its tiles
    private void Update() {
        // A puzzle with no start switch never "starts": it deals its tiles once, as soon as everything can sync
        if (!dealt && IsHost && !HasStartSwitch(transform) && PuzzlePile.AllReady(transform)) {
            dealt = true;
            PuzzlePile.Deal(transform);
        }
        if (spawnEventFired) return;
        spawnEventFired = true;
        if (onPuzzleStop != null) onPuzzleStop.InvokeLocal();
    }
    private void OnDestroy() {
        all.Remove(this);
    }

    // Whether the puzzle that owns this object is switched on. A puzzle with no start switch is always
    // on; with one, it is on between StartPuzzle() and StopPuzzle() (Win() does not stop it).
    // The puzzle (PuzzleSDK) that owns this object, or null
    internal static PuzzleSDK Of(Transform t) {
        if (t == null) return null;
        int root = t.root.GetInstanceID();
        foreach (PuzzleSDK p in all)
            if (p != null && p.transform.root.GetInstanceID() == root) return p;
        return null;
    }

    internal static bool HasStartSwitch(Transform t) {
        if (t == null) return false;
        int root = t.root.GetInstanceID();
        foreach (PuzzleStartSwitch s in PuzzleStartSwitch.all)
            if (s != null && s.transform.root.GetInstanceID() == root) return true;
        return false;
    }

    internal static bool IsOnFor(Transform t) {
        if (t == null) return true;
        int root = t.root.GetInstanceID();
        if (!HasStartSwitch(t)) return true;
        foreach (PuzzleSDK p in all)
            if (p != null && p.transform.root.GetInstanceID() == root) return p.running;
        return true;
    }

    // Starts the puzzle. Only the start switch calls this; creators react through onPuzzleStart.
    internal void StartPuzzle() {
        if (running) return;
        running = true;
        SetLiveSignals(true);
        // Each display and answer speaker takes a different tile from the puzzle (the host deals; it reaches everyone)
        PuzzlePile.Deal(transform);
        if (onPuzzleStart != null) onPuzzleStart.Invoke();
    }

    // Every live signal in this puzzle is lit while the puzzle is running
    private void SetLiveSignals(bool lit) {
        int myRoot = transform.root.GetInstanceID();
        foreach (PuzzleLiveSignal signal in PuzzleLiveSignal.all.ToArray())
            if (signal != null && signal.transform.root.GetInstanceID() == myRoot) signal.SetOn(lit);
    }

    // Stops the puzzle. Called when the start switch is flipped back.
    internal void StopPuzzle() {
        if (!running) return;
        running = false;
        SetLiveSignals(false);
        PuzzlePile.Clear(transform);   // displays go blank, answer speakers go quiet
        if (onPuzzleStop != null) onPuzzleStop.Invoke();
    }

    // True on the PC that runs the puzzle: the host, or a single-player game. Every player runs
    // the puzzle's events, but only the host's calls to SDK functions that change the puzzle
    // count (they reach everyone through the SDK's sync); on other players' PCs those calls are
    // ignored. Check this in your own code before deciding anything (e.g. with Random) that
    // every player has to agree on.
    public static bool IsHost { get { return PuzzleEvent.RunsHere; } }

    // The puzzle is solved: release every gourd vice and fire every confetti in this puzzle,
    // then fire onWin. The puzzle keeps running: players stop it themselves by flipping the start switch back.
    public void Win() {
        if (!IsHost) return;   // the host wins for everyone
        if (won) return;
        won = true;

        int myRoot = transform.root.GetInstanceID();
        int count = 0;
        foreach (PuzzleGourdVice vice in PuzzleGourdVice.all.ToArray()) {
            if (vice == null || vice.transform.root.GetInstanceID() != myRoot) continue;
            vice.Release();
            count++;
        }
        foreach (PuzzleConfetti confetti in PuzzleConfetti.all.ToArray()) {
            if (confetti == null || confetti.transform.root.GetInstanceID() != myRoot) continue;
            confetti.Fire();
        }

        if (onWin != null) onWin.Invoke();
    }
}

} // namespace BigPuzzleSDK
