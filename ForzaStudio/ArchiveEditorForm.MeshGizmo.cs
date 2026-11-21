namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Windows.Forms;
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;

    // The Move / Rotate / Scale handles on the MESH tab's selection (vertices, edges, faces, polygons, elements):
    // docs/UI_WORKFLOW.md step B. The handles start at the centre of the selection; while dragging, the selected points
    // and the red selection follow; on release the change is written to the open model file (CarbinModel.
    // TransformVertices), ONLY the LOD of the mesh (user 2026-11-16), one Ctrl+Z step, pending until Save. The selection
    // stays, so the next drag goes on from there.
    public partial class ArchiveEditorForm
    {
        private sealed class MeshMoveSet
        {
            public Dictionary<long, List<int>> Vertices = new Dictionary<long, List<int>>();   // FaceKey(section, sub, 0) -> buffer indices
            public Vector3 Center;
            public int Count;
        }

        private MeshMoveSet meshMove;                       // the selection's points, kept while the selection is the same
        private object meshMoveFor;                         // the faceLines list it was made for
        private Matrix meshDragMatrix = Matrix.Identity;    // the drag in progress (identity when none)

        private bool GizmoMeshMode
        {
            get { return this.tabSide != null && this.tabSide.SelectedTab == this.tabSideMesh; }
        }

        // The points the selection moves, per mesh: face levels = the corners of the selected faces; Vertex = every
        // point of the selected places; Edge = both ends of the selected edges. Null when nothing is selected.
        private MeshMoveSet MeshMoveSelection()
        {
            if (this.meshMove != null && this.faceLines != null && ReferenceEquals(this.meshMoveFor, this.faceLines))
            {
                return this.meshMove;
            }
            if ((this.faceSelection.Count == 0 && this.vertexSelection.Count == 0 && this.edgeSelection.Count == 0) || !this.EnsurePickData())
            {
                return null;
            }
            MeshMoveSet set = new MeshMoveSet();
            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            SubLevel level = SubObject.CurrentLevel;
            foreach (PickMesh mesh in this.pickMeshes)
            {
                Dictionary<int, bool> take = new Dictionary<int, bool>();
                if (SubObject.SelectsFaces(level))
                {
                    for (int f = 0; f < mesh.Triangles.Count; f++)
                    {
                        if (this.faceSelection.Contains(FaceKey(mesh.Section, mesh.Sub, f)))
                        {
                            foreach (int v in mesh.Triangles[f])
                            {
                                take[v] = true;
                            }
                        }
                    }
                }
                else
                {
                    // the selected places (weld groups) of this mesh
                    Dictionary<int, bool> groups = new Dictionary<int, bool>();
                    string prefix = mesh.Section + ":" + mesh.Sub + ":";
                    if (level == SubLevel.Vertex)
                    {
                        foreach (long key in this.vertexSelection)
                        {
                            int s, k, g;
                            FaceKeyParts(key, out s, out k, out g);
                            if (s == mesh.Section && k == mesh.Sub)
                            {
                                groups[g] = true;
                            }
                        }
                    }
                    else
                    {
                        foreach (string edge in this.edgeSelection)
                        {
                            if (edge.StartsWith(prefix, StringComparison.Ordinal))
                            {
                                string[] parts = edge.Split(':');
                                groups[int.Parse(parts[2], CultureInfo.InvariantCulture)] = true;
                                groups[int.Parse(parts[3], CultureInfo.InvariantCulture)] = true;
                            }
                        }
                    }
                    if (groups.Count == 0)
                    {
                        continue;
                    }
                    int[] group = this.GroupsOf(mesh);
                    foreach (int[] t in mesh.Triangles)
                    {
                        foreach (int v in t)
                        {
                            if (groups.ContainsKey(group[v]))
                            {
                                take[v] = true;
                            }
                        }
                    }
                }
                if (take.Count == 0)
                {
                    continue;
                }
                List<int> list = new List<int>(take.Keys);
                set.Vertices[FaceKey(mesh.Section, mesh.Sub, 0)] = list;
                foreach (int v in list)
                {
                    Vector3 p = PointOf(mesh.Positions, v);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
                set.Count += list.Count;
            }
            if (set.Count == 0)
            {
                return null;
            }
            set.Center = (min + max) / 2f;
            this.meshMove = set;
            this.meshMoveFor = this.faceLines;
            return set;
        }

        // Shows the drag on the model: the shown meshes' points at the selected places follow the matrix.
        private void MeshDragPreview(Matrix m)
        {
            this.meshDragMatrix = m;
            MeshMoveSet set = this.MeshMoveSelection();
            foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, false))
            {
                if (set == null || mesh.Vertices == null || mesh.SectionIndex < 0)
                {
                    mesh.PreviewVertices = null;
                    continue;
                }
                List<int> picked;
                PickMesh pick = this.pickMeshes.Find(delegate(PickMesh p) { return p.Section == mesh.SectionIndex && p.Sub == mesh.SubIndex; });
                if (pick == null || !set.Vertices.TryGetValue(FaceKey(mesh.SectionIndex, mesh.SubIndex, 0), out picked))
                {
                    mesh.PreviewVertices = null;
                    continue;
                }
                if (mesh.PreviewVertices == null)
                {
                    // the shown mesh has its own vertex order: matched by place (preview only, the write uses the carbin's)
                    Dictionary<string, bool> places = new Dictionary<string, bool>();
                    foreach (int v in picked)
                    {
                        places[PlaceKey(PointOf(pick.Positions, v))] = true;
                    }
                    List<int> shown = new List<int>();
                    for (int i = 0; i < mesh.Vertices.Length; i++)
                    {
                        if (places.ContainsKey(PlaceKey(mesh.Vertices[i].position)))
                        {
                            shown.Add(i);
                        }
                    }
                    mesh.PreviewVertices = shown.ToArray();
                }
                mesh.PreviewVertexMatrix = m;
            }
            this.viewRedrawWanted = true;
        }

        private static string PlaceKey(Vector3 p)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.000}|{1:0.000}|{2:0.000}", p.X, p.Y, p.Z);
        }

        private void ClearMeshDragPreview()
        {
            this.meshDragMatrix = Matrix.Identity;
            foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, false))
            {
                mesh.PreviewVertices = null;
            }
        }

        private string meshDragText;

        // Left button down on a handle in the MESH tab: the drag starts (nothing is written until the release).
        private bool MeshGizmoDown(MouseEventArgs e, int axis, Vector3 origin)
        {
            float start = 0f;
            if (axis != GizmoCentre)
            {
                bool ok = (this.gizmoTool == GizmoTool.Rotate) ? this.GizmoRingAngle(e.Location, origin, axis, out start)
                    : this.GizmoAxisParam(e.Location, origin, GizmoAxes[axis], out start);
                if (!ok)
                {
                    if (this.gizmoTool == GizmoTool.Rotate)
                    {
                        this.SetStatus("Rotate: this ring is seen from its edge; turn the view a little.");
                    }
                    return false;
                }
                if (this.gizmoTool == GizmoTool.Scale && start < this.GizmoLength(origin) * 0.05f)
                {
                    return false;
                }
            }
            this.gizmoDrag = axis;
            this.gizmoDragOrigin = origin;
            this.gizmoDragStart = start;
            this.gizmoDragTurn = 0f;
            this.gizmoDragPoint = e.Location;
            this.meshDragText = null;
            return true;
        }

        // The pointer moves with a handle: the matrix of the drag so far, shown on the model and the selection.
        private void MeshGizmoMove(MouseEventArgs e)
        {
            int axis = this.gizmoDrag;
            Vector3 c = this.gizmoDragOrigin;
            Matrix m;
            CultureInfo culture = CultureInfo.InvariantCulture;
            if (axis == GizmoCentre)
            {
                float factor = Math.Max(0.01f, 1f + (((e.X - this.gizmoDragPoint.X) - (e.Y - this.gizmoDragPoint.Y)) / 150f));
                m = Matrix.CreateTranslation(-c) * Matrix.CreateScale(factor) * Matrix.CreateTranslation(c);
                this.meshDragText = string.Format(culture, "Scaled to {0:0.0}% on all axes", factor * 100f);
            }
            else if (this.gizmoTool == GizmoTool.Rotate)
            {
                float angle;
                if (!this.GizmoRingAngle(e.Location, c, axis, out angle))
                {
                    return;
                }
                float step = angle - this.gizmoDragStart;
                step -= 360f * (float) Math.Round(step / 360f);
                this.gizmoDragStart = angle;
                this.gizmoDragTurn += step;
                m = Matrix.CreateTranslation(-c) * Matrix.CreateFromAxisAngle(GizmoAxes[axis], MathHelper.ToRadians(this.gizmoDragTurn)) * Matrix.CreateTranslation(c);
                this.meshDragText = string.Format(culture, "Turned {0:+0.0;-0.0} deg around {1}", this.gizmoDragTurn, "XYZ"[axis]);
            }
            else
            {
                float t;
                if (!this.GizmoAxisParam(e.Location, c, GizmoAxes[axis], out t))
                {
                    return;
                }
                if (this.gizmoTool == GizmoTool.Scale)
                {
                    float factor = Math.Max(0.01f, t / this.gizmoDragStart);
                    Vector3 scale = Vector3.One + (GizmoAxes[axis] * (factor - 1f));
                    m = Matrix.CreateTranslation(-c) * Matrix.CreateScale(scale) * Matrix.CreateTranslation(c);
                    this.meshDragText = string.Format(culture, "Scaled to {0:0.0}% on {1}", factor * 100f, "XYZ"[axis]);
                }
                else
                {
                    float delta = t - this.gizmoDragStart;
                    m = Matrix.CreateTranslation(GizmoAxes[axis] * delta);
                    this.meshDragText = string.Format(culture, "Moved {0:+0.000;-0.000} m on {1}", delta, "XYZ"[axis]);
                }
            }
            this.MeshDragPreview(m);
        }

        // The red selection drawn where the drag puts it.
        private static VertexPositionColor[] Moved(VertexPositionColor[] items, Matrix m)
        {
            if (items == null)
            {
                return null;
            }
            VertexPositionColor[] result = (VertexPositionColor[]) items.Clone();
            for (int i = 0; i < result.Length; i++)
            {
                result[i].Position = Vector3.Transform(result[i].Position, m);
            }
            return result;
        }

        // History label of a MESH-tab drag: its undo / redo only changes vertices, so the view is updated in place.
        private const string MeshStepPrefix = "Mesh: ";

        // After a vertex-only edit (a MESH-tab drag, its undo / redo) the meshes already in the tree take the file's new
        // vertices and indices; the tree, its ticks and expanded nodes, the camera and the textures stay as they are.
        // (The full reload showed the default view, LOD0 all ticked, for a moment before putting the user's view back.)
        // False when the file's parts or materials no longer match the tree: the caller reloads the whole model then.
        private bool RefreshMeshesInPlace()
        {
            if (this.scene == null || this.currentEntry == null || !this.currentEntry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            List<ForzaMesh> meshes = GetMeshes(this.tvModel.Nodes, false);
            ForzaCarSubSection[] found = new ForzaCarSubSection[meshes.Count];
            try
            {
                using (ForzaCarBin bin = new ForzaCarBin(this.currentEntry.Name, this.zip.Read(this.currentEntry)))
                {
                    int total = 0;
                    foreach (ForzaCarSection section in bin.Sections)
                    {
                        total += section.SubSections.Length;
                    }
                    if (total != meshes.Count)
                    {
                        return false;
                    }
                    for (int i = 0; i < meshes.Count; i++)
                    {
                        ForzaMesh mesh = meshes[i];
                        if (mesh.SectionIndex < 0 || mesh.SectionIndex >= bin.Sections.Length)
                        {
                            return false;
                        }
                        ForzaCarSection section = bin.Sections[mesh.SectionIndex];
                        if (mesh.SubIndex < 0 || mesh.SubIndex >= section.SubSections.Length)
                        {
                            return false;
                        }
                        ForzaCarSubSection sub = section.SubSections[mesh.SubIndex];
                        if (mesh.Name != section.Name + "_" + sub.Name)
                        {
                            return false;
                        }
                        found[i] = sub;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
            for (int i = 0; i < meshes.Count; i++)
            {
                meshes[i].Vertices = found[i].Vertices;
                meshes[i].Indices = found[i].Indices;
                meshes[i].PreviewVertices = null;
                meshes[i].Invalidate();
            }
            // what the full reload also dropped: the old picking data and selection, the game-LOD preview of the old file
            this.editSection = null;
            this.previewPart = null;
            this.ResetGameLod();
            this.MeshEditReset();
            this.UpdateModelInfo();
            this.viewRedrawWanted = true;
            this.scene.Update(true);
            return true;
        }

        // Release of a drag: the move is written to the open model file, only the meshes' own LOD.
        private void ApplyMeshDrag(Matrix m, string what)
        {
            MeshMoveSet set = this.MeshMoveSelection();
            this.ClearMeshDragPreview();
            if (set == null || this.currentEntry == null)
            {
                return;
            }
            // what to select again after the reload: faces by number, vertices / edges by their new place
            SubLevel level = SubObject.CurrentLevel;
            List<long> faces = new List<long>(this.faceSelection);
            List<KeyValuePair<long, Vector3>> points = new List<KeyValuePair<long, Vector3>>();
            List<KeyValuePair<long, Vector3[]>> edges = new List<KeyValuePair<long, Vector3[]>>();
            foreach (PickMesh mesh in this.pickMeshes)
            {
                int[] group = this.GroupsOf(mesh);
                Dictionary<int, int> anyOf = new Dictionary<int, int>();
                foreach (int[] t in mesh.Triangles)
                {
                    foreach (int v in t)
                    {
                        anyOf[group[v]] = v;
                    }
                }
                foreach (long key in this.vertexSelection)
                {
                    int s, k, g, v;
                    FaceKeyParts(key, out s, out k, out g);
                    if (s == mesh.Section && k == mesh.Sub && anyOf.TryGetValue(g, out v))
                    {
                        points.Add(new KeyValuePair<long, Vector3>(FaceKey(s, k, 0), Vector3.Transform(PointOf(mesh.Positions, v), m)));
                    }
                }
                string prefix = mesh.Section + ":" + mesh.Sub + ":";
                foreach (string edge in this.edgeSelection)
                {
                    int a, b;
                    string[] parts = edge.Split(':');
                    if (edge.StartsWith(prefix, StringComparison.Ordinal) && anyOf.TryGetValue(int.Parse(parts[2], CultureInfo.InvariantCulture), out a)
                        && anyOf.TryGetValue(int.Parse(parts[3], CultureInfo.InvariantCulture), out b))
                    {
                        edges.Add(new KeyValuePair<long, Vector3[]>(FaceKey(mesh.Section, mesh.Sub, 0),
                            new Vector3[] { Vector3.Transform(PointOf(mesh.Positions, a), m), Vector3.Transform(PointOf(mesh.Positions, b), m) }));
                    }
                }
            }
            int moved = 0;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                byte[] data = this.zip.Read(this.currentEntry);
                Fm2CarbinModel fm2 = Fm2Model(data);
                CarbinModel model = (fm2 == null) ? EditableModel(data) : null;     // FM3 in its own layout
                foreach (KeyValuePair<long, List<int>> pair in set.Vertices)
                {
                    int s, k, f;
                    FaceKeyParts(pair.Key, out s, out k, out f);
                    if (fm2 != null)
                    {
                        // FM2 (2026-11-21): its own model
                        int p, n;
                        this.LocateFm2PickMesh(fm2, s, k, out p, out n);
                        moved += fm2.TransformVertices(p, n, pair.Value, m);
                    }
                    else
                    {
                        moved += model.TransformVertices(s, k, pair.Value, m);
                    }
                }
                if (moved == 0)
                {
                    return;
                }
                this.zip.Replace(this.currentEntry, (fm2 != null) ? fm2.ToBytes() : model.ToBytes());
                this.zip.CloseStep(MeshStepPrefix + what);
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
            if (!this.RefreshMeshesInPlace())
            {
                this.ReloadModelKeepingView();
            }
            this.RestoreMeshSelection(level, faces, points, edges);
            this.SetStatus(string.Format(CultureInfo.InvariantCulture, "{0}: {1:N0} points, this LOD only (kept until Save; Ctrl+Z: undo).", what, moved));
        }

        // After the reload the selection is put back: faces keep their numbers, points and edges are found at their
        // new place.
        private void RestoreMeshSelection(SubLevel level, List<long> faces, List<KeyValuePair<long, Vector3>> points, List<KeyValuePair<long, Vector3[]>> edges)
        {
            if (!this.EnsurePickData())
            {
                return;
            }
            this.placeIndexFor = null;
            foreach (long key in faces)
            {
                this.faceSelection.Add(key);
            }
            foreach (KeyValuePair<long, Vector3> point in points)
            {
                int g;
                PickMesh mesh = this.PickMeshOf(point.Key);
                if (mesh != null && this.GroupAt(mesh, point.Value, out g))
                {
                    this.vertexSelection.Add(FaceKey(mesh.Section, mesh.Sub, g));
                }
            }
            foreach (KeyValuePair<long, Vector3[]> edge in edges)
            {
                int a, b;
                PickMesh mesh = this.PickMeshOf(edge.Key);
                if (mesh != null && this.GroupAt(mesh, edge.Value[0], out a) && this.GroupAt(mesh, edge.Value[1], out b))
                {
                    this.edgeSelection.Add(EdgeName(mesh.Section, mesh.Sub, a, b));
                }
            }
            this.FaceSelectionChanged();
        }

        private PickMesh PickMeshOf(long meshKey)
        {
            int s, k, f;
            FaceKeyParts(meshKey, out s, out k, out f);
            return this.pickMeshes.Find(delegate(PickMesh p) { return p.Section == s && p.Sub == k; });
        }

        // The weld group of the mesh's point at a place: by its place rounded to the millimetre, else the nearest within
        // 2 mm (a place on a rounding limit).
        private Dictionary<string, int> placeIndex;
        private PickMesh placeIndexFor;

        private bool GroupAt(PickMesh mesh, Vector3 place, out int group)
        {
            int[] groups = this.GroupsOf(mesh);
            if (this.placeIndexFor != mesh)
            {
                this.placeIndex = new Dictionary<string, int>();
                foreach (int[] t in mesh.Triangles)
                {
                    foreach (int v in t)
                    {
                        this.placeIndex[PlaceKey(PointOf(mesh.Positions, v))] = groups[v];
                    }
                }
                this.placeIndexFor = mesh;
            }
            if (this.placeIndex.TryGetValue(PlaceKey(place), out group))
            {
                return true;
            }
            group = -1;
            float best = 0.002f * 0.002f;
            foreach (int[] t in mesh.Triangles)
            {
                foreach (int v in t)
                {
                    float d = Vector3.DistanceSquared(PointOf(mesh.Positions, v), place);
                    if (d <= best)
                    {
                        best = d;
                        group = groups[v];
                    }
                }
            }
            return group >= 0;
        }
    }
}
