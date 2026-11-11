namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.Windows.Forms;

    // Pick one item from a searchable list (engines, cars, tracks...), with optional extra fields.
    internal class PickDialog : Form
    {
        private readonly ListView list;
        private readonly TextBox search;
        private readonly List<ListViewItem> items = new List<ListViewItem>();

        public PickDialog(string title, string[] columns, int[] widths, List<object[]> rows, Control extra)
        {
            this.Text = title;
            this.Size = new Size(Math.Max(560, Sum(widths) + 60), 560);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.Font = ForzaTheme.Regular;

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 34;
            Label label = new Label();
            label.Text = "Search:";
            label.Location = new Point(10, 10);
            label.AutoSize = true;
            this.search = new TextBox();
            this.search.Location = new Point(64, 7);
            this.search.Width = 260;
            this.search.BorderStyle = BorderStyle.FixedSingle;
            this.search.TextChanged += delegate { this.Filter(); };
            this.search.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Down && this.list.Items.Count > 0)
                {
                    this.list.Focus();
                    this.list.Items[0].Selected = true;
                    e.Handled = true;
                }
            };
            top.Controls.Add(label);
            top.Controls.Add(this.search);

            this.list = new ListView();
            this.list.Dock = DockStyle.Fill;
            this.list.View = View.Details;
            this.list.FullRowSelect = true;
            this.list.MultiSelect = false;
            this.list.HideSelection = false;
            for (int i = 0; i < columns.Length; i++)
            {
                this.list.Columns.Add(columns[i], widths[i]);
            }
            foreach (object[] row in rows)
            {
                ListViewItem item = new ListViewItem(GameDatabase.FormatValue(row[0]));
                item.Tag = row[0];
                for (int i = 1; i < row.Length; i++)
                {
                    item.SubItems.Add((row[i] == null) ? "" : GameDatabase.FormatValue(row[i]));
                }
                this.items.Add(item);
            }
            this.list.DoubleClick += delegate { this.Accept(); };
            this.list.ItemCheck += this.OnItemCheck;
            this.list.SelectedIndexChanged += delegate { this.RaiseSelectionChanged(); };

            // Options of the caller (above the buttons), full width.
            Panel options = null;
            if (extra != null)
            {
                options = new Panel();
                options.Dock = DockStyle.Bottom;
                options.Height = extra.Height + 12;
                options.Padding = new Padding(8, 6, 8, 6);
                extra.Location = new Point(8, 6);
                options.Controls.Add(extra);
                this.Width = Math.Max(this.Width, extra.Width + 48);
            }

            // The button bar (user 2026-11-11: OK and Cancel had gone out of the window): a right-to-left flow docked at
            // the bottom, so the buttons are always on screen whatever the width. The main button says what it does
            // (AcceptLabel) and stays off until something is picked.
            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Bottom;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Height = 48;
            buttons.Padding = new Padding(8, 8, 8, 8);
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.Size = new Size(96, 30);
            cancel.DialogResult = DialogResult.Cancel;
            this.ok = new Button();
            this.ok.Text = "OK";
            this.ok.AutoSize = true;
            this.ok.MinimumSize = new Size(110, 30);
            this.ok.Click += delegate { this.Accept(); };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(this.ok);
            this.CancelButton = cancel;

            this.Controls.Add(this.list);
            if (options != null)
            {
                this.Controls.Add(options);
            }
            this.Controls.Add(buttons);
            this.Controls.Add(top);
            this.MinimumSize = new Size(Math.Min(this.Width, 480), 360);
            this.BackColor = ForzaTheme.Background;
            ForzaTheme.Apply(this);
            // the main action in Forza red, the rest stays grey
            this.ok.BackColor = ForzaTheme.Red;
            this.ok.FlatAppearance.BorderColor = ForzaTheme.Red;
            this.ok.FlatAppearance.MouseOverBackColor = ForzaTheme.RedDark;
            this.ok.Font = ForzaTheme.Bold;
            this.Filter();
            this.SelectionChanged += delegate { this.UpdateAcceptButton(); };
            this.UpdateAcceptButton();
        }

        private readonly Button ok;
        private Func<int, string> acceptLabel;

        // The main button's text for n rows picked, e.g. n => n == 1 ? "Copy part" : "Copy " + n + " parts".
        public Func<int, string> AcceptLabel
        {
            get { return this.acceptLabel; }
            set
            {
                this.acceptLabel = value;
                this.UpdateAcceptButton();
            }
        }

        private void UpdateAcceptButton()
        {
            int count = this.SelectedCount;
            this.ok.Enabled = count > 0 && (this.list.MultiSelect || count == 1);
            this.ok.Text = (this.acceptLabel != null) ? this.acceptLabel(Math.Max(count, 1)) : "OK";
            this.ok.BackColor = this.ok.Enabled ? ForzaTheme.Red : ForzaTheme.Raised;
            this.ok.ForeColor = this.ok.Enabled ? Color.White : ForzaTheme.TextDim;
        }

        private static int Sum(int[] values)
        {
            int total = 0;
            foreach (int value in values)
            {
                total += value;
            }
            return total;
        }

        public object SelectedKey { get; private set; }

        // Every row picked (in list order) when MultiSelect is on; one row otherwise.
        public readonly List<object> SelectedKeys = new List<object>();

        // Several rows at once: a tick box on every row (user 2026-11-11: Ctrl + click was not found), also
        // Ctrl / Shift + click. Ticked rows win over the highlighted ones.
        public bool MultiSelect
        {
            get { return this.list.MultiSelect; }
            set
            {
                this.list.MultiSelect = value;
                this.list.CheckBoxes = value;
            }
        }

        // The rows picked now changed (to enable the extra fields that only fit one row).
        public event EventHandler SelectionChanged;

        // The ticked rows, kept here: ListViewItem.Checked asks the list for the row's state and throws (index -1) for
        // a row the list is adding (handle creation, search filter) - the crash of 2026-11-11.
        private readonly List<ListViewItem> ticked = new List<ListViewItem>();
        private bool filling = true;   // rows being (re)added: their tick events are not the user's

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (this.filling || e.Index < 0 || e.Index >= this.list.Items.Count)
            {
                return;
            }
            ListViewItem item = this.list.Items[e.Index];
            this.ticked.Remove(item);
            if (e.NewValue == CheckState.Checked)
            {
                this.ticked.Add(item);
            }
            this.RaiseSelectionChanged();
        }

        private void RaiseSelectionChanged()
        {
            if (this.SelectionChanged != null)
            {
                this.SelectionChanged(this, EventArgs.Empty);
            }
        }

        private List<ListViewItem> Ticked()
        {
            // in list order
            return this.items.FindAll(delegate(ListViewItem item) { return this.ticked.Contains(item); });
        }

        public int SelectedCount
        {
            get
            {
                int ticked = this.list.CheckBoxes ? this.Ticked().Count : 0;
                return (ticked > 0) ? ticked : this.list.SelectedItems.Count;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            this.filling = false;
            this.search.Focus();
        }

        private void Filter()
        {
            string find = this.search.Text.Trim();
            bool wasFilling = this.filling;
            this.filling = true;
            this.list.BeginUpdate();
            this.list.Items.Clear();
            List<ListViewItem> shown = new List<ListViewItem>();
            foreach (ListViewItem item in this.items)
            {
                bool match = find.Length == 0;
                foreach (ListViewItem.ListViewSubItem sub in item.SubItems)
                {
                    match = match || sub.Text.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (match)
                {
                    shown.Add(item);
                }
            }
            this.list.Items.AddRange(shown.ToArray());
            this.list.EndUpdate();
            this.filling = wasFilling;
        }

        private void Accept()
        {
            List<ListViewItem> picked = this.list.CheckBoxes ? this.Ticked() : new List<ListViewItem>();
            if (picked.Count == 0)
            {
                foreach (ListViewItem item in this.list.SelectedItems)
                {
                    picked.Add(item);
                }
            }
            if (picked.Count == 0 || (!this.list.MultiSelect && picked.Count != 1))
            {
                return;
            }
            this.SelectedKey = picked[0].Tag;
            this.SelectedKeys.Clear();
            foreach (ListViewItem item in picked)
            {
                this.SelectedKeys.Add(item.Tag);
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        // A labelled number field for the extra panel.
        public static NumericUpDown NumberField(Control parent, string caption, int x, decimal value, decimal maximum, int decimals)
        {
            Label label = new Label();
            label.Text = caption;
            label.Location = new Point(x, 4);
            label.AutoSize = true;
            NumericUpDown number = new NumericUpDown();
            number.Location = new Point(x, 22);
            number.Width = 110;
            number.Maximum = maximum;
            number.Minimum = -maximum;
            number.DecimalPlaces = decimals;
            number.Value = value;
            number.BorderStyle = BorderStyle.FixedSingle;
            parent.Controls.Add(label);
            parent.Controls.Add(number);
            return number;
        }

        public static ComboBox ListField(Control parent, string caption, int x, int width, GameDatabase.Lookup lookup, long selected)
        {
            Label label = new Label();
            label.Text = caption;
            label.Location = new Point(x, 4);
            label.AutoSize = true;
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Location = new Point(x, 22);
            combo.Width = width;
            int index = 0;
            foreach (KeyValuePair<long, string> item in lookup.Items)
            {
                combo.Items.Add(new LookupChoice(item.Key, lookup.Describe(item.Key)));
                if (item.Key == selected)
                {
                    index = combo.Items.Count - 1;
                }
            }
            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = index;
            }
            parent.Controls.Add(label);
            parent.Controls.Add(combo);
            return combo;
        }

        public class LookupChoice
        {
            public readonly long Id;
            private readonly string text;

            public LookupChoice(long id, string text)
            {
                this.Id = id;
                this.text = text;
            }

            public override string ToString()
            {
                return this.text;
            }
        }
    }
}
