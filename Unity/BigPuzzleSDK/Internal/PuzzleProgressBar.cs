// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

// Which of the game's bars it looks like
public enum PuzzleBarStyle { Straight, Spiral }

// The game's dot-matrix progress bar. Scale it freely (unlike most SDK components).
// Its look is automatic: a short flash on Increment(), victory when full, failure after Fail()
// (for one second, then it empties), normal otherwise.
[PuzzleInfo("Can be scaled.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleProgressBar : MonoBehaviour {
    [Tooltip("Straight: the usual dot-matrix bar. Spiral: the Memory Bombs spiral.")]
    [SerializeField] private PuzzleBarStyle style = PuzzleBarStyle.Straight;

    [Tooltip("Progress at which the bar is full.")]
    [Min(1)]
    [SerializeField] private int total = 10;

    [Tooltip("The bar became full.")]
    [SerializeField] private UnityEvent onFull;

    // Read-only for scripts
    public int Total => total;
    public int Progress => 0;      // always starts at 0
    public bool IsFull => false;

    // Host only. Progress above the new total is cut down to it.
    public void SetTotal(int value) { }
    // Host only. +1; does nothing once full.
    public void Increment() { }
    // Host only. Failure look for one second, then the bar empties (as Reset()).
    public void Fail() { }
    // Host only. Empty, normal look.
    public void Reset() { }
}

}
