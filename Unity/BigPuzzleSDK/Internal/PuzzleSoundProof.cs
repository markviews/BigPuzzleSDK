// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

    public enum PuzzleSoundProofMaterial { Wood, Glass }

// Makes the colliders on this object AND its children block players' voices, like the game's
// soundproof rooms and doors. The colliders become solid walls on the game's voice-blocking layer.
// Put it on a single wall, or on the parent of a whole room to soundproof every wall in it.
// Open() switches its colliders off so players can walk and talk through (e.g. a doorway);
// Close() turns them back on. The host decides; everyone, including late joiners, follows.
[PuzzleInfo("The colliders on this object and its children block players' voices, like the game's soundproof rooms. Open() lets players walk and talk through.")]
[AddComponentMenu("Big Puzzle SDK/Soundproof")]
public class PuzzleSoundProof : MonoBehaviour {
    [Tooltip("Which of the game's soundproof materials to use. Both block voices; they may sound slightly different.")]
    [SerializeField] private PuzzleSoundProofMaterial material;

    // Read-only for scripts: true while it blocks (it always starts blocking; Open() / Close() change it)
    public bool IsBlocking => true;

    public void Close() { }
    public void Open() { }
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(PuzzleSoundProof))]
internal class PuzzleSoundProofEditor : UnityEditor.Editor {
    public override void OnInspectorGUI() {
        PuzzleChecks.DrawInfo((Component)target);
        DrawDefaultInspector();
        var soundproof = (PuzzleSoundProof)target;
        PuzzleChecks.DrawHelpBoxes(soundproof);
        Collider[] colliders = soundproof.GetComponentsInChildren<Collider>(true);
        if (colliders.Length == 0) return;
        int triggers = 0;
        foreach (Collider c in colliders) if (c.isTrigger) triggers++;
        UnityEditor.EditorGUILayout.HelpBox($"Blocks voices through {colliders.Length} collider(s) on this object and its children.", UnityEditor.MessageType.Info);
        if (triggers > 0)
            UnityEditor.EditorGUILayout.HelpBox($"{triggers} of them are triggers; they will be made solid in-game (voices pass through triggers).", UnityEditor.MessageType.Warning);
    }
}
#endif

}
