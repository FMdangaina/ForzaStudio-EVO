namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // An FBX scene as the node hierarchy (Models with their transforms), the meshes under it and
    // their materials. Reading gives each mesh as triangles in meters, in the axes the editor
    // writes (X right, Y up, Z front), with every ancestor's transform applied.
    public class FbxScene
    {
        public class Model
        {
            public long Id;
            public string Name;
            public string Kind;                 // Mesh, Null, LimbNode...
            public Model Parent;
            public List<Model> Children = new List<Model>();
            public Dictionary<string, double[]> Props = new Dictionary<string, double[]>();
            public FbxNode Geometry;
            public List<string> Materials = new List<string>();
        }

        public List<Model> Roots = new List<Model>();
        public Dictionary<long, Model> ById = new Dictionary<long, Model>();
        public double MetersPerUnit = 0.01;     // FBX default unit is the centimeter
        public double[] AxisBasis = Identity();   // file coordinates -> editor axes

        #region Reading

        public static string CleanName(string raw)
        {
            if (raw == null)
            {
                return "";
            }
            int cut = raw.IndexOf('\0');
            return (cut >= 0) ? raw.Substring(0, cut) : raw;
        }

        public static FbxScene Load(byte[] data)
        {
            FbxNode top = FbxFile.Read(data);
            FbxScene scene = new FbxScene();
            FbxNode settings = top.Find("GlobalSettings");
            if (settings != null)
            {
                Dictionary<string, double[]> values = Properties(settings.Find("Properties70"));
                scene.MetersPerUnit = Value(values, "UnitScaleFactor", 1) / 100.0;
                scene.AxisBasis = Basis((int) Value(values, "CoordAxis", 0), (int) Value(values, "CoordAxisSign", 1),
                    (int) Value(values, "UpAxis", 1), (int) Value(values, "UpAxisSign", 1),
                    (int) Value(values, "FrontAxis", 2), (int) Value(values, "FrontAxisSign", 1));
            }
            FbxNode objects = top.Find("Objects");
            if (objects == null)
            {
                throw new InvalidDataException("No Objects section in the FBX file.");
            }
            Dictionary<long, FbxNode> geometries = new Dictionary<long, FbxNode>();
            Dictionary<long, string> materials = new Dictionary<long, string>();
            foreach (FbxNode node in objects.Children)
            {
                long id = node.Long(0);
                if (node.Name == "Model")
                {
                    Model model = new Model();
                    model.Id = id;
                    model.Name = CleanName(node.Text(1));
                    model.Kind = node.Text(2) ?? "";
                    model.Props = Properties(node.Find("Properties70"));
                    scene.ById[id] = model;
                }
                else if (node.Name == "Geometry")
                {
                    geometries[id] = node;
                }
                else if (node.Name == "Material")
                {
                    materials[id] = CleanName(node.Text(1));
                }
            }
            FbxNode connections = top.Find("Connections");
            if (connections != null)
            {
                foreach (FbxNode connection in connections.FindAll("C"))
                {
                    string type = connection.Text(0);
                    long child = connection.Long(1);
                    long parent = connection.Long(2);
                    if (type != "OO")
                    {
                        continue;
                    }
                    Model owner;
                    Model sub;
                    FbxNode geometry;
                    string material;
                    if (scene.ById.TryGetValue(child, out sub))
                    {
                        if (scene.ById.TryGetValue(parent, out owner))
                        {
                            sub.Parent = owner;
                            owner.Children.Add(sub);
                        }
                    }
                    else if (geometries.TryGetValue(child, out geometry) && scene.ById.TryGetValue(parent, out owner))
                    {
                        owner.Geometry = geometry;
                    }
                    else if (materials.TryGetValue(child, out material) && scene.ById.TryGetValue(parent, out owner))
                    {
                        owner.Materials.Add(material);
                    }
                }
            }
            foreach (Model model in scene.ById.Values)
            {
                if (model.Parent == null)
                {
                    scene.Roots.Add(model);
                }
            }
            return scene;
        }

        // P records: name -> numeric values (strings and flags are skipped).
        private static Dictionary<string, double[]> Properties(FbxNode node)
        {
            Dictionary<string, double[]> values = new Dictionary<string, double[]>();
            if (node == null)
            {
                return values;
            }
            foreach (FbxNode p in node.FindAll("P"))
            {
                string name = p.Text(0);
                if (name == null)
                {
                    continue;
                }
                List<double> numbers = new List<double>();
                for (int i = 4; i < p.Props.Count; i++)
                {
                    if (!(p.Props[i] is string) && !(p.Props[i] is byte[]))
                    {
                        numbers.Add(p.Number(i, 0));
                    }
                }
                values[name] = numbers.ToArray();
            }
            return values;
        }

        private static double Value(Dictionary<string, double[]> values, string name, double fallback)
        {
            double[] found;
            return (values.TryGetValue(name, out found) && found.Length > 0) ? found[0] : fallback;
        }

        private static double[] Vector(Dictionary<string, double[]> values, string name, double fallback)
        {
            double[] found;
            double[] result = new double[] { fallback, fallback, fallback };
            if (values.TryGetValue(name, out found))
            {
                for (int i = 0; i < 3 && i < found.Length; i++)
                {
                    result[i] = found[i];
                }
            }
            return result;
        }

        // Rows = the editor's X, Y, Z expressed along the file's coordinate, up and front axes.
        private static double[] Basis(int coord, int coordSign, int up, int upSign, int front, int frontSign)
        {
            double[] m = new double[16];
            m[15] = 1;
            m[(0 * 4) + coord] = coordSign;
            m[(1 * 4) + up] = upSign;
            m[(2 * 4) + front] = frontSign;
            return m;
        }

        #endregion

        #region Matrices (column vectors, row-major)

        public static double[] Identity()
        {
            double[] m = new double[16];
            m[0] = m[5] = m[10] = m[15] = 1;
            return m;
        }

        public static double[] Multiply(double[] a, double[] b)
        {
            double[] m = new double[16];
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    double sum = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        sum += a[(r * 4) + k] * b[(k * 4) + c];
                    }
                    m[(r * 4) + c] = sum;
                }
            }
            return m;
        }

        private static double[] Translation(double[] v, double sign)
        {
            double[] m = Identity();
            m[3] = v[0] * sign;
            m[7] = v[1] * sign;
            m[11] = v[2] * sign;
            return m;
        }

        private static double[] Scaling(double[] v)
        {
            double[] m = Identity();
            m[0] = v[0];
            m[5] = v[1];
            m[10] = v[2];
            return m;
        }

        private static double[] RotationAxis(int axis, double degrees)
        {
            double a = degrees * Math.PI / 180.0;
            double c = Math.Cos(a);
            double s = Math.Sin(a);
            double[] m = Identity();
            if (axis == 0) { m[5] = c; m[6] = -s; m[9] = s; m[10] = c; }
            else if (axis == 1) { m[0] = c; m[2] = s; m[8] = -s; m[10] = c; }
            else { m[0] = c; m[1] = -s; m[4] = s; m[5] = c; }
            return m;
        }

        private static readonly string[] Orders = new string[] { "XYZ", "XZY", "YZX", "YXZ", "ZXY", "ZYX" };

        // Euler angles in degrees; order "ABC" rotates about A first, then B, then C.
        private static double[] Rotation(double[] degrees, int order, bool inverse)
        {
            string letters = Orders[(order >= 0 && order < Orders.Length) ? order : 0];
            double[] m = Identity();
            for (int i = 0; i < 3; i++)
            {
                int axis = letters[i] - 'X';
                m = Multiply(RotationAxis(axis, degrees[axis]), m);
            }
            return inverse ? Transpose3(m) : m;
        }

        private static double[] Transpose3(double[] m)
        {
            double[] t = Identity();
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    t[(r * 4) + c] = m[(c * 4) + r];
                }
            }
            return t;
        }

        // The model's own transform, its parent's not included.
        private static double[] Local(Model model)
        {
            Dictionary<string, double[]> p = model.Props;
            int order = (int) Value(p, "RotationOrder", 0);
            double[] m = Translation(Vector(p, "Lcl Translation", 0), 1);
            m = Multiply(m, Translation(Vector(p, "RotationOffset", 0), 1));
            m = Multiply(m, Translation(Vector(p, "RotationPivot", 0), 1));
            m = Multiply(m, Rotation(Vector(p, "PreRotation", 0), order, false));
            m = Multiply(m, Rotation(Vector(p, "Lcl Rotation", 0), order, false));
            m = Multiply(m, Rotation(Vector(p, "PostRotation", 0), order, true));
            m = Multiply(m, Translation(Vector(p, "RotationPivot", 0), -1));
            m = Multiply(m, Translation(Vector(p, "ScalingOffset", 0), 1));
            m = Multiply(m, Translation(Vector(p, "ScalingPivot", 0), 1));
            m = Multiply(m, Scaling(Vector(p, "Lcl Scaling", 1)));
            m = Multiply(m, Translation(Vector(p, "ScalingPivot", 0), -1));
            return m;
        }

        private static double[] Geometric(Model model)
        {
            Dictionary<string, double[]> p = model.Props;
            double[] m = Translation(Vector(p, "GeometricTranslation", 0), 1);
            m = Multiply(m, Rotation(Vector(p, "GeometricRotation", 0), 0, false));
            return Multiply(m, Scaling(Vector(p, "GeometricScaling", 1)));
        }

        // Model space -> editor space (meters, editor axes) for the model's mesh.
        public double[] MeshMatrix(Model model)
        {
            List<Model> chain = new List<Model>();
            for (Model m = model; m != null; m = m.Parent)
            {
                chain.Add(m);
            }
            double[] world = Identity();
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                world = Multiply(world, Local(chain[i]));
            }
            world = Multiply(world, Geometric(model));
            double[] units = Scaling(new double[] { this.MetersPerUnit, this.MetersPerUnit, this.MetersPerUnit });
            return Multiply(this.AxisBasis, Multiply(units, world));
        }

        #endregion

        #region Meshes

        private class Layer
        {
            public string Mapping = "";
            public bool Indexed;
            public double[] Values;
            public int[] Index;

            public static Layer Read(FbxNode node, string valuesName, string indexName)
            {
                if (node == null)
                {
                    return null;
                }
                Layer layer = new Layer();
                FbxNode mapping = node.Find("MappingInformationType");
                FbxNode reference = node.Find("ReferenceInformationType");
                layer.Mapping = (mapping != null) ? (mapping.Text(0) ?? "") : "";
                layer.Indexed = reference != null && reference.Text(0) == "IndexToDirect";
                FbxNode values = node.Find(valuesName);
                FbxNode index = (indexName != null) ? node.Find(indexName) : null;
                if (values != null && values.Props.Count > 0)
                {
                    layer.Values = AsDoubles(values.Props[0]);
                }
                if (index != null && index.Props.Count > 0)
                {
                    layer.Index = AsInts(index.Props[0]);
                }
                if (layer.Indexed && layer.Index == null)
                {
                    layer.Indexed = false;
                }
                return (layer.Values != null) ? layer : null;
            }

            // Which value group a corner takes.
            public int Select(int polygonVertex, int vertex, int polygon)
            {
                int i;
                switch (this.Mapping)
                {
                    case "ByPolygonVertex": i = polygonVertex; break;
                    case "ByVertice":
                    case "ByVertex": i = vertex; break;
                    case "ByPolygon": i = polygon; break;
                    default: i = 0; break;
                }
                if (this.Indexed)
                {
                    i = (i < this.Index.Length) ? this.Index[i] : -1;
                }
                return i;
            }
        }

        private static double[] AsDoubles(object value)
        {
            if (value is double[]) { return (double[]) value; }
            if (value is float[])
            {
                float[] f = (float[]) value;
                double[] d = new double[f.Length];
                for (int i = 0; i < f.Length; i++) { d[i] = f[i]; }
                return d;
            }
            if (value is int[])
            {
                int[] n = (int[]) value;
                double[] d = new double[n.Length];
                for (int i = 0; i < n.Length; i++) { d[i] = n[i]; }
                return d;
            }
            return null;
        }

        private static int[] AsInts(object value)
        {
            return value as int[];
        }

        public bool HasMesh(Model model)
        {
            return model.Geometry != null && model.Geometry.Find("Vertices") != null;
        }

        // The triangles of a mesh model, one list per material slot (its Material connections, in order).
        // A slot with no name gets 'fallback'. Normals and UVs follow the corner; missing ones stay unset.
        public List<ObjMesh.Material> ReadMesh(Model model, string fallback)
        {
            FbxNode geometry = model.Geometry;
            double[] vertices = AsDoubles(geometry.Find("Vertices").Props[0]);
            FbxNode indexNode = geometry.Find("PolygonVertexIndex");
            if (vertices == null || indexNode == null)
            {
                return new List<ObjMesh.Material>();
            }
            int[] indices = AsInts(indexNode.Props[0]);
            Layer normals = Layer.Read(geometry.Find("LayerElementNormal"), "Normals", "NormalsIndex");
            Layer uvs = Layer.Read(geometry.Find("LayerElementUV"), "UV", "UVIndex");
            Layer slots = Layer.Read(geometry.Find("LayerElementMaterial"), "Materials", null);
            if (slots != null && slots.Indexed)
            {
                slots.Indexed = false;
            }

            double[] matrix = this.MeshMatrix(model);
            double[] normalMatrix = NormalMatrix(matrix);
            bool mirrored = Determinant3(matrix) < 0;
            int count = Math.Max(1, model.Materials.Count);
            List<ObjMesh.Material> result = new List<ObjMesh.Material>();
            for (int i = 0; i < count; i++)
            {
                ObjMesh.Material material = new ObjMesh.Material();
                material.Name = (i < model.Materials.Count) ? model.Materials[i] : fallback;
                result.Add(material);
            }

            List<ObjMesh.Corner> polygon = new List<ObjMesh.Corner>();
            int polygonIndex = 0;
            int slot = 0;
            for (int pv = 0; pv < indices.Length; pv++)
            {
                int vertex = indices[pv];
                bool last = vertex < 0;
                if (last)
                {
                    vertex = ~vertex;
                }
                if (polygon.Count == 0)
                {
                    slot = 0;
                    if (slots != null)
                    {
                        int s = slots.Select(pv, 0, polygonIndex);
                        if (s >= 0 && s < slots.Values.Length)
                        {
                            slot = (int) slots.Values[s];
                        }
                        else if (slots.Mapping == "ByPolygon" && polygonIndex < slots.Values.Length)
                        {
                            slot = (int) slots.Values[polygonIndex];
                        }
                    }
                    if (slot < 0 || slot >= result.Count)
                    {
                        slot = 0;
                    }
                }
                ObjMesh.Corner corner = new ObjMesh.Corner();
                Vector3 position = Transform(matrix, vertices[vertex * 3], vertices[(vertex * 3) + 1], vertices[(vertex * 3) + 2]);
                corner.Position = position;
                if (uvs != null)
                {
                    int u = uvs.Select(pv, vertex, polygonIndex);
                    if (u >= 0 && (u * 2) + 1 < uvs.Values.Length)
                    {
                        corner.Uv = new Vector2((float) uvs.Values[u * 2], (float) uvs.Values[(u * 2) + 1]);
                        corner.HasUv = true;
                    }
                }
                if (normals != null)
                {
                    int n = normals.Select(pv, vertex, polygonIndex);
                    if (n >= 0 && (n * 3) + 2 < normals.Values.Length)
                    {
                        Vector3 normal = Rotate(normalMatrix, normals.Values[n * 3], normals.Values[(n * 3) + 1], normals.Values[(n * 3) + 2]);
                        if (normal.LengthSquared() > 1e-12f)
                        {
                            corner.Normal = normal;
                            corner.HasNormal = true;
                        }
                    }
                }
                polygon.Add(corner);
                if (last)
                {
                    for (int i = 1; i + 1 < polygon.Count; i++)
                    {
                        result[slot].Triangles.Add(mirrored
                            ? new ObjMesh.Corner[] { polygon[0], polygon[i + 1], polygon[i] }
                            : new ObjMesh.Corner[] { polygon[0], polygon[i], polygon[i + 1] });
                    }
                    polygon.Clear();
                    polygonIndex++;
                }
            }
            result.RemoveAll(delegate(ObjMesh.Material m) { return m.Triangles.Count == 0; });
            return result;
        }

        internal static Vector3 Transform(double[] m, double x, double y, double z)
        {
            return new Vector3(
                (float) ((m[0] * x) + (m[1] * y) + (m[2] * z) + m[3]),
                (float) ((m[4] * x) + (m[5] * y) + (m[6] * z) + m[7]),
                (float) ((m[8] * x) + (m[9] * y) + (m[10] * z) + m[11]));
        }

        internal static Vector3 Rotate(double[] m, double x, double y, double z)
        {
            Vector3 v = new Vector3(
                (float) ((m[0] * x) + (m[1] * y) + (m[2] * z)),
                (float) ((m[4] * x) + (m[5] * y) + (m[6] * z)),
                (float) ((m[8] * x) + (m[9] * y) + (m[10] * z)));
            return v;
        }

        internal static double Determinant3(double[] m)
        {
            return (m[0] * ((m[5] * m[10]) - (m[6] * m[9])))
                - (m[1] * ((m[4] * m[10]) - (m[6] * m[8])))
                + (m[2] * ((m[4] * m[9]) - (m[5] * m[8])));
        }

        // Inverse transpose of the upper 3x3 (cofactors / determinant), so normals stay normals under scaling.
        internal static double[] NormalMatrix(double[] m)
        {
            double det = Determinant3(m);
            double[] n = Identity();
            if (Math.Abs(det) < 1e-18)
            {
                return n;
            }
            n[0] = ((m[5] * m[10]) - (m[6] * m[9])) / det;
            n[1] = -((m[4] * m[10]) - (m[6] * m[8])) / det;
            n[2] = ((m[4] * m[9]) - (m[5] * m[8])) / det;
            n[4] = -((m[1] * m[10]) - (m[2] * m[9])) / det;
            n[5] = ((m[0] * m[10]) - (m[2] * m[8])) / det;
            n[6] = -((m[0] * m[9]) - (m[1] * m[8])) / det;
            n[8] = ((m[1] * m[6]) - (m[2] * m[5])) / det;
            n[9] = -((m[0] * m[6]) - (m[2] * m[4])) / det;
            n[10] = ((m[0] * m[5]) - (m[1] * m[4])) / det;
            return n;
        }

        #endregion
    }

    // Writes an FBX scene: Null models for the hierarchy, Mesh models with one material each.
    // Units: meters (UnitScaleFactor 100), Y up, Z front, as the OBJ export; values are the car's own.
    public class FbxBuilder : IModelSceneBuilder
    {
        private readonly FbxNode objects = new FbxNode("Objects");
        private readonly FbxNode connections = new FbxNode("Connections");
        private readonly Dictionary<string, long> materials = new Dictionary<string, long>();
        private long nextId = 1000;
        private int models;
        private int geometries;
        private readonly string creator;

        public FbxBuilder(string creator)
        {
            this.creator = creator;
        }

        private static string Named(string name, string cls)
        {
            return name + "\0\x01" + cls;
        }

        private void Connect(long child, long parent)
        {
            this.connections.Add("C", "OO", child, parent);
        }

        private static void Property(FbxNode properties, string name, string type, string label, params object[] values)
        {
            FbxNode p = properties.Add("P", name, type, label, "");
            p.Props.AddRange(values);
        }

        // A property with flags ("A" = animatable), as the FBX SDK lists the transform ones.
        private static void Animatable(FbxNode properties, string name, params object[] values)
        {
            FbxNode p = properties.Add("P", name, name, "", "A");
            p.Props.AddRange(values);
        }

        // What 3ds Max writes on every model: the identity transform spelled out and the attribute index.
        private static void ModelProperties(FbxNode properties, bool mesh)
        {
            Property(properties, "InheritType", "enum", "", 1);
            Property(properties, "DefaultAttributeIndex", "int", "Integer", mesh ? 0 : -1);
            Animatable(properties, "Lcl Translation", 0.0, 0.0, 0.0);
            Animatable(properties, "Lcl Rotation", 0.0, 0.0, 0.0);
            Animatable(properties, "Lcl Scaling", 1.0, 1.0, 1.0);
        }

        public long AddNull(string name, long parent)
        {
            long id = this.nextId++;
            FbxNode model = this.objects.Add("Model", id, Named(name, "Model"), "Null");
            model.Add("Version", 232);
            ModelProperties(model.Add("Properties70"), false);
            model.Add("Shading", true);
            model.Add("Culling", "CullingOff");
            this.models++;
            this.Connect(id, parent);
            return id;
        }

        // A mesh from a material's triangles: positions shared by exact value, normals and UVs per corner.
        public long AddMesh(string name, long parent, string materialName, ObjMesh.Material mesh)
        {
            Dictionary<Vector3, int> vertexOf = new Dictionary<Vector3, int>();
            List<double> positions = new List<double>();
            int corners = mesh.Triangles.Count * 3;
            int[] polygons = new int[corners];
            double[] normals = new double[corners * 3];
            double[] uvs = new double[corners * 2];
            int[] uvIndex = new int[corners];
            bool hasUv = true;
            bool hasNormal = true;
            int k = 0;
            foreach (ObjMesh.Corner[] triangle in mesh.Triangles)
            {
                for (int c = 0; c < 3; c++, k++)
                {
                    ObjMesh.Corner corner = triangle[c];
                    int index;
                    if (!vertexOf.TryGetValue(corner.Position, out index))
                    {
                        index = vertexOf.Count;
                        vertexOf[corner.Position] = index;
                        positions.Add(corner.Position.X);
                        positions.Add(corner.Position.Y);
                        positions.Add(corner.Position.Z);
                    }
                    polygons[k] = (c == 2) ? ~index : index;
                    normals[k * 3] = corner.Normal.X;
                    normals[(k * 3) + 1] = corner.Normal.Y;
                    normals[(k * 3) + 2] = corner.Normal.Z;
                    uvs[k * 2] = corner.Uv.X;
                    uvs[(k * 2) + 1] = corner.Uv.Y;
                    uvIndex[k] = k;
                    hasUv &= corner.HasUv;
                    hasNormal &= corner.HasNormal;
                }
            }

            long geometryId = this.nextId++;
            FbxNode geometry = this.objects.Add("Geometry", geometryId, Named(name, "Geometry"), "Mesh");
            geometry.Add("Properties70");
            geometry.Add("GeometryVersion", 124);
            geometry.Add("Vertices", positions.ToArray());
            geometry.Add("PolygonVertexIndex", polygons);
            FbxNode layer = new FbxNode("Layer");
            layer.Props.Add(0);
            layer.Add("Version", 100);
            if (hasNormal)
            {
                FbxNode element = geometry.Add("LayerElementNormal", 0);
                element.Add("Version", 101);
                element.Add("Name", "");
                element.Add("MappingInformationType", "ByPolygonVertex");
                element.Add("ReferenceInformationType", "Direct");
                element.Add("Normals", normals);
                this.LayerEntry(layer, "LayerElementNormal");
            }
            if (hasUv)
            {
                FbxNode element = geometry.Add("LayerElementUV", 0);
                element.Add("Version", 101);
                element.Add("Name", "UVMap");
                element.Add("MappingInformationType", "ByPolygonVertex");
                element.Add("ReferenceInformationType", "IndexToDirect");
                element.Add("UV", uvs);
                element.Add("UVIndex", uvIndex);
                this.LayerEntry(layer, "LayerElementUV");
            }
            FbxNode slot = geometry.Add("LayerElementMaterial", 0);
            slot.Add("Version", 101);
            slot.Add("Name", "");
            slot.Add("MappingInformationType", "AllSame");
            slot.Add("ReferenceInformationType", "IndexToDirect");
            slot.Add("Materials", new int[] { 0 });
            this.LayerEntry(layer, "LayerElementMaterial");
            geometry.Add(layer);
            this.geometries++;

            long modelId = this.nextId++;
            FbxNode model = this.objects.Add("Model", modelId, Named(name, "Model"), "Mesh");
            model.Add("Version", 232);
            ModelProperties(model.Add("Properties70"), true);
            model.Add("Shading", true);
            model.Add("Culling", "CullingOff");
            this.models++;
            this.Connect(modelId, parent);
            this.Connect(geometryId, modelId);
            this.Connect(this.Material(materialName), modelId);
            return modelId;
        }

        private void LayerEntry(FbxNode layer, string type)
        {
            FbxNode entry = layer.Add("LayerElement");
            entry.Add("Type", type);
            entry.Add("TypedIndex", 0);
        }

        private long Material(string name)
        {
            long id;
            if (this.materials.TryGetValue(name, out id))
            {
                return id;
            }
            id = this.nextId++;
            this.materials[name] = id;
            FbxNode material = this.objects.Add("Material", id, Named(name, "Material"), "");
            material.Add("Version", 102);
            material.Add("ShadingModel", "phong");
            material.Add("MultiLayer", 0);
            FbxNode properties = material.Add("Properties70");
            Vector3 color = Utilities.GetRandomColor(name.GetHashCode()).ToVector3();
            Property(properties, "DiffuseColor", "Color", "", (double) color.X, (double) color.Y, (double) color.Z);
            return id;
        }

        public void Save(string path)
        {
            File.WriteAllBytes(path, this.ToBytes());
        }

        public byte[] ToBytes()
        {
            FbxNode top = new FbxNode("");
            // The header as the FBX SDK itself writes it (Blender copies it): Max's importer is strict about it.
            // The SDK derives the footer id (and FileId) from the creation time and rejects a file where they disagree.
            // FbxFile writes the footer id of this fixed time (the one Blender uses), so the time is fixed too.
            DateTime now = new DateTime(1970, 1, 1, 10, 0, 0, 0);
            FbxNode header = top.Add("FBXHeaderExtension");
            header.Add("FBXHeaderVersion", 1003);
            header.Add("FBXVersion", 7400);
            FbxNode stamp = header.Add("CreationTimeStamp");
            stamp.Add("Version", 1000);
            stamp.Add("Year", now.Year);
            stamp.Add("Month", now.Month);
            stamp.Add("Day", now.Day);
            stamp.Add("Hour", now.Hour);
            stamp.Add("Minute", now.Minute);
            stamp.Add("Second", now.Second);
            stamp.Add("Millisecond", now.Millisecond);
            header.Add("Creator", this.creator);
            FbxNode info = header.Add("SceneInfo", "GlobalInfo\0\x01SceneInfo", "UserData");
            info.Add("Type", "UserData");
            info.Add("Version", 100);
            FbxNode meta = info.Add("MetaData");
            meta.Add("Version", 100);
            meta.Add("Title", "");
            meta.Add("Subject", "");
            meta.Add("Author", "");
            meta.Add("Keywords", "");
            meta.Add("Revision", "");
            meta.Add("Comment", "");
            FbxNode infoProperties = info.Add("Properties70");
            Property(infoProperties, "DocumentUrl", "KString", "Url", "");
            Property(infoProperties, "SrcDocumentUrl", "KString", "Url", "");
            top.Add("FileId", new byte[] { 0x28, 0xb3, 0x2a, 0xeb, 0xb6, 0x24, 0xcc, 0xc2, 0xbf, 0xc8, 0xb0, 0x2a, 0xa9, 0x2b, 0xfc, 0xf1 });
            top.Add("CreationTime", now.ToString("yyyy-MM-dd HH:mm:ss:fff", CultureInfo.InvariantCulture));
            // The importers of the FBX SDK read the version of the writing plug-in from here (Blender writes the same kind of string).
            top.Add("Creator", "FBX SDK/FBX Plugins version 2020.3.9 build=0");

            FbxNode settings = top.Add("GlobalSettings");
            settings.Add("Version", 1000);
            FbxNode properties = settings.Add("Properties70");
            Property(properties, "UpAxis", "int", "Integer", 1);
            Property(properties, "UpAxisSign", "int", "Integer", 1);
            Property(properties, "FrontAxis", "int", "Integer", 2);
            Property(properties, "FrontAxisSign", "int", "Integer", 1);
            Property(properties, "CoordAxis", "int", "Integer", 0);
            Property(properties, "CoordAxisSign", "int", "Integer", 1);
            Property(properties, "OriginalUpAxis", "int", "Integer", 1);
            Property(properties, "OriginalUpAxisSign", "int", "Integer", 1);
            Property(properties, "UnitScaleFactor", "double", "Number", 100.0);
            Property(properties, "OriginalUnitScaleFactor", "double", "Number", 100.0);
            Property(properties, "AmbientColor", "ColorRGB", "Color", 0.0, 0.0, 0.0);
            Property(properties, "DefaultCamera", "KString", "", "Producer Perspective");
            Property(properties, "TimeMode", "enum", "", 6);
            Property(properties, "TimeProtocol", "enum", "", 2);
            Property(properties, "SnapOnFrameMode", "enum", "", 0);
            Property(properties, "TimeSpanStart", "KTime", "Time", 0L);
            Property(properties, "TimeSpanStop", "KTime", "Time", OneSecond);
            Property(properties, "CustomFrameRate", "double", "Number", -1.0);
            Property(properties, "TimeMarker", "Compound", "");
            Property(properties, "CurrentTimeMarker", "int", "Integer", -1);

            FbxNode documents = top.Add("Documents");
            documents.Add("Count", 1);
            FbxNode document = documents.Add("Document", 1000000000L, "", "Scene");
            FbxNode documentProperties = document.Add("Properties70");
            Property(documentProperties, "SourceObject", "object", "");
            Property(documentProperties, "ActiveAnimStackName", "KString", "", "");
            document.Add("RootNode", 0L);
            top.Add("References");

            FbxNode definitions = top.Add("Definitions");
            definitions.Add("Version", 100);
            definitions.Add("Count", this.models + this.geometries + this.materials.Count + 3);
            AddType(definitions, "GlobalSettings", 1);
            AddType(definitions, "AnimationStack", 1);
            AddType(definitions, "AnimationLayer", 1);
            FbxNode modelType = AddType(definitions, "Model", this.models);
            FbxNode template = modelType.Add("PropertyTemplate", "FbxNode").Add("Properties70");
            Property(template, "InheritType", "enum", "", 0);
            Property(template, "DefaultAttributeIndex", "int", "Integer", -1);
            Property(template, "Show", "bool", "", 1);
            Animatable(template, "Lcl Translation", 0.0, 0.0, 0.0);
            Animatable(template, "Lcl Rotation", 0.0, 0.0, 0.0);
            Animatable(template, "Lcl Scaling", 1.0, 1.0, 1.0);
            Animatable(template, "Visibility", 1.0);
            Property(template, "Visibility Inheritance", "Visibility Inheritance", "", 1);
            FbxNode geometryType = AddType(definitions, "Geometry", this.geometries);
            FbxNode meshTemplate = geometryType.Add("PropertyTemplate", "FbxMesh").Add("Properties70");
            Property(meshTemplate, "Color", "ColorRGB", "Color", 0.8, 0.8, 0.8);
            Property(meshTemplate, "Primary Visibility", "bool", "", 1);
            Property(meshTemplate, "Casts Shadows", "bool", "", 1);
            Property(meshTemplate, "Receive Shadows", "bool", "", 1);
            AddType(definitions, "Material", this.materials.Count);

            // The default take every SDK file has.
            long stack = this.nextId++;
            long layer = this.nextId++;
            FbxNode animationStack = this.objects.Add("AnimationStack", stack, Named("Take 001", "AnimStack"), "");
            FbxNode stackProperties = animationStack.Add("Properties70");
            Property(stackProperties, "LocalStop", "KTime", "Time", OneSecond);
            Property(stackProperties, "ReferenceStop", "KTime", "Time", OneSecond);
            this.objects.Add("AnimationLayer", layer, Named("BaseLayer", "AnimLayer"), "");
            this.Connect(layer, stack);

            top.Add(this.objects);
            top.Add(this.connections);
            FbxNode takes = top.Add("Takes");
            takes.Add("Current", "");
            FbxNode take = takes.Add("Take", "Take 001");
            take.Add("FileName", "Take_001.tak");
            take.Add("LocalTime", 0L, OneSecond);
            take.Add("ReferenceTime", 0L, OneSecond);
            return FbxFile.Write(top);
        }

        private const long OneSecond = 46186158000L;

        private static FbxNode AddType(FbxNode definitions, string name, int count)
        {
            FbxNode type = definitions.Add("ObjectType", name);
            type.Add("Count", count);
            return type;
        }
    }
}
