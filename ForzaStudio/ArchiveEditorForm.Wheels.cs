namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Windows.Forms;

    // Wheels (List_Wheels): every car has its own stock wheel (IsStock = 1, Data_Car.StockWheelID,
    // sometimes shared by sister cars), and the aftermarket wheels (IsStock = 0: BBS, Enkei...) fit any
    // car. The 3D model of each is GAME:\Media\wheels\<MediaName>.zip, the aftermarket ones also have
    // an icon in UI4\Upgrades\Tires_Rims\Licensed_Rims.
    public partial class ArchiveEditorForm
    {
        private const string Stock = "Stock";
        private const string Aftermarket = "Aftermarket";

        private TabControl tabWheel;
        private PropertyGrid pgWheel;
        private RelatedPanel relWheel;
        private Panel wheelHeader;
        private PictureBox picWheel;
        private Label lblWheelInfo;
        private Button btnWheelModel;
        private Button btnCarWheel;
        private ToolStripDropDownButton btnWheelFilter;
        private string wheelFilter = "";
        private long dbWheelId = -1;

        private void BuildWheelView(ToolStrip bar)
        {
            this.tabWheel = new TabControl();
            this.pgWheel = this.NewPropertyGrid();
            this.AddTab(this.tabWheel, "Wheel", this.pgWheel);
            this.relWheel = this.NewRelatedPanel();
            this.AddTab(this.tabWheel, "Used by", this.relWheel);

            this.wheelHeader = new Panel();
            this.wheelHeader.Height = 86;
            this.picWheel = new PictureBox();
            this.picWheel.Location = new Point(4, 4);
            this.picWheel.Size = new Size(156, 78);
            this.picWheel.SizeMode = PictureBoxSizeMode.Zoom;
            this.picWheel.BorderStyle = BorderStyle.FixedSingle;
            this.lblWheelInfo = new Label();
            this.lblWheelInfo.Location = new Point(170, 6);
            this.lblWheelInfo.Size = new Size(300, 44);
            this.lblWheelInfo.AutoEllipsis = true;
            this.btnWheelModel = new Button();
            this.btnWheelModel.Text = "Open Wheel Model";
            this.btnWheelModel.Location = new Point(170, 54);
            this.btnWheelModel.AutoSize = true;
            this.btnWheelModel.Click += delegate { this.OpenWheelFiles(); };
            this.wheelHeader.Controls.Add(this.picWheel);
            this.wheelHeader.Controls.Add(this.lblWheelInfo);
            this.wheelHeader.Controls.Add(this.btnWheelModel);
            this.wheelHeader.Resize += delegate { this.lblWheelInfo.Width = Math.Max(100, this.wheelHeader.Width - this.lblWheelInfo.Left - 6); };

            this.btnWheelFilter = new ToolStripDropDownButton("Show: All");
            foreach (string filter in new string[] { "", Stock, Aftermarket })
            {
                string picked = filter;
                this.btnWheelFilter.DropDownItems.Add(new ToolStripMenuItem((filter.Length == 0) ? "All" : filter, null, delegate
                {
                    this.wheelFilter = picked;
                    this.btnWheelFilter.Text = "Show: " + ((picked.Length == 0) ? "All" : picked);
                    this.FilterDbList();
                }));
            }
            this.btnWheelFilter.Visible = false;
            bar.Items.Insert(bar.Items.IndexOf(this.txtDbSearch) + 1, this.btnWheelFilter);
        }

        // Aftermarket first (by maker), then the stock wheels named after their cars.
        private void FillWheelList()
        {
            // Which cars use each stock wheel.
            Dictionary<long, List<string>> owners = new Dictionary<long, List<string>>();
            Dictionary<long, string> ownerMake = new Dictionary<long, string>();
            if (this.gameDb.HasTable("Data_Car"))
            {
                foreach (object[] row in this.gameDb.Sql.Query("SELECT StockWheelID, MakeID, DisplayName, MediaName FROM Data_Car ORDER BY Id").Rows)
                {
                    if (!(row[0] is long))
                    {
                        continue;
                    }
                    long wheel = (long) row[0];
                    if (!owners.ContainsKey(wheel))
                    {
                        owners[wheel] = new List<string>();
                        ownerMake[wheel] = this.gameDb.LookupName("make.short", row[1]) ?? "";
                    }
                    owners[wheel].Add(this.gameDb.Text("Data_Car", "DisplayName", row[2]) ?? GameDatabase.FormatValue(row[3]));
                }
            }
            bool makers = this.gameDb.HasTable("List_PartManufacturer");
            string sql = "SELECT w.ID, w.IsStock, " + (makers ? "m.PartManufacturer" : "NULL") + ", w.DisplayName, w.MediaName, w.Mass, w.Price FROM List_Wheels w"
                + (makers ? " LEFT JOIN List_PartManufacturer m ON m.Id = w.PartManufacturerID" : "");
            foreach (object[] row in this.gameDb.Sql.Query(sql).Rows)
            {
                long id = Convert.ToInt64(row[0]);
                bool stock = row[1] is long && (long) row[1] != 0;
                string maker;
                string name;
                List<string> cars;
                if (stock && owners.TryGetValue(id, out cars))
                {
                    maker = ownerMake[id];
                    name = cars[0] + ((cars.Count > 1) ? string.Format(" (+{0})", cars.Count - 1) : "");
                }
                else
                {
                    maker = this.gameDb.Text("List_PartManufacturer", "PartManufacturer", row[2]) ?? "";
                    name = (row[3] as string) ?? "";
                }
                this.dbItems.Add(NewItem(row[0], stock ? Stock : Aftermarket, maker, name, row[4], row[5], row[6]));
            }
            this.dbItems.Sort(delegate(ListViewItem a, ListViewItem b)
            {
                int type = string.Compare(a.SubItems[1].Text, b.SubItems[1].Text, StringComparison.Ordinal);
                if (type != 0)
                {
                    return type;
                }
                int maker = string.Compare(a.SubItems[2].Text, b.SubItems[2].Text, StringComparison.OrdinalIgnoreCase);
                return (maker != 0) ? maker : string.Compare(a.SubItems[3].Text, b.SubItems[3].Text, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void ShowWheel(long id)
        {
            this.dbWheelId = id;
            this.pgWheel.SelectedObject = this.Record("List_Wheels", id);
            this.relWheel.ShowItems(this.gameDb, this.gameDb.WheelRelated(id));

            object[] row = this.gameDb.ReadRow("List_Wheels", this.gameDb.RowIdOf("List_Wheels", id));
            GameDatabase.Table table = this.gameDb.GetTable("List_Wheels");
            string media = (row == null) ? null : row[table.Columns.IndexOf(table.Find("MediaName"))] as string;
            string icon = (row == null) ? null : row[table.Columns.IndexOf(table.Find("IconPath"))] as string;
            object stockValue = (row == null) ? null : row[table.Columns.IndexOf(table.Find("IsStock"))];
            bool stock = stockValue is long && (long) stockValue != 0;

            // Aftermarket: the game's rim icon. Stock: the thumbnail of the car that wears it.
            Bitmap image = null;
            string owner = null;
            if (!stock && !string.IsNullOrEmpty(icon))
            {
                image = this.LoadGameImage("game:\\Media\\UI\\Textures\\UI4\\Upgrades\\Tires_Rims\\Licensed_Rims\\" + icon + ".xds");
            }
            if (stock && this.gameDb.HasTable("Data_Car"))
            {
                SqliteDatabase.Result cars = this.gameDb.Sql.Query("SELECT Thumbnail, DisplayName, MediaName FROM Data_Car WHERE StockWheelID = ? ORDER BY Id", id);
                if (cars.Rows.Count > 0)
                {
                    image = this.LoadGameImage(cars.Rows[0][0] as string);
                    owner = this.gameDb.Text("Data_Car", "DisplayName", cars.Rows[0][1]) ?? GameDatabase.FormatValue(cars.Rows[0][2]);
                    if (cars.Rows.Count > 1)
                    {
                        owner += string.Format(" and {0} more", cars.Rows.Count - 1);
                    }
                }
            }
            SetPicture(this.picWheel, image);

            string root = this.GameRoot;
            string model = GamePaths.FindMedia(root, "wheels", media);
            string kind = stock ? "Stock wheel" + ((owner != null) ? " of " + owner : "") : "Aftermarket wheel (fits every car)";
            ForzaZip.Entry own = this.OwnMedia("wheels", media);
            if (own != null)
            {
                // A DLC (pack zip or development folder) carries its wheels under its own media\wheels.
                this.lblWheelInfo.Text = kind + "\nModel: " + own.Name + " (in this DLC)";
                this.btnWheelModel.Enabled = true;
                this.ShowDbContent(this.tabWheel);
                return;
            }
            this.lblWheelInfo.Text = (root == null)
                ? kind + "\nOpen the game folder to see the icon and the model."
                : kind + "\nModel: " + ((model != null) ? model.Substring(root.Length).TrimStart('\\') : "Media\\wheels\\" + media + " not found");
            this.btnWheelModel.Enabled = model != null;
            this.ShowDbContent(this.tabWheel);
        }

        // The wheel's 3D model: its zip opens on the .carbin (Back returns here).
        private void OpenWheelFiles()
        {
            object media = (this.gameDb == null) ? null : this.gameDb.Sql.Scalar("SELECT MediaName FROM List_Wheels WHERE ID = ?", this.dbWheelId);
            ForzaZip.Entry own = this.OwnMedia("wheels", media as string);
            if (own != null)
            {
                this.OpenOwnEntry(own);
                return;
            }
            string root = this.GameRoot;
            string model = GamePaths.FindMedia(root, "wheels", media as string);
            if (model == null)
            {
                return;
            }
            this.NavigateFrom(delegate
            {
                if (File.Exists(model))
                {
                    this.OpenArchive(model);
                    if (this.zip != null && string.Equals(this.zip.FilePath, model, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (ForzaZip.Entry entry in this.zip.Entries)
                        {
                            if (entry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                            {
                                this.SelectEntry(entry.Name);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    this.OpenGameFile(model, root);
                }
            });
        }

        // From a car to its stock wheel, inside the database (Back returns to the car).
        private void JumpToWheel()
        {
            object wheel = this.gameDb.Sql.Scalar("SELECT StockWheelID FROM Data_Car WHERE Id = ?", this.dbCarId);
            if (!(wheel is long) || !this.gameDb.HasTable("List_Wheels"))
            {
                return;
            }
            this.PushBookmark(this.CaptureBookmark());
            this.wheelFilter = "";
            this.btnWheelFilter.Text = "Show: All";
            this.SelectDbSection(DbSection.Wheels);
            this.ClearDbSearch();
            string key = ((long) wheel).ToString(CultureInfo.InvariantCulture);
            foreach (ListViewItem item in this.lvDb.Items)
            {
                if (item.Text == key)
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    break;
                }
            }
        }
    }
}
