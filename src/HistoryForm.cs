using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TATimesheet
{
    /// <summary>Edit what shows up in the Project / Location / Customer / Name dropdowns.</summary>
    public class HistoryForm : Form
    {
        readonly AppData data;
        readonly Dictionary<string, ListBox> lists = new Dictionary<string, ListBox>();
        Button okButton;

        public HistoryForm(AppData data)
        {
            this.data = data;
            SuspendLayout(); // before SetupDialog: controls added below must be DPI-scaled with the form
            Ui.SetupDialog(this, "Dropdown history");
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(520, 460);
            Size = new Size(560, 520);

            // Toggle buttons instead of a TabControl: Windows tab headers can't be themed dark.
            var tabRow = Ui.Flow();
            tabRow.Dock = DockStyle.Top;
            tabRow.Padding = new Padding(10, 0, 10, 4);
            var pages = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0) };
            var titles = new[] { "Projects", "Locations", "Customers", "Names" };
            var sources = new[] { data.Projects, data.Locations, data.Customers, data.Names };
            for (int i = 0; i < titles.Length; i++)
            {
                var page = Page(titles[i], sources[i]);
                page.Visible = i == 0;
                pages.Controls.Add(page);
                var tab = Theme.SetRole(new RadioButton
                {
                    Text = titles[i],
                    Appearance = Appearance.Button,
                    AutoSize = true,
                    Checked = i == 0,
                    Padding = new Padding(10, 2, 10, 2),
                    Margin = new Padding(0, 0, 4, 0),
                    TextAlign = ContentAlignment.MiddleCenter
                }, ThemeRole.Tab);
                tab.CheckedChanged += (s, e) => { if (((RadioButton)s).Checked) ShowPage(pages, page); };
                tabRow.Controls.Add(tab);
            }

            var top = Theme.SetRole(new Label
            {
                Text = "Top of the list shows first in the dropdown. Removing something here doesn't touch your saved entries.",
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 44,
                Padding = new Padding(10, 8, 10, 0)
            }, ThemeRole.Muted);

            Button ok;
            var buttons = Ui.DialogButtons(this, out ok);
            okButton = ok;
            ok.Click += (s, e) => { Apply(); DialogResult = DialogResult.OK; };
            var rebuild = Ui.Button("Add everything from my entries", (s, e) => Rebuild());
            buttons.Controls.Add(rebuild);
            rebuild.Margin = new Padding(3, 3, 60, 3);

            Controls.Add(pages);
            Controls.Add(tabRow);
            Controls.Add(top);
            Controls.Add(buttons);
            ResumeLayout(false);
            PerformLayout();
        }

        static void ShowPage(Panel pages, Control page)
        {
            foreach (Control c in pages.Controls) c.Visible = c == page;
        }

        Control Page(string title, List<string> items)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 6, 0, 0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended };
            list.Items.AddRange(items.Cast<object>().ToArray());
            lists[title] = list;

            var input = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 3) };
            var add = Ui.Button("Add", (s, e) =>
            {
                string v = input.Text.Trim();
                if (v.Length == 0) return;
                var existing = list.Items.Cast<string>().FirstOrDefault(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase));
                if (existing != null) list.Items.Remove(existing);
                list.Items.Insert(0, v);
                input.Clear();
                input.Focus();
            });
            input.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { add.PerformClick(); e.SuppressKeyPress = true; } };
            input.Enter += (s, e) => AcceptButton = add;
            input.Leave += (s, e) => AcceptButton = okButton;

            var side = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            side.Controls.Add(Wide(Ui.Button("Remove", (s, e) => Remove(list))));
            side.Controls.Add(Wide(Ui.Button("Move up", (s, e) => MoveItem(list, -1))));
            side.Controls.Add(Wide(Ui.Button("Move down", (s, e) => MoveItem(list, 1))));
            side.Controls.Add(Wide(Ui.Button("Sort A\u2013Z", (s, e) => Sort(list))));
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) Remove(list); };

            t.Controls.Add(input, 0, 0);
            t.Controls.Add(add, 1, 0);
            t.Controls.Add(list, 0, 1);
            t.Controls.Add(side, 1, 1);
            return t;
        }

        static Button Wide(Button b)
        {
            b.MinimumSize = new Size(110, 0);
            return b;
        }

        static void Remove(ListBox list)
        {
            var selected = list.SelectedItems.Cast<object>().ToList();
            if (selected.Count == 0) return;
            int index = list.SelectedIndex;
            foreach (var s in selected) list.Items.Remove(s);
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(index, list.Items.Count - 1);
        }

        static void MoveItem(ListBox list, int direction)
        {
            int i = list.SelectedIndex;
            int j = i + direction;
            if (i < 0 || j < 0 || j >= list.Items.Count) return;
            var item = list.Items[i];
            list.Items.RemoveAt(i);
            list.Items.Insert(j, item);
            list.ClearSelected();
            list.SelectedIndex = j;
        }

        static void Sort(ListBox list)
        {
            var items = list.Items.Cast<string>().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();
            list.Items.Clear();
            list.Items.AddRange(items.Cast<object>().ToArray());
        }

        /// <summary>Adds every project/location/customer/name ever used, keeping the current order on top.</summary>
        void Rebuild()
        {
            var entries = data.Entries.OrderByDescending(e => e.Date).ToList();
            AddMissing("Projects", entries.Select(e => e.Project));
            AddMissing("Locations", entries.Select(e => e.Location));
            AddMissing("Customers", data.Headers.OrderByDescending(h => h.Start).Select(h => h.Customer));
            AddMissing("Names", data.Headers.OrderByDescending(h => h.Start).Select(h => h.Name));
        }

        void AddMissing(string title, IEnumerable<string> values)
        {
            var list = lists[title];
            foreach (var v in values)
            {
                string value = (v ?? "").Trim();
                if (value.Length == 0) continue;
                if (list.Items.Cast<string>().Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))) continue;
                list.Items.Add(value);
            }
        }

        void Apply()
        {
            Copy("Projects", data.Projects);
            Copy("Locations", data.Locations);
            Copy("Customers", data.Customers);
            Copy("Names", data.Names);
        }

        void Copy(string title, List<string> into)
        {
            into.Clear();
            foreach (string s in lists[title].Items) History.AddIfMissing(into, s);
        }
    }
}
