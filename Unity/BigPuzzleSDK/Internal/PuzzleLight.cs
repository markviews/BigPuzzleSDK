// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace BigPuzzleSDK {

public enum PuzzleLightType { TableRed, Pendant, Spotlight_Black, Spotlight_Red, Tube, Circle, Ring, Conductor }

// A game lamp (Conductor is the small panel light from the conductor puzzles). Starts off: call TurnOn() from PuzzleSDK.onPuzzleStop to start it on.
[PuzzleInfo("White (#FFFFFF) keeps the lamp's default colour.")]
[AddComponentMenu("")]   // hidden from Add Component: comes from the prefab
public class PuzzleLight : MonoBehaviour {
    [SerializeField] private PuzzleLightType type;

    [Tooltip("White (1, 1, 1, 1) keeps the lamp's own colour; any other colour replaces it.")]
    [SerializeField] private Color color = Color.white;

    [Tooltip("How far the light reaches, in metres. 0 = the lamp's own. The mod log lists each lamp's own values when the puzzle loads.")]
    [Min(0f)]
    [SerializeField] private float range = 0f;

    [Tooltip("How bright the light is while the lamp is on. 0 = the lamp's own.")]
    [Min(0f)]
    [SerializeField] private float intensity = 0f;

    [Tooltip("The width of the cone, in degrees, for spotlights. 0 = the lamp's own. Does nothing for lamps that aren't spotlights.")]
    [Range(0f, 179f)]
    [SerializeField] private float spotAngle = 0f;

    // Read-only for scripts
    public Color CurrentColor => color;
    public bool IsOn => false;
    public bool IsParty => false;

    // Host only
    public void TurnOn() { }
    public void TurnOff() { }
    // "#FF0000", "FF0000", "#F00" or a colour name like "red"
    public void SetColor(string hex) { }
    // Host only. Turns the lamp on and blends through random bright colours, holding each for a random 1.5 to 6 seconds, and never goes fully dark. It pulses gently as it goes. Made for Spotlight_Black.
    public void StartParty() { }
    // Host only. Ends party mode with a one second fade back to the lamp's own (white) look. The lamp stays on.
    public void StopParty() { }

#if UNITY_EDITOR
    // Selected: shows the Range and Spot Angle live. A spotlight shows where its beam would land: rays
    // across the cone stop at the first solid collider in the scene (floors, walls), and the lit patch is
    // shaded. Other lamps get a sphere. The beam's direction and starting point inside the lamp come
    // from the game's own lamp (worked out from its template). With Range at 0 the lamp's own values
    // are used, which only the mod log knows. This is a guide: it ignores shadows and the game's
    // light limits, and a collider in the way (a truss, say) stops the beam like a wall would.
    private void OnDrawGizmosSelected() {
        bool spot = type == PuzzleLightType.Spotlight_Black || type == PuzzleLightType.Spotlight_Red;
        Color shown = color == Color.white ? new Color(1f, 0.95f, 0.5f) : color;
        string label;
        if (range <= 0f) {
            label = "Range: the lamp's own (see the mod log)";
        } else if (!spot) {
            Gizmos.color = new Color(shown.r, shown.g, shown.b, 0.9f);
            Gizmos.DrawWireSphere(transform.position, range);
            label = "Range " + range.ToString("0.#") + " m";
        } else {
            // Where the game's lamp aims its beam, relative to this object
            Vector3 beam = type == PuzzleLightType.Spotlight_Red ? new Vector3(-0.069f, -0.566f, -0.821f) : new Vector3(0.641f, -0.663f, -0.387f);
            Vector3 start = type == PuzzleLightType.Spotlight_Red ? new Vector3(0f, 0.072f, -0.041f) : new Vector3(0.032f, 0.067f, -0.019f);
            Quaternion aim = transform.rotation * Quaternion.LookRotation(beam);
            Vector3 apex = transform.position + transform.rotation * start;
            label = spotAngle > 0f
                ? DrawBeam(apex, aim, shown) + "Range " + range.ToString("0.#") + " m, angle " + spotAngle.ToString("0.#") + " deg"
                : "Range " + range.ToString("0.#") + " m (angle: the lamp's own)";
            if (spotAngle <= 0f) {
                Gizmos.color = new Color(shown.r, shown.g, shown.b, 0.9f);
                Gizmos.DrawLine(apex, apex + aim * Vector3.forward * range);
            }
        }
    }

    // Rays around the cone's edge, each stopping where it hits something solid. Returns a label prefix
    // saying how far the middle of the beam travels.
    private string DrawBeam(Vector3 apex, Quaternion aim, Color shown) {
        Physics.SyncTransforms();
        float half = spotAngle * 0.5f * Mathf.Deg2Rad;
        const int rays = 32;
        var ends = new Vector3[rays];
        for (int i = 0; i < rays; i++) {
            float a = i * 2f * Mathf.PI / rays;
            Vector3 dir = aim * new Vector3(Mathf.Sin(half) * Mathf.Cos(a), Mathf.Sin(half) * Mathf.Sin(a), Mathf.Cos(half));
            ends[i] = Reach(apex, dir, out _);
        }
        Vector3 centreDir = aim * Vector3.forward;
        Vector3 centre = Reach(apex, centreDir, out bool centreHit);

        Color line = new Color(shown.r, shown.g, shown.b, 0.9f);
        UnityEditor.Handles.color = new Color(shown.r, shown.g, shown.b, 0.18f);
        for (int i = 0; i < rays; i++)
            UnityEditor.Handles.DrawAAConvexPolygon(centre, ends[i], ends[(i + 1) % rays]);
        Gizmos.color = line;
        for (int i = 0; i < rays; i++) {
            Gizmos.DrawLine(ends[i], ends[(i + 1) % rays]);
            if (i % 4 == 0) Gizmos.DrawLine(apex, ends[i]);
        }
        Gizmos.DrawLine(apex, centre);
        if (centreHit) Gizmos.DrawWireSphere(centre, 0.1f);
        return centreHit ? "Middle lands " + Vector3.Distance(apex, centre).ToString("0.#") + " m away. " : "Middle doesn't reach anything. ";
    }

    // The first solid thing along the ray within Range, or the end of the range
    private Vector3 Reach(Vector3 from, Vector3 dir, out bool hitSomething) {
        hitSomething = false;
        RaycastHit[] hits = Physics.RaycastAll(from, dir, range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));   // nearest first
        foreach (RaycastHit h in hits) {
            if (h.distance < 0.2f || h.collider.transform.IsChildOf(transform)) continue;   // the lamp's own body
            hitSomething = true;
            return h.point;
        }
        return from + dir * range;
    }
#endif
}

}
