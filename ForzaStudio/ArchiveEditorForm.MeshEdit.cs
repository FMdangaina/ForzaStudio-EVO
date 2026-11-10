namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.Windows.Forms;

    // The MESH tab: select faces in the viewer and delete them or detach them into a part of their own.
    // Click picks a face, Ctrl (or Shift) adds, Alt removes (3ds Max); a drag draws a region that selects the faces
    // whose centre falls inside it (same modifiers); middle drag turns the view, right drag moves it.
    // Faces are the triangles of the meshes (material + LOD) checked in the tree; an edit touches only
    // the LOD of the mesh the face is in, in this model file, and stays pending until Save.
    public partial class ArchiveEditorForm
    {
        private TabPage tabSideMesh;
        private Label lblFaceSelection;
        private CheckBox chkFrontFaces;

        // One shown mesh: where it is in the carbin, its triangles and the positions they index.
        private sealed class PickMesh
        {
            public int Section, Sub;
            public string Label;
            public List<int[]> Triangles;
            public double[] Positions;
            public int[] Group;               // vertex -> id of the vertices at the same place (UV seams split vertices)
            public int[] ElementId, PolygonId;  // triangle -> its element / flat polygon (SubObject), made when first needed
        }

        private CarbinModel faceModel;
        private List<PickMesh> pickMeshes = new List<PickMesh>();
        private bool pickDirty = true;
        private readonly Set<long> faceSelection = new Set<long>();
        private List<VertexPositionColor> faceLines;     // the selected faces' edges, rebuilt when the selection changes
        private List<VertexPositionColor> faceFill;      // the selected faces as red translucent triangles, rebuilt with faceLines
        private readonly SelectionRegion region = new SelectionRegion();
        private bool faceDragging;

        private static long FaceKey(int section, int sub, int face)
        {
            return ((long) section << 44) | ((long) sub << 28) | (long) face;
        }

        private static void FaceKeyParts(long key, out int section, out int sub, out int face)
        {
            section = (int) (key >> 44);
            sub = (int) ((key >> 28) & 0xFFFF);
            face = (int) (key & 0xFFFFFFF);
        }

        private void AddMeshEditControls(FlowLayoutPanel side)
        {
            this.lblFaceSelection = SideNote("Select faces in the view.");
            this.lblFaceSelection.ForeColor = this.ForeColor;
            this.chkFrontFaces = new CheckBox();
            this.chkFrontFaces.Text = "Region: only faces turned to the camera";
            this.chkFrontFaces.Checked = true;
            this.chkFrontFaces.Size = new Size(SideWidth, 22);

            side.Controls.Add(SideHeading("Faces", true));
            side.Controls.Add(this.lblFaceSelection);
            side.Controls.Add(this.chkFrontFaces);
            side.Controls.Add(SideNote("The tools (select all / none / linked, delete, detach, UV mapper) are in the tool bar above the view, with their keys in the hints. Click: pick a face. Ctrl (or Shift): add. Alt: remove (as in 3ds Max). Drag: the region chosen in the tool bar (rectangle, circle, fence, lasso or brush; same keys; Esc cancels, a fence closes with a double click or Enter). Middle drag: move the view, Alt + middle drag: turn it, wheel: zoom. Delete and Detach touch only the LOD of the mesh the faces are in; pending until File > Save."));
            SubObject.LevelChanged += this.SubLevelChanged;
            this.Disposed += delegate { SubObject.LevelChanged -= this.SubLevelChanged; };
            this.UpdateFaceSelectionLabel();
        }

        // The MESH tab turns the wireframe on (an editing view, as in 3ds Max); leaving it puts the Wireframe button back as it was.
        private bool wireframeBeforeMesh, meshWireframeOn;

        private void MeshWireframeFollowTab()
        {
            bool mesh = this.tabSide != null && this.tabSide.SelectedTab == this.tabSideMesh;
            if (this.btnWireframe == null || mesh == this.meshWireframeOn)
            {
                return;
            }
            this.meshWireframeOn = mesh;
            if (mesh)
            {
                this.wireframeBeforeMesh = this.btnWireframe.Checked;
                this.btnWireframe.Checked = true;
            }
            else
            {
                this.btnWireframe.Checked = this.wireframeBeforeMesh;
            }
        }

        private static Button MakeButton(string text, EventHandler click)
        {
            Button button = new Button();
            button.Text = text;
            button.Click += click;
            return button;
        }

        private bool MeshEditActive
        {
            get { return this.scene != null && this.tabSide != null && this.tabSide.SelectedTab == this.tabSideMesh && this.pnlModel.Visible; }
        }

        // The model's vertices change with every edit: the picking data and the selection start over.
        private void MeshEditReset()
        {
            this.CloseUvMapperForModelChange();
            this.meshSelection.Clear();
            this.faceModel = null;
            this.pickMeshes.Clear();
            this.pickDirty = true;
            this.faceSelection.Clear();
            this.faceLines = null;
            this.faceDragging = false;
            this.region.Cancel();
            this.vertexSelection.Clear();
            this.edgeSelection.Clear();
            if (this.lblFaceSelection != null)
            {
                this.UpdateFaceSelectionLabel();
            }
        }

        // The checked meshes change: the faces to pick from do, the selection (indices) stays.
        private void MeshEditShownChanged()
        {
            this.pickDirty = true;
            this.faceLines = null;
        }

        private bool EnsurePickData()
        {
            if (!this.pickDirty && this.faceModel != null)
            {
                return true;
            }
            this.pickMeshes.Clear();
            this.faceLines = null;
            try
            {
                if (this.faceModel == null)
                {
                    if (this.currentEntry == null || !this.currentEntry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    this.faceModel = new CarbinModel(this.zip.Read(this.currentEntry));
                }
                foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, true))
                {
                    if (mesh.Hidden || mesh.Frozen || mesh.SectionIndex < 0 || mesh.SectionIndex >= this.faceModel.Sections.Count)
                    {
                        continue;
                    }
                    CarbinModel.Section section = this.faceModel.Sections[mesh.SectionIndex];
                    if (mesh.SubIndex < 0 || mesh.SubIndex >= section.SubSections.Count)
                    {
                        continue;
                    }
                    CarbinModel.SubSection sub = section.SubSections[mesh.SubIndex];
                    PickMesh pick = new PickMesh();
                    pick.Section = mesh.SectionIndex;
                    pick.Sub = mesh.SubIndex;
                    pick.Label = section.Name + " / " + sub.Name;
                    pick.Triangles = CarbinModel.StripTriangles(sub);
                    pick.Positions = CarbinModel.Positions(section, (sub.Lod == 0) ? section.Lod0 : section.Lod);
                    this.pickMeshes.Add(pick);
                }
                this.pickDirty = false;
                return true;
            }
            catch (Exception exception)
            {
                this.faceModel = null;
                this.SetStatus("Mesh edit not available for this file: " + exception.Message);
                return false;
            }
        }

        // ---- picking ----

        private bool PixelRay(System.Drawing.Point p, out Vector3 origin, out Vector3 direction)
        {
            Camera camera = this.scene.Camera;
            Matrix inverse = Matrix.Invert(camera.View * camera.Projection);
            float w = Math.Max(1, this.pnlViewport.ClientSize.Width), h = Math.Max(1, this.pnlViewport.ClientSize.Height);
            float x = (2f * p.X / w) - 1f, y = 1f - (2f * p.Y / h);
            Vector3 near = Unproject(inverse, x, y, 0f), far = Unproject(inverse, x, y, 1f);
            origin = near;
            direction = far - near;
            if (direction.LengthSquared() < 1e-12f)
            {
                return false;
            }
            direction.Normalize();
            return true;
        }

        private static Vector3 Unproject(Matrix inverse, float x, float y, float z)
        {
            Vector4 v = Vector4.Transform(new Vector4(x, y, z, 1f), inverse);
            return new Vector3(v.X / v.W, v.Y / v.W, v.Z / v.W);
        }

        private static Vector3 PointOf(double[] p, int i)
        {
            return new Vector3((float) p[i * 3], (float) p[(i * 3) + 1], (float) p[(i * 3) + 2]);
        }

        // Moller-Trumbore, both sides. Distance along the ray, or -1.
        private static float RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Math.Abs(det) < 1e-12f)
            {
                return -1f;
            }
            float inv = 1f / det;
            Vector3 t = o - a;
            float u = Vector3.Dot(t, p) * inv;
            if (u < 0f || u > 1f)
            {
                return -1f;
            }
            Vector3 q = Vector3.Cross(t, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f)
            {
                return -1f;
            }
            float distance = Vector3.Dot(e2, q) * inv;
            return distance > 0f ? distance : -1f;
        }

        private bool PickFace(System.Drawing.Point p, out long key)
        {
            key = -1;
            Vector3 origin, direction;
            if (!this.PixelRay(p, out origin, out direction))
            {
                return false;
            }
            float best = float.MaxValue;
            foreach (PickMesh mesh in this.pickMeshes)
            {
                for (int f = 0; f < mesh.Triangles.Count; f++)
                {
                    int[] t = mesh.Triangles[f];
                    float distance = RayTriangle(origin, direction, PointOf(mesh.Positions, t[0]), PointOf(mesh.Positions, t[1]), PointOf(mesh.Positions, t[2]));
                    if (distance > 0f && distance < best)
                    {
                        best = distance;
                        key = FaceKey(mesh.Section, mesh.Sub, f);
                    }
                }
            }
            return key >= 0;
        }

        // The faces whose centre falls inside the region drawn on the screen (rectangle, circle, fence, lasso or brush).
        private List<long> FacesInRegion(bool frontOnly)
        {
            List<long> found = new List<long>();
            Camera camera = this.scene.Camera;
            Matrix viewProjection = camera.View * camera.Projection;
            float w = Math.Max(1, this.pnlViewport.ClientSize.Width), h = Math.Max(1, this.pnlViewport.ClientSize.Height);
            Vector3 eye = camera.EyePosition, forward = camera.ForwardDirection;
            foreach (PickMesh mesh in this.pickMeshes)
            {
                for (int f = 0; f < mesh.Triangles.Count; f++)
                {
                    int[] t = mesh.Triangles[f];
                    Vector3 a = PointOf(mesh.Positions, t[0]), b = PointOf(mesh.Positions, t[1]), c = PointOf(mesh.Positions, t[2]);
                    Vector3 centre = (a + b + c) / 3f;
                    Vector4 clip = Vector4.Transform(new Vector4(centre, 1f), viewProjection);
                    if (clip.W <= 1e-6f)
                    {
                        continue;
                    }
                    float sx = ((clip.X / clip.W) + 1f) * 0.5f * w, sy = (1f - (clip.Y / clip.W)) * 0.5f * h;
                    if (!this.region.Contains(new PointF(sx, sy)))
                    {
                        continue;
                    }
                    if (frontOnly)
                    {
                        Vector3 normal = Vector3.Cross(b - a, c - a);
                        Vector3 toEye = camera.Orthographic ? -forward : eye - centre;
                        if (Vector3.Dot(normal, toEye) <= 0f)
                        {
                            continue;
                        }
                    }
                    found.Add(FaceKey(mesh.Section, mesh.Sub, f));
                }
            }
            return found;
        }

        // ---- mouse (called from the viewport's handlers while the MESH tab is open) ----

        private void MeshEditMouseDown(MouseEventArgs e)
        {
            this.faceDragging = true;
            if (this.region.Press(e.Location, e.Clicks))
            {
                this.ApplyRegion(e.Location);       // a fence closed by this click
            }
            this.UpdateViewOverlay();
        }

        private void MeshEditMouseMove(MouseEventArgs e)
        {
            if (!this.region.Active)
            {
                return;
            }
            this.region.Move(e.Location);
            this.UpdateViewOverlay();
        }

        // The pointer moves with no button down: a fence being drawn follows it.
        private void MeshEditHover(MouseEventArgs e)
        {
            if (this.region.Active && this.region.Mode == RegionMode.Fence)
            {
                this.region.Move(e.Location);
                this.UpdateViewOverlay();
            }
        }

        // Escape cancels a region being drawn, Enter closes a fence.
        private bool MeshEditRegionKey(Keys keyData)
        {
            if (!this.region.Active)
            {
                return false;
            }
            if (keyData == Keys.Escape)
            {
                this.region.Cancel();
                this.faceDragging = false;
                this.UpdateViewOverlay();
                return true;
            }
            if (keyData == Keys.Enter && this.region.Mode == RegionMode.Fence)
            {
                this.ApplyRegion(System.Drawing.Point.Empty);
                return true;
            }
            return false;
        }

        private void MeshEditMouseUp(MouseEventArgs e)
        {
            if (!this.faceDragging || !this.region.Active)
            {
                return;
            }
            if (!this.region.Release(e.Location))
            {
                return;         // a fence goes on with the next click
            }
            this.ApplyRegion(e.Location);
        }

        // The region is complete: what it holds at the sub-object level in use (or, for a click, what is under the pointer)
        // joins the selection. Vertex and Edge keep their own selection; the faces they touch are what Delete, Detach and the
        // UV mapper work on.
        private void ApplyRegion(System.Drawing.Point click)
        {
            this.faceDragging = false;
            bool useRegion = !this.region.WasClick || this.region.Mode == RegionMode.Fence;
            if (!this.EnsurePickData())
            {
                this.region.Finish();
                this.UpdateViewOverlay();
                return;
            }
            SelectOp op = CurrentSelectOp();
            List<long> faces = new List<long>(), vertices = new List<long>();
            List<string> edges = new List<string>();
            this.LevelHits(useRegion, click, faces, vertices, edges);
            this.region.Finish();
            SubLevel level = SubObject.CurrentLevel;
            if (level == SubLevel.Vertex)
            {
                Merge(this.vertexSelection, vertices, op);
                this.SyncFacesFromSub();
                return;
            }
            if (level == SubLevel.Edge)
            {
                Merge(this.edgeSelection, edges, op);
                this.SyncFacesFromSub();
                return;
            }
            Merge(this.faceSelection, faces, op);
            this.FaceSelectionChanged();
        }

        // 3ds Max rules: no key replaces the selection, Ctrl (or Shift) adds, Alt removes. Nothing ever toggles, so going
        // over what is already selected again never deselects it.
        internal enum SelectOp { Replace, Add, Subtract }

        internal static SelectOp CurrentSelectOp()
        {
            Keys keys = Control.ModifierKeys;
            if ((keys & Keys.Alt) != 0)
            {
                return SelectOp.Subtract;
            }
            return ((keys & (Keys.Control | Keys.Shift)) != 0) ? SelectOp.Add : SelectOp.Replace;
        }

        private static void Merge<T>(Set<T> selection, List<T> hit, SelectOp op)
        {
            if (op == SelectOp.Replace)
            {
                selection.Clear();
            }
            foreach (T key in hit)
            {
                if (op == SelectOp.Subtract)
                {
                    selection.Remove(key);
                }
                else
                {
                    selection.Add(key);
                }
            }
        }

        // ---- sub-object levels (Vertex, Edge, Face, Polygon, Element; SubObject) ----

        private readonly Set<long> vertexSelection = new Set<long>();       // FaceKey(section, mesh, weld group of the point)
        private readonly Set<string> edgeSelection = new Set<string>();     // EdgeName(section, mesh, group, group)

        private static string EdgeName(int section, int sub, int a, int b)
        {
            return (a < b) ? section + ":" + sub + ":" + a + ":" + b : section + ":" + sub + ":" + b + ":" + a;
        }

        private int[] ElementsOf(PickMesh mesh)
        {
            if (mesh.ElementId == null)
            {
                mesh.ElementId = SubObject.Elements(mesh.Triangles, this.GroupsOf(mesh));
            }
            return mesh.ElementId;
        }

        private int[] PolygonsOf(PickMesh mesh)
        {
            if (mesh.PolygonId == null)
            {
                mesh.PolygonId = SubObject.Polygons(mesh.Triangles, this.GroupsOf(mesh), mesh.Positions);
            }
            return mesh.PolygonId;
        }

        private PickMesh MeshOf(int section, int sub)
        {
            foreach (PickMesh mesh in this.pickMeshes)
            {
                if (mesh.Section == section && mesh.Sub == sub)
                {
                    return mesh;
                }
            }
            return null;
        }

        // A model point on the screen (pixels); false behind the camera.
        private bool ToScreen(Vector3 point, out PointF screen)
        {
            Camera camera = this.scene.Camera;
            Vector4 clip = Vector4.Transform(new Vector4(point, 1f), camera.View * camera.Projection);
            screen = PointF.Empty;
            if (clip.W <= 1e-6f)
            {
                return false;
            }
            float w = Math.Max(1, this.pnlViewport.ClientSize.Width), h = Math.Max(1, this.pnlViewport.ClientSize.Height);
            screen = new PointF(((clip.X / clip.W) + 1f) * 0.5f * w, (1f - (clip.Y / clip.W)) * 0.5f * h);
            return true;
        }

        private bool FacesCamera(Vector3 a, Vector3 b, Vector3 c)
        {
            Camera camera = this.scene.Camera;
            Vector3 toEye = camera.Orthographic ? -camera.ForwardDirection : camera.EyePosition - ((a + b + c) / 3f);
            return Vector3.Dot(Vector3.Cross(b - a, c - a), toEye) > 0f;
        }

        private static float SegmentDistance(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, length = (dx * dx) + (dy * dy);
            float t = (length <= 0f) ? 0f : Math.Max(0f, Math.Min(1f, (((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / length));
            float x = a.X + (t * dx) - p.X, y = a.Y + (t * dy) - p.Y;
            return (float) Math.Sqrt((x * x) + (y * y));
        }

        // What a click or the region hits at the level in use: faces (Face; Polygon and Element grown to their whole
        // polygon / element), points (Vertex) or edges (Edge). A click on Vertex / Edge takes the point / edge of the face
        // under the pointer that is nearest to it, so hidden ones are never picked.
        private void LevelHits(bool useRegion, System.Drawing.Point click, List<long> faces, List<long> vertices, List<string> edges)
        {
            SubLevel level = SubObject.CurrentLevel;
            bool frontOnly = this.chkFrontFaces.Checked;
            if (SubObject.SelectsFaces(level))
            {
                List<long> seed = new List<long>();
                long picked;
                if (useRegion)
                {
                    seed = this.FacesInRegion(frontOnly);
                }
                else if (this.PickFace(click, out picked))
                {
                    seed.Add(picked);
                }
                if (level == SubLevel.Face)
                {
                    faces.AddRange(seed);
                    return;
                }
                Dictionary<PickMesh, Dictionary<int, bool>> groups = new Dictionary<PickMesh, Dictionary<int, bool>>();
                foreach (long key in seed)
                {
                    int s, k, f;
                    FaceKeyParts(key, out s, out k, out f);
                    PickMesh mesh = this.MeshOf(s, k);
                    if (mesh == null)
                    {
                        continue;
                    }
                    int[] ids = (level == SubLevel.Polygon) ? this.PolygonsOf(mesh) : this.ElementsOf(mesh);
                    Dictionary<int, bool> wanted;
                    if (!groups.TryGetValue(mesh, out wanted))
                    {
                        wanted = new Dictionary<int, bool>();
                        groups[mesh] = wanted;
                    }
                    wanted[ids[f]] = true;
                }
                foreach (KeyValuePair<PickMesh, Dictionary<int, bool>> pair in groups)
                {
                    int[] ids = (level == SubLevel.Polygon) ? pair.Key.PolygonId : pair.Key.ElementId;
                    for (int f = 0; f < ids.Length; f++)
                    {
                        if (pair.Value.ContainsKey(ids[f]))
                        {
                            faces.Add(FaceKey(pair.Key.Section, pair.Key.Sub, f));
                        }
                    }
                }
                return;
            }
            if (!useRegion)
            {
                long key;
                if (!this.PickFace(click, out key))
                {
                    return;
                }
                int s, k, f;
                FaceKeyParts(key, out s, out k, out f);
                PickMesh mesh = this.MeshOf(s, k);
                if (mesh == null)
                {
                    return;
                }
                int[] t = mesh.Triangles[f], group = this.GroupsOf(mesh);
                PointF[] corner = new PointF[3];
                for (int i = 0; i < 3; i++)
                {
                    this.ToScreen(PointOf(mesh.Positions, t[i]), out corner[i]);
                }
                PointF p = new PointF(click.X, click.Y);
                int best = 0;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    float d = (level == SubLevel.Vertex)
                        ? (float) Math.Sqrt(((corner[i].X - p.X) * (corner[i].X - p.X)) + ((corner[i].Y - p.Y) * (corner[i].Y - p.Y)))
                        : SegmentDistance(p, corner[i], corner[(i + 1) % 3]);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = i;
                    }
                }
                if (level == SubLevel.Vertex)
                {
                    vertices.Add(FaceKey(s, k, group[t[best]]));
                }
                else
                {
                    edges.Add(EdgeName(s, k, group[t[best]], group[t[(best + 1) % 3]]));
                }
                return;
            }
            foreach (PickMesh mesh in this.pickMeshes)
            {
                int[] group = this.GroupsOf(mesh);
                for (int f = 0; f < mesh.Triangles.Count; f++)
                {
                    int[] t = mesh.Triangles[f];
                    Vector3 a = PointOf(mesh.Positions, t[0]), b = PointOf(mesh.Positions, t[1]), c = PointOf(mesh.Positions, t[2]);
                    if (frontOnly && !this.FacesCamera(a, b, c))
                    {
                        continue;
                    }
                    bool[] inside = new bool[3];
                    Vector3[] corners = { a, b, c };
                    for (int i = 0; i < 3; i++)
                    {
                        PointF screen;
                        inside[i] = this.ToScreen(corners[i], out screen) && this.region.Contains(screen);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        if (level == SubLevel.Vertex && inside[i])
                        {
                            vertices.Add(FaceKey(mesh.Section, mesh.Sub, group[t[i]]));
                        }
                        else if (level == SubLevel.Edge && inside[i] && inside[(i + 1) % 3])
                        {
                            edges.Add(EdgeName(mesh.Section, mesh.Sub, group[t[i]], group[t[(i + 1) % 3]]));
                        }
                    }
                }
            }
        }

        // Vertex / Edge: the faces that touch the selected points / edges become the face selection (what the tools use).
        private void SyncFacesFromSub()
        {
            this.faceSelection.Clear();
            SubLevel level = SubObject.CurrentLevel;
            foreach (PickMesh mesh in this.pickMeshes)
            {
                int[] group = this.GroupsOf(mesh);
                for (int f = 0; f < mesh.Triangles.Count; f++)
                {
                    int[] t = mesh.Triangles[f];
                    for (int i = 0; i < 3; i++)
                    {
                        bool touches = (level == SubLevel.Vertex)
                            ? this.vertexSelection.Contains(FaceKey(mesh.Section, mesh.Sub, group[t[i]]))
                            : this.edgeSelection.Contains(EdgeName(mesh.Section, mesh.Sub, group[t[i]], group[t[(i + 1) % 3]]));
                        if (touches)
                        {
                            this.faceSelection.Add(FaceKey(mesh.Section, mesh.Sub, f));
                            break;
                        }
                    }
                }
            }
            this.FaceSelectionChanged();
        }

        // The points / edges of the selected faces (going to Vertex / Edge, Select All, Linked).
        private void SubFromFaces()
        {
            this.vertexSelection.Clear();
            this.edgeSelection.Clear();
            foreach (long key in this.faceSelection)
            {
                int s, k, f;
                FaceKeyParts(key, out s, out k, out f);
                PickMesh mesh = this.MeshOf(s, k);
                if (mesh == null || f >= mesh.Triangles.Count)
                {
                    continue;
                }
                int[] t = mesh.Triangles[f], group = this.GroupsOf(mesh);
                for (int i = 0; i < 3; i++)
                {
                    this.vertexSelection.Add(FaceKey(s, k, group[t[i]]));
                    this.edgeSelection.Add(EdgeName(s, k, group[t[i]], group[t[(i + 1) % 3]]));
                }
            }
        }

        // The level changes (tool bar): going to Vertex / Edge the selected faces become their points / edges, as 3ds Max
        // converts a selection; going to Face / Polygon / Element the faces stay.
        private void SubLevelChanged(object sender, EventArgs e)
        {
            if (this.lblFaceSelection == null || this.IsDisposed)
            {
                return;
            }
            if (!SubObject.SelectsFaces(SubObject.CurrentLevel) && this.EnsurePickData())
            {
                this.SubFromFaces();
                this.SyncFacesFromSub();
                return;
            }
            this.FaceSelectionChanged();
        }

        // ---- selection ----

        private void FaceSelectionChanged()
        {
            this.faceLines = null;
            this.UpdateFaceSelectionLabel();
            this.UpdateViewOverlay();
        }

        private void UpdateFaceSelectionLabel()
        {
            bool any = this.faceSelection.Count > 0;
            SubLevel level = SubObject.CurrentLevel;
            string what = (level == SubLevel.Vertex) ? string.Format("{0:N0} points selected ({1:N0} faces)", this.vertexSelection.Count, this.faceSelection.Count)
                : (level == SubLevel.Edge) ? string.Format("{0:N0} edges selected ({1:N0} faces)", this.edgeSelection.Count, this.faceSelection.Count)
                : string.Format("{0:N0} faces selected", this.faceSelection.Count);
            this.lblFaceSelection.Text = SubObject.Name(level) + ": " + (any ? what : "nothing selected. Click or drag in the view.");
            if (this.btnToolDelete != null)
            {
                this.btnToolDelete.Enabled = any;
                this.btnToolDetach.Enabled = any;
                this.btnToolLinked.Enabled = any;
                this.btnToolNone.Enabled = any;
            }
        }

        private void ClearFaceSelection()
        {
            this.faceSelection.Clear();
            this.vertexSelection.Clear();
            this.edgeSelection.Clear();
            this.FaceSelectionChanged();
        }

        private void SelectAllFaces()
        {
            if (!this.EnsurePickData())
            {
                return;
            }
            this.faceSelection.Clear();
            foreach (PickMesh mesh in this.pickMeshes)
            {
                for (int f = 0; f < mesh.Triangles.Count; f++)
                {
                    this.faceSelection.Add(FaceKey(mesh.Section, mesh.Sub, f));
                }
            }
            if (!SubObject.SelectsFaces(SubObject.CurrentLevel))
            {
                this.SubFromFaces();
            }
            this.FaceSelectionChanged();
        }

        private int[] GroupsOf(PickMesh mesh)
        {
            if (mesh.Group == null)
            {
                int count = mesh.Positions.Length / 3;
                mesh.Group = new int[count];
                Dictionary<string, int> ids = new Dictionary<string, int>();
                for (int v = 0; v < count; v++)
                {
                    string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}", Math.Round(mesh.Positions[v * 3], 4), Math.Round(mesh.Positions[(v * 3) + 1], 4), Math.Round(mesh.Positions[(v * 3) + 2], 4));
                    int id;
                    if (!ids.TryGetValue(key, out id))
                    {
                        id = ids.Count;
                        ids[key] = id;
                    }
                    mesh.Group[v] = id;
                }
            }
            return mesh.Group;
        }

        // Grows the selection over every face that shares a point with it, again and again, inside the same mesh.
        private void SelectLinkedFaces()
        {
            if (!this.EnsurePickData() || this.faceSelection.Count == 0)
            {
                return;
            }
            foreach (PickMesh mesh in this.pickMeshes)
            {
                int[] group = this.GroupsOf(mesh);
                Dictionary<int, List<int>> facesAt = new Dictionary<int, List<int>>();
                for (int f = 0; f < mesh.Triangles.Count; f++)
                {
                    foreach (int v in mesh.Triangles[f])
                    {
                        List<int> list;
                        if (!facesAt.TryGetValue(group[v], out list))
                        {
                            list = new List<int>();
                            facesAt[group[v]] = list;
                        }
                        list.Add(f);
                    }
                }
                Queue<int> queue = new Queue<int>();
                for (int f = 0; f < mesh.Triangles.Count; f++)
                {
                    if (this.faceSelection.Contains(FaceKey(mesh.Section, mesh.Sub, f)))
                    {
                        queue.Enqueue(f);
                    }
                }
                while (queue.Count > 0)
                {
                    int f = queue.Dequeue();
                    foreach (int v in mesh.Triangles[f])
                    {
                        foreach (int other in facesAt[group[v]])
                        {
                            if (this.faceSelection.Add(FaceKey(mesh.Section, mesh.Sub, other)))
                            {
                                queue.Enqueue(other);
                            }
                        }
                    }
                }
            }
            if (!SubObject.SelectsFaces(SubObject.CurrentLevel))
            {
                this.SubFromFaces();
            }
            this.FaceSelectionChanged();
        }

        // Vertex: a small cross on each selected point; Edge: the selected edges.
        private void AddSubObjectLines(List<VertexPositionColor> lines, Microsoft.Xna.Framework.Graphics.Color color)
        {
            if ((this.vertexSelection.Count == 0 && this.edgeSelection.Count == 0) || !this.EnsurePickData())
            {
                return;
            }
            Vector3 min, max;
            float size = this.ModelBounds(out min, out max) ? Math.Max(0.002f, (max - min).Length() * 0.004f) : 0.01f;
            bool vertex = SubObject.CurrentLevel == SubLevel.Vertex;
            foreach (PickMesh mesh in this.pickMeshes)
            {
                int[] group = this.GroupsOf(mesh);
                Dictionary<long, bool> done = new Dictionary<long, bool>();
                foreach (int[] t in mesh.Triangles)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int a = group[t[i]], b = group[t[(i + 1) % 3]];
                        if (vertex)
                        {
                            if (done.ContainsKey(a) || !this.vertexSelection.Contains(FaceKey(mesh.Section, mesh.Sub, a)))
                            {
                                continue;
                            }
                            done[a] = true;
                            Vector3 p = PointOf(mesh.Positions, t[i]);
                            foreach (Vector3 axis in new Vector3[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                            {
                                lines.Add(new VertexPositionColor(p - (axis * size), color));
                                lines.Add(new VertexPositionColor(p + (axis * size), color));
                            }
                        }
                        else
                        {
                            long key = SubObject.EdgeKey(a, b);
                            if (done.ContainsKey(key) || !this.edgeSelection.Contains(EdgeName(mesh.Section, mesh.Sub, a, b)))
                            {
                                continue;
                            }
                            done[key] = true;
                            lines.Add(new VertexPositionColor(PointOf(mesh.Positions, t[i]), color));
                            lines.Add(new VertexPositionColor(PointOf(mesh.Positions, t[(i + 1) % 3]), color));
                        }
                    }
                }
            }
        }

        // The selected faces filled red (Face, Polygon, Element), made with MeshEditOverlayLines.
        private VertexPositionColor[] MeshEditFaceFill()
        {
            return (this.faceFill != null && this.faceFill.Count >= 3 && SubObject.SelectsFaces(SubObject.CurrentLevel)) ? this.faceFill.ToArray() : null;
        }

        // Selected faces' edges, points or edges (red), and the region being dragged (white) as lines 0.5 m in front of the camera.
        private List<VertexPositionColor> MeshEditOverlayLines()
        {
            List<VertexPositionColor> lines = new List<VertexPositionColor>();
            Microsoft.Xna.Framework.Graphics.Color red = new Microsoft.Xna.Framework.Graphics.Color(240, 30, 30, 255);     // as 3ds Max shows a selection
            if (this.faceLines == null)
            {
                this.faceLines = new List<VertexPositionColor>();
                this.faceFill = new List<VertexPositionColor>();
                if (!SubObject.SelectsFaces(SubObject.CurrentLevel))
                {
                    this.AddSubObjectLines(this.faceLines, red);
                }
                else if (this.faceSelection.Count > 0 && this.EnsurePickData())
                {
                    Dictionary<int, PickMesh> byKey = new Dictionary<int, PickMesh>();
                    foreach (PickMesh mesh in this.pickMeshes)
                    {
                        byKey[(mesh.Section << 16) | mesh.Sub] = mesh;
                    }
                    foreach (long key in this.faceSelection)
                    {
                        int s, k, f;
                        FaceKeyParts(key, out s, out k, out f);
                        PickMesh mesh;
                        if (!byKey.TryGetValue((s << 16) | k, out mesh) || f >= mesh.Triangles.Count)
                        {
                            continue;
                        }
                        int[] t = mesh.Triangles[f];
                        Vector3 a = PointOf(mesh.Positions, t[0]), b = PointOf(mesh.Positions, t[1]), c = PointOf(mesh.Positions, t[2]);
                        Microsoft.Xna.Framework.Graphics.Color fill = new Microsoft.Xna.Framework.Graphics.Color(240, 30, 30, 110);
                        this.faceFill.Add(new VertexPositionColor(a, fill));
                        this.faceFill.Add(new VertexPositionColor(b, fill));
                        this.faceFill.Add(new VertexPositionColor(c, fill));
                        this.faceLines.Add(new VertexPositionColor(a, red));
                        this.faceLines.Add(new VertexPositionColor(b, red));
                        this.faceLines.Add(new VertexPositionColor(b, red));
                        this.faceLines.Add(new VertexPositionColor(c, red));
                        this.faceLines.Add(new VertexPositionColor(c, red));
                        this.faceLines.Add(new VertexPositionColor(a, red));
                    }
                }
            }
            lines.AddRange(this.faceLines);
            if (this.region.Active && (this.region.Mode == RegionMode.Fence || !this.region.WasClick))
            {
                Microsoft.Xna.Framework.Graphics.Color white = new Microsoft.Xna.Framework.Graphics.Color(255, 255, 255, 255);
                PointF[] outline = this.region.Outline();
                List<Vector3> world = new List<Vector3>();
                foreach (PointF corner in outline)
                {
                    Vector3 origin, direction;
                    if (!this.PixelRay(new System.Drawing.Point((int) corner.X, (int) corner.Y), out origin, out direction))
                    {
                        return lines;
                    }
                    world.Add(origin + (direction * (this.scene.Camera.Orthographic ? 1f : 0.5f)));
                }
                for (int i = 0; i + 1 < world.Count; i++)
                {
                    lines.Add(new VertexPositionColor(world[i], white));
                    lines.Add(new VertexPositionColor(world[i + 1], white));
                }
            }
            return lines;
        }

        // ---- edits ----

        // Selected faces per mesh, highest section / mesh first: removing a mesh or a part shifts the
        // numbers of the later ones only.
        private List<KeyValuePair<long, List<int>>> SelectedFacesPerMesh()
        {
            Dictionary<long, List<int>> perMesh = new Dictionary<long, List<int>>();
            foreach (long key in this.faceSelection)
            {
                int s, k, f;
                FaceKeyParts(key, out s, out k, out f);
                long mesh = FaceKey(s, k, 0);
                List<int> list;
                if (!perMesh.TryGetValue(mesh, out list))
                {
                    list = new List<int>();
                    perMesh[mesh] = list;
                }
                list.Add(f);
            }
            List<KeyValuePair<long, List<int>>> ordered = new List<KeyValuePair<long, List<int>>>(perMesh);
            ordered.Sort(delegate(KeyValuePair<long, List<int>> x, KeyValuePair<long, List<int>> y) { return y.Key.CompareTo(x.Key); });
            return ordered;
        }

        private void DeleteSelectedFaces()
        {
            if (this.faceSelection.Count == 0 || !this.EnsurePickData())
            {
                return;
            }
            string question = string.Format("Delete {0:N0} selected faces?\n\nThe vertices only they use are removed. Only the LOD of each mesh they are in changes. Pending until you save (Archive > Revert Selected File undoes it).", this.faceSelection.Count);
            if (MessageBox.Show(this, question, "Delete Faces", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            this.ApplyFaceEdit(delegate(CarbinModel model, int section, int sub, List<int> faces) { return model.DeleteFaces(section, sub, faces); });
        }

        // The faces become a mesh of their own in the same part (same material, same LOD).
        private void DetachSelectedFaces()
        {
            if (this.faceSelection.Count == 0 || !this.EnsurePickData())
            {
                return;
            }
            this.ApplyFaceEdit(delegate(CarbinModel model, int section, int sub, List<int> faces) { return model.DetachFaces(section, sub, faces); });
        }

        private delegate string FaceEdit(CarbinModel model, int section, int sub, List<int> faces);

        private void ApplyFaceEdit(FaceEdit edit)
        {
            List<string> report = new List<string>();
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                CarbinModel model = new CarbinModel(this.zip.Read(this.currentEntry));
                foreach (KeyValuePair<long, List<int>> pair in this.SelectedFacesPerMesh())
                {
                    int s, k, f;
                    FaceKeyParts(pair.Key, out s, out k, out f);
                    report.Add(edit(model, s, k, pair.Value));
                }
                this.zip.Replace(this.currentEntry, model.ToBytes());
                this.RefreshEntryNode(this.currentEntry);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Mesh Edit", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            this.ReloadModelKeepingView();
            this.SetStatus(string.Join("; ", report.ToArray()) + "   (pending: Save to write the archive)");
        }
    }
}
