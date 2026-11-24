using System;
using UnityEngine;

namespace BigPuzzleSDK {

    // Must stay identical, in the same order, to the enum in the Unity SDK.
    public enum PuzzleCullMode { Default, NeverCull, CullBeyondDistance }

    // Runtime (mod) version of the SDK's PuzzleCulling: changes when the pictures under this object stop being drawn for
    // players far away. It does nothing by itself: PuzzleCuller (the loop that does the hiding) reads it.
    public class PuzzleCulling : MonoBehaviour {
        public PuzzleCulling(IntPtr ptr) : base(ptr) { }

        // Set in the Inspector (filled in by the loader); the names must match the Unity-side fields
        internal PuzzleCullMode mode = PuzzleCullMode.Default;
        internal float distance = 200f;
    }
}
