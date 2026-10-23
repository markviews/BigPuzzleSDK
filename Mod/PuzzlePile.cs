using System.Collections.Generic;
using UnityEngine;

namespace BigPuzzleSDK {

// The pile of tiles a puzzle deals from. When a puzzle starts, every flap of every Display (that doesn't copy another)
// takes one Peg Tile from the puzzle, and every Sound Peg Answer takes one Sound Peg, each at random and never the same
// tile twice. Every tile counts the same, so with ten triangles and one circle a display will most likely get a
// triangle. A picture only repeats when the puzzle really has more than one tile of it. If the pile runs out, the
// extra displays stay blank and the extra speakers stay silent (and the puzzle can't be solved).
// Only the host deals; everyone else follows what the host's displays and speakers show.
internal static class PuzzlePile {
    private static void Shuffle<T>(List<T> list) {
        for (int i = list.Count - 1; i > 0; i--) {
            int j = UnityEngine.Random.Range(0, i + 1);
            T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }

    private static List<PuzzleDisplay> DisplaysOf(int root) {
        var list = new List<PuzzleDisplay>();
        foreach (PuzzleDisplay d in PuzzleDisplay.all)
            if (d != null && d.transform.root.GetInstanceID() == root && !d.CopiesAnother) list.Add(d);
        return list;
    }

    // Every flap that takes a tile: a display with three flaps counts three times
    private static List<KeyValuePair<PuzzleDisplay, int>> FlapsOf(int root) {
        var list = new List<KeyValuePair<PuzzleDisplay, int>>();
        foreach (PuzzleDisplay d in DisplaysOf(root))
            for (int i = 0; i < d.FlapCount; i++) list.Add(new KeyValuePair<PuzzleDisplay, int>(d, i));
        return list;
    }

    private static List<PuzzleSoundPegAnswer> SpeakersOf(int root) {
        var list = new List<PuzzleSoundPegAnswer>();
        foreach (PuzzleVanillaCopy c in PuzzleVanillaCopy.all)
            if (c is PuzzleSoundPegAnswer a && a.transform.root.GetInstanceID() == root) list.Add(a);
        return list;
    }

    // Whether the displays and speakers can already send their state over the network
    internal static bool AllReady(Transform puzzle) {
        int root = puzzle.root.GetInstanceID();
        foreach (PuzzleDisplay d in PuzzleDisplay.all) if (d != null && d.transform.root.GetInstanceID() == root && !d.Ready) return false;
        foreach (PuzzleSoundPegAnswer a in SpeakersOf(root)) if (!a.Ready) return false;
        return true;
    }

    // Host only. Deals the pile out.
    internal static void Deal(Transform puzzle) {
        if (!PuzzleSDK.IsHost || puzzle == null) return;
        int root = puzzle.root.GetInstanceID();

        var tiles = new List<PuzzlePegTile>();
        foreach (PuzzlePegTile t in PuzzlePegTile.all)
            if (t != null && t.transform.root.GetInstanceID() == root && t.Image >= 1) tiles.Add(t);
        List<KeyValuePair<PuzzleDisplay, int>> flaps = FlapsOf(root);
        Shuffle(tiles);
        for (int i = 0; i < flaps.Count; i++) {
            if (i < tiles.Count) flaps[i].Key.ShowTile(tiles[i], flaps[i].Value);
            else flaps[i].Key.ShowBlank(flaps[i].Value);
        }
        if (flaps.Count > tiles.Count)
            Plugin.log.LogWarning($"[PuzzlePile] '{puzzle.root.name}': {tiles.Count} tile(s) for {flaps.Count} display flap(s), so {flaps.Count - tiles.Count} flap(s) stay blank and the puzzle can't be solved.");

        var pegs = new List<PuzzleSoundPeg>();
        foreach (PuzzleVanillaCopy c in PuzzleVanillaCopy.all)
            if (c is PuzzleSoundPeg peg && peg.transform.root.GetInstanceID() == root) pegs.Add(peg);
        List<PuzzleSoundPegAnswer> speakers = SpeakersOf(root);
        Shuffle(pegs);
        for (int i = 0; i < speakers.Count; i++) {
            if (i < pegs.Count) speakers[i].PlayPeg(pegs[i]); else speakers[i].Silence();
        }
        if (speakers.Count > pegs.Count)
            Plugin.log.LogWarning($"[PuzzlePile] '{puzzle.root.name}': {pegs.Count} sound peg(s) for {speakers.Count} answer speaker(s), so {speakers.Count - pegs.Count} speaker(s) stay silent and the puzzle can't be solved.");
    }

    // Host only. Displays go blank and speakers go quiet (the puzzle stopped).
    internal static void Clear(Transform puzzle) {
        if (!PuzzleSDK.IsHost || puzzle == null) return;
        int root = puzzle.root.GetInstanceID();
        foreach (PuzzleDisplay d in DisplaysOf(root)) d.ShowBlank();
        foreach (PuzzleSoundPegAnswer a in SpeakersOf(root)) a.Silence();
    }
}

}
