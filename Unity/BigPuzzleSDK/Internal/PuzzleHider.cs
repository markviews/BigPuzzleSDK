// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

// Keeps this object, and everything under it, only in the lobby sizes ticked below. In the
// others it is switched off when the puzzle loads, and SDK components under it aren't built.
// Use it for player-count versions of a room, e.g. a 2-player wall and a 4-player wall.
[PuzzleInfo("Keeps this object only in the lobby sizes selected below. In the others it is switched off when the puzzle loads.")]
[AddComponentMenu("Big Puzzle SDK/Hider")]
public class PuzzleHider : MonoBehaviour {
    [Tooltip("Show in 2-player lobbies.")]
    [SerializeField] private bool show2 = true;

    [Tooltip("Show in 3-player lobbies.")]
    [SerializeField] private bool show3 = true;

    [Tooltip("Show in 4-player lobbies (and larger).")]
    [SerializeField] private bool show4 = true;
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(PuzzleHider))]
internal class PuzzleHiderEditor : UnityEditor.Editor {
    public override void OnInspectorGUI() {
        PuzzleChecks.DrawInfo((Component)target);
        serializedObject.Update();
        UnityEditor.EditorGUILayout.LabelField("Show this object in", UnityEditor.EditorStyles.boldLabel);
        UnityEditor.SerializedProperty p2 = serializedObject.FindProperty("show2");
        UnityEditor.SerializedProperty p3 = serializedObject.FindProperty("show3");
        UnityEditor.SerializedProperty p4 = serializedObject.FindProperty("show4");
        UnityEditor.EditorGUILayout.PropertyField(p2, new GUIContent("2 players", p2.tooltip));
        UnityEditor.EditorGUILayout.PropertyField(p3, new GUIContent("3 players", p3.tooltip));
        UnityEditor.EditorGUILayout.PropertyField(p4, new GUIContent("4+ players", p4.tooltip));
        serializedObject.ApplyModifiedProperties();

        PuzzleChecks.DrawHelpBoxes((PuzzleHider)target);
        if (((PuzzleHider)target).GetComponent<PuzzleSDK>() != null)
            UnityEditor.EditorGUILayout.HelpBox("This is the puzzle's root: unticked lobby sizes won't get the puzzle at all.", UnityEditor.MessageType.Info);
    }
}
#endif

}
