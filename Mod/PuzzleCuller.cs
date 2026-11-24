using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

    // Stops drawing the small parts of puzzles that are far from the camera. Automatic, and by size: a renderer is hidden when it
    // would be tiny on screen (its radius divided by its distance is below MinSize), so big structures stay visible from far
    // away and small details drop out first. Lights are switched off when the camera is well past their range. A PuzzleCulling on
    // an object (or a parent) changes that for what is under it: Never Cull, or Cull Beyond Distance. Only the picture changes:
    // logic, colliders, networking and sound keep running. (Parts outside the camera's view are skipped by the engine already, so
    // no extra check is made for them.) Settings: BepInEx/config/markviews.BigPuzzleSDK.cfg, Culling section.
    internal static class PuzzleCuller {
        internal static bool Enabled = true;
        internal static float MinSize = 0.005f;      // radius / distance below this: hidden (0.005: a part a few pixels wide)
        private const float Comeback = 1.15f;        // a hidden part returns when it is this much above the limit (no flicker)
        private const float RefreshSeconds = 8f;     // how often the list of parts and their sizes is rebuilt

        // What a PuzzleCulling says about the parts under it
        private sealed class Group {
            public PuzzleCullMode mode;
            public float distance;
            public Vector3 min, max;      // the parts under it, together
            public bool hasBounds;
            public float current;         // the camera's distance to it this tick
        }

        private sealed class Part {
            public Renderer renderer;
            public Light light;
            public Group group;           // null: no PuzzleCulling above it
            public Vector3 center;
            public float radius;          // renderer: half its size; light: the distance beyond which it is switched off
            public bool hidden;
        }

        private sealed class State {
            public readonly Dictionary<int, Part> parts = new Dictionary<int, Part>();
            public readonly Dictionary<int, Group> groups = new Dictionary<int, Group>();
            public float refreshAt;
            public int lastLogged = -1;
        }

        private static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        private static readonly HashSet<int> alive = new HashSet<int>();
        private static float nextTick;

        internal static void Tick(List<GameObject> puzzles) {
            if (!Enabled || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.25f;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            alive.Clear();
            foreach (GameObject puzzle in puzzles) {
                if (puzzle == null) continue;
                try { Update(puzzle, eye); } catch (Exception ex) { Plugin.log.LogWarning($"[Cull] '{puzzle.name}': {ex.GetType().Name}: {ex.Message}"); }
            }
            if (states.Count > alive.Count) {   // puzzles that were rebuilt or destroyed
                var gone = new List<int>();
                foreach (int id in states.Keys) if (!alive.Contains(id)) gone.Add(id);
                foreach (int id in gone) states.Remove(id);
            }
        }

        private static void Update(GameObject puzzle, Vector3 eye) {
            int id = puzzle.GetInstanceID();
            alive.Add(id);
            if (!states.TryGetValue(id, out State s)) { s = new State(); states[id] = s; }
            if (Time.unscaledTime >= s.refreshAt) Refresh(puzzle, s);

            foreach (Group g in s.groups.Values) {
                g.current = g.hasBounds ? DistanceToBox(g.min, g.max, eye) : 0f;
            }

            int hiddenCount = 0;
            foreach (Part p in s.parts.Values) {
                bool hide;
                Group g = p.group;
                if (g != null && g.mode == PuzzleCullMode.NeverCull) {
                    hide = false;
                } else if (g != null && g.mode == PuzzleCullMode.CullBeyondDistance) {
                    hide = p.hidden ? g.current > g.distance / Comeback : g.current > g.distance;
                } else {
                    float dx = p.center.x - eye.x, dy = p.center.y - eye.y, dz = p.center.z - eye.z;
                    float distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (p.renderer != null) {
                        float limit = distance * MinSize;   // the radius it needs to be worth drawing
                        hide = p.hidden ? p.radius < limit * Comeback : p.radius < limit;
                    } else {
                        hide = p.hidden ? distance > p.radius / Comeback : distance > p.radius;
                    }
                }
                if (hide != p.hidden) {
                    if (p.renderer != null) p.renderer.forceRenderingOff = hide;
                    else if (p.light != null) p.light.enabled = !hide;
                    p.hidden = hide;
                }
                if (hide) hiddenCount++;
            }
            if (s.lastLogged < 0) s.lastLogged = hiddenCount;
            else if (Math.Abs(hiddenCount - s.lastLogged) >= 25) {
                s.lastLogged = hiddenCount;
                Plugin.log.LogInfo($"[Cull] '{puzzle.name}': {hiddenCount} of {s.parts.Count} parts hidden");
            }
        }

        private static float DistanceToBox(Vector3 lo, Vector3 hi, Vector3 p) {
            float dx = Math.Max(Math.Max(lo.x - p.x, 0f), p.x - hi.x);
            float dy = Math.Max(Math.Max(lo.y - p.y, 0f), p.y - hi.y);
            float dz = Math.Max(Math.Max(lo.z - p.z, 0f), p.z - hi.z);
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // The PuzzleCulling nearest above (or on) this object, as a Group; null if there is none
        private static Group GroupOf(Transform t, State s) {
            Component c = t.GetComponentInParent(Il2CppType.Of<PuzzleCulling>(), true);
            PuzzleCulling pc = c != null ? c.TryCast<PuzzleCulling>() : null;
            if (pc == null) return null;
            int id = pc.GetInstanceID();
            if (!s.groups.TryGetValue(id, out Group g)) { g = new Group(); s.groups[id] = g; }
            g.mode = pc.mode;
            g.distance = Math.Max(1f, pc.distance);
            return g;
        }

        private static void Grow(Group g, Vector3 lo, Vector3 hi) {
            if (g == null) return;
            if (!g.hasBounds) { g.min = lo; g.max = hi; g.hasBounds = true; return; }
            g.min = new Vector3(Math.Min(g.min.x, lo.x), Math.Min(g.min.y, lo.y), Math.Min(g.min.z, lo.z));
            g.max = new Vector3(Math.Max(g.max.x, hi.x), Math.Max(g.max.y, hi.y), Math.Max(g.max.z, hi.z));
        }

        // Rebuilds the list of parts (a part keeps its hidden state), with each one's current position and size
        private static void Refresh(GameObject puzzle, State s) {
            s.refreshAt = Time.unscaledTime + RefreshSeconds;
            foreach (Group old in s.groups.Values) old.hasBounds = false;
            var seen = new HashSet<int>();
            foreach (Component c in puzzle.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
                Renderer r = c != null ? c.TryCast<Renderer>() : null;
                if (r == null) continue;
                int id = r.GetInstanceID();
                seen.Add(id);
                Bounds b = r.bounds;
                Vector3 size = b.size;
                Vector3 center = b.center;
                float radius = 0.5f * (float)Math.Sqrt(size.x * size.x + size.y * size.y + size.z * size.z);
                if (!s.parts.TryGetValue(id, out Part p)) {
                    if (r.forceRenderingOff) continue;   // already off for some other reason: not ours to manage
                    p = new Part { renderer = r };
                    s.parts[id] = p;
                }
                p.group = GroupOf(r.transform, s);
                p.center = center;
                // a huge one (a sky, a long trail) would be hidden at the wrong time: treat it as always worth drawing
                p.radius = radius > 250f ? float.MaxValue : radius;
                // The group's box counts only parts that are really there: not zero-sized ones (they sit at the world origin), not parked
                // or switched-off ones (templates kept out of sight), and not huge ones
                if (radius > 0.001f && radius <= 250f && r.enabled && r.gameObject.activeInHierarchy) { Vector3 half = size * 0.5f; Grow(p.group, center - half, center + half); }
            }
            foreach (Component c in puzzle.GetComponentsInChildren(Il2CppType.Of<Light>(), true)) {
                Light l = c != null ? c.TryCast<Light>() : null;
                if (l == null) continue;
                int id = l.GetInstanceID();
                seen.Add(id);
                if (!s.parts.TryGetValue(id, out Part p)) {
                    if (!l.enabled) continue;   // off already: leave it alone
                    p = new Part { light = l };
                    s.parts[id] = p;
                }
                p.group = GroupOf(l.transform, s);
                p.center = l.transform.position;
                p.radius = Math.Max(80f, l.range * 4f);   // well past where it could be seen
                if (l.enabled && l.gameObject.activeInHierarchy) Grow(p.group, p.center, p.center);
            }
            var gone = new List<int>();
            foreach (var kv in s.parts) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (int id in gone) s.parts.Remove(id);
        }
    }
}
