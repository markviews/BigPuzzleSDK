// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// Lights one of the pairs at random; players press its button. Every right press fills the Progress Bars by one.
[PuzzleInfo("Call Begin() to start. One pair at a time lights up and players must press its button. Each right press fills the Progress Bars by one; after Rounds right presses the lights turn off and On End fires. A wrong button turns the lights off and fires On Fail, then On End. Begin() sets every Progress Bar's Total to Rounds and empties them first. With Hold At End on, pressing does nothing: each lit pair has a time limit, and when it runs out that pair's button must be held and no other, or it fails.")]
[AddComponentMenu("Big Puzzle SDK/Press In Order")]
public class PuzzlePressInOrder : MonoBehaviour {
    // Pairs: lights[i] belongs with buttons[i]. Drawn side by side in the Inspector; drag a Hold Button or Poke Button in.
    [SerializeField] private List<PuzzleLight> lights = new List<PuzzleLight>();
    [SerializeField] private List<PuzzleButtonBase> buttons = new List<PuzzleButtonBase>();

    [Tooltip("Every bar goes up on a right press, so they all show the same progress.")]
    [SerializeField] private List<PuzzleProgressBar> progressBars = new List<PuzzleProgressBar>();

    [Tooltip("Right presses needed to win. Sets every Progress Bar's Total to this when it starts.")]
    [Min(1)]
    [SerializeField] private int rounds = 5;

    [Tooltip("Timed mode. Each lit pair has a set time and pressing does nothing. When the time runs out its button must be held (Hold Button) or occupied (Presence Sensor), and no other button may be. Poke Buttons can't be held.")]
    [SerializeField] private bool holdAtEnd = false;

    [Tooltip("Timed mode: how long each pair stays lit, in seconds.")]
    [Min(0.5f)]
    [SerializeField] private float phaseSeconds = 5f;

    [Tooltip("Timed mode: bars that show the time left in the phase. Each starts full and empties as the phase ticks. Use bars other than the Progress Bars above.")]
    [SerializeField] private List<PuzzleProgressBar> phaseBars = new List<PuzzleProgressBar>();

    [Tooltip("Begin() was called. The first light is about to come on.")]
    [SerializeField] private UnityEvent onBegin;

    [Tooltip("A button other than the lit one was pressed (or, in Hold At End mode, the wrong buttons were held when the phase ended). The lights are already off, and Fail() has been called on every Progress Bar and Phase Bar.")]
    [SerializeField] private UnityEvent onFail;

    [Tooltip("The game is over, however it ended. The lights are already off.")]
    [SerializeField] private UnityEvent onEnd;

    // Read-only for scripts
    public bool IsRunning => false;

    // Host only. Sets the Progress Bars' Total to Rounds, empties them, fires On Begin, then lights a random pair.
    public void Begin() { }
    // Host only. Turns the lights off, stops, calls Fail() on every Progress Bar and fires On End; presses are ignored until Begin(). Does nothing if it isn't running.
    public void End() { }
}

}
