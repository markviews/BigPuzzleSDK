using System;

namespace BigPuzzleSDK {

// The description shown in an info box at the top of an SDK component's Inspector:
//   [PuzzleInfo("What this does, in a sentence or two.")]
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
internal sealed class PuzzleInfoAttribute : Attribute {
    internal readonly string text;
    internal PuzzleInfoAttribute(string text) { this.text = text; }
}

}
