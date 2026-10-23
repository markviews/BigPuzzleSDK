// BigPuzzleSDK: the shapes the editor previews are built from (the data is in
// PreviewTable.Generated.cs). Plain C#, no UnityEngine.
namespace BigPuzzleSDK.GameAssets {

internal enum Kind { VertexColor, Glass, Screen, Plain, PegTileFace, RedTint, Cannonball, Tinted }

internal struct V3 { public float x, y, z; public V3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
internal struct Q { public float x, y, z, w; public Q(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; } }

internal sealed class Part {
    public readonly string mesh;      // a game mesh name, or "#cube" (Unity's cube) / "#text" (a text preview)
    public readonly int vertices;
    public readonly V3 position;
    public readonly Q rotation;
    public readonly V3 scale;
    public readonly Kind[] kinds;   // one per submesh
    public Part(string mesh, int vertices, V3 position, Q rotation, V3 scale, Kind[] kinds) {
        this.mesh = mesh; this.vertices = vertices; this.position = position; this.rotation = rotation; this.scale = scale; this.kinds = kinds;
    }
}

internal sealed class Template {
    public readonly string label;    // e.g. "PuzzleButton", "PuzzleLight Tube"
    public readonly V3 worldScale;   // the vanilla object's size; the mod keeps it whatever the creator's scale
    public readonly Part[] parts;
    public Template(string label, V3 worldScale, Part[] parts) { this.label = label; this.worldScale = worldScale; this.parts = parts; }
}

// A mesh the game merged into a scene's combined mesh, cut back out by the ripper
internal sealed class Slice {
    public readonly string name;          // the name the previews ask for
    public readonly string source;        // the combined mesh
    public readonly int sourceVertices;   // to pick the right one if several share the name
    public readonly int firstSubMesh, subMeshCount;
    public readonly float[] toLocal;      // 3 rows of 4: world space -> the object's own space
    public Slice(string name, string source, int sourceVertices, int firstSubMesh, int subMeshCount, float[] toLocal) {
        this.name = name; this.source = source; this.sourceVertices = sourceVertices;
        this.firstSubMesh = firstSubMesh; this.subMeshCount = subMeshCount; this.toLocal = toLocal;
    }
}

// One picture on a peg tile: a cell of one of the game's image sheets
internal struct TileCell {
    public string texture;   // a game texture name, or null for the creator's custom sheet
    public int columns, rows, x, y;   // x, y count from the top-left cell
}

internal static partial class PreviewTable {
    internal static Template Find(string label) {
        foreach (Template t in Templates) if (t.label == label) return t;
        if (label == "PuzzleBuoyLight Red") return RedBuoy();
        if (label.EndsWith(" NoBase")) return WithoutBase(Find(label.Substring(0, label.Length - " NoBase".Length)));
        return null;
    }

    // The buoy light without its pedestal: just the buoy, at the object's own position
    private static Template WithoutBase(Template full) {
        if (full == null) return null;
        var parts = new System.Collections.Generic.List<Part>();
        foreach (Part p in full.parts)
            if (p.mesh == "buoy") parts.Add(new Part(p.mesh, p.vertices, new V3(0f, 0f, 0f), p.rotation, p.scale, p.kinds));
        return new Template(full.label + " NoBase", full.worldScale, parts.ToArray());
    }

    // The red buoy: the orange one with the buoy itself tinted red (the pedestal stays as it is)
    private static Template RedBuoy() {
        Template baseT = null;
        foreach (Template t in Templates) if (t.label == "PuzzleBuoyLight") baseT = t;
        if (baseT == null) return null;
        var parts = new Part[baseT.parts.Length];
        for (int i = 0; i < parts.Length; i++) {
            Part p = baseT.parts[i];
            if (p.mesh != "buoy") { parts[i] = p; continue; }
            var kinds = new Kind[p.kinds.Length];
            for (int k = 0; k < kinds.Length; k++) kinds[k] = Kind.RedTint;
            parts[i] = new Part(p.mesh, p.vertices, p.position, p.rotation, p.scale, kinds);
        }
        return new Template("PuzzleBuoyLight Red", baseT.worldScale, parts);
    }

    // The game's image sheets (from the Ctrl+F3 dump)
    internal static readonly string[] Sheets = { "pegtileatlas", "goodbyeatlas", "pegtileatlas_b", "Cannonball_BaseColor" };   // (the last is the cannonball's skin, not a tile sheet)
    private static readonly int[] SheetGrid = { 6, 3, 6, 1 };

    // Which cell a peg tile shows for an image set and image number; false = no picture (blank)
    internal static bool CellFor(string set, int image, out TileCell cell) {
        cell = default;
        if (set == "Custom") {   // the creator's 4 x 4 sheet: top-left cell blank, then image N is cell N
            int n = image > 0 && image < 16 ? image : 0;
            cell = new TileCell { texture = null, columns = 4, rows = 4, x = n % 4, y = n / 4 };
            return true;
        }
        if (image <= 0) return false;
        int sheet, x, y;
        int i = image - 1;
        switch (set) {
            case "A": if (image > 9) return false; sheet = 0; x = i % 3; y = i / 3; break;
            case "B": if (image > 9) return false; sheet = 0; x = 3 + i % 3; y = i / 3; break;
            case "C": if (image > 9) return false; sheet = 2; x = i % 3; y = i / 3; break;
            case "D": if (image > 9) return false; sheet = 0; x = 3 + i % 3; y = 3 + i / 3; break;
            case "Scout": if (image > 12) return false; sheet = 0; x = image <= 6 ? 3 : 4; y = 0; break;
            case "Circle": if (image > 9) return false; sheet = 1; x = i % 3; y = i / 3; break;
            case "Pontiff":
                if (image > 18) return false;
                sheet = 2;
                if (image <= 9) { x = i % 3; y = i / 3; } else { int j = image - 10; x = 3 + j % 3; y = j / 3; }
                break;
            case "LargeA":
                if (image > 18) return false;
                sheet = 0;
                if (image <= 9) { x = i % 3; y = i / 3; } else { int j = image - 10; x = 3 + j % 3; y = j / 3; }
                break;
            default: return false;   // Camo and anything unknown: plain tile
        }
        cell = new TileCell { texture = Sheets[sheet], columns = SheetGrid[sheet], rows = SheetGrid[sheet], x = x, y = y };
        return true;
    }
}

} // namespace BigPuzzleSDK.GameAssets