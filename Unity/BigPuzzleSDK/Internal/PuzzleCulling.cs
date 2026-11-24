// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// Must stay identical, in the same order, to the enum in the mod.
public enum PuzzleCullMode { Default, NeverCull, CullBeyondDistance }

// Changes when the pictures of this object, and everything under it, stop being drawn for players far away. Only the
// drawing changes: scripts, colliders, networking and sound are not affected.
[PuzzleInfo("Changes how this object, and everything under it, is hidden when far from the player. Without it, small parts are hidden first and big ones stay visible.")]
[AddComponentMenu("Big Puzzle SDK/Culling")]
public class PuzzleCulling : MonoBehaviour {
    [Tooltip("Default: the automatic behaviour (small parts hide first, big ones stay visible). Also cancels an override on a parent.\nNever Cull: Never culled by PuzzleSDK. The vanilla game still culls super far away objects.\nCull Beyond Distance: hidden when the player is farther than Distance.")]
    [SerializeField] private PuzzleCullMode mode = PuzzleCullMode.Default;

    [Tooltip("Metres from the player to the nearest edge of this object (everything under it), beyond which it is hidden.")]
    [Min(1f)]
    [SerializeField] private float distance = 200f;
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(PuzzleCulling))]
internal class PuzzleCullingEditor : UnityEditor.Editor {
    public override void OnInspectorGUI() {
        PuzzleChecks.DrawInfo((Component)target);
        serializedObject.Update();
        UnityEditor.SerializedProperty mode = serializedObject.FindProperty("mode");
        UnityEditor.EditorGUILayout.PropertyField(mode, new GUIContent("Mode", mode.tooltip));
        if (mode.enumValueIndex == (int)PuzzleCullMode.CullBeyondDistance) {
            UnityEditor.SerializedProperty d = serializedObject.FindProperty("distance");
            UnityEditor.EditorGUILayout.PropertyField(d, new GUIContent("Distance", d.tooltip));
        }
        serializedObject.ApplyModifiedProperties();

        string help = mode.enumValueIndex == (int)PuzzleCullMode.NeverCull ? "Never culled by PuzzleSDK. The vanilla game still culls super far away objects."
            : mode.enumValueIndex == (int)PuzzleCullMode.CullBeyondDistance ? "Hidden when the player is farther than Distance, whatever its size."
            : "The automatic behaviour. Use this to cancel an override on a parent object.";
        UnityEditor.EditorGUILayout.HelpBox(help, UnityEditor.MessageType.None);
        PuzzleChecks.DrawHelpBoxes((PuzzleCulling)target);
    }
}
#endif

}
