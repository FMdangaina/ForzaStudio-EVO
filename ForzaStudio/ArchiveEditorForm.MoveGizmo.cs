namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Windows.Forms;
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;

    // docs/UI_WORKFLOW.md step B: Select / Move / Rotate / Scale tools with handles in the view, as in 3ds Max.
    // The four buttons are in the view's bar. With the PART tab open and a part (or one material of it) selected:
    //   Move    three arrows from the part's centre; dragging one moves the part along that axis;
    //   Rotate  three rings around the pivot; dragging one turns the part around that axis;
    //   Scale   three lines ending in a square from the pivot; dragging one scales along that axis (all three with
    //           "Same scale on all axes" ticked);
    //   Select  no handles, a click only selects.
    // X red, Y green, Z blue, the one under the pointer yellow. The handles drive the Move / Rotate / Scale boxes, so
    // a drag is the same preview as typing the numbers: kept until Save, one Ctrl+Z step per drag, the LODs follow
    // "Also in lod0 and cockpit".
    // A double-click on the car selects the whole part of the mesh under the pointer (a click selects the mesh).
    public partial class ArchiveEditorForm
    {
        private enum GizmoTool { Select, Move, Rotate, Scale }

        private const float GizmoPixels = 90f;      // handle length on the screen, whatever the zoom
        private const float GizmoGrab = 8f;         // pixels around a handle that take the click
        private const int RingSteps = 48;
        private const int GizmoCentre = 3;          // Scale: the centre handle, all three axes

        private System.Drawing.Point gizmoDragPoint;                // Scale centre: where the drag began on the screen
        private readonly decimal[] gizmoDragValues = new decimal[3]; // Scale centre: the three boxes when it began

        private GizmoTool gizmoTool = GizmoTool.Move;
        private ToolStripButton[] gizmoButtons;
        private int gizmoHover = -1;                // axis under the pointer (0 X, 1 Y, 2 Z), -1 none
        private int gizmoDrag = -1;                 // axis being dragged
        private Vector3 gizmoDragOrigin;            // the handles' centre when the drag began
        private float gizmoDragStart;               // Move / Scale: where on the axis the pointer took it (metres); Rotate: the last angle
        private float gizmoDragTurn;                // Rotate: degrees turned since the drag began
        private decimal gizmoDragValue;             // the box value when the drag began
        private string gizmoSignature;              // what the drawn handles depend on: redrawn when it changes

        private static readonly Vector3[] GizmoAxes = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };

        // Select | Move | Rotate | Scale, first in the view's bar; the choice is kept (ForzaStudio.ini Editor.Tool).
        private void AddGizmoToolButtons(ToolStrip toolbar)
        {
            string[] names = { "Select", "Move", "Rotate", "Scale" };
            string[] tips =
            {
                "Select (Q): a click on the car selects the mesh, a double-click its whole part. No handles.",
                "Move (W): drag an arrow (X red, Y green, Z blue) to move the selected part along that axis.",
                "Rotate (E): drag a ring to turn the selected part around that axis (around the pivot of the PART tab).",
                "Scale (R): drag a line's square to scale the selected part along that axis, the centre triangle for all three."
            };
            Bitmap[] icons = { ToolIcons.UvSelect(), ToolIcons.UvMove(), ToolIcons.UvRotate(), ToolIcons.UvScale() };
            this.gizmoTool = GizmoTool.Select;      // every start with Select (user 2026-11-16)
            this.gizmoButtons = new ToolStripButton[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                GizmoTool tool = (GizmoTool) i;
                ToolStripButton button = new ToolStripButton(names[i], icons[i]);
                button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
                button.ToolTipText = tips[i];
                button.Checked = tool == this.gizmoTool;
                button.Click += delegate { this.SetGizmoTool(tool); };
                this.gizmoButtons[i] = button;
                toolbar.Items.Add(button);
            }
            toolbar.Items.Add(new ToolStripSeparator());
        }

        private void SetGizmoTool(GizmoTool tool)
        {
            this.gizmoTool = tool;
            this.gizmoDrag = -1;
            this.gizmoHover = -1;
            for (int i = 0; i < this.gizmoButtons.Length; i++)
            {
                this.gizmoButtons[i].Checked = i == (int) tool;
            }
            this.gizmoSignature = null;     // drawn again at the next tick
            // the handles belong to the PART tab (parts) and the MESH tab (vertices, edges, faces)
            if (this.tabSide != null && this.tabSidePart != null && this.tabSide.SelectedTab != this.tabSidePart && !this.GizmoMeshMode
                && tool != GizmoTool.Select && this.tabSide.TabPages.Contains(this.tabSidePart))
            {
                this.tabSide.SelectedTab = this.tabSidePart;    // the handles belong to the PART tab
            }
        }

        // Q / W / E / R as in 3ds Max, with the view or the model tree focused (not on the MESH tab, not while typing).
        private bool HandleGizmoKey(Keys keyData)
        {
            GizmoTool tool;
            switch (keyData)
            {
                case Keys.Q: tool = GizmoTool.Select; break;
                case Keys.W: tool = GizmoTool.Move; break;
                case Keys.E: tool = GizmoTool.Rotate; break;
                case Keys.R: tool = GizmoTool.Scale; break;
                default: return false;
            }
            if (this.gizmoButtons == null || this.pnlModel == null || !this.pnlModel.Visible || this.scene == null
                || !(this.pnlViewport.Focused || this.tvModel.Focused) || this.FocusIsTextEntry() || (this.tvModel.LabelEdit && IsEditingLabel(this.tvModel)))
            {
                return false;
            }
            this.SetGizmoTool(tool);
            return true;
        }

        private bool GizmoActive
        {
            get
            {
                if (this.scene == null || this.gizmoTool == GizmoTool.Select || this.tabSide == null || !this.pnlModel.Visible || this.pickMode != PickMode.None)
                {
                    return false;
                }
                if (this.GizmoMeshMode)
                {
                    return !this.region.Active && this.MeshMoveSelection() != null;
                }
                return this.tabSide.SelectedTab == this.tabSidePart && this.editSection != null && this.numMove != null && this.numMove[0].Enabled;
            }
        }

        // The handles' centre. MESH tab: the centre of the selection, moved with the drag. Move: the part's centre,
        // moved with the preview. Rotate / Scale: the pivot they act around (PART tab), moved with the part.
        private Vector3 GizmoOrigin
        {
            get
            {
                if (this.GizmoMeshMode)
                {
                    MeshMoveSet set = this.MeshMoveSelection();
                    return (set == null) ? Vector3.Zero : Vector3.Transform(set.Center, this.meshDragMatrix);
                }
                if (this.gizmoTool == GizmoTool.Move)
                {
                    return Vector3.Transform(this.editCenter, this.HasPendingPartEdit ? this.PartEditMatrix : Matrix.Identity);
                }
                return this.EditPivot + new Vector3((float) this.numMove[0].Value, (float) this.numMove[1].Value, (float) this.numMove[2].Value);
            }
        }

        // The handles' length in metres that shows as GizmoPixels on the screen at the part's distance.
        private float GizmoLength(Vector3 origin)
        {
            Camera camera = this.scene.Camera;
            float height = Math.Max(1, this.pnlViewport.ClientSize.Height);
            float visible = camera.Orthographic ? camera.OrthoHeight
                : 2f * Vector3.Distance(camera.EyePosition, origin) * (float) Math.Tan(MathHelper.ToRadians(camera.FieldOfViewDegrees) / 2f);
            return Math.Max(0.02f, visible * GizmoPixels / height);
        }

        // A point of the ring around an axis: the angle grows the way a positive rotation of that axis turns.
        private static Vector3 RingPoint(Vector3 origin, int axis, float radius, float angle)
        {
            Vector3 u = GizmoAxes[(axis + 1) % 3], v = GizmoAxes[(axis + 2) % 3];
            return origin + (u * (radius * (float) Math.Cos(angle))) + (v * (radius * (float) Math.Sin(angle)));
        }

        // The overlay of the PART tab (UpdateViewOverlay): the handles of the tool in use.
        private List<VertexPositionColor> GizmoLines()
        {
            if (!this.GizmoActive)
            {
                return null;
            }
            Microsoft.Xna.Framework.Graphics.Color[] colors =
            {
                new Microsoft.Xna.Framework.Graphics.Color(235, 60, 60, 255),
                new Microsoft.Xna.Framework.Graphics.Color(80, 210, 80, 255),
                new Microsoft.Xna.Framework.Graphics.Color(80, 140, 255, 255)
            };
            Microsoft.Xna.Framework.Graphics.Color yellow = new Microsoft.Xna.Framework.Graphics.Color(255, 220, 40, 255);
            List<VertexPositionColor> lines = new List<VertexPositionColor>();
            Vector3 origin = this.GizmoOrigin;
            float length = this.GizmoLength(origin);
            int lit = (this.gizmoDrag >= 0) ? this.gizmoDrag : this.gizmoHover;
            if (this.gizmoTool == GizmoTool.Scale)
            {
                // the centre handle (all three axes at once, as in 3ds Max): a triangle between the three lines
                Microsoft.Xna.Framework.Graphics.Color centre = (lit == GizmoCentre) ? yellow : new Microsoft.Xna.Framework.Graphics.Color(230, 230, 230, 255);
                for (int axis = 0; axis < 3; axis++)
                {
                    lines.Add(new VertexPositionColor(origin + (GizmoAxes[axis] * length * 0.3f), centre));
                    lines.Add(new VertexPositionColor(origin + (GizmoAxes[(axis + 1) % 3] * length * 0.3f), centre));
                }
            }
            for (int axis = 0; axis < 3; axis++)
            {
                Microsoft.Xna.Framework.Graphics.Color color = (axis == lit) ? yellow : colors[axis];
                if (this.gizmoTool == GizmoTool.Rotate)
                {
                    for (int step = 0; step < RingSteps; step++)
                    {
                        lines.Add(new VertexPositionColor(RingPoint(origin, axis, length, MathHelper.TwoPi * step / RingSteps), color));
                        lines.Add(new VertexPositionColor(RingPoint(origin, axis, length, MathHelper.TwoPi * (step + 1) / RingSteps), color));
                    }
                    continue;
                }
                Vector3 tip = origin + (GizmoAxes[axis] * length);
                Vector3 a = GizmoAxes[(axis + 1) % 3] * length * 0.07f, b = GizmoAxes[(axis + 2) % 3] * length * 0.07f;
                lines.Add(new VertexPositionColor(origin, color));
                lines.Add(new VertexPositionColor(tip, color));
                if (this.gizmoTool == GizmoTool.Move)
                {
                    // the head: four short lines back from the tip, across the two other axes
                    Vector3 back = origin + (GizmoAxes[axis] * length * 0.8f);
                    foreach (Vector3 side in new Vector3[] { a, -a, b, -b })
                    {
                        lines.Add(new VertexPositionColor(tip, color));
                        lines.Add(new VertexPositionColor(back + side, color));
                    }
                }
                else
                {
                    // Scale: a square at the tip
                    Vector3[] corners = { tip + a + b, tip + a - b, tip - a - b, tip - a + b };
                    for (int c = 0; c < 4; c++)
                    {
                        lines.Add(new VertexPositionColor(corners[c], color));
                        lines.Add(new VertexPositionColor(corners[(c + 1) % 4], color));
                    }
                }
            }
            return lines;
        }

        // Called at every tick of the view's timer: the handles keep their size on the screen while the camera moves
        // and follow the part. True when they were drawn again (the view then redraws).
        private bool RefreshGizmo()
        {
            bool mesh = this.GizmoMeshMode;
            if (this.scene == null || this.tabSide == null || (this.tabSide.SelectedTab != this.tabSidePart && !mesh))
            {
                this.gizmoSignature = null;
                return false;
            }
            string signature = "off";
            if (this.GizmoActive)
            {
                Camera camera = this.scene.Camera;
                Vector3 origin = this.GizmoOrigin;
                signature = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1:0.0000} {2:0.0000} {3:0.0000} {4:0.0000} {5} {6} {7} {8}",
                    this.gizmoTool, origin.X, origin.Y, origin.Z, this.GizmoLength(origin), this.gizmoHover, this.gizmoDrag, camera.Orthographic, this.meshDragMatrix);
            }
            if (signature == this.gizmoSignature)
            {
                return false;
            }
            this.gizmoSignature = signature;
            if (mesh)
            {
                // the MESH tab's selection (where the drag puts it) and the handles
                List<VertexPositionColor> all = this.MeshOverlayWithGizmo();
                this.scene.Overlay = (all.Count >= 2) ? all.ToArray() : null;
                bool dragging = this.meshDragMatrix != Matrix.Identity;
                this.scene.OverlayFaces = dragging ? Moved(this.MeshEditFaceFill(), this.meshDragMatrix) : this.MeshEditFaceFill();
                this.scene.OverlayPointsOnTop = dragging ? Moved(this.MeshEditVertexDots(true), this.meshDragMatrix) : this.MeshEditVertexDots(true);
                return true;
            }
            List<VertexPositionColor> lines = this.GizmoLines();
            this.scene.Overlay = (lines != null && lines.Count >= 2) ? lines.ToArray() : null;
            return true;
        }

        // The MESH tab's overlay: its selection lines (moved with a drag in progress) and the handles.
        private List<VertexPositionColor> MeshOverlayWithGizmo()
        {
            List<VertexPositionColor> lines = this.MeshEditOverlayLines();
            if (this.meshDragMatrix != Matrix.Identity)
            {
                lines = new List<VertexPositionColor>(Moved(lines.ToArray(), this.meshDragMatrix));
            }
            List<VertexPositionColor> handles = this.GizmoLines();
            if (handles != null)
            {
                lines.AddRange(handles);
            }
            return lines;
        }

        // The handle under a point of the view, -1 none.
        private int GizmoAxisAt(System.Drawing.Point p)
        {
            if (!this.GizmoActive)
            {
                return -1;
            }
            Vector3 origin = this.GizmoOrigin;
            float length = this.GizmoLength(origin);
            PointF pointer = new PointF(p.X, p.Y);
            if (this.gizmoTool == GizmoTool.Scale)
            {
                // the centre triangle first: the pointer inside it on the screen
                PointF[] corner = new PointF[3];
                bool seen = true;
                for (int axis = 0; axis < 3; axis++)
                {
                    seen &= this.ToScreen(origin + (GizmoAxes[axis] * length * 0.3f), out corner[axis]);
                }
                PointF middle;
                if (seen && (InTriangle(pointer, corner[0], corner[1], corner[2])
                    || (this.ToScreen(origin, out middle) && Math.Abs(middle.X - pointer.X) <= GizmoGrab && Math.Abs(middle.Y - pointer.Y) <= GizmoGrab)))
                {
                    return GizmoCentre;
                }
            }
            int best = -1;
            float nearest = GizmoGrab;
            for (int axis = 0; axis < 3; axis++)
            {
                float distance = float.MaxValue;
                if (this.gizmoTool == GizmoTool.Rotate)
                {
                    PointF previous;
                    bool have = this.ToScreen(RingPoint(origin, axis, length, 0f), out previous);
                    for (int step = 1; step <= RingSteps; step++)
                    {
                        PointF next;
                        bool ok = this.ToScreen(RingPoint(origin, axis, length, MathHelper.TwoPi * step / RingSteps), out next);
                        if (have && ok)
                        {
                            distance = Math.Min(distance, SegmentDistance(pointer, previous, next));
                        }
                        previous = next;
                        have = ok;
                    }
                }
                else
                {
                    PointF start, tip;
                    if (this.ToScreen(origin, out start) && this.ToScreen(origin + (GizmoAxes[axis] * length), out tip))
                    {
                        distance = SegmentDistance(pointer, start, tip);
                    }
                }
                if (distance < nearest)
                {
                    nearest = distance;
                    best = axis;
                }
            }
            return best;
        }

        private static bool InTriangle(PointF p, PointF a, PointF b, PointF c)
        {
            float d1 = ((p.X - b.X) * (a.Y - b.Y)) - ((a.X - b.X) * (p.Y - b.Y));
            float d2 = ((p.X - c.X) * (b.Y - c.Y)) - ((b.X - c.X) * (p.Y - c.Y));
            float d3 = ((p.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (p.Y - a.Y));
            bool negative = d1 < 0 || d2 < 0 || d3 < 0, positive = d1 > 0 || d2 > 0 || d3 > 0;
            return !(negative && positive);
        }

        // Where along an axis line (from origin, in metres) the pointer is: the point of the line closest to the ray
        // through the pixel. False when the axis points at the camera (no useful answer).
        private bool GizmoAxisParam(System.Drawing.Point p, Vector3 origin, Vector3 axis, out float t)
        {
            t = 0f;
            Vector3 rayOrigin, ray;
            if (!this.PixelRay(p, out rayOrigin, out ray))
            {
                return false;
            }
            Vector3 w = origin - rayOrigin;
            float b = Vector3.Dot(axis, ray), d = Vector3.Dot(axis, w), e = Vector3.Dot(ray, w);
            float denominator = 1f - (b * b);
            if (denominator < 1e-4f)
            {
                return false;
            }
            t = ((b * e) - d) / denominator;
            return true;
        }

        // The angle (degrees) of the pointer around an axis: where the ray through the pixel meets the ring's plane.
        // False when the ring is seen edge-on.
        private bool GizmoRingAngle(System.Drawing.Point p, Vector3 origin, int axis, out float degrees)
        {
            degrees = 0f;
            Vector3 rayOrigin, ray;
            if (!this.PixelRay(p, out rayOrigin, out ray))
            {
                return false;
            }
            Vector3 normal = GizmoAxes[axis];
            float facing = Vector3.Dot(normal, ray);
            if (Math.Abs(facing) < 0.05f)
            {
                return false;
            }
            Vector3 hit = rayOrigin + (ray * (Vector3.Dot(normal, origin - rayOrigin) / facing)) - origin;
            degrees = MathHelper.ToDegrees((float) Math.Atan2(Vector3.Dot(hit, GizmoAxes[(axis + 2) % 3]), Vector3.Dot(hit, GizmoAxes[(axis + 1) % 3])));
            return true;
        }

        // Left button down in the view (PART tab). True: a handle took it, a drag starts.
        private bool GizmoMouseDown(MouseEventArgs e)
        {
            int axis = this.GizmoAxisAt(e.Location);
            if (axis < 0)
            {
                return false;
            }
            Vector3 origin = this.GizmoOrigin;
            float start;
            if (this.GizmoMeshMode)
            {
                return this.MeshGizmoDown(e, axis, origin);
            }
            if (axis == GizmoCentre)
            {
                // all axes: the pointer's way on the screen scales (right / up larger, left / down smaller)
                this.gizmoDrag = axis;
                this.gizmoDragPoint = e.Location;
                for (int c = 0; c < 3; c++)
                {
                    this.gizmoDragValues[c] = this.numScale[c].Value;
                }
                return true;
            }
            if (this.gizmoTool == GizmoTool.Rotate)
            {
                if (!this.GizmoRingAngle(e.Location, origin, axis, out start))
                {
                    this.SetStatus("Rotate: this ring is seen from its edge; turn the view a little or use the Rotate boxes.");
                    return false;
                }
                this.gizmoDragValue = this.numRotate[axis].Value;
            }
            else
            {
                if (!this.GizmoAxisParam(e.Location, origin, GizmoAxes[axis], out start))
                {
                    return false;
                }
                if (this.gizmoTool == GizmoTool.Scale && start < this.GizmoLength(origin) * 0.05f)
                {
                    return false;       // taken at the centre: no ratio to scale by
                }
                this.gizmoDragValue = (this.gizmoTool == GizmoTool.Scale) ? this.numScale[axis].Value : this.numMove[axis].Value;
            }
            this.gizmoDrag = axis;
            this.gizmoDragOrigin = origin;
            this.gizmoDragStart = start;
            this.gizmoDragTurn = 0f;
            return true;
        }

        // The pointer moves with the left button down on a handle: the box of that axis follows it.
        private void GizmoMouseMove(MouseEventArgs e)
        {
            if (this.gizmoDrag < 0)
            {
                return;
            }
            int axis = this.gizmoDrag;
            if (this.GizmoMeshMode)
            {
                this.MeshGizmoMove(e);
                return;
            }
            if (axis == GizmoCentre)
            {
                double factor = Math.Max(0.01, 1.0 + (((e.X - this.gizmoDragPoint.X) - (e.Y - this.gizmoDragPoint.Y)) / 150.0));
                bool changedAny = false;
                // the first two silently, the third as a typed value: one preview and one undo step for the three
                this.fillingPartEdit = true;
                for (int c = 0; c < 2; c++)
                {
                    decimal v = ScaleValue(this.numScale[c], this.gizmoDragValues[c], factor);
                    changedAny |= this.numScale[c].Value != v;
                    this.numScale[c].Value = v;
                }
                this.fillingPartEdit = false;
                decimal last = ScaleValue(this.numScale[2], this.gizmoDragValues[2], factor);
                if (this.numScale[2].Value != last)
                {
                    this.numScale[2].Value = last;
                }
                else if (changedAny)
                {
                    this.PartEditValueChanged(this.numScale[2], EventArgs.Empty);
                }
                return;
            }
            NumericUpDown box;
            double wanted;
            int decimals;
            if (this.gizmoTool == GizmoTool.Rotate)
            {
                float angle;
                if (!this.GizmoRingAngle(e.Location, this.gizmoDragOrigin, axis, out angle))
                {
                    return;
                }
                float step = angle - this.gizmoDragStart;
                step -= 360f * (float) Math.Round(step / 360f);     // the short way round
                this.gizmoDragStart = angle;
                this.gizmoDragTurn += step;
                box = this.numRotate[axis];
                wanted = (double) this.gizmoDragValue + this.gizmoDragTurn;
                decimals = 1;
            }
            else
            {
                float t;
                if (!this.GizmoAxisParam(e.Location, this.gizmoDragOrigin, GizmoAxes[axis], out t))
                {
                    return;
                }
                if (this.gizmoTool == GizmoTool.Scale)
                {
                    box = this.numScale[axis];
                    wanted = (double) this.gizmoDragValue * Math.Max(0.01f, t / this.gizmoDragStart);
                    decimals = 1;
                }
                else
                {
                    box = this.numMove[axis];
                    wanted = (double) this.gizmoDragValue + (t - this.gizmoDragStart);
                    decimals = 3;
                }
            }
            if (double.IsNaN(wanted) || double.IsInfinity(wanted))
            {
                return;
            }
            wanted = Math.Max((double) box.Minimum, Math.Min((double) box.Maximum, wanted));
            decimal value = Math.Round((decimal) wanted, decimals);
            if (box.Value != value)
            {
                box.Value = value;      // PartEditValueChanged: preview, one undo step for the whole drag
            }
        }

        private static decimal ScaleValue(NumericUpDown box, decimal start, double factor)
        {
            double wanted = Math.Max((double) box.Minimum, Math.Min((double) box.Maximum, (double) start * factor));
            return Math.Round((decimal) wanted, 1);
        }

        private void GizmoMouseUp()
        {
            if (this.gizmoDrag < 0)
            {
                return;
            }
            int axis = this.gizmoDrag;
            this.gizmoDrag = -1;
            if (this.GizmoMeshMode)
            {
                Matrix m = this.meshDragMatrix;
                if (m == Matrix.Identity)
                {
                    this.ClearMeshDragPreview();
                    return;
                }
                this.ApplyMeshDrag(m, this.meshDragText);
                return;
            }
            if (axis == GizmoCentre)
            {
                if (this.numScale[0].Value != this.gizmoDragValues[0] || this.numScale[1].Value != this.gizmoDragValues[1] || this.numScale[2].Value != this.gizmoDragValues[2])
                {
                    this.SetStatus(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} scaled to {1:0.0}% / {2:0.0}% / {3:0.0}% (X / Y / Z; kept until Save; Ctrl+Z: undo).",
                        this.editMaterial ?? this.editSection, this.numScale[0].Value, this.numScale[1].Value, this.numScale[2].Value));
                }
                return;
            }
            string name = this.editMaterial ?? this.editSection;
            System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;
            string text = null;
            if (this.gizmoTool == GizmoTool.Move && this.numMove[axis].Value != this.gizmoDragValue)
            {
                text = string.Format(culture, "{0} moved {1:+0.000;-0.000} m on {2}", name, this.numMove[axis].Value - this.gizmoDragValue, "XYZ"[axis]);
            }
            else if (this.gizmoTool == GizmoTool.Rotate && this.numRotate[axis].Value != this.gizmoDragValue)
            {
                text = string.Format(culture, "{0} turned {1:+0.0;-0.0} deg around {2}", name, this.numRotate[axis].Value - this.gizmoDragValue, "XYZ"[axis]);
            }
            else if (this.gizmoTool == GizmoTool.Scale && this.numScale[axis].Value != this.gizmoDragValue)
            {
                text = string.Format(culture, "{0} scaled to {1:0.0}% on {2}", name, this.numScale[axis].Value,
                    this.chkUniformScale.Checked ? "all axes" : "XYZ"[axis].ToString());
            }
            if (text != null)
            {
                this.SetStatus(text + " (kept until Save; Ctrl+Z: undo).");
            }
        }

        // The pointer moves with no button down: the handle under it lights up.
        private void GizmoHover(System.Drawing.Point p)
        {
            int axis = this.GizmoAxisAt(p);
            if (axis != this.gizmoHover)
            {
                this.gizmoHover = axis;
                this.pnlViewport.Cursor = (axis >= 0) ? Cursors.SizeAll : Cursors.Default;
            }
        }

        // A double-click on the car: the whole part of the mesh under the pointer (a click selects the mesh only).
        private void SelectPartAt(System.Drawing.Point p)
        {
            this.SelectMeshAt(p, false, false);
            TreeNode node = this.tvModel.SelectedNode;
            if (node != null && node.Tag is ForzaMesh && node.Parent != null)
            {
                this.tvModel.SelectedNode = node.Parent;
            }
        }
    }
}
