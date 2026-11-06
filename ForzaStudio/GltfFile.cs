namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // Minimal JSON for glTF: objects are Dictionary<string, object> (key order kept), arrays List<object>,
    // numbers double, plus string, bool and null. The writer also takes int, long, float and any IEnumerable.
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            int p = 0;
            object value = ParseValue(text, ref p);
            SkipSpace(text, ref p);
            if (p != text.Length)
            {
                throw new InvalidDataException("JSON: unexpected text at character " + p + ".");
            }
            return value;
        }

        private static char Peek(string s, int p)
        {
            return (p < s.Length) ? s[p] : '\0';
        }

        private static void SkipSpace(string s, ref int p)
        {
            while (p < s.Length && (s[p] == ' ' || s[p] == '\t' || s[p] == '\r' || s[p] == '\n'))
            {
                p++;
            }
        }

        private static void Expect(string s, ref int p, char c)
        {
            SkipSpace(s, ref p);
            if (Peek(s, p) != c)
            {
                throw new InvalidDataException("JSON: '" + c + "' expected at character " + p + ".");
            }
            p++;
        }

        private static object ParseValue(string s, ref int p)
        {
            SkipSpace(s, ref p);
            char c = Peek(s, p);
            if (c == '{')
            {
                p++;
                Dictionary<string, object> obj = new Dictionary<string, object>(StringComparer.Ordinal);
                SkipSpace(s, ref p);
                if (Peek(s, p) == '}')
                {
                    p++;
                    return obj;
                }
                while (true)
                {
                    SkipSpace(s, ref p);
                    string key = ParseString(s, ref p);
                    Expect(s, ref p, ':');
                    obj[key] = ParseValue(s, ref p);
                    SkipSpace(s, ref p);
                    char next = Peek(s, p++);
                    if (next == '}')
                    {
                        return obj;
                    }
                    if (next != ',')
                    {
                        throw new InvalidDataException("JSON: ',' or '}' expected at character " + (p - 1) + ".");
                    }
                }
            }
            if (c == '[')
            {
                p++;
                List<object> list = new List<object>();
                SkipSpace(s, ref p);
                if (Peek(s, p) == ']')
                {
                    p++;
                    return list;
                }
                while (true)
                {
                    list.Add(ParseValue(s, ref p));
                    SkipSpace(s, ref p);
                    char next = Peek(s, p++);
                    if (next == ']')
                    {
                        return list;
                    }
                    if (next != ',')
                    {
                        throw new InvalidDataException("JSON: ',' or ']' expected at character " + (p - 1) + ".");
                    }
                }
            }
            if (c == '"')
            {
                return ParseString(s, ref p);
            }
            if (string.CompareOrdinal(s, p, "true", 0, 4) == 0)
            {
                p += 4;
                return true;
            }
            if (string.CompareOrdinal(s, p, "false", 0, 5) == 0)
            {
                p += 5;
                return false;
            }
            if (string.CompareOrdinal(s, p, "null", 0, 4) == 0)
            {
                p += 4;
                return null;
            }
            int start = p;
            while (p < s.Length && "+-0123456789.eE".IndexOf(s[p]) >= 0)
            {
                p++;
            }
            if (p == start)
            {
                throw new InvalidDataException("JSON: value expected at character " + p + ".");
            }
            return double.Parse(s.Substring(start, p - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string ParseString(string s, ref int p)
        {
            if (Peek(s, p) != '"')
            {
                throw new InvalidDataException("JSON: string expected at character " + p + ".");
            }
            p++;
            StringBuilder text = new StringBuilder();
            while (true)
            {
                if (p >= s.Length)
                {
                    throw new InvalidDataException("JSON: unterminated string.");
                }
                char c = s[p++];
                if (c == '"')
                {
                    return text.ToString();
                }
                if (c != '\\')
                {
                    text.Append(c);
                    continue;
                }
                char e = Peek(s, p++);
                switch (e)
                {
                    case 'b': text.Append('\b'); break;
                    case 'f': text.Append('\f'); break;
                    case 'n': text.Append('\n'); break;
                    case 'r': text.Append('\r'); break;
                    case 't': text.Append('\t'); break;
                    case 'u':
                        if (p + 4 > s.Length)
                        {
                            throw new InvalidDataException("JSON: bad \\u escape.");
                        }
                        text.Append((char) int.Parse(s.Substring(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        p += 4;
                        break;
                    default: text.Append(e); break;
                }
            }
        }

        public static string Write(object value)
        {
            StringBuilder text = new StringBuilder();
            Write(text, value);
            return text.ToString();
        }

        private static void Write(StringBuilder text, object value)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            if (value == null)
            {
                text.Append("null");
            }
            else if (value is bool)
            {
                text.Append(((bool) value) ? "true" : "false");
            }
            else if (value is string)
            {
                WriteString(text, (string) value);
            }
            else if (value is int || value is long || value is uint)
            {
                text.Append(Convert.ToInt64(value, inv).ToString(inv));
            }
            else if (value is float)
            {
                float f = (float) value;
                text.Append((float.IsNaN(f) || float.IsInfinity(f)) ? "0" : f.ToString("R", inv));
            }
            else if (value is double)
            {
                double d = (double) value;
                text.Append((double.IsNaN(d) || double.IsInfinity(d)) ? "0" : d.ToString("R", inv));
            }
            else if (value is Dictionary<string, object>)
            {
                text.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> pair in (Dictionary<string, object>) value)
                {
                    if (!first)
                    {
                        text.Append(',');
                    }
                    first = false;
                    WriteString(text, pair.Key);
                    text.Append(':');
                    Write(text, pair.Value);
                }
                text.Append('}');
            }
            else if (value is IEnumerable)
            {
                text.Append('[');
                bool first = true;
                foreach (object item in (IEnumerable) value)
                {
                    if (!first)
                    {
                        text.Append(',');
                    }
                    first = false;
                    Write(text, item);
                }
                text.Append(']');
            }
            else
            {
                WriteString(text, value.ToString());
            }
        }

        private static void WriteString(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            text.Append("\\u").Append(((int) c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            text.Append(c);
                        }
                        break;
                }
            }
            text.Append('"');
        }
    }

    // A glTF 2.0 scene (.gltf with .bin or data: buffers, or .glb) as the node hierarchy and the meshes
    // under it. Reading gives each mesh as triangles in meters with every ancestor's transform applied;
    // glTF is meters, Y up, right handed, the axes the editor writes, so values pass through unchanged.
    // UVs: glTF has v going down (as Forza), the corners use the OBJ convention (v up), so v = 1 - v.
    public class GltfScene
    {
        public class Node
        {
            public int Index;
            public string Name;
            public Node Parent;
            public List<Node> Children = new List<Node>();
            public double[] Local = FbxScene.Identity();
            public int Mesh = -1;
            // One slot per material the mesh's primitives use, in order of first use; "" = no material.
            public List<string> Materials = new List<string>();
            internal Dictionary<int, int> SlotOfMaterial = new Dictionary<int, int>();
        }

        public List<Node> Nodes = new List<Node>();
        public List<Node> Roots = new List<Node>();

        private List<object> meshes = new List<object>();
        private List<object> accessors = new List<object>();
        private List<object> views = new List<object>();
        private List<byte[]> buffers = new List<byte[]>();

        private const uint GlbMagic = 0x46546C67;   // "glTF"
        private const uint ChunkJson = 0x4E4F534A;
        private const uint ChunkBin = 0x004E4942;

        #region Reading

        public static GltfScene Load(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            string json = null;
            byte[] bin = null;
            if (data.Length >= 12 && BitConverter.ToUInt32(data, 0) == GlbMagic)
            {
                uint version = BitConverter.ToUInt32(data, 4);
                if (version != 2)
                {
                    throw new InvalidDataException("GLB version " + version + " is not supported (glTF 2.0 only).");
                }
                int p = 12;
                while (p + 8 <= data.Length)
                {
                    int length = (int) BitConverter.ToUInt32(data, p);
                    uint type = BitConverter.ToUInt32(data, p + 4);
                    if (p + 8 + length > data.Length)
                    {
                        throw new InvalidDataException("GLB chunk past the end of the file.");
                    }
                    if (type == ChunkJson && json == null)
                    {
                        json = Encoding.UTF8.GetString(data, p + 8, length);
                    }
                    else if (type == ChunkBin && bin == null)
                    {
                        bin = new byte[length];
                        Buffer.BlockCopy(data, p + 8, bin, 0, length);
                    }
                    p += 8 + ((length + 3) & ~3);
                }
                if (json == null)
                {
                    throw new InvalidDataException("GLB file without a JSON chunk.");
                }
            }
            else
            {
                json = Encoding.UTF8.GetString(data);
            }
            json = json.TrimStart('﻿').TrimEnd('\0', ' ');
            Dictionary<string, object> root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null)
            {
                throw new InvalidDataException("The glTF file is not a JSON object.");
            }
            Dictionary<string, object> asset = Obj(root, "asset");
            string fileVersion = (asset != null) ? Str(asset, "version", "") : "";
            if (!fileVersion.StartsWith("2", StringComparison.Ordinal))
            {
                throw new InvalidDataException("glTF version '" + fileVersion + "' is not supported (glTF 2.0 only).");
            }

            GltfScene scene = new GltfScene();
            scene.meshes = List(root, "meshes");
            scene.accessors = List(root, "accessors");
            scene.views = List(root, "bufferViews");
            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            foreach (object item in List(root, "buffers"))
            {
                scene.buffers.Add(LoadBuffer((Dictionary<string, object>) item, bin, folder));
            }

            List<object> materials = List(root, "materials");
            List<object> nodes = List(root, "nodes");
            for (int i = 0; i < nodes.Count; i++)
            {
                Dictionary<string, object> source = (Dictionary<string, object>) nodes[i];
                Node node = new Node();
                node.Index = i;
                node.Name = Str(source, "name", null);
                node.Local = LocalMatrix(source);
                node.Mesh = Int(source, "mesh", -1);
                scene.Nodes.Add(node);
            }
            Dictionary<string, int> taken = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = scene.Nodes[i];
                foreach (object child in List((Dictionary<string, object>) nodes[i], "children"))
                {
                    int c = (int) (double) child;
                    if (c >= 0 && c < scene.Nodes.Count && scene.Nodes[c].Parent == null && scene.Nodes[c] != node)
                    {
                        scene.Nodes[c].Parent = node;
                        node.Children.Add(scene.Nodes[c]);
                    }
                }
                if (node.Name == null)
                {
                    // Unnamed node: its mesh's name, else a numbered one.
                    if (node.Mesh >= 0 && node.Mesh < scene.meshes.Count)
                    {
                        node.Name = Str((Dictionary<string, object>) scene.meshes[node.Mesh], "name", null);
                    }
                    if (node.Name == null)
                    {
                        node.Name = "node_" + i.ToString(CultureInfo.InvariantCulture);
                    }
                }
                if (node.Mesh >= 0 && node.Mesh < scene.meshes.Count)
                {
                    foreach (object primitive in List((Dictionary<string, object>) scene.meshes[node.Mesh], "primitives"))
                    {
                        int material = Int((Dictionary<string, object>) primitive, "material", -1);
                        if (!node.SlotOfMaterial.ContainsKey(material))
                        {
                            node.SlotOfMaterial[material] = node.Materials.Count;
                            string name = (material >= 0 && material < materials.Count) ? Str((Dictionary<string, object>) materials[material], "name", "") : "";
                            node.Materials.Add(name);
                        }
                    }
                }
                else
                {
                    node.Mesh = -1;
                }
            }

            // The scene's roots; without scenes every node with no parent.
            List<object> scenes = List(root, "scenes");
            int which = Int(root, "scene", 0);
            if (which >= 0 && which < scenes.Count)
            {
                foreach (object item in List((Dictionary<string, object>) scenes[which], "nodes"))
                {
                    int n = (int) (double) item;
                    if (n >= 0 && n < scene.Nodes.Count && scene.Nodes[n].Parent == null && !scene.Roots.Contains(scene.Nodes[n]))
                    {
                        scene.Roots.Add(scene.Nodes[n]);
                    }
                }
            }
            if (scene.Roots.Count == 0)
            {
                foreach (Node node in scene.Nodes)
                {
                    if (node.Parent == null)
                    {
                        scene.Roots.Add(node);
                    }
                }
            }
            // Only the nodes of that scene count.
            List<Node> inScene = new List<Node>();
            Stack<Node> walk = new Stack<Node>(scene.Roots);
            while (walk.Count > 0)
            {
                Node node = walk.Pop();
                inScene.Add(node);
                foreach (Node child in node.Children)
                {
                    walk.Push(child);
                }
            }
            inScene.Sort(delegate(Node a, Node b) { return a.Index.CompareTo(b.Index); });
            scene.Nodes = inScene;
            return scene;
        }

        private static byte[] LoadBuffer(Dictionary<string, object> buffer, byte[] bin, string folder)
        {
            string uri = Str(buffer, "uri", null);
            if (uri == null)
            {
                if (bin == null)
                {
                    throw new InvalidDataException("A glTF buffer has no uri and the file has no binary chunk.");
                }
                return bin;
            }
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = uri.IndexOf(',');
                if (comma < 0 || uri.IndexOf(";base64", 0, comma, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new InvalidDataException("Only base64 data: URIs are supported in glTF buffers.");
                }
                return Convert.FromBase64String(uri.Substring(comma + 1));
            }
            string file = Path.Combine(folder, Uri.UnescapeDataString(uri).Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file))
            {
                throw new FileNotFoundException("The glTF buffer file is missing: " + file, file);
            }
            return File.ReadAllBytes(file);
        }

        // The node's own transform (row-major, column vectors, as FbxScene): its matrix, else T * R * S.
        private static double[] LocalMatrix(Dictionary<string, object> node)
        {
            List<object> matrix = List(node, "matrix");
            if (matrix.Count == 16)
            {
                // glTF stores the matrix column by column.
                double[] m = new double[16];
                for (int i = 0; i < 16; i++)
                {
                    m[((i % 4) * 4) + (i / 4)] = (double) matrix[i];
                }
                return m;
            }
            double[] t = Numbers(node, "translation", new double[] { 0, 0, 0 });
            double[] q = Numbers(node, "rotation", new double[] { 0, 0, 0, 1 });
            double[] s = Numbers(node, "scale", new double[] { 1, 1, 1 });
            double x = q[0], y = q[1], z = q[2], w = q[3];
            double[] r = FbxScene.Identity();
            r[0] = 1 - (2 * ((y * y) + (z * z)));
            r[1] = 2 * ((x * y) - (z * w));
            r[2] = 2 * ((x * z) + (y * w));
            r[4] = 2 * ((x * y) + (z * w));
            r[5] = 1 - (2 * ((x * x) + (z * z)));
            r[6] = 2 * ((y * z) - (x * w));
            r[8] = 2 * ((x * z) - (y * w));
            r[9] = 2 * ((y * z) + (x * w));
            r[10] = 1 - (2 * ((x * x) + (y * y)));
            double[] result = new double[16];
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    result[(row * 4) + col] = r[(row * 4) + col] * s[col];
                }
                result[(row * 4) + 3] = t[row];
            }
            result[15] = 1;
            return result;
        }

        // Model space -> editor space: the node's transform and its ancestors'.
        public double[] WorldMatrix(Node node)
        {
            double[] world = FbxScene.Identity();
            for (Node n = node; n != null; n = n.Parent)
            {
                world = FbxScene.Multiply(n.Local, world);
            }
            return world;
        }

        public bool HasMesh(Node node)
        {
            return node.Mesh >= 0;
        }

        // The triangles of a node's mesh, one list per material slot; a slot with no name gets 'fallback'.
        // Triangle lists, strips and fans are read; points and lines are skipped.
        public List<ObjMesh.Material> ReadMesh(Node node, string fallback)
        {
            List<ObjMesh.Material> result = new List<ObjMesh.Material>();
            if (node.Mesh < 0)
            {
                return result;
            }
            for (int i = 0; i < node.Materials.Count; i++)
            {
                ObjMesh.Material material = new ObjMesh.Material();
                material.Name = (node.Materials[i].Length > 0) ? node.Materials[i] : fallback;
                result.Add(material);
            }
            double[] matrix = this.WorldMatrix(node);
            double[] normalMatrix = FbxScene.NormalMatrix(matrix);
            bool mirrored = FbxScene.Determinant3(matrix) < 0;
            foreach (object item in List((Dictionary<string, object>) this.meshes[node.Mesh], "primitives"))
            {
                Dictionary<string, object> primitive = (Dictionary<string, object>) item;
                int mode = Int(primitive, "mode", 4);
                if (mode != 4 && mode != 5 && mode != 6)
                {
                    continue;
                }
                Dictionary<string, object> attributes = Obj(primitive, "attributes");
                if (attributes == null || !attributes.ContainsKey("POSITION"))
                {
                    continue;
                }
                int components;
                double[] positions = this.ReadAccessor(Int(attributes, "POSITION", -1), out components);
                int vertexCount = positions.Length / 3;
                double[] normals = null;
                double[] uvs = null;
                if (attributes.ContainsKey("NORMAL"))
                {
                    normals = this.ReadAccessor(Int(attributes, "NORMAL", -1), out components);
                    if (components != 3 || normals.Length / 3 != vertexCount)
                    {
                        normals = null;
                    }
                }
                if (attributes.ContainsKey("TEXCOORD_0"))
                {
                    uvs = this.ReadAccessor(Int(attributes, "TEXCOORD_0", -1), out components);
                    if (components != 2 || uvs.Length / 2 != vertexCount)
                    {
                        uvs = null;
                    }
                }
                int[] indices;
                if (primitive.ContainsKey("indices"))
                {
                    double[] raw = this.ReadAccessor(Int(primitive, "indices", -1), out components);
                    indices = new int[raw.Length];
                    for (int i = 0; i < raw.Length; i++)
                    {
                        indices[i] = (int) raw[i];
                    }
                }
                else
                {
                    indices = new int[vertexCount];
                    for (int i = 0; i < vertexCount; i++)
                    {
                        indices[i] = i;
                    }
                }

                ObjMesh.Material target = result[node.SlotOfMaterial[Int(primitive, "material", -1)]];
                ObjMesh.Corner[] corners = new ObjMesh.Corner[vertexCount];
                for (int v = 0; v < vertexCount; v++)
                {
                    ObjMesh.Corner corner = new ObjMesh.Corner();
                    corner.Position = FbxScene.Transform(matrix, positions[v * 3], positions[(v * 3) + 1], positions[(v * 3) + 2]);
                    if (uvs != null)
                    {
                        corner.Uv = new Vector2((float) uvs[v * 2], 1f - (float) uvs[(v * 2) + 1]);
                        corner.HasUv = true;
                    }
                    if (normals != null)
                    {
                        Vector3 normal = FbxScene.Rotate(normalMatrix, normals[v * 3], normals[(v * 3) + 1], normals[(v * 3) + 2]);
                        float length = normal.LengthSquared();
                        if (length > 1e-12f)
                        {
                            if (Math.Abs(length - 1f) > 1e-4f)
                            {
                                normal.Normalize();
                            }
                            corner.Normal = normal;
                            corner.HasNormal = true;
                        }
                    }
                    corners[v] = corner;
                }

                int triangles = (mode == 4) ? indices.Length / 3 : Math.Max(0, indices.Length - 2);
                for (int t = 0; t < triangles; t++)
                {
                    int a, b, c;
                    if (mode == 4)
                    {
                        a = indices[t * 3];
                        b = indices[(t * 3) + 1];
                        c = indices[(t * 3) + 2];
                    }
                    else if (mode == 5)
                    {
                        a = indices[t];
                        b = indices[t + 1];
                        c = indices[t + 2];
                        if ((t & 1) == 1)
                        {
                            int swap = a;
                            a = b;
                            b = swap;
                        }
                        if (a == b || b == c || a == c)
                        {
                            continue;   // strip joint
                        }
                    }
                    else
                    {
                        a = indices[0];
                        b = indices[t + 1];
                        c = indices[t + 2];
                    }
                    if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount)
                    {
                        continue;
                    }
                    target.Triangles.Add(mirrored
                        ? new ObjMesh.Corner[] { corners[a], corners[c], corners[b] }
                        : new ObjMesh.Corner[] { corners[a], corners[b], corners[c] });
                }
            }
            result.RemoveAll(delegate(ObjMesh.Material m) { return m.Triangles.Count == 0; });
            return result;
        }

        private static int ComponentCount(string type)
        {
            switch (type)
            {
                case "SCALAR": return 1;
                case "VEC2": return 2;
                case "VEC3": return 3;
                case "VEC4": return 4;
                case "MAT2": return 4;
                case "MAT3": return 9;
                case "MAT4": return 16;
            }
            throw new InvalidDataException("Unknown glTF accessor type " + type + ".");
        }

        private static int ComponentSize(int componentType)
        {
            switch (componentType)
            {
                case 5120:
                case 5121: return 1;
                case 5122:
                case 5123: return 2;
                case 5125:
                case 5126: return 4;
            }
            throw new InvalidDataException("Unknown glTF component type " + componentType + ".");
        }

        private static double ReadComponent(byte[] data, int at, int componentType, bool normalized)
        {
            if (at < 0 || at + ComponentSize(componentType) > data.Length)
            {
                throw new InvalidDataException("A glTF accessor reads past the end of its buffer.");
            }
            switch (componentType)
            {
                case 5120: { double v = (sbyte) data[at]; return normalized ? Math.Max(v / 127.0, -1.0) : v; }
                case 5121: { double v = data[at]; return normalized ? v / 255.0 : v; }
                case 5122: { double v = BitConverter.ToInt16(data, at); return normalized ? Math.Max(v / 32767.0, -1.0) : v; }
                case 5123: { double v = BitConverter.ToUInt16(data, at); return normalized ? v / 65535.0 : v; }
                case 5125: return BitConverter.ToUInt32(data, at);
                default: return BitConverter.ToSingle(data, at);
            }
        }

        // An accessor's values, element after element (sparse substitutions applied).
        private double[] ReadAccessor(int index, out int components)
        {
            if (index < 0 || index >= this.accessors.Count)
            {
                throw new InvalidDataException("glTF accessor " + index + " does not exist.");
            }
            Dictionary<string, object> accessor = (Dictionary<string, object>) this.accessors[index];
            int count = Int(accessor, "count", 0);
            components = ComponentCount(Str(accessor, "type", "SCALAR"));
            int componentType = Int(accessor, "componentType", 5126);
            bool normalized = Bool(accessor, "normalized");
            int size = ComponentSize(componentType);
            double[] values = new double[count * components];
            if (accessor.ContainsKey("bufferView"))
            {
                Dictionary<string, object> view = this.View(Int(accessor, "bufferView", -1));
                byte[] data = this.buffers[Int(view, "buffer", 0)];
                int offset = Int(view, "byteOffset", 0) + Int(accessor, "byteOffset", 0);
                int stride = Int(view, "byteStride", 0);
                if (stride == 0)
                {
                    stride = size * components;
                }
                for (int i = 0; i < count; i++)
                {
                    for (int c = 0; c < components; c++)
                    {
                        values[(i * components) + c] = ReadComponent(data, offset + (i * stride) + (c * size), componentType, normalized);
                    }
                }
            }
            Dictionary<string, object> sparse = Obj(accessor, "sparse");
            if (sparse != null)
            {
                int sparseCount = Int(sparse, "count", 0);
                Dictionary<string, object> indexInfo = Obj(sparse, "indices");
                Dictionary<string, object> valueInfo = Obj(sparse, "values");
                Dictionary<string, object> indexView = this.View(Int(indexInfo, "bufferView", -1));
                Dictionary<string, object> valueView = this.View(Int(valueInfo, "bufferView", -1));
                byte[] indexData = this.buffers[Int(indexView, "buffer", 0)];
                byte[] valueData = this.buffers[Int(valueView, "buffer", 0)];
                int indexType = Int(indexInfo, "componentType", 5125);
                int indexOffset = Int(indexView, "byteOffset", 0) + Int(indexInfo, "byteOffset", 0);
                int valueOffset = Int(valueView, "byteOffset", 0) + Int(valueInfo, "byteOffset", 0);
                for (int k = 0; k < sparseCount; k++)
                {
                    int target = (int) ReadComponent(indexData, indexOffset + (k * ComponentSize(indexType)), indexType, false);
                    if (target < 0 || target >= count)
                    {
                        continue;
                    }
                    for (int c = 0; c < components; c++)
                    {
                        values[(target * components) + c] = ReadComponent(valueData, valueOffset + (((k * components) + c) * size), componentType, normalized);
                    }
                }
            }
            return values;
        }

        private Dictionary<string, object> View(int index)
        {
            if (index < 0 || index >= this.views.Count)
            {
                throw new InvalidDataException("glTF buffer view " + index + " does not exist.");
            }
            Dictionary<string, object> view = (Dictionary<string, object>) this.views[index];
            int buffer = Int(view, "buffer", 0);
            if (buffer < 0 || buffer >= this.buffers.Count)
            {
                throw new InvalidDataException("glTF buffer " + buffer + " does not exist.");
            }
            return view;
        }

        #endregion

        #region JSON helpers

        internal static Dictionary<string, object> Obj(Dictionary<string, object> obj, string key)
        {
            object value;
            return (obj != null && obj.TryGetValue(key, out value)) ? value as Dictionary<string, object> : null;
        }

        internal static List<object> List(Dictionary<string, object> obj, string key)
        {
            object value;
            List<object> list = (obj != null && obj.TryGetValue(key, out value)) ? value as List<object> : null;
            return list ?? new List<object>();
        }

        internal static string Str(Dictionary<string, object> obj, string key, string fallback)
        {
            object value;
            return (obj != null && obj.TryGetValue(key, out value) && value is string) ? (string) value : fallback;
        }

        internal static int Int(Dictionary<string, object> obj, string key, int fallback)
        {
            object value;
            return (obj != null && obj.TryGetValue(key, out value) && value is double) ? (int) (double) value : fallback;
        }

        internal static bool Bool(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) && value is bool && (bool) value;
        }

        private static double[] Numbers(Dictionary<string, object> obj, string key, double[] fallback)
        {
            List<object> list = List(obj, key);
            if (list.Count != fallback.Length)
            {
                return fallback;
            }
            double[] values = new double[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                values[i] = (list[i] is double) ? (double) list[i] : fallback[i];
            }
            return values;
        }

        #endregion
    }

    // Writes a glTF 2.0 scene: empty nodes for the hierarchy, one mesh node per material with one
    // triangle primitive (positions, normals, UVs, 32-bit or 16-bit indices). Meters, Y up, values the car's
    // own. 'path' ending in .glb writes one binary file, else a .gltf with its .bin next to it.
    public class GltfBuilder : IModelSceneBuilder
    {
        private readonly List<Dictionary<string, object>> nodes = new List<Dictionary<string, object>>();
        private readonly List<object> meshes = new List<object>();
        private readonly List<object> accessors = new List<object>();
        private readonly List<object> views = new List<object>();
        private readonly List<object> materialList = new List<object>();
        private readonly Dictionary<string, int> materials = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<object> roots = new List<object>();
        private readonly MemoryStream bin = new MemoryStream();
        private readonly string creator;

        public GltfBuilder(string creator)
        {
            this.creator = creator;
        }

        public long AddNull(string name, long parent)
        {
            return this.AddNode(name, parent, -1);
        }

        private long AddNode(string name, long parent, int mesh)
        {
            Dictionary<string, object> node = new Dictionary<string, object>(StringComparer.Ordinal);
            node["name"] = name;
            if (mesh >= 0)
            {
                node["mesh"] = mesh;
            }
            this.nodes.Add(node);
            int index = this.nodes.Count - 1;
            if (parent <= 0)
            {
                this.roots.Add(index);
            }
            else
            {
                Dictionary<string, object> owner = this.nodes[(int) parent - 1];
                if (!owner.ContainsKey("children"))
                {
                    owner["children"] = new List<object>();
                }
                ((List<object>) owner["children"]).Add(index);
            }
            return index + 1;
        }

        private struct VertexKey
        {
            public Vector3 Position;
            public Vector2 Uv;
            public Vector3 Normal;

            public override bool Equals(object other)
            {
                VertexKey b = (VertexKey) other;
                return this.Position == b.Position && this.Uv == b.Uv && this.Normal == b.Normal;
            }

            public override int GetHashCode()
            {
                return this.Position.GetHashCode() ^ (this.Uv.GetHashCode() * 31) ^ (this.Normal.GetHashCode() * 17);
            }
        }

        // A mesh node from a material's triangles: corners shared by exact value.
        public long AddMesh(string name, long parent, string materialName, ObjMesh.Material mesh, ExportTexture texture)
        {
            bool hasUv = mesh.Triangles.Count > 0;
            bool hasNormal = mesh.Triangles.Count > 0;
            foreach (ObjMesh.Corner[] triangle in mesh.Triangles)
            {
                foreach (ObjMesh.Corner corner in triangle)
                {
                    hasUv &= corner.HasUv;
                    hasNormal &= corner.HasNormal;
                }
            }
            Dictionary<VertexKey, int> index = new Dictionary<VertexKey, int>();
            List<ObjMesh.Corner> order = new List<ObjMesh.Corner>();
            List<int> faces = new List<int>();
            foreach (ObjMesh.Corner[] triangle in mesh.Triangles)
            {
                foreach (ObjMesh.Corner corner in triangle)
                {
                    VertexKey key = new VertexKey();
                    key.Position = corner.Position;
                    key.Uv = hasUv ? corner.Uv : Vector2.Zero;
                    key.Normal = hasNormal ? corner.Normal : Vector3.Zero;
                    int at;
                    if (!index.TryGetValue(key, out at))
                    {
                        at = order.Count;
                        index[key] = at;
                        order.Add(corner);
                    }
                    faces.Add(at);
                }
            }
            if (order.Count == 0)
            {
                return this.AddNode(name, parent, -1);
            }

            Dictionary<string, object> attributes = new Dictionary<string, object>(StringComparer.Ordinal);
            float[] positions = new float[order.Count * 3];
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            for (int i = 0; i < order.Count; i++)
            {
                Vector3 p = order[i].Position;
                positions[i * 3] = p.X;
                positions[(i * 3) + 1] = p.Y;
                positions[(i * 3) + 2] = p.Z;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            attributes["POSITION"] = this.AddFloats(positions, 3, "VEC3", new float[] { min.X, min.Y, min.Z }, new float[] { max.X, max.Y, max.Z });
            if (hasNormal)
            {
                float[] normals = new float[order.Count * 3];
                for (int i = 0; i < order.Count; i++)
                {
                    normals[i * 3] = order[i].Normal.X;
                    normals[(i * 3) + 1] = order[i].Normal.Y;
                    normals[(i * 3) + 2] = order[i].Normal.Z;
                }
                attributes["NORMAL"] = this.AddFloats(normals, 3, "VEC3", null, null);
            }
            if (hasUv)
            {
                float[] uvs = new float[order.Count * 2];
                for (int i = 0; i < order.Count; i++)
                {
                    uvs[i * 2] = order[i].Uv.X;
                    uvs[(i * 2) + 1] = 1f - order[i].Uv.Y;
                }
                attributes["TEXCOORD_0"] = this.AddFloats(uvs, 2, "VEC2", null, null);
            }
            Dictionary<string, object> primitive = new Dictionary<string, object>(StringComparer.Ordinal);
            primitive["attributes"] = attributes;
            primitive["indices"] = this.AddIndices(faces, order.Count);
            primitive["material"] = this.Material(materialName, texture);
            primitive["mode"] = 4;
            Dictionary<string, object> meshObject = new Dictionary<string, object>(StringComparer.Ordinal);
            meshObject["name"] = name;
            meshObject["primitives"] = new List<object>(new object[] { primitive });
            this.meshes.Add(meshObject);
            return this.AddNode(name, parent, this.meshes.Count - 1);
        }

        private int AddView(byte[] data, int target)
        {
            while ((this.bin.Length % 4) != 0)
            {
                this.bin.WriteByte(0);
            }
            Dictionary<string, object> view = new Dictionary<string, object>(StringComparer.Ordinal);
            view["buffer"] = 0;
            view["byteOffset"] = this.bin.Length;
            view["byteLength"] = data.Length;
            if (target > 0)
            {
                view["target"] = target;    // not for images
            }
            this.bin.Write(data, 0, data.Length);
            this.views.Add(view);
            return this.views.Count - 1;
        }

        private int AddFloats(float[] values, int components, string type, float[] min, float[] max)
        {
            byte[] data = new byte[values.Length * 4];
            Buffer.BlockCopy(values, 0, data, 0, data.Length);
            Dictionary<string, object> accessor = new Dictionary<string, object>(StringComparer.Ordinal);
            accessor["bufferView"] = this.AddView(data, 34962);
            accessor["componentType"] = 5126;
            accessor["count"] = values.Length / components;
            accessor["type"] = type;
            if (min != null)
            {
                accessor["min"] = min;
                accessor["max"] = max;
            }
            this.accessors.Add(accessor);
            return this.accessors.Count - 1;
        }

        private int AddIndices(List<int> faces, int vertexCount)
        {
            bool small = vertexCount <= 65535;
            byte[] data = new byte[faces.Count * (small ? 2 : 4)];
            for (int i = 0; i < faces.Count; i++)
            {
                if (small)
                {
                    data[i * 2] = (byte) faces[i];
                    data[(i * 2) + 1] = (byte) (faces[i] >> 8);
                }
                else
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(faces[i]), 0, data, i * 4, 4);
                }
            }
            Dictionary<string, object> accessor = new Dictionary<string, object>(StringComparer.Ordinal);
            accessor["bufferView"] = this.AddView(data, 34963);
            accessor["componentType"] = small ? 5123 : 5125;
            accessor["count"] = faces.Count;
            accessor["type"] = "SCALAR";
            this.accessors.Add(accessor);
            return this.accessors.Count - 1;
        }

        // A material per car material name and texture: with a texture it shows that picture (opaque, so
        // the UVs are easy to see), without one a stable random color (as the OBJ and FBX exports). The same
        // material name with two textures (main and lod0 files) gives two materials with that name.
        private int Material(string name, ExportTexture texture)
        {
            string key = name + "|" + ((texture != null) ? texture.Name : "");
            int at;
            if (this.materials.TryGetValue(key, out at))
            {
                return at;
            }
            Dictionary<string, object> pbr = new Dictionary<string, object>(StringComparer.Ordinal);
            if (texture != null)
            {
                pbr["baseColorTexture"] = new Dictionary<string, object>(StringComparer.Ordinal) { { "index", this.Texture(texture) } };
            }
            else
            {
                Vector3 color = Utilities.GetRandomColor(name.GetHashCode()).ToVector3();
                pbr["baseColorFactor"] = new float[] { color.X, color.Y, color.Z, 1f };
            }
            pbr["metallicFactor"] = 0f;
            pbr["roughnessFactor"] = 0.6f;
            Dictionary<string, object> material = new Dictionary<string, object>(StringComparer.Ordinal);
            material["name"] = name;
            material["pbrMetallicRoughness"] = pbr;
            material["doubleSided"] = true;
            this.materialList.Add(material);
            at = this.materialList.Count - 1;
            this.materials[key] = at;
            return at;
        }

        private readonly List<ExportTexture> pictures = new List<ExportTexture>();
        private readonly Dictionary<string, int> textureOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // One glTF texture (and image) per picture; the images are written by Save.
        private int Texture(ExportTexture texture)
        {
            int at;
            if (!this.textureOf.TryGetValue(texture.Name, out at))
            {
                this.pictures.Add(texture);
                at = this.pictures.Count - 1;
                this.textureOf[texture.Name] = at;
            }
            return at;
        }

        // images / samplers / textures: in the binary buffer for a .glb, PNG files in "textures" otherwise.
        private void WriteImages(Dictionary<string, object> root, string path, bool glb)
        {
            if (this.pictures.Count == 0)
            {
                return;
            }
            List<object> images = new List<object>();
            List<object> textures = new List<object>();
            string folder = ExportTexture.TexturesFolder(path);
            foreach (ExportTexture picture in this.pictures)
            {
                Dictionary<string, object> image = new Dictionary<string, object>(StringComparer.Ordinal);
                image["name"] = picture.Stem;
                if (glb)
                {
                    image["bufferView"] = this.AddView(picture.Png, 0);
                    image["mimeType"] = "image/png";
                }
                else
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, picture.Name), picture.Png);
                    image["uri"] = "textures/" + Uri.EscapeDataString(picture.Name);
                }
                images.Add(image);
                Dictionary<string, object> texture = new Dictionary<string, object>(StringComparer.Ordinal);
                texture["name"] = picture.Stem;
                texture["sampler"] = 0;
                texture["source"] = images.Count - 1;
                textures.Add(texture);
            }
            Dictionary<string, object> sampler = new Dictionary<string, object>(StringComparer.Ordinal);
            sampler["magFilter"] = 9729;    // LINEAR
            sampler["minFilter"] = 9987;    // LINEAR_MIPMAP_LINEAR
            sampler["wrapS"] = 10497;       // REPEAT
            sampler["wrapT"] = 10497;
            root["samplers"] = new List<object>(new object[] { sampler });
            root["images"] = images;
            root["textures"] = textures;
        }

        public void Save(string path)
        {
            bool glb = string.Equals(Path.GetExtension(path), ".glb", StringComparison.OrdinalIgnoreCase);
            Dictionary<string, object> root = new Dictionary<string, object>(StringComparer.Ordinal);
            Dictionary<string, object> images = new Dictionary<string, object>(StringComparer.Ordinal);
            this.WriteImages(images, path, glb);
            while ((this.bin.Length % 4) != 0)
            {
                this.bin.WriteByte(0);
            }
            Dictionary<string, object> asset = new Dictionary<string, object>(StringComparer.Ordinal);
            asset["version"] = "2.0";
            asset["generator"] = this.creator;
            root["asset"] = asset;
            root["scene"] = 0;
            Dictionary<string, object> scene = new Dictionary<string, object>(StringComparer.Ordinal);
            scene["name"] = "Scene";
            scene["nodes"] = this.roots;
            root["scenes"] = new List<object>(new object[] { scene });
            if (this.nodes.Count > 0)
            {
                root["nodes"] = this.nodes;
            }
            if (this.meshes.Count > 0)
            {
                root["meshes"] = this.meshes;
            }
            if (this.materialList.Count > 0)
            {
                root["materials"] = this.materialList;
            }
            foreach (KeyValuePair<string, object> pair in images)
            {
                root[pair.Key] = pair.Value;
            }
            if (this.accessors.Count > 0)
            {
                root["accessors"] = this.accessors;
            }
            if (this.views.Count > 0)
            {
                root["bufferViews"] = this.views;
                Dictionary<string, object> buffer = new Dictionary<string, object>(StringComparer.Ordinal);
                buffer["byteLength"] = this.bin.Length;
                if (!glb)
                {
                    buffer["uri"] = Uri.EscapeDataString(Path.GetFileNameWithoutExtension(path) + ".bin");
                }
                root["buffers"] = new List<object>(new object[] { buffer });
            }
            byte[] json = Encoding.UTF8.GetBytes(MiniJson.Write(root));
            if (!glb)
            {
                File.WriteAllBytes(path, json);
                if (this.accessors.Count > 0)
                {
                    File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFileNameWithoutExtension(path) + ".bin"), this.bin.ToArray());
                }
                return;
            }
            int jsonLength = (json.Length + 3) & ~3;
            int binLength = (int) this.bin.Length;
            int total = 12 + 8 + jsonLength + ((binLength > 0) ? 8 + binLength : 0);
            using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(file))
            {
                writer.Write(0x46546C67u);
                writer.Write(2u);
                writer.Write((uint) total);
                writer.Write((uint) jsonLength);
                writer.Write(0x4E4F534Au);
                writer.Write(json);
                for (int i = json.Length; i < jsonLength; i++)
                {
                    writer.Write((byte) ' ');
                }
                if (binLength > 0)
                {
                    writer.Write((uint) binLength);
                    writer.Write(0x004E4942u);
                    writer.Write(this.bin.ToArray());
                }
            }
        }
    }
}
