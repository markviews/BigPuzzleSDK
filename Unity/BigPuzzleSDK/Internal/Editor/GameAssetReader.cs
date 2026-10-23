// BigPuzzleSDK: reads meshes and textures straight out of Big Walk's data.unity3d, on the
// creator's own computer, for the editor previews. Nothing it reads is ever exported.
//
// Plain C# (no UnityEngine), so the same file is used by the Unity editor and by tests.
// Handles: UnityFS bundles (uncompressed or LZ4/LZ4HC blocks), serialized files format 14-23+,
// Mesh and Texture2D objects (read with the Unity 6000.3 layouts embedded below, checked
// byte-for-byte so a future Unity update is reported instead of producing garbage).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BigPuzzleSDK.GameAssets {

// ---------------------------------------------------------------------------------------------
// Results
// ---------------------------------------------------------------------------------------------
public sealed class RippedSubMesh {
    public int[] indices;       // triangle list, already relative to vertex 0
    public int topology;        // 0 = triangles
}

public sealed class RippedMesh {
    public string name;
    public string sourceFile;
    public long pathId;
    public int vertexCount;
    public float[] positions;   // xyz per vertex (Unity space, untouched)
    public float[] normals;     // xyz per vertex, or null
    public float[] colors;      // rgba per vertex (0..1), or null
    public float[] uv0;         // uv per vertex, or null
    public List<RippedSubMesh> subMeshes = new List<RippedSubMesh>();
}

public sealed class RippedTexture {
    public string name;
    public string sourceFile;
    public long pathId;
    public int width, height, format, mipCount;
    public byte[] data;         // raw GPU data exactly as stored (e.g. DXT5), all mips
}

// One Mesh/Texture2D found in the game files, before it is read
public sealed class AssetEntry {
    public string name;
    public int classId;         // 43 Mesh, 28 Texture2D
    public SerializedFile file;
    public long pathId;
    public long byteStart;
    public uint byteSize;
}

public class GameAssetException : Exception {
    public GameAssetException(string message) : base(message) { }
}

// ---------------------------------------------------------------------------------------------
// The whole game: opens data.unity3d and lists every mesh and texture by name
// ---------------------------------------------------------------------------------------------
public sealed class GameAssets : IDisposable {
    public readonly UnityBundle bundle;
    public readonly List<SerializedFile> files = new List<SerializedFile>();
    public readonly Dictionary<string, List<AssetEntry>> meshes = new Dictionary<string, List<AssetEntry>>();
    public readonly Dictionary<string, List<AssetEntry>> textures = new Dictionary<string, List<AssetEntry>>();
    public string UnityVersion => bundle.engineVersion;

    public GameAssets(string dataUnity3dPath, Action<string, float> progress = null) {
        bundle = new UnityBundle(dataUnity3dPath);
        int n = 0;
        foreach (UnityBundle.Node node in bundle.nodes) {
            n++;
            if (!LooksSerialized(node.path)) continue;
            progress?.Invoke("Reading " + node.path, (float)n / bundle.nodes.Count);
            SerializedFile sf;
            try { sf = new SerializedFile(bundle, node); }
            catch (Exception ex) { throw new GameAssetException($"Could not read '{node.path}': {ex.Message}"); }
            files.Add(sf);
            foreach (SerializedFile.ObjectInfo o in sf.objects) {
                if (o.classId != 43 && o.classId != 28) continue;
                string name = sf.PeekName(o);
                var entry = new AssetEntry { name = name, classId = o.classId, file = sf, pathId = o.pathId, byteStart = o.byteStart, byteSize = o.byteSize };
                var dict = o.classId == 43 ? meshes : textures;
                if (!dict.TryGetValue(name, out var list)) dict[name] = list = new List<AssetEntry>();
                list.Add(entry);
            }
        }
    }

    private static bool LooksSerialized(string path) {
        string lower = path.ToLowerInvariant();
        if (lower.EndsWith(".ress") || lower.EndsWith(".resource") || lower.EndsWith(".json") || lower.EndsWith(".txt")) return false;
        string file = Path.GetFileName(lower);
        return file.EndsWith(".assets") || file.StartsWith("level") || file == "globalgamemanagers" || file.StartsWith("cab-");
    }

    // The mesh with this name; when the game has several, the one with this many vertices (if given)
    public RippedMesh ReadMesh(string name, int expectedVertices = -1) {
        if (!meshes.TryGetValue(name, out var list)) return null;
        RippedMesh first = null;
        foreach (AssetEntry e in list) {
            RippedMesh m = MeshReader.Read(e);
            if (expectedVertices < 0 || m.vertexCount == expectedVertices) return m;
            if (first == null) first = m;
        }
        return first;   // no exact match: the first one (the caller can compare vertexCount)
    }

    public RippedTexture ReadTexture(string name) {
        if (!textures.TryGetValue(name, out var list)) return null;
        return TextureReader.Read(list[0]);
    }

    public void Dispose() { bundle.Dispose(); }
}

// ---------------------------------------------------------------------------------------------
// UnityFS bundle with random access: blocks are decompressed only when read, a few kept cached
// ---------------------------------------------------------------------------------------------
public sealed class UnityBundle : IDisposable {
    public sealed class Node { public long offset, size; public uint flags; public string path; }
    private struct Block { public uint uncompressed, compressed; public ushort flags; public long fileOffset, dataOffset; }

    public readonly string playerVersion, engineVersion;
    public readonly List<Node> nodes = new List<Node>();
    private readonly FileStream stream;
    private readonly Block[] blocks;
    private readonly Dictionary<int, byte[]> cache = new Dictionary<int, byte[]>();
    private readonly LinkedList<int> cacheOrder = new LinkedList<int>();
    private const int CacheBlocks = 48;

    public UnityBundle(string path) {
        stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        var r = new BigEndianReader(stream);
        string signature = r.CString();
        if (signature != "UnityFS") throw new GameAssetException($"Not a UnityFS bundle ('{signature}').");
        uint format = r.U32();
        playerVersion = r.CString();
        engineVersion = r.CString();
        long totalSize = r.I64();
        uint compressedInfo = r.U32();
        uint uncompressedInfo = r.U32();
        uint flags = r.U32();
        if (format >= 7) r.Align(16);
        long afterHeader = stream.Position;

        byte[] infoBytes;
        if ((flags & 0x80) != 0) {          // blocks info at the end of the file
            stream.Position = stream.Length - compressedInfo;
            infoBytes = r.Bytes((int)compressedInfo);
            stream.Position = afterHeader;
        } else {
            infoBytes = r.Bytes((int)compressedInfo);
        }
        infoBytes = Decompress(infoBytes, (int)uncompressedInfo, (int)(flags & 0x3F));

        var ir = new BigEndianReader(new MemoryStream(infoBytes));
        ir.Bytes(16);   // uncompressed data hash
        int blockCount = ir.I32();
        blocks = new Block[blockCount];
        for (int i = 0; i < blockCount; i++) {
            blocks[i].uncompressed = ir.U32();
            blocks[i].compressed = ir.U32();
            blocks[i].flags = ir.U16();
        }
        int nodeCount = ir.I32();
        for (int i = 0; i < nodeCount; i++)
            nodes.Add(new Node { offset = ir.I64(), size = ir.I64(), flags = ir.U32(), path = ir.CString() });

        if ((flags & 0x200) != 0) {         // BlockInfoNeedPaddingAtStart
            long p = stream.Position;
            stream.Position = (p + 15) & ~15L;
        }
        long fileOffset = stream.Position;
        long dataOffset = 0;
        for (int i = 0; i < blockCount; i++) {
            blocks[i].fileOffset = fileOffset;
            blocks[i].dataOffset = dataOffset;
            fileOffset += blocks[i].compressed;
            dataOffset += blocks[i].uncompressed;
        }
    }

    public Node Find(string nameOrPath) {
        foreach (Node n in nodes) if (n.path == nameOrPath) return n;
        string file = Path.GetFileName(nameOrPath);
        foreach (Node n in nodes) if (Path.GetFileName(n.path) == file) return n;
        return null;
    }

    // Reads 'count' bytes at 'offset' inside the uncompressed data of a node
    public byte[] Read(Node node, long offset, int count) {
        if (offset < 0 || offset + count > node.size) throw new GameAssetException($"Read outside '{node.path}' ({offset}+{count} > {node.size}).");
        var result = new byte[count];
        long pos = node.offset + offset;
        int written = 0;
        while (written < count) {
            int b = BlockAt(pos);
            byte[] data = GetBlock(b);
            int inBlock = (int)(pos - blocks[b].dataOffset);
            int n = Math.Min(count - written, data.Length - inBlock);
            Buffer.BlockCopy(data, inBlock, result, written, n);
            written += n;
            pos += n;
        }
        return result;
    }

    private int BlockAt(long pos) {
        int lo = 0, hi = blocks.Length - 1;
        while (lo < hi) {
            int mid = (lo + hi + 1) >> 1;
            if (blocks[mid].dataOffset <= pos) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    private byte[] GetBlock(int i) {
        if (cache.TryGetValue(i, out byte[] hit)) {
            cacheOrder.Remove(i); cacheOrder.AddFirst(i);
            return hit;
        }
        stream.Position = blocks[i].fileOffset;
        var raw = new byte[blocks[i].compressed];
        ReadFully(stream, raw);
        byte[] data = Decompress(raw, (int)blocks[i].uncompressed, blocks[i].flags & 0x3F);
        cache[i] = data;
        cacheOrder.AddFirst(i);
        if (cacheOrder.Count > CacheBlocks) { cache.Remove(cacheOrder.Last.Value); cacheOrder.RemoveLast(); }
        return data;
    }

    private static void ReadFully(Stream s, byte[] buffer) {
        int done = 0;
        while (done < buffer.Length) {
            int n = s.Read(buffer, done, buffer.Length - done);
            if (n <= 0) throw new GameAssetException("Unexpected end of data.unity3d.");
            done += n;
        }
    }

    private static byte[] Decompress(byte[] data, int uncompressedSize, int compression) {
        switch (compression) {
            case 0: return data;
            case 2:
            case 3: return Lz4.Decode(data, uncompressedSize);
            case 1: throw new GameAssetException("This game's data is LZMA-compressed, which the SDK can't read yet.");
            default: throw new GameAssetException($"Unknown compression type {compression} in data.unity3d.");
        }
    }

    public void Dispose() { stream.Dispose(); }
}

// LZ4 block format decoder (what Unity uses for LZ4 and LZ4HC)
internal static class Lz4 {
    public static byte[] Decode(byte[] src, int outSize) {
        var dst = new byte[outSize];
        int s = 0, d = 0;
        while (s < src.Length) {
            int token = src[s++];
            int lit = token >> 4;
            if (lit == 15) { int b; do { b = src[s++]; lit += b; } while (b == 255); }
            Buffer.BlockCopy(src, s, dst, d, lit);
            s += lit; d += lit;
            if (s >= src.Length) break;   // last sequence has literals only
            int offset = src[s] | (src[s + 1] << 8);
            s += 2;
            int len = token & 15;
            if (len == 15) { int b; do { b = src[s++]; len += b; } while (b == 255); }
            len += 4;
            int from = d - offset;
            if (offset <= 0 || from < 0) throw new GameAssetException("Corrupt LZ4 data.");
            if (offset >= len) { Buffer.BlockCopy(dst, from, dst, d, len); d += len; }
            else { for (int i = 0; i < len; i++) dst[d++] = dst[from + i]; }   // overlapping copy
        }
        if (d != outSize) throw new GameAssetException($"LZ4 size mismatch ({d} != {outSize}).");
        return dst;
    }
}

// ---------------------------------------------------------------------------------------------
// Serialized file (sharedassetsN.assets, levelN, ...): the object table
// ---------------------------------------------------------------------------------------------
public sealed class SerializedFile {
    public struct ObjectInfo { public long pathId; public long byteStart; public uint byteSize; public int classId; }

    public readonly string name;
    public readonly UnityBundle bundle;
    public readonly UnityBundle.Node node;
    public readonly uint version;
    public readonly bool bigEndian;
    public readonly string unityVersion;
    public readonly List<ObjectInfo> objects = new List<ObjectInfo>();

    public SerializedFile(UnityBundle bundle, UnityBundle.Node node) {
        this.bundle = bundle;
        this.node = node;
        name = Path.GetFileName(node.path);

        // The header is always big-endian; metadata follows in the file's own byte order
        byte[] head = bundle.Read(node, 0, (int)Math.Min(64, node.size));
        var h = new BigEndianReader(new MemoryStream(head));
        uint metadataSize = h.U32();
        uint fileSize = h.U32();
        version = h.U32();
        long dataOffset = h.U32();
        if (version < 14) throw new GameAssetException($"'{name}' uses serialized format {version}, which is too old.");
        bigEndian = h.U8() != 0;
        h.Bytes(3);
        long metaStart = 20;
        if (version >= 22) {
            metadataSize = h.U32();
            h.I64();                // file size
            dataOffset = h.I64();
            h.I64();                // unknown
            metaStart = 48;
        }

        byte[] meta = bundle.Read(node, metaStart, (int)metadataSize);
        var r = new DataReader(meta, bigEndian);
        unityVersion = r.CString();
        r.I32();                    // target platform
        bool typeTrees = r.Bool();
        int typeCount = r.I32();
        var classIds = new int[typeCount];
        for (int i = 0; i < typeCount; i++) classIds[i] = ReadType(r, typeTrees, false);

        int objectCount = r.I32();
        for (int i = 0; i < objectCount; i++) {
            r.Align(4);
            long pathId = r.I64();
            long start = version >= 22 ? r.I64() : r.U32();
            uint size = r.U32();
            int typeIndex = r.I32();
            if (version < 16) { r.U16(); }
            if (version < 17) { r.I16(); }
            int classId = version >= 16 ? (typeIndex >= 0 && typeIndex < classIds.Length ? classIds[typeIndex] : -1) : typeIndex;
            objects.Add(new ObjectInfo { pathId = pathId, byteStart = dataOffset + start, byteSize = size, classId = classId });
        }
    }

    // Reads one entry of the type table and returns its class id (skips any type tree)
    private int ReadType(DataReader r, bool typeTrees, bool refType) {
        int classId = r.I32();
        if (version >= 16) r.Bool();                    // stripped
        short scriptIndex = version >= 17 ? r.I16() : (short)-1;
        if ((refType && scriptIndex >= 0) || (version >= 16 && classId == 114)) r.Skip(16);   // script id
        r.Skip(16);                                     // type hash
        if (typeTrees) {
            int blobSize = -1;
            if (version >= 23) { r.Skip(16); blobSize = r.I32(); }
            if (version >= 23) {
                if (blobSize > 0) r.Skip(blobSize);
            } else {
                int nodeCount = r.I32();
                int stringSize = r.I32();
                r.Skip(nodeCount * (version >= 19 ? 32 : 24) + stringSize);
            }
            if (version >= 21) {
                if (refType) { r.CString(); r.CString(); r.CString(); }
                else { int deps = r.I32(); r.Skip(deps * 4); }
            }
        }
        return classId;
    }

    public byte[] ReadObject(long byteStart, uint byteSize) => bundle.Read(node, byteStart, (int)byteSize);

    // Mesh and Texture2D both start with their name
    public string PeekName(ObjectInfo o) {
        byte[] head = bundle.Read(node, o.byteStart, (int)Math.Min(o.byteSize, 4));
        int len = bigEndian ? (head[0] << 24 | head[1] << 16 | head[2] << 8 | head[3]) : BitConverter.ToInt32(head, 0);
        if (len <= 0 || len > 1024 || len + 4 > o.byteSize) return "";
        byte[] text = bundle.Read(node, o.byteStart + 4, len);
        return Encoding.UTF8.GetString(text);
    }

    // Bytes from a .resS / .resource file next to this one (mesh and texture data streamed out of the object)
    public byte[] ReadResource(string path, ulong offset, uint size) {
        UnityBundle.Node res = bundle.Find(path);
        if (res == null) {
            string file = path.Replace("archive:/", "");
            res = bundle.Find(Path.GetFileName(file));
        }
        if (res == null) throw new GameAssetException($"Resource '{path}' not found in data.unity3d.");
        return bundle.Read(res, (long)offset, (int)size);
    }
}

// ---------------------------------------------------------------------------------------------
// Type tree: the field layout of a class, and a generic reader for it
// ---------------------------------------------------------------------------------------------
internal sealed class TreeNode {
    public string type, name;
    public int level, meta, byteSize;
    public readonly List<TreeNode> children = new List<TreeNode>();
    public bool Aligned => (meta & 0x4000) != 0;

    // Parses "level,type,name,meta,size;..." (as produced from Unity's own type trees)
    public static TreeNode Parse(string compact) {
        TreeNode root = null;
        var stack = new List<TreeNode>();
        foreach (string row in compact.Split(';')) {
            string[] p = row.Split(',');
            var n = new TreeNode { level = int.Parse(p[0]), type = p[1], name = p[2], meta = int.Parse(p[3]), byteSize = int.Parse(p[4]) };
            while (stack.Count > n.level) stack.RemoveAt(stack.Count - 1);
            if (stack.Count == 0) root = n; else stack[stack.Count - 1].children.Add(n);
            stack.Add(n);
        }
        return root;
    }
}

internal static class TreeReader {
    // Reads one value: primitives as long/ulong/double/bool/string, byte arrays as byte[],
    // other arrays as List<object>, everything else as Dictionary<string, object>
    public static object Read(TreeNode node, DataReader r) {
        object value;
        bool align = node.Aligned;
        switch (node.type) {
            case "SInt8": value = (long)(sbyte)r.U8(); break;
            case "UInt8": case "char": value = (long)r.U8(); break;
            case "short": case "SInt16": value = (long)r.I16(); break;
            case "unsigned short": case "UInt16": value = (long)r.U16(); break;
            case "int": case "SInt32": value = (long)r.I32(); break;
            case "unsigned int": case "UInt32": case "Type*": value = (long)r.U32(); break;
            case "long long": case "SInt64": value = r.I64(); break;
            case "unsigned long long": case "UInt64": case "FileSize": value = (long)r.U64(); break;
            case "float": value = (double)r.F32(); break;
            case "double": value = r.F64(); break;
            case "bool": value = r.Bool(); break;
            case "string": value = r.AlignedString(); break;
            case "TypelessData": { int n = r.I32(); value = r.Bytes(n); break; }
            default:
                if (node.children.Count > 0 && node.children[0].type == "Array") {
                    TreeNode array = node.children[0];
                    if (array.Aligned) align = true;
                    int size = r.I32();
                    if (size < 0) throw new GameAssetException("Negative array size.");
                    TreeNode item = array.children[1];
                    if (item.type == "UInt8" || item.type == "char" || item.type == "SInt8") {
                        value = r.Bytes(size);
                        if (item.Aligned) r.Align(4);
                    } else {
                        var list = new List<object>(size);
                        for (int i = 0; i < size; i++) list.Add(Read(item, r));
                        value = list;
                    }
                } else if (node.type == "pair") {
                    value = new List<object> { Read(node.children[0], r), Read(node.children[1], r) };
                } else {
                    var dict = new Dictionary<string, object>();
                    foreach (TreeNode child in node.children) dict[child.name] = Read(child, r);
                    value = dict;
                }
                break;
        }
        if (align) r.Align(4);
        return value;
    }

    public static Dictionary<string, object> ReadObject(TreeNode tree, AssetEntry e, string what) {
        byte[] data = e.file.ReadObject(e.byteStart, e.byteSize);
        var r = new DataReader(data, e.file.bigEndian);
        var obj = (Dictionary<string, object>)Read(tree, r);
        if (r.Position != data.Length)
            throw new GameAssetException($"{what} '{e.name}' didn't read cleanly ({r.Position} of {data.Length} bytes). " +
                                         $"The game was probably updated to a newer Unity ({e.file.unityVersion}); the SDK needs an update.");
        return obj;
    }

    // Small helpers for the dictionaries
    public static Dictionary<string, object> D(object o, string key) => (Dictionary<string, object>)((Dictionary<string, object>)o)[key];
    public static long L(object o, string key) => Convert.ToInt64(((Dictionary<string, object>)o)[key]);
    public static List<object> A(object o, string key) => (List<object>)((Dictionary<string, object>)o)[key];
    public static byte[] B(object o, string key) => ((Dictionary<string, object>)o)[key] as byte[] ?? new byte[0];
    public static string S(object o, string key) => (string)((Dictionary<string, object>)o)[key];
}

// ---------------------------------------------------------------------------------------------
// Mesh
// ---------------------------------------------------------------------------------------------
internal static class MeshReader {
    private static TreeNode tree;

    public static RippedMesh Read(AssetEntry e) {
        if (tree == null) tree = TreeNode.Parse(Layouts.MeshTree);
        var m = TreeReader.ReadObject(tree, e, "Mesh");
        var mesh = new RippedMesh { name = e.name, sourceFile = e.file.name, pathId = e.pathId };

        var vd = TreeReader.D(m, "m_VertexData");
        int vertexCount = (int)TreeReader.L(vd, "m_VertexCount");
        byte[] vertexData = TreeReader.B(vd, "m_DataSize");
        var streamInfo = TreeReader.D(m, "m_StreamData");
        string streamPath = TreeReader.S(streamInfo, "path");
        if (!string.IsNullOrEmpty(streamPath) && TreeReader.L(streamInfo, "size") > 0)
            vertexData = e.file.ReadResource(streamPath, (ulong)TreeReader.L(streamInfo, "offset"), (uint)TreeReader.L(streamInfo, "size"));
        if (vertexCount == 0 || vertexData.Length == 0)
            throw new GameAssetException($"Mesh '{e.name}' has no plain vertex data (compressed meshes aren't supported).");
        mesh.vertexCount = vertexCount;

        // Channels and the streams they sit in (each stream is 16-byte aligned, one after another)
        var channels = new List<int[]>();   // stream, offset, format, dimension
        foreach (object c in TreeReader.A(vd, "m_Channels"))
            channels.Add(new[] { (int)TreeReader.L(c, "stream"), (int)TreeReader.L(c, "offset"), (int)TreeReader.L(c, "format"), (int)TreeReader.L(c, "dimension") & 0xF });
        int streamCount = 0;
        foreach (int[] c in channels) if (c[3] > 0) streamCount = Math.Max(streamCount, c[0] + 1);
        var streamOffset = new int[streamCount];
        var streamStride = new int[streamCount];
        int offset = 0;
        for (int s = 0; s < streamCount; s++) {
            int stride = 0;
            foreach (int[] c in channels) if (c[0] == s && c[3] > 0) stride += c[3] * FormatSize(c[2]);
            streamOffset[s] = offset;
            streamStride[s] = stride;
            offset += vertexCount * stride;
            offset = (offset + 15) & ~15;
        }

        bool be = e.file.bigEndian;
        float[] ReadChannel(int index, int wantDims) {
            if (index >= channels.Count || channels[index][3] == 0) return null;
            int[] c = channels[index];
            int dims = c[3], fmt = c[2], size = FormatSize(fmt);
            var result = new float[vertexCount * wantDims];
            for (int v = 0; v < vertexCount; v++) {
                int p = streamOffset[c[0]] + v * streamStride[c[0]] + c[1];
                for (int k = 0; k < wantDims; k++)
                    result[v * wantDims + k] = k < dims ? Component(vertexData, p + k * size, fmt, be) : (k == 3 ? 1f : 0f);
            }
            return result;
        }
        mesh.positions = ReadChannel(0, 3);
        mesh.normals = ReadChannel(1, 3);
        mesh.colors = ReadChannel(3, 4);
        mesh.uv0 = ReadChannel(4, 2);
        if (mesh.positions == null) throw new GameAssetException($"Mesh '{e.name}' has no positions.");

        // Indices
        byte[] ib = TreeReader.B(m, "m_IndexBuffer");
        bool use16 = TreeReader.L(m, "m_IndexFormat") == 0;
        foreach (object sm in TreeReader.A(m, "m_SubMeshes")) {
            int firstByte = (int)TreeReader.L(sm, "firstByte");
            int count = (int)TreeReader.L(sm, "indexCount");
            int topology = (int)TreeReader.L(sm, "topology");
            int baseVertex = (int)TreeReader.L(sm, "baseVertex");
            var idx = new int[count];
            for (int i = 0; i < count; i++) {
                int p = firstByte + i * (use16 ? 2 : 4);
                int value = use16 ? (be ? (ib[p] << 8 | ib[p + 1]) : (ib[p] | ib[p + 1] << 8))
                                  : (be ? (ib[p] << 24 | ib[p + 1] << 16 | ib[p + 2] << 8 | ib[p + 3]) : BitConverter.ToInt32(ib, p));
                idx[i] = value + baseVertex;
            }
            mesh.subMeshes.Add(new RippedSubMesh { indices = idx, topology = topology });
        }
        return mesh;
    }

    // Unity's VertexFormat: Float, Float16, UNorm8, SNorm8, UNorm16, SNorm16, UInt8, SInt8, UInt16, SInt16, UInt32, SInt32
    private static int FormatSize(int format) {
        switch (format) {
            case 0: case 10: case 11: return 4;
            case 1: case 4: case 5: case 8: case 9: return 2;
            default: return 1;
        }
    }

    private static float Component(byte[] d, int p, int format, bool be) {
        switch (format) {
            case 0: return BitConverter.ToSingle(Ordered(d, p, 4, be), 0);
            case 1: return HalfToFloat((ushort)U16(d, p, be));
            case 2: return d[p] / 255f;
            case 3: return Math.Max((sbyte)d[p] / 127f, -1f);
            case 4: return U16(d, p, be) / 65535f;
            case 5: return Math.Max((short)U16(d, p, be) / 32767f, -1f);
            case 6: return d[p];
            case 7: return (sbyte)d[p];
            case 8: return U16(d, p, be);
            case 9: return (short)U16(d, p, be);
            case 10: return BitConverter.ToUInt32(Ordered(d, p, 4, be), 0);
            case 11: return BitConverter.ToInt32(Ordered(d, p, 4, be), 0);
            default: throw new GameAssetException($"Unknown vertex format {format}.");
        }
    }

    private static int U16(byte[] d, int p, bool be) => be ? (d[p] << 8 | d[p + 1]) : (d[p] | d[p + 1] << 8);

    private static byte[] Ordered(byte[] d, int p, int n, bool be) {
        var b = new byte[n];
        Buffer.BlockCopy(d, p, b, 0, n);
        if (be == BitConverter.IsLittleEndian) Array.Reverse(b);
        return b;
    }

    private static float HalfToFloat(ushort h) {
        int sign = (h >> 15) & 1, exp = (h >> 10) & 0x1F, mant = h & 0x3FF;
        float v;
        if (exp == 0) v = (float)(mant / 1024.0 * Math.Pow(2, -14));
        else if (exp == 31) v = mant == 0 ? float.PositiveInfinity : float.NaN;
        else v = (float)((1 + mant / 1024.0) * Math.Pow(2, exp - 15));
        return sign == 1 ? -v : v;
    }
}

// ---------------------------------------------------------------------------------------------
// Cuts one object back out of a scene's combined (static-batched) mesh
// ---------------------------------------------------------------------------------------------
internal static class MeshSlicer {
    public static RippedMesh Cut(RippedMesh combined, Slice slice) {
        if (slice.firstSubMesh + slice.subMeshCount > combined.subMeshes.Count)
            throw new GameAssetException($"'{combined.name}' has only {combined.subMeshes.Count} submeshes, not {slice.firstSubMesh + slice.subMeshCount}.");
        float[] m = slice.toLocal;
        var map = new Dictionary<int, int>();
        var pos = new List<float>(); var nrm = new List<float>(); var col = new List<float>(); var uv = new List<float>();
        var result = new RippedMesh { name = slice.name, sourceFile = combined.sourceFile, pathId = combined.pathId };
        for (int s = 0; s < slice.subMeshCount; s++) {
            RippedSubMesh src = combined.subMeshes[slice.firstSubMesh + s];
            var idx = new int[src.indices.Length];
            for (int i = 0; i < idx.Length; i++) {
                int old = src.indices[i];
                if (!map.TryGetValue(old, out int now)) {
                    now = map.Count;
                    map[old] = now;
                    float x = combined.positions[old * 3], y = combined.positions[old * 3 + 1], z = combined.positions[old * 3 + 2];
                    pos.Add(m[0] * x + m[1] * y + m[2] * z + m[3]);
                    pos.Add(m[4] * x + m[5] * y + m[6] * z + m[7]);
                    pos.Add(m[8] * x + m[9] * y + m[10] * z + m[11]);
                    if (combined.normals != null) {
                        float a = combined.normals[old * 3], b = combined.normals[old * 3 + 1], c = combined.normals[old * 3 + 2];
                        float nx = m[0] * a + m[1] * b + m[2] * c, ny = m[4] * a + m[5] * b + m[6] * c, nz = m[8] * a + m[9] * b + m[10] * c;
                        float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (len > 0) { nx /= len; ny /= len; nz /= len; }
                        nrm.Add(nx); nrm.Add(ny); nrm.Add(nz);
                    }
                    if (combined.colors != null) for (int k = 0; k < 4; k++) col.Add(combined.colors[old * 4 + k]);
                    if (combined.uv0 != null) { uv.Add(combined.uv0[old * 2]); uv.Add(combined.uv0[old * 2 + 1]); }
                }
                idx[i] = now;
            }
            result.subMeshes.Add(new RippedSubMesh { indices = idx, topology = src.topology });
        }
        result.vertexCount = map.Count;
        result.positions = pos.ToArray();
        result.normals = combined.normals != null ? nrm.ToArray() : null;
        result.colors = combined.colors != null ? col.ToArray() : null;
        result.uv0 = combined.uv0 != null ? uv.ToArray() : null;

        // A cut-out piece should be a small object around its own origin; if not, the slice is wrong
        float extent = 0f;
        for (int i = 0; i < result.positions.Length; i++) extent = Math.Max(extent, Math.Abs(result.positions[i]));
        if (extent > 20f) throw new GameAssetException($"'{slice.name}' cut from '{combined.name}' is {extent:0}m from its origin; the game has probably changed.");
        return result;
    }
}

// ---------------------------------------------------------------------------------------------
// Texture2D (raw GPU data; Unity itself decodes it on the editor side)
// ---------------------------------------------------------------------------------------------
internal static class TextureReader {
    private static TreeNode tree;

    public static RippedTexture Read(AssetEntry e) {
        if (tree == null) tree = TreeNode.Parse(Layouts.Texture2DTree);
        var t = TreeReader.ReadObject(tree, e, "Texture2D");
        var tex = new RippedTexture {
            name = e.name, sourceFile = e.file.name, pathId = e.pathId,
            width = (int)TreeReader.L(t, "m_Width"), height = (int)TreeReader.L(t, "m_Height"),
            format = (int)TreeReader.L(t, "m_TextureFormat"), mipCount = (int)TreeReader.L(t, "m_MipCount"),
        };
        byte[] data = TreeReader.B(t, "image data");
        var stream = TreeReader.D(t, "m_StreamData");
        string path = TreeReader.S(stream, "path");
        if (!string.IsNullOrEmpty(path) && TreeReader.L(stream, "size") > 0)
            data = e.file.ReadResource(path, (ulong)TreeReader.L(stream, "offset"), (uint)TreeReader.L(stream, "size"));
        tex.data = data;
        return tex;
    }
}

// ---------------------------------------------------------------------------------------------
// Byte readers
// ---------------------------------------------------------------------------------------------
internal sealed class BigEndianReader {
    private readonly Stream s;
    public BigEndianReader(Stream s) { this.s = s; }
    public byte U8() { int b = s.ReadByte(); if (b < 0) throw new GameAssetException("Unexpected end of file."); return (byte)b; }
    public byte[] Bytes(int n) {
        var b = new byte[n];
        int done = 0;
        while (done < n) { int k = s.Read(b, done, n - done); if (k <= 0) throw new GameAssetException("Unexpected end of file."); done += k; }
        return b;
    }
    public ushort U16() { var b = Bytes(2); return (ushort)(b[0] << 8 | b[1]); }
    public uint U32() { var b = Bytes(4); return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }
    public int I32() => (int)U32();
    public long I64() { long hi = U32(); long lo = U32(); return hi << 32 | lo; }
    public string CString() {
        var sb = new List<byte>();
        for (byte b = U8(); b != 0; b = U8()) sb.Add(b);
        return Encoding.UTF8.GetString(sb.ToArray());
    }
    public void Align(int n) { long p = s.Position; s.Position = (p + n - 1) / n * n; }
}

internal sealed class DataReader {
    private readonly byte[] d;
    private readonly bool be;
    public int Position;
    public DataReader(byte[] data, bool bigEndian) { d = data; be = bigEndian; }
    private void Need(int n) { if (Position + n > d.Length) throw new GameAssetException("Read past the end of an object."); }
    public byte U8() { Need(1); return d[Position++]; }
    public bool Bool() => U8() != 0;
    public byte[] Bytes(int n) { Need(n); var b = new byte[n]; Buffer.BlockCopy(d, Position, b, 0, n); Position += n; return b; }
    public void Skip(int n) { Need(n); Position += n; }
    private byte[] Take(int n) { var b = Bytes(n); if (be == BitConverter.IsLittleEndian) Array.Reverse(b); return b; }
    public short I16() => BitConverter.ToInt16(Take(2), 0);
    public ushort U16() => BitConverter.ToUInt16(Take(2), 0);
    public int I32() => BitConverter.ToInt32(Take(4), 0);
    public uint U32() => BitConverter.ToUInt32(Take(4), 0);
    public long I64() => BitConverter.ToInt64(Take(8), 0);
    public ulong U64() => BitConverter.ToUInt64(Take(8), 0);
    public float F32() => BitConverter.ToSingle(Take(4), 0);
    public double F64() => BitConverter.ToDouble(Take(8), 0);
    public void Align(int n) { Position = (Position + n - 1) / n * n; if (Position > d.Length) Position = d.Length; }
    public string CString() {
        int start = Position;
        while (Position < d.Length && d[Position] != 0) Position++;
        string s = Encoding.UTF8.GetString(d, start, Position - start);
        Position++;
        return s;
    }
    public string AlignedString() {
        int n = I32();
        string s = n > 0 ? Encoding.UTF8.GetString(Bytes(n)) : "";
        Align(4);
        return s;
    }
}

// ---------------------------------------------------------------------------------------------
// Field layouts of Mesh and Texture2D in Unity 6000.3 (Big Walk's version). Generated from
// Unity's type tree database; each row is level,type,name,metaFlags,byteSize.
// ---------------------------------------------------------------------------------------------
internal static class Layouts {
    internal const string MeshTree =
        "0,Mesh,Base,32768,-1;1,string,m_Name,557057,-1;2,Array,Array,540673,-1;3,int,size,524289,4;3,char,data,524289,1;1,vector,m_SubMeshes,32769,-1;2,Array,Array,16385,-1;3,int,size,1,4;3,SubMesh,data,1,48;4,unsigned int,firstByte,1,4;4,unsigned int,indexCount,1,4;4,int,topology,1,4;4,unsigned int,baseVertex,1,4;4,unsigned int,firstVertex,1,4;4,unsigned int,vertexCount,1,4;4,AABB,localAABB,1,24;5,Vector3f,m_Center,2097153,12;6,float,x,2097153,4;6,float,y,2097153,4;6,float,z,2097153,4;5,Vector3f,m_Extent,2097153,12;6,float,x,2097153,4;6,float,y,2097153,4;6,float,z,2097153,4;1,BlendShapeData,m_Shapes,32769,-1;2,vector,vertices,32769,-1;3,Array,Array,16385,-1;4,int,size,1,4;4,BlendShapeVertex,data,1,40;5,Vector3f,vertex,2097153,12;6,float,x,2097153,4;6,float,y,2097153,4;6,float,z,2097153,4;5,Vector3f,normal,2097153,12;6,float,x,2097153,4;6,float,y,2097153,4;6,float,z,2097153,4;5,Vector3f,tangent,2097153,12;6,float,x,2097153,4;6,float,y,2097153,4;6,float,z,2097153,4;5,unsigned int,index,1,4;2,vector,shapes,32769,-1;3,Array,Array,49153,-1;4,int,size,1,4;4,MeshBlendShape,data,32769,10;5,unsigned int,firstVertex,1,4;5,unsigned int,vertexCount,1,4;5,bool,hasNormals,1,1;5,bool,hasTangents,16385,1;2,vector,channels,32769,-1;3,Array,Array,49153,-1;4,int,size,1,4;4,MeshBlendShapeChannel,data,32769,-1;5,string,name,32769,-1;6,Array,Array,16385,-1;7,int,size,1,4;7,char,data,1,1;5,unsigned int,nameHash,1,4;5,int,frameIndex,1,4;5,int,frameCount,1,4;2,vector,fullWeights,32769,-1;3,Array,Array,16385,-1;4,int,size,1,4;4,float,data,1,4;1,vector,m_BindPose,32769,-1;2,Array,Array,16385,-1;3,int,size,1,4;3,Matrix4x4f,data,1,64;4,float,e00,1,4;4,float,e01,1,4;4,float,e02,1,4;4,float,e03,1,4;4,float,e10,1,4;4,float,e11,1,4;4,float,e12,1,4;4,float,e13,1,4;4,float,e20,1,4;4,float,e21,1,4;4,float,e22,1,4;4,float,e23,1,4;4,float,e30,1,4;4,float,e31,1,4;4,float,e32,1,4;4,float,e33,1,4;1,vector,m_BoneNameHashes,32769,-1;2,Array,Array,16385,-1;3,int,size,1,4;3,unsigned int,data,1,4;1,unsigned int,m_RootBoneNameHash,1,4;1,vector,m_BonesAABB,32769,-1;2,Array,Array,16385,-1;3,int,size,1,4;3,MinMaxAABB,data,1,24;4,Vector3f,m_Min,2097153,12;5,float,x,2097153,4;5,float,y,2097153,4;5,float,z,2097153,4;4,Vector3f,m_Max,2097153,12;5,float,x,2097153,4;5,float,y,2097153,4;5,float,z,2097153,4;1,VariableBoneCountWeights,m_VariableBoneCountWeights,32769,-1;2,vector,m_Data,32769,-1;3,Array,Array,16385,-1;4,int,size,1,4;4,unsigned int,data,1,4;1,UInt8,m_MeshCompression,1,1;1,bool,m_IsReadable,1,1;1,bool,m_KeepVertices,1,1;1,bool,m_KeepIndices,16385,1;1,int,m_IndexFormat,1,4;1,vector,m_IndexBuffer,32769,-1;2,Array,Array,16385,-1;3,int,size,1,4;3,UInt8,data,1,1;1,VertexData,m_VertexData,49153,-1;2,unsigned int,m_VertexCount,1,4;2,vector,m_Channels,32769,-1;3,Array,Array,16385,-1;4,int,size,1,4;4,ChannelInfo,data,1,4;5,UInt8,stream,1,1;5,UInt8,offset,1,1;5,UInt8,format,1,1;5,UInt8,dimension,1,1;2,TypelessData,m_DataSize,16385,-1;3,int,size,1,4;3,UInt8,data,1,1;1,CompressedMesh,m_CompressedMesh,32769,-1;2,PackedBitVector,m_Vertices,32769,-1;3,unsigned int,m_NumItems,1,4;3,float,m_Range,1,4;3,float,m_Start,1,4;3,vector,m_Data,32769,-1;4,Array,Array,16385,-1;5,int,size,1,4;5,UInt8,data,1,1;3,UInt8,m_BitSize,16385,1;2,PackedBitVector,m_UV,32769,-1;3,unsigned int,m_NumItems,1,4;3,float,m_Range,1,4;3,float,m_Start,1,4;3,vector,m_Data,32769,-1;4,Array,Array,16385,-1;5,int,size,1,4;5,UInt8,data,1,1;3,UInt8,m_BitSize,16385,1;2,PackedBitVector,m_Normals,32769,-1;3,unsigned int,m_NumItems,1,4;3,float,m_Range,1,4;3,float,m_Start,1,4;3,vector,m_Data,32769,-1;4,Array,Array,16385,-1;5,int,size,1,4;5,UInt8,data,1,1;3,UInt8,m_BitSize,16385,1;2,PackedBitVector,m_Tangents,32769,-1;3,unsigned int,m_NumItems,1,4;3,float,m_Range,1,4;3,float,m_Start,1,4;3,vector,m_Data,32769,-1;4,Array,Array,16385,-1;5,int,size,1,4;5,UInt8,data,1,1;3,UInt8,m_BitSize,16385,1;2,PackedBitVector,m_Weights,32769,-1;3,unsigned int,m_NumItems,1,4;3,vector,m_Data,32769,-1;4,Array,Array,16385,-1;5,int,size,1,4;5,UInt8,data,1,1;3,UInt8,m_BitSize,16385,1;2,PackedBitVector,m_NormalSigns,32769,-1;3,unsigned int,m_NumItems,1,4;3,vector,m_Data,32769,-1;4,Array,Array,16385,-1;5,int,size,1,4;5,UInt8,data,1,1;3,UInt8,m_BitSize,16385,1;2,PackedBitVector,m_TangentSigns,32769,-1;3,unsigned int,m_NumItems,1,4;3,vector,m_Data,32769,-1;4,Array,Array,16385,-1;5,int,size,1,4;5,UInt8,data,1,1;3,UInt8,m_BitSize,16385,1;2,PackedBitVector,m_FloatColors,32769,-1;3,unsigned int,m_NumItems,1,4;3,float,m_Range,1,4;3,float,m_Start,1,4;3,vector,m_Data,32769,-1;4,Array,Array,16385,-1;5,int,size,1,4;5,UInt8,data,1,1;3,UInt8,m_BitSize,16385,1;2,PackedBitVector,m_BoneIndices,32769,-1;3,unsigned int,m_NumItems,1,4;3,vector,m_Data,32769,-1;4,Array,Array,16385,-1;5,int,size,1,4;5,UInt8,data,1,1;3,UInt8,m_BitSize,16385,1;2,PackedBitVector,m_Triangles,32769,-1;3,unsigned int,m_NumItems,1,4;3,vector,m_Data,32769,-1;4,Array,Array,16385,-1;5,int,size,1,4;5,UInt8,data,1,1;3,UInt8,m_BitSize,16385,1;2,unsigned int,m_UVInfo,1,4;1,AABB,m_LocalAABB,1,24;2,Vector3f,m_Center,2097153,12;3,float,x,2097153,4;3,float,y,2097153,4;3,float,z,2097153,4;2,Vector3f,m_Extent,2097153,12;3,float,x,2097153,4;3,float,y,2097153,4;3,float,z,2097153,4;1,int,m_MeshUsageFlags,1,4;1,int,m_CookingOptions,0,4;1,vector,m_BakedConvexCollisionMesh,32769,-1;2,Array,Array,16385,-1;3,int,size,1,4;3,UInt8,data,1,1;1,vector,m_BakedTriangleCollisionMesh,32769,-1;2,Array,Array,16385,-1;3,int,size,1,4;3,UInt8,data,1,1;1,float,m_MeshMetrics[0],0,4;1,float,m_MeshMetrics[1],16384,4;1,StreamingInfo,m_StreamData,32768,-1;2,UInt64,offset,0,8;2,unsigned int,size,0,4;2,string,path,32768,-1;3,Array,Array,16385,-1;4,int,size,1,4;4,char,data,1,1;1,MeshLodInfo,m_MeshLodInfo,32769,-1;2,LodSelectionCurve,m_LodSelectionCurve,32769,8;3,float,m_LodSlope,1,4;3,float,m_LodBias,16385,4;2,int,m_NumLevels,16385,4;2,vector,m_SubMeshes,49153,-1;3,Array,Array,49153,-1;4,int,size,1,4;4,MeshLodSubMesh,data,32769,-1;5,vector,m_Levels,49153,-1;6,Array,Array,49153,-1;7,int,size,1,4;7,MeshLodRange,data,32769,8;8,unsigned int,m_IndexStart,1,4;8,unsigned int,m_IndexCount,16385,4";
    internal const string Texture2DTree =
        "0,Texture2D,Base,32768,-1;1,string,m_Name,557057,-1;2,Array,Array,540673,-1;3,int,size,524289,4;3,char,data,524289,1;1,bool,m_IsAlphaChannelOptional,16384,1;1,int,m_Width,16,4;1,int,m_Height,16,4;1,unsigned int,m_CompleteImageSize,16,4;1,int,m_MipsStripped,16,4;1,int,m_TextureFormat,1,4;1,int,m_MipCount,16,4;1,bool,m_IsReadable,0,1;1,bool,m_IsPreProcessed,1,1;1,bool,m_IgnoreMipmapLimit,16384,1;1,string,m_MipmapLimitGroupName,32768,-1;2,Array,Array,16385,-1;3,int,size,1,4;3,char,data,1,1;1,bool,m_StreamingMipmaps,16384,1;1,int,m_StreamingMipmapsPriority,16384,4;1,int,m_ImageCount,16,4;1,int,m_TextureDimension,1,4;1,GLTextureSettings,m_TextureSettings,0,24;2,int,m_FilterMode,0,4;2,int,m_Aniso,0,4;2,float,m_MipBias,0,4;2,int,m_WrapU,0,4;2,int,m_WrapV,0,4;2,int,m_WrapW,0,4;1,int,m_LightmapFormat,0,4;1,int,m_ColorSpace,0,4;1,vector,m_PlatformBlob,32768,-1;2,Array,Array,16384,-1;3,int,size,0,4;3,UInt8,data,0,1;1,TypelessData,image data,16385,-1;2,int,size,1,4;2,UInt8,data,1,1;1,StreamingInfo,m_StreamData,32768,-1;2,UInt64,offset,0,8;2,unsigned int,size,0,4;2,string,path,32768,-1;3,Array,Array,16385,-1;4,int,size,1,4;4,char,data,1,1";
}

} // namespace BigPuzzleSDK.GameAssets
