namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Windows.Forms;

    // The cockpit camera of the car whose model is open (CarCamera): the steering wheel point
    // (CarPartPositions part 9) and the eye offset (CameraOverrides) from GAME:\Media\db\gamedb.slt,
    // drawn in the viewport, editable, rebuilt from the cockpit model's steering_wheel when missing,
    // and written back to gamedb.slt (backed up first). Values are in database axes (Z forward).
    public partial class ArchiveEditorForm
    {
        private Label lblCamCar;
        private NumericUpDown[] numCamWheel;
        private NumericUpDown[] numCamOffset;
        private Label lblCamEye;
        private CheckBox chkCamShow;
        private CheckBox chkCamLive;
        private Button btnCamFromModel;
        private Button btnCamView;
        private Button btnCamSave;
        private CarCamera camState;
        private string camDbPath;
        private bool fillingCam;

        private void AddCameraControls(FlowLayoutPanel side)
        {
            this.lblCamCar = SideNote("Open a car model.");
            this.lblCamCar.ForeColor = this.ForeColor;

            side.Controls.Add(SideHeading("Cockpit camera", true));
            side.Controls.Add(this.lblCamCar);
            side.Controls.Add(SideCaption("Steering wheel position (m)"));
            side.Controls.Add(this.CameraNumberRow(out this.numCamWheel));
            side.Controls.Add(SideCaption("Eye offset from the wheel (m)"));
            side.Controls.Add(this.CameraNumberRow(out this.numCamOffset));
            this.lblCamEye = SideNote("");
            side.Controls.Add(this.lblCamEye);

            this.chkCamLive = new CheckBox();
            this.chkCamLive.Text = "Live: look through the camera while editing";
            this.chkCamLive.Checked = true;
            this.chkCamLive.Size = new Size(SideWidth, 22);
            side.Controls.Add(this.chkCamLive);

            this.chkCamShow = new CheckBox();
            this.chkCamShow.Text = "Show in the view";
            this.chkCamShow.Checked = true;
            this.chkCamShow.Size = new Size(SideWidth, 22);
            this.chkCamShow.CheckedChanged += delegate { this.UpdateCameraOverlay(); };
            side.Controls.Add(this.chkCamShow);

            this.btnCamFromModel = SideWideButton("Wheel From Cockpit Model", delegate { this.CameraFromModel(true); });
            this.btnCamView = SideWideButton("View From Camera", delegate { this.ViewFromCockpitCamera(); });
            this.btnCamSave = SideWideButton("Save to Game Database Now", delegate { this.SaveCockpitCamera(); });
            side.Controls.Add(this.btnCamFromModel);
            side.Controls.Add(this.btnCamView);
            side.Controls.Add(SideHeading("Save", false));
            side.Controls.Add(this.btnCamSave);
            side.Controls.Add(SideNote("Writes gamedb.slt at once (a backup is made first), not with File > Save."));

            side.Controls.Add(SideNote("Yellow cross: steering wheel. Cyan: the eye and what it sees (FOV 62). An offset of 0, 0, 0 is the game's default (0, 0.21, -0.51). Z points forward."));
            this.AddExteriorCameraControls(side);
            this.EnableCameraEdit(false);
            this.AddThumbnailCameraControls(side);
        }

        private FlowLayoutPanel CameraNumberRow(out NumericUpDown[] numbers)
        {
            FlowLayoutPanel row = this.NumberRow(out numbers, -10, 10, 3, 0.005m, 0);
            foreach (NumericUpDown number in numbers)
            {
                // NumberRow wires the part edit preview; the camera has its own.
                number.ValueChanged -= new EventHandler(this.PartEditValueChanged);
                number.ValueChanged += delegate { this.CameraValueChanged(); };
            }
            return row;
        }

        private void EnableCameraEdit(bool enabled)
        {
            foreach (NumericUpDown number in this.numCamWheel)
            {
                number.Enabled = enabled;
            }
            foreach (NumericUpDown number in this.numCamOffset)
            {
                number.Enabled = enabled;
            }
            this.btnCamFromModel.Enabled = enabled;
            this.btnCamView.Enabled = enabled;
            this.btnCamSave.Enabled = enabled;
            this.EnableExteriorEdit(enabled);
        }

        // The car of the open model: its file name without _cockpit / _lod0.
        private static string CarMediaName(string entryName)
        {
            string name = Path.GetFileNameWithoutExtension(entryName.Substring(entryName.LastIndexOf('/') + 1));
            foreach (string suffix in new string[] { "_cockpit", "_lod0" })
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - suffix.Length);
                }
            }
            return name;
        }

        // GAME:\Media\db\gamedb.slt, read from the open folder when it is there (pending edits show).
        private byte[] ReadGameDbFile(out string path)
        {
            path = null;
            this.camMergeEntry = this.MergeEntry();
            if (this.camMergeEntry != null)
            {
                // A DLC car keeps its rows in the pack's merge database, edited inside the package.
                path = this.camMergeEntry.FullPath ?? this.camMergeEntry.Name;
                return this.zip.Read(this.camMergeEntry);
            }
            string root = this.GameRoot;
            if (root == null)
            {
                return null;
            }
            path = Path.Combine(Path.Combine(Path.Combine(root, "Media"), "db"), "gamedb.slt");
            if (!File.Exists(path))
            {
                return null;
            }
            ForzaZip.Entry entry = this.EntryForFile(path);
            return (entry != null) ? this.zip.Read(entry) : File.ReadAllBytes(path);
        }

        // Called with every car model shown.
        private void LoadCockpitCamera()
        {
            this.LoadCockpitCameraCore();
            this.LoadThumbnailCamera();
        }

        private void LoadCockpitCameraCore()
        {
            // Edits not saved yet survive a switch to another model of the same car (SwitchCameraModel).
            CarCamera kept = (this.camPending && this.camState != null) ? this.camState : null;
            this.LoadCockpitCameraFromDb();
            if (kept != null && this.camState != null && string.Equals(kept.MediaName, this.camState.MediaName, StringComparison.OrdinalIgnoreCase))
            {
                this.camState = kept;
                this.camPending = true;
                this.FillCameraControls();
                this.UpdateCameraLabels();
                this.UpdateCameraOverlay();
            }
        }

        private void LoadCockpitCameraFromDb()
        {
            this.camState = null;
            this.camDbPath = null;
            this.camPending = false;
            this.lblCamCar.ForeColor = this.ForeColor;
            this.EnableCameraEdit(false);
            string name = (this.currentEntry != null) ? this.currentEntry.Name : "";
            string lower = name.ToLowerInvariant();
            bool car = lower.EndsWith(".carbin") && !lower.Contains("_caliper") && !lower.Contains("_rotor");
            this.SetCameraTabVisible(car);
            if (!car)
            {
                this.lblCamCar.Text = "Open a car model (.carbin).";
                this.UpdateCameraOverlay();
                return;
            }
            string media = CarMediaName(name);
            try
            {
                string path;
                byte[] data = this.ReadGameDbFile(out path);
                if (data == null)
                {
                    this.lblCamCar.Text = "Game database not found: open the car from the game folder (the one with Media\\db\\gamedb.slt).";
                    this.UpdateCameraOverlay();
                    return;
                }
                using (SqliteDatabase db = SqliteDatabase.FromBytes(data))
                {
                    if (db.Scalar("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'CarPartPositions'") == null)
                    {
                        this.lblCamCar.Text = "This game's database has no cockpit camera data.";
                        this.UpdateCameraOverlay();
                        return;
                    }
                    this.camState = CarCamera.Load(db, media);
                }
                if (this.camState == null)
                {
                    this.lblCamCar.Text = media + " is not in the game database (a DLC car keeps its data in its package).";
                    this.UpdateCameraOverlay();
                    return;
                }
                this.camDbPath = path;
                this.FillCameraControls();
                this.EnableCameraEdit(true);
                if (!this.camState.HasSteeringWheel)
                {
                    // Rebuilt at once (not saved): the game would put the cockpit view at 0,0,0.
                    this.CameraFromModel(false);
                }
                this.UpdateCameraLabels();
            }
            catch (Exception exception)
            {
                this.camState = null;
                this.lblCamCar.Text = "Cockpit camera not available: " + exception.Message;
            }
            this.UpdateCameraOverlay();
        }

        private void FillCameraControls()
        {
            this.fillingCam = true;
            SetVector(this.numCamWheel, this.camState.SteeringWheel);
            SetVector(this.numCamOffset, this.camState.CockpitOffset);
            this.fillingCam = false;
            this.FillExteriorControls();
        }

        private static void SetVector(NumericUpDown[] numbers, Vector3 value)
        {
            float[] parts = { value.X, value.Y, value.Z };
            for (int c = 0; c < 3; c++)
            {
                numbers[c].Value = Math.Max(numbers[c].Minimum, Math.Min(numbers[c].Maximum, (decimal) Math.Round(parts[c], 3)));
            }
        }

        // The boxes show 3 decimals: a component the user did not change keeps its full value (changing the
        // eye's Y must not round the steering wheel 0.650491 to 0.650, 2026-11-05).
        private static Vector3 KeepUntouched(Vector3 old, NumericUpDown[] numbers)
        {
            float[] was = { old.X, old.Y, old.Z };
            for (int c = 0; c < 3; c++)
            {
                if ((decimal) Math.Round(was[c], 3) != numbers[c].Value)
                {
                    was[c] = (float) numbers[c].Value;
                }
            }
            return new Vector3(was[0], was[1], was[2]);
        }

        private static Vector3 GetVector(NumericUpDown[] numbers)
        {
            return new Vector3((float) numbers[0].Value, (float) numbers[1].Value, (float) numbers[2].Value);
        }

        private void CameraValueChanged()
        {
            if (this.fillingCam || this.camState == null)
            {
                return;
            }
            this.camState.SteeringWheel = KeepUntouched(this.camState.SteeringWheel, this.numCamWheel);
            this.camState.CockpitOffset = KeepUntouched(this.camState.CockpitOffset, this.numCamOffset);
            this.camState.HasSteeringWheel = true;
            this.camPending = true;
            this.UpdateCameraLabels();
            this.UpdateCameraOverlay();
            if (this.LiveCameraView)
            {
                this.ViewFromCockpitCamera();
            }
        }

        // Camera editors show the view from the camera at once, with every change of a number.
        private bool LiveCameraView
        {
            get { return this.chkCamLive != null && this.chkCamLive.Checked && this.scene != null; }
        }

        private bool camPending;
        private ForzaZip.Entry camMergeEntry;     // the pack's merge database when the car is in a DLC package

        private void UpdateCameraLabels()
        {
            CarCamera c = this.camState;
            if (c == null)
            {
                return;
            }
            string state = this.camPending ? "edited, not saved" : (c.HasSteeringWheel ? "as in the database" : "");
            this.lblCamCar.Text = string.Format("{0} (id {1})\n{2}", c.MediaName, c.CarId, state);
            this.lblCamCar.ForeColor = this.camPending ? ForzaTheme.Red : this.ForeColor;
            Vector3 eye = c.Eye;
            this.lblCamEye.Text = string.Format(CultureInfo.InvariantCulture, "Eye position: {0:0.000}, {1:0.000}, {2:0.000}{3}{4}",
                eye.X, eye.Y, eye.Z, c.UsesDefaultOffset ? " (game default offset)" : "", c.HasOverrides ? "" : "\nThe car has no camera offset row yet: saving adds it.");
        }

        // The steering wheel point from the car's _cockpit.carbin (next to the open model).
        private void CameraFromModel(bool userAsked)
        {
            if (this.camState == null)
            {
                return;
            }
            string folder = this.ModelFolder;
            ForzaZip.Entry cockpit = null;
            foreach (ForzaZip.Entry entry in this.zip.Entries)
            {
                if (entry.Name.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && entry.Name.EndsWith("_cockpit.carbin", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(CarMediaName(entry.Name), this.camState.MediaName, StringComparison.OrdinalIgnoreCase))
                {
                    cockpit = entry;
                }
            }
            Vector3 point;
            string problem = null;
            if (cockpit == null)
            {
                problem = "no " + this.camState.MediaName + "_cockpit.carbin next to this model";
            }
            else
            {
                try
                {
                    byte[] data = this.zip.Read(cockpit);
                    CarbinModel model = CarbinModel.IsMotorsport3File(data) ? new CarbinModel(data, true) : new CarbinModel(data);
                    if (!CarCamera.SteeringWheelFromModel(model, out point))
                    {
                        problem = "the cockpit model has no steering_wheel part";
                    }
                    else
                    {
                        bool centred = this.camState.SetSteeringWheelFromModel(point);
                        this.camPending = true;
                        this.FillCameraControls();
                        this.SetStatus(string.Format(CultureInfo.InvariantCulture, "{0}steering wheel point from {1}: {2:0.000}, {3:0.000}, {4:0.000}{5} (Save to Game Database Now writes it)",
                            userAsked ? "" : "CarPartPositions part 9 is missing (the game puts the cockpit view at 0,0,0); ", cockpit.Name, point.X, point.Y, point.Z,
                            centred ? ", eye centred on the wheel (offset x 0)" : ""));
                    }
                }
                catch (Exception exception)
                {
                    problem = exception.Message;
                }
            }
            if (problem != null)
            {
                this.SetStatus("Steering wheel point not rebuilt: " + problem);
                if (userAsked)
                {
                    MessageBox.Show(this, "The steering wheel point could not be rebuilt: " + problem + ".\nType it by hand (database axes, Z forward).", "Cockpit Camera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            this.UpdateCameraLabels();
            this.UpdateCameraOverlay();
        }

        private void ViewFromCockpitCamera()
        {
            if (this.scene == null || this.camState == null)
            {
                return;
            }
            this.SwitchCameraModel(true);
            Vector3 eye = CarCamera.ToModel(this.camState.Eye);
            this.scene.Camera.LookFrom(eye, eye + new Vector3(0f, 0f, -1f));
            this.scene.Camera.FieldOfViewDegrees = CarCamera.CockpitFov;
            this.scene.Update(true);
        }

        private void SaveCockpitCamera()
        {
            if (this.camState == null || this.camDbPath == null)
            {
                return;
            }
            if (this.gameDb != null && this.gameDb.Changes.Count > 0)
            {
                MessageBox.Show(this, "The database has unsaved changes in the database view. Save or revert them first.", "Cockpit Camera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ForzaZip.Entry open = (this.camMergeEntry != null) ? null : this.EntryForFile(this.camDbPath);
            if (open != null && open.IsModified)
            {
                MessageBox.Show(this, "gamedb.slt has pending changes in the open folder. Save them first (File > Save).", "Cockpit Camera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Vector3 eye = this.camState.Eye;
            string question = string.Format(CultureInfo.InvariantCulture,
                "Write the cockpit camera of {0} (id {1}) into\n{2}?\n\nSteering wheel (part 9): {3:0.000}, {4:0.000}, {5:0.000}\nEye offset: {6:0.000}, {7:0.000}, {8:0.000}{9}\nEye: {10:0.000}, {11:0.000}, {12:0.000}\n\nThe file is backed up first (bak folder next to the exe).",
                this.camState.MediaName, this.camState.CarId, this.camDbPath,
                this.camState.SteeringWheel.X, this.camState.SteeringWheel.Y, this.camState.SteeringWheel.Z,
                this.camState.CockpitOffset.X, this.camState.CockpitOffset.Y, this.camState.CockpitOffset.Z, this.camState.UsesDefaultOffset ? " (the game's default)" : "",
                eye.X, eye.Y, eye.Z);
            if (MessageBox.Show(this, question, "Cockpit Camera", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }
            try
            {
                List<string> done;
                byte[] bytes;
                using (SqliteDatabase db = SqliteDatabase.FromBytes((this.camMergeEntry != null) ? this.zip.Read(this.camMergeEntry) : File.ReadAllBytes(this.camDbPath)))
                {
                    done = this.camState.Save(db);
                    bytes = db.ToBytes();
                }
                if (this.camMergeEntry != null)
                {
                    // Into the pack zip, then the usual save packs the zip and the package again.
                    this.zip.Replace(this.camMergeEntry, bytes);
                    this.camPending = false;
                    this.UpdateCameraLabels();
                    this.SaveArchive(false);
                    this.SetStatus(string.Join("; ", done.ToArray()) + "   (merge database saved in the package)");
                    return;
                }
                string backup = BackupFile(this.camDbPath, "gamedb.slt", BackupStamp());
                File.WriteAllBytes(this.camDbPath, bytes);
                if (this.gameDb != null)
                {
                    // The database view reads the file again when it is shown next.
                    this.CloseGameDb();
                }
                this.camPending = false;
                this.UpdateCameraLabels();
                this.SetStatus(string.Join("; ", done.ToArray()) + (backup != null ? "   (backup " + Path.GetFileName(backup) + ")" : "   (no backup: Backup is off in the ini)"));
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "The database was not written: " + exception.Message, "Cockpit Camera", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        private void UpdateCameraOverlay()
        {
            this.UpdateViewOverlay();
        }

        // Wheel point (yellow cross), eye (cyan cross), the line between them and the eye's view
        // (FOV 62, 16:9, 0.6 m deep) looking forward along the car. Drawn while the CAMERA tab is open.
        private List<VertexPositionColor> CameraOverlayLines()
        {
            List<VertexPositionColor> thumbnail = this.ThumbnailOverlayLines();
            if (this.camState != null && this.chkCamShow.Checked && this.camState.HasOverrides)
            {
                thumbnail = thumbnail ?? new List<VertexPositionColor>();
                this.AddHoodOverlay(thumbnail);
            }
            if (this.camState == null || !this.chkCamShow.Checked || !this.camState.HasSteeringWheel)
            {
                return thumbnail;
            }
            Microsoft.Xna.Framework.Graphics.Color yellow = new Microsoft.Xna.Framework.Graphics.Color(255, 220, 40, 255);
            Microsoft.Xna.Framework.Graphics.Color cyan = new Microsoft.Xna.Framework.Graphics.Color(60, 220, 255, 255);
            Microsoft.Xna.Framework.Graphics.Color grey = new Microsoft.Xna.Framework.Graphics.Color(200, 200, 200, 255);
            List<VertexPositionColor> lines = thumbnail ?? new List<VertexPositionColor>();
            Vector3 wheel = CarCamera.ToModel(this.camState.SteeringWheel);
            Vector3 eye = CarCamera.ToModel(this.camState.Eye);
            AddCross(lines, wheel, 0.06f, yellow);
            AddCross(lines, eye, 0.06f, cyan);
            lines.Add(new VertexPositionColor(wheel, grey));
            lines.Add(new VertexPositionColor(eye, grey));
            float depth = 0.6f;
            float halfHeight = depth * (float) Math.Tan(MathHelper.ToRadians(CarCamera.CockpitFov) / 2f);
            float halfWidth = halfHeight * 16f / 9f;
            Vector3 forward = new Vector3(0f, 0f, -depth);
            Vector3[] corners =
            {
                eye + forward + new Vector3(-halfWidth, halfHeight, 0f), eye + forward + new Vector3(halfWidth, halfHeight, 0f),
                eye + forward + new Vector3(halfWidth, -halfHeight, 0f), eye + forward + new Vector3(-halfWidth, -halfHeight, 0f)
            };
            for (int i = 0; i < 4; i++)
            {
                lines.Add(new VertexPositionColor(eye, cyan));
                lines.Add(new VertexPositionColor(corners[i], cyan));
                lines.Add(new VertexPositionColor(corners[i], cyan));
                lines.Add(new VertexPositionColor(corners[(i + 1) % 4], cyan));
            }
            return lines;
        }

        // Toolbar menu: the viewer jumps to a game camera. The cockpit from the panel above; the garage,
        // photo mode, livery and upgrade cameras from GAME:\Media\UICarTargetCameraPresets.xml
        // (position, facing and FOV per preset, car space with Z forward, read as relative to the model
        // origin; a preset for this car's ordinal wins over the default one, ordinal -1). The race
        // chase, hood and bumper cameras are built by the game from the car's box and the
        // CameraOverrides offsets in a way not decoded yet, so they are not offered.
        private ToolStripDropDownButton BuildGoToCameraButton()
        {
            ToolStripDropDownButton button = new ToolStripDropDownButton("Go to Camera");
            button.DropDownOpening += delegate { this.FillGoToCamera(button); };
            button.DropDownItems.Add("(none)");
            return button;
        }

        private void FillGoToCamera(ToolStripDropDownButton button)
        {
            button.DropDownItems.Clear();
            ToolStripMenuItem cockpit = new ToolStripMenuItem("Cockpit (FOV 62)", null, delegate { this.ViewFromCockpitCamera(); });
            cockpit.Enabled = this.camState != null && this.camState.HasSteeringWheel && this.scene != null;
            if (!cockpit.Enabled)
            {
                cockpit.Text += " - open a car model from the game folder";
            }
            button.DropDownItems.Add(cockpit);
            button.DropDownItems.Add(new ToolStripSeparator());
            List<CameraPreset> presets = this.LoadCameraPresets();
            if (presets.Count == 0)
            {
                ToolStripMenuItem none = new ToolStripMenuItem("Garage cameras: GAME:\\Media\\UICarTargetCameraPresets.xml not found");
                none.Enabled = false;
                button.DropDownItems.Add(none);
            }
            foreach (CameraPreset preset in presets)
            {
                CameraPreset picked = preset;
                string text = string.Format(CultureInfo.InvariantCulture, "{0} (FOV {1:0.#}){2}", preset.Name.Replace('_', ' '), preset.Fov, preset.Ordinal >= 0 ? " - this car's own" : "");
                button.DropDownItems.Add(new ToolStripMenuItem(text, null, delegate { this.GoToPreset(picked); }));
            }
        }

        private sealed class CameraPreset
        {
            public string Name;
            public long Ordinal;
            public Vector3 Position;   // car space, Z forward
            public Vector3 Facing;
            public float Fov;
        }

        private List<CameraPreset> LoadCameraPresets()
        {
            List<CameraPreset> presets = new List<CameraPreset>();
            string root = this.GameRoot;
            string path = (root == null) ? null : Path.Combine(Path.Combine(root, "Media"), "UICarTargetCameraPresets.xml");
            if (path == null || !File.Exists(path))
            {
                return presets;
            }
            long car = (this.camState != null) ? this.camState.CarId : -1;
            Dictionary<string, CameraPreset> byName = new Dictionary<string, CameraPreset>(StringComparer.OrdinalIgnoreCase);
            List<string> order = new List<string>();
            try
            {
                System.Xml.XmlDocument document = new System.Xml.XmlDocument();
                document.Load(path);
                foreach (System.Xml.XmlElement element in document.GetElementsByTagName("Preset"))
                {
                    CameraPreset preset = new CameraPreset();
                    preset.Name = element.GetAttribute("name");
                    preset.Ordinal = (long) Attribute(element, "ordinal");
                    if (preset.Ordinal >= 0 && preset.Ordinal != car)
                    {
                        continue;
                    }
                    preset.Position = new Vector3(Attribute(element, "posx"), Attribute(element, "posy"), Attribute(element, "posz"));
                    preset.Facing = new Vector3(Attribute(element, "facex"), Attribute(element, "facey"), Attribute(element, "facez"));
                    preset.Fov = Attribute(element, "fov");
                    CameraPreset existing;
                    if (!byName.TryGetValue(preset.Name, out existing))
                    {
                        order.Add(preset.Name);
                        byName[preset.Name] = preset;
                    }
                    else if (preset.Ordinal >= 0)
                    {
                        byName[preset.Name] = preset;
                    }
                }
            }
            catch (Exception exception)
            {
                this.SetStatus("UICarTargetCameraPresets.xml not read: " + exception.Message);
            }
            foreach (string name in order)
            {
                presets.Add(byName[name]);
            }
            return presets;
        }

        private static float Attribute(System.Xml.XmlElement element, string name)
        {
            float value;
            return float.TryParse(element.GetAttribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0f;
        }

        private void GoToPreset(CameraPreset preset)
        {
            if (this.scene == null || preset.Facing.LengthSquared() < 1e-6f)
            {
                return;
            }
            Vector3 position = CarCamera.ToModel(preset.Position);
            Vector3 facing = Vector3.Normalize(CarCamera.ToModel(preset.Facing));
            this.scene.Camera.LookFrom(position, position + facing);
            this.scene.Camera.FieldOfViewDegrees = preset.Fov;
            this.scene.Update(true);
            this.SetStatus(string.Format(CultureInfo.InvariantCulture, "Camera {0}: position {1:0.000}, {2:0.000}, {3:0.000}, FOV {4:0.#} (UICarTargetCameraPresets.xml{5})",
                preset.Name, preset.Position.X, preset.Position.Y, preset.Position.Z, preset.Fov, preset.Ordinal >= 0 ? ", this car's own preset" : ""));
        }

        private static void AddCross(List<VertexPositionColor> lines, Vector3 at, float size, Microsoft.Xna.Framework.Graphics.Color color)
        {
            foreach (Vector3 axis in new Vector3[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            {
                lines.Add(new VertexPositionColor(at - (axis * size), color));
                lines.Add(new VertexPositionColor(at + (axis * size), color));
            }
        }
    }
}
