// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;

namespace BigPuzzleSDK {

public enum PuzzleGlassStyle { Window, Rippled, Clear }
public enum PuzzleGlassSound { Muffles, Thick, Soundproof }

// Turns this object's mesh into the game's puzzle glass. Use any mesh, at any size or shape (a
// scaled cube makes a pane). In-game it gets the game's glass material and becomes a solid
// glass wall that muffles (or fully blocks) voices, like the game's own windows.
// The material set here doesn't matter: it's replaced in-game, and the editor shows a preview.
// Also works on a parent: every renderer and collider in its children becomes glass too.
[PuzzleInfo("Turns this mesh into the game's glass. Any shape or size.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleGlass : MonoBehaviour {
    [Tooltip("Window: most of the game's windows. Rippled: the wavier glass of the gourd boxes. Clear: barely distorts.")]
    [SerializeField] private PuzzleGlassStyle style;

    [Tooltip("Muffles: voices come through quieter, like the game's windows. Thick: more muffled. Soundproof: blocks voices completely.")]
    [SerializeField] private PuzzleGlassSound sound;

    [Tooltip("Adds the black wire mesh the game's soundproof windows have, just inside the glass.")]
    [SerializeField] private bool wire;
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(PuzzleGlass))]
internal class PuzzleGlassEditor : UnityEditor.Editor {
    public override void OnInspectorGUI() {
        PuzzleChecks.DrawInfo((Component)target);
        DrawDefaultInspector();
        var glass = (PuzzleGlass)target;
        if (glass.GetComponentsInChildren<Renderer>(true).Length == 0)
            UnityEditor.EditorGUILayout.HelpBox("No renderer on this object or its children, so there's no glass to see. Use a Cube (or any mesh).", UnityEditor.MessageType.Warning);
        PuzzleChecks.DrawHelpBoxes(glass);
        Collider[] colliders = glass.GetComponentsInChildren<Collider>(true);
        if (colliders.Length > 0) {
            int triggers = 0;
            foreach (Collider c in colliders) if (c.isTrigger) triggers++;
            if (triggers > 0)
                UnityEditor.EditorGUILayout.HelpBox($"{triggers} collider(s) are triggers; they will be made solid in-game.", UnityEditor.MessageType.Warning);
        }
    }
}
#endif

}
