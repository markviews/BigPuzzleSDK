using System.Runtime.CompilerServices;

// The exporter window lives in the editor assembly and uses the SDK's editor checks
[assembly: InternalsVisibleTo("BigPuzzleSDK.Editor")]
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
