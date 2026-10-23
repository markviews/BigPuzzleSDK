// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// The game's countdown clock. Scale it freely (unlike most SDK components).
[PuzzleInfo("Can be scaled. Add Progress Bars and they drain as the time runs out.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleTimer : MonoBehaviour {
    [Tooltip("Seconds it counts down from.")]
    [Min(0f)]
    [SerializeField] private float duration = 60f;

    [Tooltip("Optional. These bars drain from full to empty as the time runs out.")]
    [SerializeField] private List<PuzzleProgressBar> progressBars = new List<PuzzleProgressBar>();

    [SerializeField] private UnityEvent onFinished;

    // Read-only for scripts
    public float Duration => duration;
    public float TimeLeft => 0f;
    public bool IsRunning => false;

    // Host only. Starts the countdown from the full duration, or continues it if paused.
    public void StartTimer() { }
    // Host only. Pauses where it is.
    public void PauseTimer() { }
    // Host only. Stops and shows 00:00. The next StartTimer begins from the full duration.
    public void ResetTimer() { }
    // Host only. Used from the next StartTimer after a reset or after it runs out.
    public void SetDuration(float seconds) { }
}

}
