namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Windows.Forms;

    // The driver of a car (ROADMAP 6.2), edited by hand like the exhaust points. The game keeps two rigs in the headers'
    // CDriverControls (default.xex, 2026-11-07; CarbinModel.DriverControlNames):
    //  - LOD 0, in the header of the car's _cockpit.carbin and shown with it or with the _lod0.carbin (whose own block is
    //    zero: the close-up car is the two files together, one set of points): the six hand grips on the steering wheel
    //    (Right / LeftHandOuter, Inner, Center), SteeringPivotLOD0, SteeringWheelAxisLOD0, ButtAnchorPositionLOD0 and
    //    ShifterAnchorPositionLOD0 (vectors 6..15; the gauges that follow are in the GAUGES tab);
    //  - LOD 1..5, in the header of the car's main .carbin and shown with it: SteeringWheelPosition / Axis, Left /
    //    RightHandAnchor, ButtAnchor, ShifterAnchor (vectors 0..5; zero in a cockpit but the axis).
    //    The two are not the same points: on FM4's own cars each follows the geometry of its own file (wheel 4.9 cm apart
    //    as a median, 313 cars, docs\DRIVER_POSITION.md), so each is edited with its own model in the view. A car made
    //    from scratch has the donor's in both and nothing to compute them from (user, 2026-11-17): hence by hand;
    //  - the offsets of carattribs.xml, with any of the three files: DriverHandsOffset and the Driver element (ButtOffset
    //    = where the body sits, the hand, shifter and hand brake offsets; seat and hands have a LOD0 twin), plus BodyLean
    //    and BodyScale.
    // The header points are drawn as crosses in the view while the DRIVER tab is open. The carattribs offsets are
    // relative to something the game adds (seat / hands anchors), so they are only listed. Apply writes both files
    // into the archive, pending until File > Save. The header names are the game's; what each point does in game
    // follows from the names, not all checked by hand.
    public partial class ArchiveEditorForm
    {
        private sealed class DriverItem
        {
            public string Name;
            public int Offset = -1;         // header point / direction (3 floats), or -1
            public int Vector = -1;         // its index in CDriverControls (the offset moves with the exhaust tips)
            public string Attribute;        // carattribs: "Driver:ButtOffset" (x / y / z), "Hands" (DriverHandsOffset), or a scalar "Driver:BodyLean"
            public bool Scalar;
            public bool Direction;          // drawn as a line, not a cross
            public float[] Value = new float[3];
            public float[] Original = new float[3];
            public bool Changed
            {
                get { return this.Value[0] != this.Original[0] || this.Value[1] != this.Original[1] || this.Value[2] != this.Original[2]; }
            }
        }

        private Label lblDrvInfo;
        private ListBox lstDriver;
        private NumericUpDown[] numDrvPos;
        private CheckBox chkDrvShow;
        private Button btnDrvApply;
        private Button btnDrvReset;

        private ForzaZip.Entry drvEntry;    // the file whose header holds the points listed (the cockpit's with a _lod0 shown)
        private List<DriverItem> drvItems;
        private bool drvCloseUp;            // the rig listed: LOD 0 (cockpit header) or LOD 1..5 (main header)
        private bool fillingDrv;

        private static readonly string[] DriverVectors = { "ButtOffset", "ButtOffsetLOD0", "LeftHandOffset", "RightHandOffset", "LeftHandOffsetLOD0", "RightHandOffsetLOD0", "ShifterOffset", "HandBrakeOffset", "IKGuideTranslation" };
        private static readonly string[] DriverScalars = { "BodyLean", "BodyScale" };

        private void AddDriverControls(FlowLayoutPanel side)
        {
            this.lblDrvInfo = SideNote("Open a car's main model (LOD 1..5) or its _lod0 / _cockpit model (LOD 0).");
            this.lblDrvInfo.ForeColor = this.ForeColor;

            this.lstDriver = new ListBox();
            this.lstDriver.Size = new Size(SideWidth, 190);
            this.lstDriver.IntegralHeight = false;
            this.lstDriver.SelectedIndexChanged += delegate
            {
                this.FillDriverControls();
                this.UpdateViewOverlay();
            };

            FlowLayoutPanel position = this.NumberRow(out this.numDrvPos, -20, 20, 4, 0.005m, 0);
            foreach (NumericUpDown number in this.numDrvPos)
            {
                number.ValueChanged -= new EventHandler(this.PartEditValueChanged);
                number.ValueChanged += delegate { this.DriverValueChanged(); };
            }

            this.chkDrvShow = new CheckBox();
            this.chkDrvShow.Text = "Show points in the view";
            this.chkDrvShow.Checked = true;
            this.chkDrvShow.Size = new Size(SideWidth, 22);
            this.chkDrvShow.CheckedChanged += delegate { this.UpdateViewOverlay(); };

            this.btnDrvReset = SideWideButton("Undo the selected value", delegate { this.ResetDriverItem(); });
            this.btnDrvApply = SideWideButton("Apply to Model", delegate { this.ApplyDriver(); });

            side.Controls.Add(SideHeading("Driver", true));
            side.Controls.Add(this.lblDrvInfo);
            side.Controls.Add(this.lstDriver);
            side.Controls.Add(SideCaption("Value (m, model axes; one number for lean / scale)"));
            side.Controls.Add(position);
            side.Controls.Add(this.btnDrvReset);
            side.Controls.Add(this.chkDrvShow);
            side.Controls.Add(SideHeading("Write", false));
            side.Controls.Add(this.btnDrvApply);
            side.Controls.Add(SideNote("The game places the driver twice. LOD 0 (the _lod0 and _cockpit models together): the points stored in the cockpit model (hand grips on the wheel rim, wheel pivot and axis, seat and shifter points). LOD 1..5: the points stored in the main model (wheel pivot and axis, hands, seat, shifter). They are separate values: open the main model to edit the second set, the _lod0 or the _cockpit model for the first. Drawn in the view: yellow the selected one, orange the others, a line for a direction. Z is the model axis (positive to the rear). Carattribs: the offsets of the Driver element and DriverHandsOffset, relative to the points the game places the driver from (listed, not drawn; the same list with every model). Apply writes the model holding the points and carattribs.xml into the car file, pending until File > Save."));
            this.EnableDriverEdit();
        }

        private DriverItem SelectedDriver
        {
            get
            {
                int index = (this.lstDriver == null) ? -1 : this.lstDriver.SelectedIndex;
                return (this.drvItems != null && index >= 0 && index < this.drvItems.Count) ? this.drvItems[index] : null;
            }
        }

        private bool DriverChanged
        {
            get { return this.drvItems != null && this.drvItems.Exists(delegate(DriverItem item) { return item.Changed; }); }
        }

        private void EnableDriverEdit()
        {
            DriverItem item = this.SelectedDriver;
            for (int i = 0; i < 3; i++)
            {
                this.numDrvPos[i].Enabled = item != null && (!item.Scalar || i == 0);
            }
            this.btnDrvReset.Enabled = item != null && item.Changed;
            this.btnDrvApply.Enabled = this.DriverChanged;
        }

        private static readonly string[] DriverHints =
        {
            "steering wheel pivot", "steering wheel axis", "left hand", "right hand", "seat point", "shifter point",
            "right grip, rim outer edge", "left grip, rim outer edge", "right grip, rim inner edge", "left grip, rim inner edge",
            "right grip, tube middle", "left grip, tube middle", "steering wheel pivot", "steering wheel axis",
            "seat point", "shifter point",
        };

        private const string CockpitSuffix = "_cockpit.carbin";
        private const string Lod0Suffix = "_lod0.carbin";

        // Called with every model shown. A car's _cockpit.carbin or _lod0.carbin: the LOD 0 rig, vectors 6..15 of the
        // cockpit's header. Its main .carbin: the LOD 1..5 rig, vectors 0..5 of its own header.
        private void LoadDriver()
        {
            this.drvEntry = null;
            this.drvItems = null;
            this.lblDrvInfo.Text = "";
            this.lblDrvInfo.ForeColor = this.ForeColor;
            this.lstDriver.Items.Clear();
            string name = (this.currentEntry != null) ? this.currentEntry.Name : "";
            string low = name.ToLowerInvariant();
            ForzaZip.Entry source = null;
            bool closeUp = low.EndsWith(CockpitSuffix) || low.EndsWith(Lod0Suffix);
            if (low.EndsWith(CockpitSuffix))
            {
                source = this.currentEntry;
            }
            else if (low.EndsWith(Lod0Suffix))
            {
                // The _lod0 file's own block is zero (313 FM4 cars): the points of the close-up car are the cockpit's.
                // A brake's _lod0 has no cockpit beside it and stays out.
                source = this.zip.Find(name.Substring(0, name.Length - Lod0Suffix.Length) + CockpitSuffix);
            }
            else if (low.EndsWith(".carbin"))
            {
                source = this.currentEntry;
            }
            if (source != null)
            {
                try
                {
                    CarbinModel model = new CarbinModel(this.zip.Read(source));
                    int controls = DriverControlsOffset(model.Head, closeUp);
                    if (controls >= 0)
                    {
                        List<DriverItem> items = new List<DriverItem>();
                        int first = closeUp ? 6 : 0;
                        int end = closeUp ? CarbinModel.HeaderLayout.DriverVectorCount : 6;
                        for (int v = first; v < end; v++)
                        {
                            DriverItem item = HeaderItem(CarbinModel.DriverControlNames[v] + " (" + DriverHints[v] + ")", controls + 4 + (v * 16), v == 1 || v == 13, model.Head);
                            item.Vector = v;
                            items.Add(item);
                        }
                        // From 0x270 the cockpit header holds the 15 analogue gauges (pivot, axis, max rotation), not
                        // driver points (default.xex, 2026-11-07): they are edited in the GAUGES tab (CarGauges).
                        this.drvEntry = source;
                        this.drvItems = items;
                        this.drvCloseUp = closeUp;
                        this.AddDriverAttributes(items);
                    }
                }
                catch (Exception)
                {
                }
            }
            this.SetDriverTabVisible(this.drvItems != null);
            this.FillDriverList(0);
        }

        // Where CDriverControls starts in a header, or -1. A cockpit has no emitters, so its vectors 6..15 sit at
        // 0x1C8..0x258 even when the header does not walk to its end. The main file's emitters (exhaust tips) come
        // first and move the block, so it counts only with a header that walks and holds CDriverControls version 3:
        // brakes and wheels (another header) and the other games' files stay out.
        private static int DriverControlsOffset(byte[] head, bool closeUp)
        {
            CarbinModel.HeaderLayout layout = (head != null) ? CarbinModel.HeaderLayout.Walk(head) : null;
            if (layout != null)
            {
                return (closeUp || ReadBigU32(head, layout.DriverControls) == 3) ? layout.DriverControls : -1;
            }
            return (closeUp && head != null && head.Length >= 0x300) ? 0x164 : -1;
        }

        private static uint ReadBigU32(byte[] data, int offset)
        {
            return ((uint) data[offset] << 24) | ((uint) data[offset + 1] << 16) | ((uint) data[offset + 2] << 8) | data[offset + 3];
        }

        private static DriverItem HeaderItem(string name, int offset, bool direction, byte[] head)
        {
            DriverItem item = new DriverItem();
            item.Name = name;
            item.Offset = offset;
            item.Direction = direction;
            for (int i = 0; i < 3; i++)
            {
                item.Value[i] = ReadBigFloat(head, offset + (i * 4));
                item.Original[i] = item.Value[i];
            }
            return item;
        }

        private static float ReadBigFloat(byte[] data, int offset)
        {
            byte[] b = { data[offset + 3], data[offset + 2], data[offset + 1], data[offset] };
            return BitConverter.ToSingle(b, 0);
        }

        private static void WriteBigFloat(byte[] data, int offset, float value)
        {
            byte[] b = BitConverter.GetBytes(value);
            for (int k = 0; k < 4; k++)
            {
                data[offset + k] = b[3 - k];
            }
        }

        // carattribs.xml: DriverHandsOffset and the Driver element.
        private void AddDriverAttributes(List<DriverItem> items)
        {
            ForzaZip.Entry attributes = this.zip.Find("carattribs.xml");
            if (attributes == null)
            {
                return;
            }
            string text = Encoding.UTF8.GetString(this.zip.Read(attributes));
            Match hands = Regex.Match(text, "<DriverHandsOffset\\b[^>]*/>");
            if (hands.Success)
            {
                DriverItem item = new DriverItem();
                item.Name = "DriverHandsOffset (carattribs)";
                item.Attribute = "Hands";
                for (int i = 0; i < 3; i++)
                {
                    // The attributes come in any order (FM4 writes w, z, y, x).
                    item.Value[i] = ParseFloat(Regex.Match(hands.Value, "value\\." + "xyz"[i] + "=\"([^\"]*)\"").Groups[1].Value);
                    item.Original[i] = item.Value[i];
                }
                items.Add(item);
            }
            Match driver = Regex.Match(text, "<Driver [^>]*/>");
            if (!driver.Success)
            {
                return;
            }
            foreach (string vector in DriverVectors)
            {
                Match x = Regex.Match(driver.Value, "\\b" + vector + "\\.x=\"([^\"]*)\"");
                Match y = Regex.Match(driver.Value, "\\b" + vector + "\\.y=\"([^\"]*)\"");
                Match z = Regex.Match(driver.Value, "\\b" + vector + "\\.z=\"([^\"]*)\"");
                if (x.Success && y.Success && z.Success)
                {
                    DriverItem item = new DriverItem();
                    // Seat and hands come in pairs like the header's (ButtOffset / ButtOffsetLOD0): by the names, the
                    // plain one is the distant LODs'.
                    string lod = vector.EndsWith("LOD0") ? ", LOD 0" : ((Array.IndexOf(DriverVectors, vector + "LOD0") >= 0) ? ", LOD 1..5" : "");
                    item.Name = vector + " (carattribs" + lod + ")";
                    item.Attribute = "Driver:" + vector;
                    item.Value[0] = ParseFloat(x.Groups[1].Value);
                    item.Value[1] = ParseFloat(y.Groups[1].Value);
                    item.Value[2] = ParseFloat(z.Groups[1].Value);
                    Array.Copy(item.Value, item.Original, 3);
                    items.Add(item);
                }
            }
            foreach (string scalar in DriverScalars)
            {
                Match m = Regex.Match(driver.Value, "\\b" + scalar + "=\"([^\"]*)\"");
                if (m.Success)
                {
                    DriverItem item = new DriverItem();
                    item.Name = scalar + " (carattribs)";
                    item.Attribute = "Driver:" + scalar;
                    item.Scalar = true;
                    item.Value[0] = ParseFloat(m.Groups[1].Value);
                    item.Original[0] = item.Value[0];
                    items.Add(item);
                }
            }
        }

        private static float ParseFloat(string text)
        {
            float value;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0f;
        }

        private void FillDriverList(int select)
        {
            this.fillingDrv = true;
            this.lstDriver.Items.Clear();
            if (this.drvItems != null)
            {
                foreach (DriverItem item in this.drvItems)
                {
                    this.lstDriver.Items.Add(DriverText(item));
                }
                if (this.lstDriver.Items.Count > 0)
                {
                    this.lstDriver.SelectedIndex = Math.Max(0, Math.Min(select, this.lstDriver.Items.Count - 1));
                }
                this.UpdateDriverInfo();
            }
            this.fillingDrv = false;
            this.FillDriverControls();
            this.UpdateViewOverlay();
        }

        private void UpdateDriverInfo()
        {
            if (this.drvItems == null)
            {
                return;
            }
            int changed = this.drvItems.FindAll(delegate(DriverItem item) { return item.Changed; }).Count;
            this.lblDrvInfo.ForeColor = (changed > 0) ? ForzaTheme.Red : this.ForeColor;
            string rig = this.drvCloseUp
                ? "LOD 0 driver (_lod0 and _cockpit; stored in " + this.drvEntry.Name + "), " + this.drvItems.Count + " values.\nLOD 1..5: open the main model."
                : "LOD 1..5 driver (stored in " + this.drvEntry.Name + "), " + this.drvItems.Count + " values.\nLOD 0: open the _lod0 or _cockpit model.";
            this.lblDrvInfo.Text = rig + ((changed > 0) ? "\n" + changed + " edited, not applied" : "");
        }

        private static string DriverText(DriverItem item)
        {
            string value = item.Scalar
                ? item.Value[0].ToString("0.###", CultureInfo.InvariantCulture)
                : string.Format(CultureInfo.InvariantCulture, "{0:0.###}, {1:0.###}, {2:0.###}", item.Value[0], item.Value[1], item.Value[2]);
            return (item.Changed ? "* " : "") + item.Name + ": " + value;
        }

        private void FillDriverControls()
        {
            DriverItem item = this.SelectedDriver;
            this.fillingDrv = true;
            for (int i = 0; i < 3; i++)
            {
                this.numDrvPos[i].Value = (item == null) ? 0 : Math.Max(this.numDrvPos[i].Minimum, Math.Min(this.numDrvPos[i].Maximum, (decimal) Math.Round(item.Value[i], 4)));
            }
            this.fillingDrv = false;
            this.EnableDriverEdit();
        }

        private void DriverValueChanged()
        {
            DriverItem item = this.SelectedDriver;
            if (this.fillingDrv || item == null)
            {
                return;
            }
            for (int i = 0; i < (item.Scalar ? 1 : 3); i++)
            {
                item.Value[i] = (float) this.numDrvPos[i].Value;
            }
            this.fillingDrv = true;
            this.lstDriver.Items[this.lstDriver.SelectedIndex] = DriverText(item);
            this.fillingDrv = false;
            this.UpdateDriverInfo();
            this.EnableDriverEdit();
            this.UpdateViewOverlay();
        }

        private void ResetDriverItem()
        {
            DriverItem item = this.SelectedDriver;
            if (item == null)
            {
                return;
            }
            Array.Copy(item.Original, item.Value, 3);
            this.lstDriver.Items[this.lstDriver.SelectedIndex] = DriverText(item);
            this.UpdateDriverInfo();
            this.FillDriverControls();
            this.UpdateViewOverlay();
        }

        private void ApplyDriver()
        {
            if (this.drvEntry == null || this.drvItems == null || !this.DriverChanged)
            {
                return;
            }
            try
            {
                int written = 0;
                CarbinModel model = new CarbinModel(this.zip.Read(this.drvEntry));
                // Found again in the file as it is now: exhaust tips added or removed since the list was filled
                // (EXHAUST tab) move the main file's block.
                int controls = DriverControlsOffset(model.Head, this.drvCloseUp);
                bool header = false;
                foreach (DriverItem item in this.drvItems)
                {
                    if (item.Offset >= 0 && item.Changed)
                    {
                        if (controls < 0)
                        {
                            throw new System.IO.InvalidDataException("The driver points are no longer where they were in " + this.drvEntry.Name + ": open the model again.");
                        }
                        item.Offset = controls + 4 + (item.Vector * 16);
                        for (int i = 0; i < 3; i++)
                        {
                            WriteBigFloat(model.Head, item.Offset + (i * 4), item.Value[i]);
                        }
                        header = true;
                        written++;
                    }
                }
                if (header)
                {
                    this.zip.Replace(this.drvEntry, model.ToBytes());
                    this.RefreshEntryNode(this.drvEntry);
                }
                ForzaZip.Entry attributes = this.zip.Find("carattribs.xml");
                if (attributes != null && this.drvItems.Exists(delegate(DriverItem item) { return item.Attribute != null && item.Changed; }))
                {
                    string text = Encoding.UTF8.GetString(this.zip.Read(attributes));
                    foreach (DriverItem item in this.drvItems)
                    {
                        if (item.Attribute != null && item.Changed)
                        {
                            text = WriteDriverAttribute(text, item);
                            written++;
                        }
                    }
                    this.zip.Replace(attributes, Encoding.UTF8.GetBytes(text));
                    this.RefreshEntryNode(attributes);
                }
                foreach (DriverItem item in this.drvItems)
                {
                    Array.Copy(item.Value, item.Original, 3);
                }
                this.FillDriverList(Math.Max(0, this.lstDriver.SelectedIndex));
                this.SetStatus("Driver: " + written + " values written (pending: Save to write the archive)");
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Driver", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        }

        private static string FloatText(float value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string WriteDriverAttribute(string text, DriverItem item)
        {
            if (item.Attribute == "Hands")
            {
                return Regex.Replace(text, "<DriverHandsOffset\\b[^>]*/>", delegate(Match m)
                {
                    string result = m.Value;
                    for (int i = 0; i < 3; i++)
                    {
                        result = Regex.Replace(result, "(value\\." + "xyz"[i] + "=\")[^\"]*\"", "${1}" + FloatText(item.Value[i]) + "\"");
                    }
                    return result;
                });
            }
            Match driver = Regex.Match(text, "<Driver [^>]*/>");
            if (!driver.Success)
            {
                return text;
            }
            string name = item.Attribute.Substring("Driver:".Length);
            string element = driver.Value;
            if (item.Scalar)
            {
                element = Regex.Replace(element, "(\\b" + name + "=\")[^\"]*\"", delegate(Match m) { return m.Groups[1].Value + FloatText(item.Value[0]) + "\""; });
            }
            else
            {
                string[] axes = { "x", "y", "z" };
                for (int i = 0; i < 3; i++)
                {
                    int axis = i;
                    element = Regex.Replace(element, "(\\b" + name + "\\." + axes[i] + "=\")[^\"]*\"", delegate(Match m) { return m.Groups[1].Value + FloatText(item.Value[axis]) + "\""; });
                }
            }
            return text.Substring(0, driver.Index) + element + text.Substring(driver.Index + driver.Length);
        }

        // A cross at every header point, a line along the direction from its point (the wheel axis from the pivot).
        private List<VertexPositionColor> DriverOverlayLines()
        {
            if (this.drvItems == null || !this.chkDrvShow.Checked)
            {
                return null;
            }
            List<VertexPositionColor> lines = new List<VertexPositionColor>();
            DriverItem selected = this.SelectedDriver;
            Microsoft.Xna.Framework.Graphics.Color orange = new Microsoft.Xna.Framework.Graphics.Color(255, 150, 40, 255);
            Microsoft.Xna.Framework.Graphics.Color yellow = new Microsoft.Xna.Framework.Graphics.Color(255, 230, 40, 255);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < this.drvItems.Count; i++)
                {
                    DriverItem item = this.drvItems[i];
                    if (item.Offset < 0 || (item == selected) != (pass == 1))
                    {
                        continue;
                    }
                    Microsoft.Xna.Framework.Graphics.Color color = (pass == 1) ? yellow : orange;
                    if (item.Direction)
                    {
                        // From the point just before it in the list (wheel pivot, record point).
                        DriverItem origin = (i > 0 && this.drvItems[i - 1].Offset >= 0 && !this.drvItems[i - 1].Direction) ? this.drvItems[i - 1] : null;
                        if (origin != null)
                        {
                            Vector3 p = new Vector3(origin.Value[0], origin.Value[1], origin.Value[2]);
                            Vector3 d = new Vector3(item.Value[0], item.Value[1], item.Value[2]);
                            if (d.LengthSquared() > 1e-6f)
                            {
                                d.Normalize();
                                lines.Add(new VertexPositionColor(p, color));
                                lines.Add(new VertexPositionColor(p + (d * 0.3f), color));
                            }
                        }
                        continue;
                    }
                    Vector3 point = new Vector3(item.Value[0], item.Value[1], item.Value[2]);
                    const float arm = 0.025f;
                    foreach (Vector3 axis in new Vector3[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                    {
                        lines.Add(new VertexPositionColor(point - (axis * arm), color));
                        lines.Add(new VertexPositionColor(point + (axis * arm), color));
                    }
                }
            }
            return lines;
        }
    }
}
