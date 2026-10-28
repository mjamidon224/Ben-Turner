using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace TATimesheet
{
    /// <summary>
    /// The main window: pay period at the top, the entry editor, the period's days in a grid,
    /// totals and export buttons at the bottom. Every change is saved immediately.
    /// </summary>
    public partial class MainForm : Form
    {
        readonly DataStore store;
        AppData data;
        Period period;
        TimeEntry editing;     // entry loaded in the editor, or null when adding a new one
        int suppress;          // > 0 while code (not the user) is filling in controls
        bool jumpDropped;

        Label lblPeriod;
        DateTimePicker dtpJump;
        ComboBox cboName, cboCustomer;

        Label lblEditorTitle;
        DateTimePicker dtpDate;
        ComboBox cboProject, cboLocation, cboStart, cboEnd;
        TextBox txtHours, txtNotes;
        Button btnSave, btnNew, btnDelete;
        Label lblMode, lblHint;

        DataGridView grid;
        Label lblWeeks, lblTotal;
        ToolStripStatusLabel lblStatus, lblFile;
        readonly ToolTip tips = new ToolTip();
        Font boldFont, italicFont, totalFont;

        public MainForm(DataStore store, AppData data)
        {
            this.store = store;
            this.data = data;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Ui.BaseFont();
            boldFont = new Font(Font, FontStyle.Bold);
            italicFont = new Font(Font, FontStyle.Italic);
            totalFont = new Font("Segoe UI Semibold", 9F);
            Text = BuildInfo.IsTest ? "TA Timesheet \u2013 TEST COPY (practice data)" : "TA Timesheet";
            Icon = AppResources.AppIcon();
            MinimumSize = new Size(900, 560);
            Size = new Size(1200, 800);
            StartPosition = FormStartPosition.CenterScreen;
            Load += (s, e) =>
            {
                // On a small or zoomed-in laptop screen the default size may not fit: just fill the screen.
                var area = Screen.FromControl(this).WorkingArea;
                if (WindowState == FormWindowState.Normal && (Width > area.Width || Height > area.Height))
                    WindowState = FormWindowState.Maximized;
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12, 8, 12, 6)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildEditor(), 0, 1);
            root.Controls.Add(BuildGrid(), 0, 2);
            root.Controls.Add(BuildFooter(), 0, 3);

            Controls.Add(root);
            Controls.Add(BuildStatusBar());
            if (BuildInfo.IsTest)
            {
                Controls.Add(new Label
                {
                    Text = "TEST COPY \u2013 practice data, kept separate from the real app. Break whatever you want. File > Reset test data starts over.",
                    Dock = DockStyle.Top,
                    AutoSize = false,
                    Height = (int)(Font.Height * 1.9),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.FromArgb(255, 196, 0),
                    ForeColor = Color.Black,
                    Font = boldFont
                });
            }
            var menu = BuildMenu();
            Controls.Add(menu);
            MainMenuStrip = menu;
            ResumeLayout(false);
            PerformLayout();

            AcceptButton = btnSave;
            CancelButton = btnNew;
            RestoreWindowBounds();

            period = Period.Containing(DateTime.Today, data.Settings);
            LoadPeriod(true);
            Shown += OnFirstShown;
            FormClosing += OnClosing;
        }

        // ---- Layout ---------------------------------------------------------------------------

        Control BuildHeader()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 6) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var left = Ui.Flow();
            var prev = Ui.Button("\u25C0", (s, e) => MovePeriod(-1));
            var next = Ui.Button("\u25B6", (s, e) => MovePeriod(1));
            prev.Anchor = next.Anchor = AnchorStyles.Left;
            tips.SetToolTip(prev, "Previous pay period (Ctrl+PgUp)");
            tips.SetToolTip(next, "Next pay period (Ctrl+PgDn)");
            lblPeriod = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 14F),
                ForeColor = Ui.Slate,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(6, 0, 6, 0)
            };
            var today = Ui.Button("This period", (s, e) => GoToPeriod(Period.Containing(DateTime.Today, data.Settings)));
            today.Anchor = AnchorStyles.Left;
            tips.SetToolTip(today, "Jump back to the pay period with today in it (Ctrl+T)");

            dtpJump = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 120, Anchor = AnchorStyles.Left, Margin = new Padding(10, 3, 3, 3) };
            tips.SetToolTip(dtpJump, "Jump to the pay period containing this date");
            dtpJump.DropDown += (s, e) => jumpDropped = true;
            dtpJump.CloseUp += (s, e) => { jumpDropped = false; JumpToPicked(); };
            dtpJump.ValueChanged += (s, e) => { if (!jumpDropped) JumpToPicked(); };

            left.Controls.AddRange(new Control[] { prev, lblPeriod, next, today, Ui.InlineLabel("Go to"), dtpJump });

            var right = Ui.Flow();
            cboName = Ui.HistoryCombo();
            cboCustomer = Ui.HistoryCombo();
            cboName.Dock = cboCustomer.Dock = DockStyle.None;
            cboName.Anchor = cboCustomer.Anchor = AnchorStyles.Left;
            cboName.Width = 190;
            cboCustomer.Width = 230;
            cboName.Margin = cboCustomer.Margin = new Padding(3);
            foreach (var c in new[] { cboName, cboCustomer })
            {
                c.Leave += (s, e) => CommitHeader();
                c.SelectionChangeCommitted += (s, e) => BeginInvoke(new Action(CommitHeader));
            }
            tips.SetToolTip(cboName, "Printed on this pay period's timesheet");
            tips.SetToolTip(cboCustomer, "Printed on this pay period's timesheet");
            right.Controls.AddRange(new Control[] { Ui.InlineLabel("Name"), cboName, Ui.InlineLabel("Customer"), cboCustomer });

            t.Controls.Add(left, 0, 0);
            t.Controls.Add(right, 2, 0);
            return t;
        }

        Control BuildEditor()
        {
            // A white "card" holding the editor; plain TableLayoutPanels autosize reliably.
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 7,
                RowCount = 4,
                BackColor = Color.White,
                Padding = new Padding(10, 8, 10, 8),
                Margin = new Padding(0, 0, 0, 10)
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            for (int i = 0; i < 4; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            lblEditorTitle = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 10.5F),
                ForeColor = Ui.Slate,
                Margin = new Padding(3, 0, 3, 2)
            };
            t.Controls.Add(lblEditorTitle, 0, 0);
            t.SetColumnSpan(lblEditorTitle, 7);

            string[] labels = { "Date", "Project", "Location", "Start", "End", "Hours", "Notes (just for you, not printed)" };
            for (int i = 0; i < labels.Length; i++) t.Controls.Add(Ui.FieldLabel(labels[i]), i, 1);

            dtpDate = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "ddd M/d/yyyy",
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 3, 3)
            };
            dtpDate.ValueChanged += (s, e) => { if (suppress == 0) { UpdateMode(); HighlightEditorRow(); } };

            cboProject = Ui.HistoryCombo();
            cboLocation = Ui.HistoryCombo();
            cboProject.SelectionChangeCommitted += (s, e) => BeginInvoke(new Action(() => FillLocationForProject(true)));
            cboProject.Leave += (s, e) => FillLocationForProject(false);

            cboStart = TimeCombo();
            cboEnd = TimeCombo();
            tips.SetToolTip(cboStart, "Pick or type: 6, 0630, 6:30, 6:30p ...");
            tips.SetToolTip(cboEnd, "Earlier than the start time = shift ran past midnight");

            // Not a Tab stop: hours fill themselves in, so Tab goes End -> Notes. Click it to override.
            txtHours = new TextBox { Dock = DockStyle.Fill, TextAlign = HorizontalAlignment.Right, Margin = new Padding(3, 1, 3, 3), TabStop = false };
            txtHours.TextChanged += (s, e) => UpdateHint();
            tips.SetToolTip(txtHours, "Filled in from Start/End. Type over it if you bill something different.");
            txtNotes = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3, 1, 3, 3) };

            t.Controls.Add(dtpDate, 0, 2);
            t.Controls.Add(cboProject, 1, 2);
            t.Controls.Add(cboLocation, 2, 2);
            t.Controls.Add(cboStart, 3, 2);
            t.Controls.Add(cboEnd, 4, 2);
            t.Controls.Add(txtHours, 5, 2);
            t.Controls.Add(txtNotes, 6, 2);

            var buttons = Ui.Flow();
            buttons.Dock = DockStyle.Fill;
            buttons.Margin = new Padding(0, 4, 0, 0);
            btnSave = Ui.PrimaryButton("Add entry", (s, e) => SaveEntry());
            btnSave.MinimumSize = new Size(110, 0);
            btnNew = Ui.Button("New", (s, e) => NewEntry(dtpDate.Value.Date));
            btnDelete = Ui.Button("Delete", (s, e) => DeleteEntry());
            var copyLast = Ui.Button("Copy last entry", (s, e) => CopyLastEntry());
            tips.SetToolTip(btnSave, "Enter");
            tips.SetToolTip(btnNew, "Esc \u2013 stop editing and start a new entry (keeps what you typed as a starting point)");
            tips.SetToolTip(copyLast, "Fill in project, location and times from your previous entry (Ctrl+L)");
            lblMode = new Label { AutoSize = true, ForeColor = Ui.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(12, 0, 3, 0) };
            lblHint = new Label { AutoSize = true, ForeColor = Ui.WarnText, Anchor = AnchorStyles.Left, Margin = new Padding(12, 0, 3, 0) };
            buttons.Controls.AddRange(new Control[] { btnSave, btnNew, btnDelete, copyLast, lblMode, lblHint });
            t.Controls.Add(buttons, 0, 3);
            t.SetColumnSpan(buttons, 7);
            return t;
        }

        ComboBox TimeCombo()
        {
            var c = Ui.HistoryCombo();
            c.MaxDropDownItems = 14;
            c.DrawMode = DrawMode.OwnerDrawFixed;
            c.DrawItem += DrawTimeItem;
            c.Tag = 0; // how many "recent" times sit at the top of the list
            c.TextChanged += (s, e) => { if (suppress == 0) AutoHours(); };
            c.Leave += (s, e) =>
            {
                int m;
                string t = c.Text.Trim();
                c.BackColor = t.Length == 0 || TimeUtil.TryParseTime(t, out m) ? SystemColors.Window : Ui.WarnTint;
                string norm = TimeUtil.NormalizeTime(t);
                if (norm != c.Text) c.Text = norm;
            };
            return c;
        }

        /// <summary>Your most-used start/end times on top, then every time of day in steps.</summary>
        void FillTimeCombos()
        {
            var recent = data.Entries.OrderByDescending(e => e.Date).Take(60).ToList();
            FillTimes(cboStart, recent.Select(e => e.Start));
            FillTimes(cboEnd, recent.Select(e => e.End));
        }

        void FillTimes(ComboBox c, IEnumerable<string> used)
        {
            var favourites = used.Where(t => t.Length > 0)
                                 .GroupBy(t => t).OrderByDescending(g => g.Count()).Take(4)
                                 .Select(g => g.Key).OrderBy(t => t, StringComparer.Ordinal).ToList();
            int step = Math.Max(1, data.Settings.TimeStepMinutes);
            var items = new List<string>(favourites);
            for (int m = 0; m < 1440; m += step) items.Add(TimeUtil.FormatTime(m));
            c.Tag = favourites.Count;
            Ui.Fill(c, items);
        }

        void DrawTimeItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var c = (ComboBox)sender;
            int favourites = c.Tag is int ? (int)c.Tag : 0;
            bool favourite = e.Index < favourites;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            e.DrawBackground();
            TextRenderer.DrawText(e.Graphics, (string)c.Items[e.Index], favourite ? boldFont : e.Font,
                new Rectangle(e.Bounds.X + 3, e.Bounds.Y, e.Bounds.Width - 3, e.Bounds.Height),
                selected ? SystemColors.HighlightText : (favourite ? Ui.Green : SystemColors.ControlText),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (favourite && e.Index == favourites - 1)
                using (var pen = new Pen(Color.Silver))
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        Control BuildGrid()
        {
            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToOrderColumns = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(226, 229, 232),
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                StandardTab = true,
                ShowCellToolTips = true,
                Margin = new Padding(0)
            };
            grid.ColumnHeadersHeight = (int)(Font.Height * 2.1);
            grid.RowTemplate.Height = (int)(Font.Height * 1.9);
            var head = grid.ColumnHeadersDefaultCellStyle;
            head.BackColor = Ui.Slate;
            head.ForeColor = Color.White;
            head.SelectionBackColor = Ui.Slate;
            head.Font = new Font("Segoe UI Semibold", 9F);
            head.Padding = new Padding(4, 0, 4, 0);
            grid.DefaultCellStyle.SelectionBackColor = Ui.Selection;
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);

            AddColumn("Day", 6, false);
            AddColumn("Date", 9, false);
            AddColumn("Project", 22, false);
            AddColumn("Location", 16, false);
            AddColumn("Start", 7, true);
            AddColumn("End", 7, true);
            AddColumn("Hours", 7, true);
            AddColumn("Notes", 26, false);

            // React to clicks and arrow keys only: the grid also moves its selection on its own
            // (e.g. when it first shows), and that must not replace what's in the editor.
            grid.CellClick += (s, e) => { if (e.RowIndex >= 0) OnGridSelection(); };
            grid.KeyUp += (s, e) =>
            {
                switch (e.KeyCode)
                {
                    case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                        OnGridSelection();
                        break;
                }
            };
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) { cboStart.Focus(); cboStart.SelectAll(); } };
            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && editing != null) { DeleteEntry(); e.Handled = true; }
            };
            grid.CellMouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
                    grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
            };

            var menu = new ContextMenuStrip();
            var miDup = new ToolStripMenuItem("Copy to next day", null, (s, e) => DuplicateToNextDay());
            var miDel = new ToolStripMenuItem("Delete entry", null, (s, e) => DeleteEntry());
            menu.Items.Add(miDup);
            menu.Items.Add(miDel);
            menu.Opening += (s, e) =>
            {
                var row = grid.CurrentRow;
                bool isEntry = row != null && row.Tag is TimeEntry;
                miDup.Enabled = miDel.Enabled = isEntry;
                if (!isEntry) e.Cancel = true;
            };
            grid.ContextMenuStrip = menu;
            return grid;
        }

        void AddColumn(string name, float weight, bool center)
        {
            var c = new DataGridViewTextBoxColumn { HeaderText = name, Name = name, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable };
            if (center)
            {
                c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            grid.Columns.Add(c);
        }

        Control BuildFooter()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var totals = Ui.Flow();
            totals.Anchor = AnchorStyles.Left;
            lblTotal = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 15F), ForeColor = Ui.Green, Anchor = AnchorStyles.Left };
            lblWeeks = new Label { AutoSize = true, Font = new Font("Segoe UI", 10F), ForeColor = Ui.Slate, Anchor = AnchorStyles.Left, Margin = new Padding(12, 0, 3, 0) };
            totals.Controls.Add(lblTotal);
            totals.Controls.Add(lblWeeks);

            var exports = Ui.Flow();
            exports.Anchor = AnchorStyles.Right;
            exports.Controls.Add(new Label { Text = "Export:", AutoSize = true, ForeColor = Ui.Slate, Anchor = AnchorStyles.Left });
            var word = Ui.PrimaryButton("Word timesheet", (s, e) => ExportWord());
            var pdf = Ui.PrimaryButton("PDF", (s, e) => ExportPdf());
            var excel = Ui.Button("Excel", (s, e) => ExportData(true));
            var csv = Ui.Button("CSV", (s, e) => ExportData(false));
            var print = Ui.Button("Print...", (s, e) => PrintPreview());
            tips.SetToolTip(word, "Your customer timesheet as a .docx, same layout as before (Ctrl+E)");
            tips.SetToolTip(pdf, "The same timesheet as a PDF, ready to email (Ctrl+Shift+E)");
            tips.SetToolTip(excel, "Every entry in a date range, plus totals by project and week");
            tips.SetToolTip(csv, "Every entry in a date range as plain CSV");
            tips.SetToolTip(print, "Preview and print this pay period (Ctrl+P)");
            exports.Controls.AddRange(new Control[] { word, pdf, excel, csv, print });

            t.Controls.Add(totals, 0, 0);
            t.Controls.Add(exports, 1, 0);
            return t;
        }

        StatusStrip BuildStatusBar()
        {
            var bar = new StatusStrip { SizingGrip = true };
            lblStatus = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            lblFile = new ToolStripStatusLabel { IsLink = true, Text = "Data folder", ToolTipText = store.FilePath };
            lblFile.Click += (s, e) => OpenDataFolder();
            bar.Items.Add(lblStatus);
            bar.Items.Add(lblFile);
            return bar;
        }

        MenuStrip BuildMenu()
        {
            var m = new MenuStrip();

            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(Item("&Import Word timesheets (.docx)...", Keys.None, ImportDocx));
            file.DropDownItems.Add(Item("Import &CSV...", Keys.None, ImportCsv));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("&Back up data to...", Keys.None, BackupData));
            file.DropDownItems.Add(Item("&Restore from backup...", Keys.None, RestoreData));
            file.DropDownItems.Add(Item("&Open data folder", Keys.None, OpenDataFolder));
            if (BuildInfo.IsTest) file.DropDownItems.Add(Item("Reset &test data", Keys.None, ResetTestData));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("E&xit", Keys.Alt | Keys.F4, Close));

            var entry = new ToolStripMenuItem("&Entry");
            entry.DropDownItems.Add(Item("&Save entry", Keys.Control | Keys.S, SaveEntry));
            entry.DropDownItems.Add(Item("&New entry", Keys.Control | Keys.N, () => NewEntry(dtpDate.Value.Date)));
            entry.DropDownItems.Add(Item("Copy &last entry", Keys.Control | Keys.L, CopyLastEntry));
            entry.DropDownItems.Add(Item("Copy to next &day", Keys.Control | Keys.Shift | Keys.D, DuplicateToNextDay));
            entry.DropDownItems.Add(Item("&Delete entry", Keys.None, DeleteEntry));

            var per = new ToolStripMenuItem("&Period");
            per.DropDownItems.Add(Item("&Previous pay period", Keys.Control | Keys.PageUp, () => MovePeriod(-1)));
            per.DropDownItems.Add(Item("&Next pay period", Keys.Control | Keys.PageDown, () => MovePeriod(1)));
            per.DropDownItems.Add(Item("&This pay period", Keys.Control | Keys.T, () => GoToPeriod(Period.Containing(DateTime.Today, data.Settings))));

            var export = new ToolStripMenuItem("E&xport");
            export.DropDownItems.Add(Item("&Word timesheet (.docx)...", Keys.Control | Keys.E, ExportWord));
            export.DropDownItems.Add(Item("&PDF timesheet...", Keys.Control | Keys.Shift | Keys.E, ExportPdf));
            export.DropDownItems.Add(new ToolStripSeparator());
            export.DropDownItems.Add(Item("&Excel workbook (.xlsx)...", Keys.None, () => ExportData(true)));
            export.DropDownItems.Add(Item("&CSV file...", Keys.None, () => ExportData(false)));
            export.DropDownItems.Add(new ToolStripSeparator());
            export.DropDownItems.Add(Item("Print pre&view...", Keys.Control | Keys.P, PrintPreview));
            export.DropDownItems.Add(Item("P&rint...", Keys.None, PrintNow));

            var tools = new ToolStripMenuItem("&Tools");
            tools.DropDownItems.Add(Item("Manage &dropdown history...", Keys.Control | Keys.H, ManageHistory));
            tools.DropDownItems.Add(Item("&Settings...", Keys.None, ShowSettings));

            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(Item("&How to use", Keys.F1, ShowHelp));
            help.DropDownItems.Add(Item("&About", Keys.None, ShowAbout));

            m.Items.AddRange(new ToolStripItem[] { file, entry, per, export, tools, help });
            return m;
        }

        static ToolStripMenuItem Item(string text, Keys keys, Action action)
        {
            var i = new ToolStripMenuItem(text, null, (s, e) => action());
            if (keys != Keys.None)
            {
                if (keys == (Keys.Alt | Keys.F4)) i.ShortcutKeyDisplayString = "Alt+F4";
                else i.ShortcutKeys = keys;
            }
            return i;
        }

        // ---- Period ---------------------------------------------------------------------------

        static string PeriodTitle(Period p)
        {
            var c = CultureInfo.CurrentCulture;
            string from = p.Start.Year == p.End.Year ? p.Start.ToString("MMM d", c) : p.Start.ToString("MMM d, yyyy", c);
            return from + " \u2013 " + p.End.ToString("MMM d, yyyy", c);
        }

        void LoadPeriod(bool resetEditor)
        {
            suppress++;
            try
            {
                lblPeriod.Text = PeriodTitle(period);
                if (dtpJump.Value.Date < period.Start || dtpJump.Value.Date > period.End)
                    dtpJump.Value = period.Contains(DateTime.Today) ? DateTime.Today : period.Start;
                var h = data.HeaderFor(period) ?? data.DefaultHeaderFor(period);
                Ui.Fill(cboName, data.Names);
                Ui.Fill(cboCustomer, data.Customers);
                cboName.Text = h.Name;
                cboCustomer.Text = h.Customer;
                FillEditorCombos();
                RefreshGrid();
            }
            finally
            {
                suppress--;
            }
            if (resetEditor) NewEntry(SuggestedDate());
        }

        void MovePeriod(int direction)
        {
            GoToPeriod(direction < 0 ? period.Previous : period.Next);
        }

        void GoToPeriod(Period p)
        {
            if (p.Start == period.Start && p.Weeks == period.Weeks) return;
            period = p;
            LoadPeriod(true);
        }

        void JumpToPicked()
        {
            if (suppress > 0) return;
            GoToPeriod(Period.Containing(dtpJump.Value, data.Settings));
        }

        /// <summary>First day of the period with nothing logged yet, preferring days up to today.</summary>
        DateTime SuggestedDate()
        {
            bool current = period.Contains(DateTime.Today);
            DateTime last = current ? DateTime.Today : period.End;
            for (var d = period.Start; d <= last; d = d.AddDays(1))
                if (!data.Entries.Any(e => e.Date == d)) return d;
            for (var d = last.AddDays(1); d <= period.End; d = d.AddDays(1))
                if (!data.Entries.Any(e => e.Date == d)) return d;
            return current ? DateTime.Today : period.Start;
        }

        DateTime NextEmptyDay(DateTime after)
        {
            for (var d = after.AddDays(1); d <= period.End; d = d.AddDays(1))
                if (!data.Entries.Any(e => e.Date == d)) return d;
            return after.AddDays(1);
        }

        void CommitHeader()
        {
            if (suppress > 0) return;
            string name = cboName.Text.Trim(), customer = cboCustomer.Text.Trim();
            var h = data.HeaderFor(period);
            if (h != null && h.Name == name && h.Customer == customer) return;
            if (h == null)
            {
                h = new PeriodHeader { Start = period.Start };
                data.Headers.Add(h);
            }
            h.Name = name;
            h.Customer = customer;
            History.Touch(data.Names, name);
            History.Touch(data.Customers, customer);
            if (data.Settings.DefaultName.Length == 0) data.Settings.DefaultName = name;
            Persist("Saved name and customer for this pay period");
        }

        // ---- Grid -----------------------------------------------------------------------------

        void RefreshGrid()
        {
            suppress++;
            try
            {
                grid.SuspendLayout();
                grid.Rows.Clear();
                var entries = data.Entries.Where(e => period.Contains(e.Date)).ToList();
                var weekTotals = new List<double>();
                for (int w = 0; w < period.Weeks; w++)
                {
                    double total = 0;
                    for (int d = 0; d < 7; d++)
                    {
                        DateTime date = period.WeekStart(w).AddDays(d);
                        var day = entries.Where(e => e.Date == date).OrderBy(e => e.SortMinutes).ThenBy(e => e.Project).ToList();
                        if (day.Count == 0) AddEmptyDayRow(date);
                        foreach (var e in day)
                        {
                            AddEntryRow(e);
                            total += e.Hours ?? 0;
                        }
                    }
                    total = Math.Round(total, 2);
                    weekTotals.Add(total);
                    AddTotalRow(w + 1, total);
                }
                grid.ClearSelection();
                grid.CurrentCell = null;

                double all = Math.Round(weekTotals.Sum(), 2);
                lblTotal.Text = "Total " + TimeUtil.FormatHours(all) + " h";
                lblWeeks.Text = period.Weeks == 1 ? "" : string.Join("     ",
                    weekTotals.Select((t, i) => string.Format("Week {0}: {1} h", i + 1, TimeUtil.FormatHours(t))));
            }
            finally
            {
                grid.ResumeLayout();
                suppress--;
            }
        }

        void AddEntryRow(TimeEntry e)
        {
            int i = grid.Rows.Add(e.Date.ToString("ddd", CultureInfo.CurrentCulture), TimeUtil.FormatDate(e.Date),
                e.Project, e.Location, e.Start, e.End, TimeUtil.FormatHours(e.Hours), e.Notes);
            var row = grid.Rows[i];
            row.Tag = e;
            if (e.Date == DateTime.Today) row.Cells[1].Style.Font = boldFont;
            if (e.Date.DayOfWeek == DayOfWeek.Saturday || e.Date.DayOfWeek == DayOfWeek.Sunday)
                row.DefaultCellStyle.BackColor = Ui.WeekendTint;
            if (e.HoursMismatch)
            {
                var c = row.Cells["Hours"];
                c.Style.BackColor = Ui.WarnTint;
                c.ToolTipText = string.Format("{0}\u2013{1} works out to {2} h, but {3} h is entered.",
                    e.Start, e.End, TimeUtil.FormatHours(e.ComputedHours), TimeUtil.FormatHours(e.Hours));
            }
        }

        void AddEmptyDayRow(DateTime date)
        {
            int i = grid.Rows.Add(date.ToString("ddd", CultureInfo.CurrentCulture), TimeUtil.FormatDate(date), "click to add", "", "", "", "", "");
            var row = grid.Rows[i];
            row.Tag = date;
            row.DefaultCellStyle.ForeColor = Ui.Muted;
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                row.DefaultCellStyle.BackColor = Ui.WeekendTint;
            row.Cells["Project"].Style.Font = italicFont;
            row.Cells["Project"].Style.ForeColor = Color.FromArgb(170, 176, 181);
            if (date == DateTime.Today) row.Cells[1].Style.Font = boldFont;
        }

        void AddTotalRow(int week, double hours)
        {
            int i = grid.Rows.Add("", "", period.Weeks == 1 ? "Week total" : "Week " + week + " total", "", "", "", TimeUtil.FormatHours(hours), "");
            var row = grid.Rows[i];
            row.Tag = "total";
            row.DefaultCellStyle.BackColor = Ui.GreenTint;
            row.DefaultCellStyle.SelectionBackColor = Ui.GreenTint;
            row.DefaultCellStyle.Font = totalFont;
        }

        void OnGridSelection()
        {
            if (suppress > 0) return;
            var row = grid.CurrentRow;
            if (row == null) return;
            var e = row.Tag as TimeEntry;
            if (e != null) { LoadEntry(e); return; }
            if (row.Tag is DateTime) NewEntry((DateTime)row.Tag);
        }

        /// <summary>Highlights the grid row that matches what's in the editor.</summary>
        void HighlightEditorRow()
        {
            suppress++;
            try
            {
                grid.ClearSelection();
                DataGridViewRow match = null;
                foreach (DataGridViewRow r in grid.Rows)
                {
                    if (editing != null ? r.Tag == editing : (r.Tag is DateTime && (DateTime)r.Tag == dtpDate.Value.Date))
                    {
                        match = r;
                        break;
                    }
                }
                if (match != null)
                {
                    grid.CurrentCell = match.Cells[1];
                    match.Selected = true;
                }
                else
                {
                    grid.CurrentCell = null;
                }
            }
            finally
            {
                suppress--;
            }
        }

        // ---- Editor ---------------------------------------------------------------------------

        void FillEditorCombos()
        {
            Ui.Fill(cboProject, data.Projects);
            Ui.Fill(cboLocation, data.Locations);
            FillTimeCombos();
        }

        void NewEntry(DateTime date)
        {
            editing = null;
            suppress++;
            try
            {
                dtpDate.Value = date;
                if (cboProject.Text.Trim().Length == 0 && cboStart.Text.Trim().Length == 0)
                {
                    var last = data.LastEntryBefore(date) ?? data.Entries.OrderByDescending(e => e.Date).FirstOrDefault();
                    if (last != null)
                    {
                        cboProject.Text = last.Project;
                        cboLocation.Text = last.Location;
                        cboStart.Text = last.Start;
                        cboEnd.Text = last.End;
                    }
                    else
                    {
                        cboProject.Text = data.Projects.FirstOrDefault() ?? "";
                        cboLocation.Text = data.Locations.FirstOrDefault() ?? "";
                    }
                }
                txtNotes.Text = "";
                txtHours.Text = TimeUtil.FormatHours(TimeUtil.ComputeHours(cboStart.Text, cboEnd.Text));
                cboStart.BackColor = cboEnd.BackColor = SystemColors.Window;
            }
            finally
            {
                suppress--;
            }
            UpdateMode();
            UpdateHint();
            HighlightEditorRow();
        }

        void LoadEntry(TimeEntry e)
        {
            editing = e;
            suppress++;
            try
            {
                dtpDate.Value = e.Date;
                cboProject.Text = e.Project;
                cboLocation.Text = e.Location;
                cboStart.Text = e.Start;
                cboEnd.Text = e.End;
                txtHours.Text = TimeUtil.FormatHours(e.Hours);
                txtNotes.Text = e.Notes;
                cboStart.BackColor = cboEnd.BackColor = SystemColors.Window;
            }
            finally
            {
                suppress--;
            }
            UpdateMode();
            UpdateHint();
            HighlightEditorRow();
        }

        void UpdateMode()
        {
            DateTime d = dtpDate.Value.Date;
            string day = d.ToString("dddd M/d", CultureInfo.CurrentCulture);
            if (editing == null)
            {
                lblEditorTitle.Text = "New entry";
                btnSave.Text = "Add entry";
                btnDelete.Enabled = false;
                lblMode.Text = "Adding " + day + (period.Contains(d) ? "" : " (different pay period)") + ".  Enter saves.";
            }
            else
            {
                lblEditorTitle.Text = "Editing entry";
                btnSave.Text = "Save changes";
                btnDelete.Enabled = true;
                lblMode.Text = "Editing " + day + ".  Esc to stop editing.";
            }
        }

        void AutoHours()
        {
            var h = TimeUtil.ComputeHours(cboStart.Text, cboEnd.Text);
            if (!h.HasValue) return;
            suppress++;
            txtHours.Text = TimeUtil.FormatHours(h);
            suppress--;
            UpdateHint();
        }

        void UpdateHint()
        {
            var computed = TimeUtil.ComputeHours(cboStart.Text, cboEnd.Text);
            double typed;
            if (computed.HasValue && TimeUtil.TryParseHours(txtHours.Text, out typed) && Math.Abs(typed - computed.Value) > 0.005)
                lblHint.Text = string.Format("Heads up: {0}\u2013{1} is {2} h", TimeUtil.NormalizeTime(cboStart.Text),
                    TimeUtil.NormalizeTime(cboEnd.Text), TimeUtil.FormatHours(computed));
            else
                lblHint.Text = "";
        }

        void FillLocationForProject(bool fromDropdown)
        {
            if (suppress > 0) return;
            string project = cboProject.Text.Trim();
            if (project.Length == 0) return;
            if (!fromDropdown && cboLocation.Text.Trim().Length > 0) return;
            var loc = data.LastLocationFor(project);
            if (loc != null) cboLocation.Text = loc;
        }

        void CopyLastEntry()
        {
            var last = data.LastEntryBefore(dtpDate.Value.Date) ?? data.Entries.OrderByDescending(e => e.Date).FirstOrDefault();
            if (last == null) { lblStatus.Text = "No earlier entry to copy."; return; }
            cboProject.Text = last.Project;
            cboLocation.Text = last.Location;
            cboStart.Text = last.Start;
            cboEnd.Text = last.End;
            txtHours.Text = TimeUtil.FormatHours(last.ComputedHours ?? last.Hours);
            lblStatus.Text = "Copied from " + last.Date.ToString("ddd M/d", CultureInfo.CurrentCulture) + ".";
        }

        static bool Overlaps(TimeEntry a, TimeEntry b)
        {
            int a1, a2, b1, b2;
            if (TimeUtil.TryParseTime(a.Start, out a1) && TimeUtil.TryParseTime(a.End, out a2)
                && TimeUtil.TryParseTime(b.Start, out b1) && TimeUtil.TryParseTime(b.End, out b2))
            {
                if (a2 <= a1) a2 += 1440;
                if (b2 <= b1) b2 += 1440;
                return a1 < b2 && b1 < a2;
            }
            return string.Equals(a.Project.Trim(), b.Project.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        void SaveEntry()
        {
            string start = cboStart.Text.Trim(), end = cboEnd.Text.Trim();
            int m;
            if (start.Length > 0 && !TimeUtil.TryParseTime(start, out m))
            {
                Problem(cboStart, "\"" + start + "\" isn't a time I can read. Try 7:00, 0700 or 7a.");
                return;
            }
            if (end.Length > 0 && !TimeUtil.TryParseTime(end, out m))
            {
                Problem(cboEnd, "\"" + end + "\" isn't a time I can read. Try 17:30, 1730 or 5:30p.");
                return;
            }
            start = TimeUtil.NormalizeTime(start);
            end = TimeUtil.NormalizeTime(end);

            double? hours;
            string ht = txtHours.Text.Trim();
            if (ht.Length > 0)
            {
                double h;
                if (!TimeUtil.TryParseHours(ht, out h))
                {
                    Problem(txtHours, "Hours has to be a number, like 8 or 7.5.");
                    return;
                }
                hours = Math.Round(h, 2);
            }
            else
            {
                hours = TimeUtil.ComputeHours(start, end);
            }

            var e = new TimeEntry
            {
                Date = dtpDate.Value.Date,
                Project = cboProject.Text.Trim(),
                Location = cboLocation.Text.Trim(),
                Start = start,
                End = end,
                Hours = hours,
                Notes = txtNotes.Text.Trim()
            };
            if (e.IsBlank)
            {
                Problem(cboProject, "There's nothing to save yet. Fill in a project or some times.");
                return;
            }

            if (editing == null)
            {
                var clash = data.Entries.FirstOrDefault(x => x.Date == e.Date && Overlaps(x, e));
                if (clash != null && !Ui.Confirm(this, string.Format(
                        "{0} already has an entry ({1} {2}\u2013{3}).\n\nAdd this one too?",
                        e.Date.ToString("dddd M/d", CultureInfo.CurrentCulture), clash.Project, clash.Start, clash.End),
                        "Possible duplicate"))
                    return;
                data.Entries.Add(e);
            }
            else
            {
                editing.Date = e.Date;
                editing.Project = e.Project;
                editing.Location = e.Location;
                editing.Start = e.Start;
                editing.End = e.End;
                editing.Hours = e.Hours;
                editing.Notes = e.Notes;
                e = editing;
            }

            History.Touch(data.Projects, e.Project);
            History.Touch(data.Locations, e.Location);
            Persist(string.Format("Saved {0}: {1} h", e.Date.ToString("ddd M/d", CultureInfo.CurrentCulture), TimeUtil.FormatHours(e.Hours)));

            ActiveControl = dtpDate; // keep combo item refreshes away from the focused box
            if (!period.Contains(e.Date))
            {
                period = Period.Containing(e.Date, data.Settings);
                LoadPeriod(false);
            }
            else
            {
                FillEditorCombos();
                RefreshGrid();
            }
            NewEntry(NextEmptyDay(e.Date));
            cboStart.Focus();
            cboStart.SelectAll();
        }

        void Problem(Control c, string message)
        {
            c.BackColor = Ui.WarnTint;
            lblStatus.Text = message;
            Ui.Error(this, message);
            c.Focus();
        }

        void DeleteEntry()
        {
            if (editing == null) return;
            var e = editing;
            string what = string.Format("{0}  {1}  {2}\u2013{3}  ({4} h)", e.Date.ToString("ddd M/d/yy", CultureInfo.CurrentCulture),
                e.Project, e.Start, e.End, TimeUtil.FormatHours(e.Hours));
            if (!Ui.Confirm(this, "Delete this entry?\n\n" + what, "Delete entry")) return;
            data.Entries.Remove(e);
            Persist("Deleted " + what);
            RefreshGrid();
            NewEntry(e.Date);
        }

        void DuplicateToNextDay()
        {
            if (editing == null) return;
            var copy = editing.Clone();
            copy.Id = Guid.NewGuid();
            copy.Date = editing.Date.AddDays(1);
            data.Entries.Add(copy);
            Persist("Copied to " + copy.Date.ToString("ddd M/d", CultureInfo.CurrentCulture));
            if (!period.Contains(copy.Date))
            {
                period = Period.Containing(copy.Date, data.Settings);
                LoadPeriod(false);
            }
            else
            {
                RefreshGrid();
            }
            LoadEntry(copy);
            cboStart.Focus();
            cboStart.SelectAll();
        }

        // ---- Saving ---------------------------------------------------------------------------

        bool Persist(string message)
        {
            try
            {
                store.Save(data);
                lblStatus.Text = message + "   \u2713 saved " + DateTime.Now.ToString("h:mm tt", CultureInfo.CurrentCulture);
                return true;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "NOT SAVED: " + ex.Message;
                Ui.Error(this, "Couldn't save your timesheet data:\n\n" + ex.Message + "\n\nFile: " + store.FilePath);
                return false;
            }
        }

        void RestoreWindowBounds()
        {
            var parts = (data.Settings.WindowBounds ?? "").Split(',');
            int x, y, w, h;
            if (parts.Length < 4 || !int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y)
                || !int.TryParse(parts[2], out w) || !int.TryParse(parts[3], out h)) return;
            var rect = new Rectangle(x, y, w, h);
            if (w < 400 || h < 300 || !Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect))) return;
            StartPosition = FormStartPosition.Manual;
            Bounds = rect;
            if (parts.Length > 4 && parts[4] == "max") WindowState = FormWindowState.Maximized;
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            CommitHeader();
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            data.Settings.WindowBounds = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}",
                b.X, b.Y, b.Width, b.Height, WindowState == FormWindowState.Maximized ? "max" : "");
            try { store.Save(data); } catch (Exception) { }
        }

        void OnFirstShown(object sender, EventArgs e)
        {
            HighlightEditorRow();
            cboStart.Focus();
            cboStart.SelectAll();
            if (!store.IsFirstRun) return;
            Persist("Welcome! Your data is saved automatically.");
            if (BuildInfo.IsTest)
            {
                Ui.Info(this,
                    "This is the TEST copy of TA Timesheet.\n\n" +
                    "It keeps its own practice data, completely separate from the real app, and starts with the " +
                    "entries from your 10/26/26 timesheet. Add, edit, delete, export, import \u2013 nothing here touches your real timesheets.\n\n" +
                    "File > Reset test data puts it back the way it started.");
                return;
            }
            if (Ui.Confirm(this,
                    "Welcome to TA Timesheet.\n\nGot old timesheets in Word (TA_Timesheet_*.docx)? " +
                    "Import them now so your entries and dropdowns are filled in.\n\nImport some now?",
                    BuildInfo.AppName))
                ImportDocx();
        }
    }
}
