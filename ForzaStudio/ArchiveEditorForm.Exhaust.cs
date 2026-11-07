namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.Windows.Forms;

    // The exhaust locators of the main car carbin (CarbinModel.SetExhaustTips): the points the backfire flames come
    // out of. Drawn as crosses with a line pointing backwards while the EXHAUST tab is open, edited one by one
    // (position, add, delete, mirror to the other side) and written into the archive with Apply (pending until
    // File > Save). Positions are in model axes, as the carbin stores them (Z points to the rear).
    public partial class ArchiveEditorForm
    {
        private Label lblExhInfo;
        private ListBox lstExhaust;
        private NumericUpDown[] numExhPos;
        private CheckBox chkExhShow;
        private Button btnExhAdd;
        private Button btnExhDelete;
        private Button btnExhMirror;
        private Button btnExhApply;

        private ForzaZip.Entry exhEntry;
        private List<float[]> exhTips;
        private bool fillingExh;
        private bool exhDirty;

        private void AddExhaustControls(FlowLayoutPanel side)
        {
            this.lblExhInfo = SideNote("Open a car model.");
            this.lblExhInfo.ForeColor = this.ForeColor;

            this.lstExhaust = new ListBox();
            this.lstExhaust.Size = new Size(SideWidth, 90);
            this.lstExhaust.IntegralHeight = false;
            this.lstExhaust.SelectedIndexChanged += delegate
            {
                this.FillExhaustControls();
                this.UpdateViewOverlay();
            };

            FlowLayoutPanel position = this.NumberRow(out this.numExhPos, -20, 20, 3, 0.01m, 0);
            foreach (NumericUpDown number in this.numExhPos)
            {
                number.ValueChanged -= new EventHandler(this.PartEditValueChanged);
                number.ValueChanged += delegate { this.ExhaustValueChanged(); };
            }

            this.chkExhShow = new CheckBox();
            this.chkExhShow.Text = "Show points in the view";
            this.chkExhShow.Checked = true;
            this.chkExhShow.Size = new Size(SideWidth, 22);
            this.chkExhShow.CheckedChanged += delegate { this.UpdateViewOverlay(); };

            this.btnExhAdd = new Button();
            this.btnExhAdd.Text = "Add";
            this.btnExhAdd.Click += delegate { this.AddExhaustPoint(); };
            this.btnExhDelete = new Button();
            this.btnExhDelete.Text = "Delete";
            this.btnExhDelete.Click += delegate { this.DeleteExhaustPoint(); };
            this.btnExhMirror = new Button();
            this.btnExhMirror.Text = "Mirror";
            this.btnExhMirror.Click += delegate { this.MirrorExhaustPoint(); };
            this.btnExhApply = SideWideButton("Apply to Model", delegate { this.ApplyExhaust(); });

            side.Controls.Add(SideHeading("Exhaust", true));
            side.Controls.Add(this.lblExhInfo);
            side.Controls.Add(this.lstExhaust);
            side.Controls.Add(SideCaption("Pipe tip (m, model axes)"));
            side.Controls.Add(position);
            side.Controls.Add(SideButtonRow(this.btnExhAdd, this.btnExhDelete, this.btnExhMirror));
            side.Controls.Add(this.chkExhShow);
            side.Controls.Add(SideHeading("Write", false));
            side.Controls.Add(this.btnExhApply);
            side.Controls.Add(SideNote("The exhaust emitters of the car (CCarEmitterData in the model header): the backfire flames (Media\\Effects\\Backfire.xml) start at these points. Yellow: the selected one, orange: the others, the line points backwards. Z is the model axis (positive to the rear). Add copies the selected point, Mirror copies it to the other side (X negated). Apply writes the points into the car file, pending until File > Save."));
            this.EnableExhaustEdit();
        }

        private float[] SelectedExhaust
        {
            get
            {
                int index = (this.lstExhaust == null) ? -1 : this.lstExhaust.SelectedIndex;
                return (this.exhTips != null && index >= 0 && index < this.exhTips.Count) ? this.exhTips[index] : null;
            }
        }

        private void EnableExhaustEdit()
        {
            bool point = this.SelectedExhaust != null;
            foreach (NumericUpDown number in this.numExhPos)
            {
                number.Enabled = point;
            }
            this.btnExhDelete.Enabled = point;
            this.btnExhMirror.Enabled = point;
            this.btnExhAdd.Enabled = this.exhTips != null;
            this.btnExhApply.Enabled = this.exhTips != null && this.exhDirty;
        }

        // Called with every model shown: the main carbin of a car with an FM4 exhaust locator block.
        private void LoadExhaust()
        {
            this.exhEntry = null;
            this.exhTips = null;
            this.exhDirty = false;
            this.lblExhInfo.Text = "";
            this.lblExhInfo.ForeColor = this.ForeColor;
            this.lstExhaust.Items.Clear();
            string name = (this.currentEntry != null) ? this.currentEntry.Name.ToLowerInvariant() : "";
            bool main = name.EndsWith(".carbin") && !name.Contains("_caliper") && !name.Contains("_rotor") && !name.EndsWith("_lod0.carbin") && !name.EndsWith("_cockpit.carbin");
            if (main)
            {
                try
                {
                    CarbinModel model = new CarbinModel(this.zip.Read(this.currentEntry));
                    if (model.HasExhaustLocators)
                    {
                        this.exhEntry = this.currentEntry;
                        this.exhTips = new List<float[]>();
                        for (int i = 0; i < model.ExhaustCount; i++)
                        {
                            this.exhTips.Add(model.GetExhaustTip(i));
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
            this.SetExhaustTabVisible(this.exhTips != null);
            this.FillExhaustList(0);
        }

        private void FillExhaustList(int select)
        {
            this.fillingExh = true;
            this.lstExhaust.Items.Clear();
            if (this.exhTips != null)
            {
                for (int i = 0; i < this.exhTips.Count; i++)
                {
                    this.lstExhaust.Items.Add(ExhaustText(i, this.exhTips[i]));
                }
                if (this.lstExhaust.Items.Count > 0)
                {
                    this.lstExhaust.SelectedIndex = Math.Max(0, Math.Min(select, this.lstExhaust.Items.Count - 1));
                }
                this.lblExhInfo.Text = this.exhTips.Count + " exhaust emitter" + (this.exhTips.Count == 1 ? "" : "s") + (this.exhDirty ? "\nedited, not applied" : "");
            }
            this.fillingExh = false;
            this.FillExhaustControls();
            this.UpdateViewOverlay();
        }

        private static string ExhaustText(int index, float[] tip)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.###}, {2:0.###}, {3:0.###}", index + 1, tip[0], tip[1], tip[2]);
        }

        private void FillExhaustControls()
        {
            float[] tip = this.SelectedExhaust;
            this.fillingExh = true;
            for (int i = 0; i < 3; i++)
            {
                this.numExhPos[i].Value = (tip == null) ? 0 : Math.Max(this.numExhPos[i].Minimum, Math.Min(this.numExhPos[i].Maximum, (decimal) Math.Round(tip[i], 3)));
            }
            this.fillingExh = false;
            this.EnableExhaustEdit();
        }

        private void ExhaustValueChanged()
        {
            float[] tip = this.SelectedExhaust;
            if (this.fillingExh || tip == null)
            {
                return;
            }
            for (int i = 0; i < 3; i++)
            {
                tip[i] = (float) this.numExhPos[i].Value;
            }
            int index = this.lstExhaust.SelectedIndex;
            this.fillingExh = true;
            this.lstExhaust.Items[index] = ExhaustText(index, tip);
            this.fillingExh = false;
            this.MarkExhaustEdited();
            this.UpdateViewOverlay();
        }

        private void MarkExhaustEdited()
        {
            this.exhDirty = true;
            this.lblExhInfo.ForeColor = ForzaTheme.Red;
            if (!this.lblExhInfo.Text.EndsWith("not applied"))
            {
                this.lblExhInfo.Text += "\nedited, not applied";
            }
            this.btnExhApply.Enabled = true;
        }

        private void AddExhaustPoint()
        {
            float[] tip = this.SelectedExhaust ?? new float[] { 0f, 0.35f, 2.2f };
            this.exhTips.Add((float[]) tip.Clone());
            this.FillExhaustList(this.exhTips.Count - 1);
            this.MarkExhaustEdited();
        }

        private void DeleteExhaustPoint()
        {
            int index = this.lstExhaust.SelectedIndex;
            if (this.SelectedExhaust == null)
            {
                return;
            }
            this.exhTips.RemoveAt(index);
            this.FillExhaustList(index);
            this.MarkExhaustEdited();
        }

        private void MirrorExhaustPoint()
        {
            float[] tip = this.SelectedExhaust;
            if (tip == null)
            {
                return;
            }
            this.exhTips.Add(new float[] { -tip[0], tip[1], tip[2] });
            this.FillExhaustList(this.exhTips.Count - 1);
            this.MarkExhaustEdited();
        }

        private void ApplyExhaust()
        {
            if (this.exhEntry == null || this.exhTips == null)
            {
                return;
            }
            try
            {
                CarbinModel model = new CarbinModel(this.zip.Read(this.exhEntry));
                int old = model.ExhaustCount;
                model.SetExhaustTips(this.exhTips);
                this.zip.Replace(this.exhEntry, model.ToBytes());
                this.RefreshEntryNode(this.exhEntry);
                this.exhDirty = false;
                this.lblExhInfo.ForeColor = this.ForeColor;
                this.FillExhaustList(Math.Max(0, this.lstExhaust.SelectedIndex));
                this.SetStatus("Exhaust points: " + old + " -> " + this.exhTips.Count + " (pending: Save to write the archive)");
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Exhaust", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        }

        // A cross at every point and a short line along +Z (the flame goes out to the rear).
        private List<VertexPositionColor> ExhaustOverlayLines()
        {
            if (this.exhTips == null || !this.chkExhShow.Checked)
            {
                return null;
            }
            List<VertexPositionColor> lines = new List<VertexPositionColor>();
            float[] selected = this.SelectedExhaust;
            foreach (float[] tip in this.exhTips)
            {
                if (tip != selected)
                {
                    AddExhaustMark(lines, tip, new Microsoft.Xna.Framework.Graphics.Color(255, 150, 40, 255));
                }
            }
            if (selected != null)
            {
                AddExhaustMark(lines, selected, new Microsoft.Xna.Framework.Graphics.Color(255, 230, 40, 255));
            }
            return lines;
        }

        private static void AddExhaustMark(List<VertexPositionColor> lines, float[] tip, Microsoft.Xna.Framework.Graphics.Color color)
        {
            Vector3 p = new Vector3(tip[0], tip[1], tip[2]);
            const float arm = 0.06f;
            foreach (Vector3 axis in new Vector3[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            {
                lines.Add(new VertexPositionColor(p - (axis * arm), color));
                lines.Add(new VertexPositionColor(p + (axis * arm), color));
            }
            lines.Add(new VertexPositionColor(p, color));
            lines.Add(new VertexPositionColor(p + (Vector3.UnitZ * 0.4f), color));
        }
    }
}
