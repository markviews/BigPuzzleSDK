using System;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleButtonBase: what Hold Button and Poke Button share, so
// other components (Press In Order) can listen to either kind.
public class PuzzleButtonBase : MonoBehaviour {
    public PuzzleButtonBase(IntPtr ptr) : base(ptr) { }

    // Called on the host at every press
    internal Action pressedHook;
}

} // namespace BigPuzzleSDK
