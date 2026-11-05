namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Windows.Forms;

    // The EXTERIOR CAMERAS section of the CAMERA tab: the hood, bumper and chase offsets of the car's
    // CameraOverrides row (CarCamera.Exterior), saved with the cockpit camera by Save to Game Database Now.
    // Edited as numbers only: the preview is off (ExteriorPreviewDecoded) because the game's base point for
    // these offsets is unknown. SwitchCameraModel opens the cockpit model for the cockpit view.
    public partial class ArchiveEditorForm
    {
        private ComboBox cmbExterior;
        private Label[] lblExterior;
        private NumericUpDown[] numExterior;
        private Button btnExteriorView;
        private bool fillingExterior;

        // Per choice: the first CarCamera.ExteriorColumns index, then the labels of its fields.
        private static readonly string[] ExteriorNames = { "Hood", "Bumper high", "Bumper low", "Chase low", "Chase high" };
        private static readonly int[] ExteriorFirst = { 0, 3, 5, 7, 10 };
        private static readonly string[][] ExteriorLabels =
        {
            new string[] { "Height (m)", "Z forward (m)", "Pitch" },
            new string[] { "Height (m)", "Z (m)" },
            new string[] { "Height (m)", "Z (m)" },
            new string[] { "Height (m)", "Radius (m)", "Target Y (m)" },
            new string[] { "Height (m)", "Radius (m)", "Target Y (m)" }
        };

        private void AddExteriorCameraControls(FlowLayoutPanel side)
        {
            side.Controls.Add(SideHeading("Exterior cameras", false));
            side.Controls.Add(SideNote("Hood, bumper and chase offsets of the car (CameraOverrides). 0 = the game's default camera."));
            this.cmbExterior = new ComboBox();
            this.cmbExterior.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbExterior.Items.AddRange(ExteriorNames);
            this.cmbExterior.SelectedIndex = 0;
            this.cmbExterior.Width = SideWidth;
            this.cmbExterior.Margin = new Padding(0, 3, 0, 3);
            this.cmbExterior.SelectedIndexChanged += delegate
            {
                this.FillExteriorControls();
                this.UpdateCameraOverlay();
            };
            side.Controls.Add(this.cmbExterior);

            this.lblExterior = new Label[3];
            this.numExterior = new NumericUpDown[3];
            for (int i = 0; i < 3; i++)
            {
                FlowLayoutPanel row = new FlowLayoutPanel();
                row.Size = new Size(SideWidth, 28);
                row.Margin = new Padding(0);
                Label label = new Label();
                label.AutoSize = false;
                label.Size = new Size(110, 22);
                label.TextAlign = ContentAlignment.MiddleLeft;
                NumericUpDown number = new NumericUpDown();
                number.Width = 80;
                number.Minimum = -20;
                number.Maximum = 20;
                number.DecimalPlaces = 3;
                number.Increment = 0.01m;
                number.BorderStyle = BorderStyle.FixedSingle;
                number.ValueChanged += delegate { this.ExteriorValueChanged(); };
                row.Controls.Add(label);
                row.Controls.Add(number);
                side.Controls.Add(row);
                this.lblExterior[i] = label;
                this.numExterior[i] = number;
            }
            this.btnExteriorView = SideWideButton("View From This Camera", delegate { this.ViewFromExteriorCamera(); });
            this.btnExteriorView.Visible = false;
            side.Controls.Add(this.btnExteriorView);
            side.Controls.Add(SideNote("Offsets only, no preview: the game adds them to a point of its own (not the model's origin, not in the files), so a preview here would be wrong. A change does move the camera in game (hood +0.30 m checked 2026-11-05). Saved with Save to Game Database Now."));
            this.FillExteriorControls();
        }

        private void FillExteriorControls()
        {
            int choice = Math.Max(0, this.cmbExterior.SelectedIndex);
            string[] labels = ExteriorLabels[choice];
            this.fillingExterior = true;
            for (int i = 0; i < 3; i++)
            {
                bool used = i < labels.Length;
                this.lblExterior[i].Visible = used;
                this.numExterior[i].Visible = used;
                if (used)
                {
                    this.lblExterior[i].Text = labels[i];
                    double value = (this.camState != null) ? this.camState.Exterior[ExteriorFirst[choice] + i] : 0.0;
                    this.numExterior[i].Value = Math.Max(this.numExterior[i].Minimum, Math.Min(this.numExterior[i].Maximum, (decimal) Math.Round(value, 3)));
                }
            }
            this.fillingExterior = false;
            this.btnExteriorView.Enabled = (this.camState != null) && choice <= 2;
        }

        private void ExteriorValueChanged()
        {
            if (this.fillingExterior || this.camState == null)
            {
                return;
            }
            int choice = Math.Max(0, this.cmbExterior.SelectedIndex);
            for (int i = 0; i < ExteriorLabels[choice].Length; i++)
            {
                // A field the user did not touch keeps the database's full value (the box shows 3 decimals:
                // the Supra's hood Z 0.0447978 had become 0.045 when only the height was changed, 2026-11-05).
                int index = ExteriorFirst[choice] + i;
                double shown = (double) this.numExterior[i].Value;
                if (Math.Round(this.camState.Exterior[index], 3) != shown)
                {
                    this.camState.Exterior[index] = shown;
                }
            }
            this.camPending = true;
            this.UpdateCameraLabels();
            this.UpdateCameraOverlay();
        }

        private void EnableExteriorEdit(bool enabled)
        {
            this.cmbExterior.Enabled = enabled;
            foreach (NumericUpDown number in this.numExterior)
            {
                number.Enabled = enabled;
            }
            this.btnExteriorView.Enabled = enabled && this.cmbExterior.SelectedIndex <= 2;
        }

        private Vector3 HoodEye()
        {
            return new Vector3(0f, (float) this.camState.Exterior[CarCamera.HoodHeight], (float) this.camState.Exterior[CarCamera.HoodZ]);
        }

        // Bumper cameras: the game builds them from the car's box in a way not decoded (the offsets are 0
        // on all but one car). The preview ASSUMES the eye on the centre line at the front of the model's
        // shown parts, 5 cm back (CameraSettings.ini BumperHighCam CamZOffset -0.05), at this fraction of
        // the car's height above its lowest point, plus the height / Z offsets of the row. FOV 53.
        private const float BumperHighHeightFraction = 0.45f;
        private const float BumperLowHeightFraction = 0.30f;
        private const float BumperBackOffset = 0.05f;
        private const float BumperFov = 53f;
        private const bool ExteriorPreviewDecoded = false;

        // The eye of the chosen exterior camera in model axes, and its FOV. False when there is no
        // preview (chase cameras) or the bumper needs a model and none is shown.
        private bool ExteriorEye(int choice, out Vector3 eye, out float fov)
        {
            eye = Vector3.Zero;
            fov = CarCamera.HoodFov;
            // No exterior preview (user 2026-11-05): the game adds the CameraOverrides offsets to a base point
            // of its own. Measured on 12 cars, the DB hood eye taken from the model's origin is always 0.52 -
            // 0.61 m under the body's top at that Z (inside the car); the base is not in the DB, CameraSettings.ini,
            // the carbin header, maxdata.xml or carattribs.xml. The code below stays for when it is decoded.
            if (this.camState == null || !ExteriorPreviewDecoded)
            {
                return false;
            }
            if (choice == 0)
            {
                eye = CarCamera.ToModel(this.HoodEye());
                return true;
            }
            if (choice == 1 || choice == 2)
            {
                Vector3 min, max;
                if (!this.ModelBounds(out min, out max))
                {
                    return false;
                }
                float height = (float) this.camState.Exterior[ExteriorFirst[choice]];
                float z = (float) this.camState.Exterior[ExteriorFirst[choice] + 1];
                float fraction = (choice == 1) ? BumperHighHeightFraction : BumperLowHeightFraction;
                eye = new Vector3(0f, min.Y + ((max.Y - min.Y) * fraction) + height, min.Z + BumperBackOffset - z);
                fov = BumperFov;
                return true;
            }
            return false;
        }

        // The game draws the hood and bumper views with the car's exterior and the cockpit view with the
        // cockpit model. A preview on the other model looked into the dashboard (hood) or at the floor
        // (bumper: its eye comes from the front of the shown model): user 2026-11-05, NSX '97 with
        // _cockpit.carbin open. So the preview opens the right model of the same car first (<car>_lod0 /
        // <car>_cockpit next to the open one, as a click in the archive tree). True when it switched.
        private bool SwitchCameraModel(bool cockpit)
        {
            if (this.currentEntry == null || this.camState == null || this.zip == null)
            {
                return false;
            }
            bool isCockpit = this.currentEntry.Name.EndsWith("_cockpit.carbin", StringComparison.OrdinalIgnoreCase);
            if (isCockpit == cockpit)
            {
                return false;
            }
            ForzaZip.Entry entry = this.zip.Find(this.ModelFolder + this.camState.MediaName + (cockpit ? "_cockpit.carbin" : "_lod0.carbin"));
            TreeNode node = (entry == null) ? null : FindNode(this.tvArchive.Nodes, entry);
            if (node == null)
            {
                return false;
            }
            this.tvArchive.SelectedNode = node;
            return this.currentEntry == entry;
        }

        private void ViewFromExteriorCamera()
        {
            Vector3 eye;
            float fov;
            int choice = Math.Max(0, this.cmbExterior.SelectedIndex);
            if (this.scene == null)
            {
                return;
            }
            if (choice > 2)
            {
                // Chase: no preview; leave the last camera's view for the normal one framing the model.
                Vector3 min, max;
                this.scene.Camera.FieldOfViewDegrees = this.trkFov.Value;
                if (this.ModelBounds(out min, out max))
                {
                    this.scene.Camera.FocusOn((min + max) / 2f, (max - min).Length() / 2f);
                }
                this.scene.Update(true);
                return;
            }
            this.SwitchCameraModel(false);
            if (!this.ExteriorEye(choice, out eye, out fov))
            {
                return;
            }
            this.scene.Camera.LookFrom(eye, eye + new Vector3(0f, 0f, -1f));
            this.scene.Camera.FieldOfViewDegrees = fov;
            this.scene.Update(true);
        }

        // The eye of the chosen camera (green cross) and its view, 0.6 m deep, 16:9.
        private void AddHoodOverlay(List<VertexPositionColor> lines)
        {
            Microsoft.Xna.Framework.Graphics.Color green = new Microsoft.Xna.Framework.Graphics.Color(90, 230, 90, 255);
            Vector3 eye;
            float fov;
            if (!this.ExteriorEye(Math.Max(0, this.cmbExterior.SelectedIndex), out eye, out fov) && !this.ExteriorEye(0, out eye, out fov))
            {
                return;
            }
            AddCross(lines, eye, 0.06f, green);
            float depth = 0.6f;
            float halfHeight = depth * (float) Math.Tan(MathHelper.ToRadians(fov) / 2f);
            float halfWidth = halfHeight * 16f / 9f;
            Vector3 centre = eye + new Vector3(0f, 0f, -depth);
            Vector3[] corners =
            {
                centre + new Vector3(-halfWidth, halfHeight, 0f), centre + new Vector3(halfWidth, halfHeight, 0f),
                centre + new Vector3(halfWidth, -halfHeight, 0f), centre + new Vector3(-halfWidth, -halfHeight, 0f)
            };
            for (int i = 0; i < 4; i++)
            {
                lines.Add(new VertexPositionColor(eye, green));
                lines.Add(new VertexPositionColor(corners[i], green));
                lines.Add(new VertexPositionColor(corners[i], green));
                lines.Add(new VertexPositionColor(corners[(i + 1) % 4], green));
            }
        }
    }
}
