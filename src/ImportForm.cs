using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TATimesheet
{
    /// <summary>One file found by the folder search, and what reading it produced.</summary>
    public class FoundFile
    {
        public string Path;
        public ImportResult Result;
        public string Error;
        public int NewEntries;
    }

    /// <summary>
    /// Search a folder (and its subfolders) for old timesheets in PDF and Word format, show what
    /// each one contains, and import the ticked ones. Searching runs in the background.
    /// </summary>
    public class ImportForm : Form
    {
        readonly AppData data;
        readonly TextBox txtFolder;
        readonly CheckBox chkSub, chkPdf, chkDocx;
        readonly Button btnSearch, btnImport;
        readonly DataGridView grid;
        readonly Label lblStatus;
        readonly List<FoundFile> found = new List<FoundFile>();
        BackgroundWorker worker;

        /// <summary>Results the user ticked; filled when the dialog closes with OK.</summary>
        public List<FoundFile> Selected { get; private set; }

        public ImportForm(AppData data, string startFolder)
        {
            this.data = data;
            SuspendLayout(); // before SetupDialog: controls added below must be DPI-scaled with the form
            Ui.SetupDialog(this, "Import old timesheets");
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimumSize = new Size(760, 480);
            Size = new Size(940, 620);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12, 10, 12, 4) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(Theme.SetRole(new Label
            {
                Text = "Pick the folder where your old timesheets are. Every PDF and Word file in it (and its subfolders) gets checked " +
                       "for a timesheet table. Tick the ones to bring in; entries already in the app are skipped.",
                AutoSize = true,
                MaximumSize = new Size(880, 0),
                Margin = new Padding(0, 0, 0, 8)
            }, ThemeRole.Muted), 0, 0);

            var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var lookIn = Ui.InlineLabel("Look in");
            lookIn.Margin = new Padding(0, 0, 6, 0);
            txtFolder = new TextBox { Text = startFolder, Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 3) };
            folderRow.Controls.Add(lookIn, 0, 0);
            folderRow.Controls.Add(txtFolder, 1, 0);
            folderRow.Controls.Add(Ui.Button("Browse...", (s, e) => PickFolder()), 2, 0);
            root.Controls.Add(folderRow, 0, 1);

            var options = Ui.Flow();
            options.Margin = new Padding(0, 2, 0, 6);
            chkSub = new CheckBox { Text = "Include subfolders", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 16, 3) };
            chkPdf = new CheckBox { Text = "PDF files", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 3, 12, 3) };
            chkDocx = new CheckBox { Text = "Word files (.docx)", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 3, 16, 3) };
            btnSearch = Ui.PrimaryButton("Search", (s, e) => StartOrStop());
            btnSearch.MinimumSize = new Size(96, 0);
            lblStatus = Theme.SetRole(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 0, 0, 0) }, ThemeRole.Muted);
            options.Controls.AddRange(new Control[] { chkSub, chkPdf, chkDocx, btnSearch, lblStatus });
            root.Controls.Add(options, 0, 2);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ShowCellToolTips = true,
                Margin = new Padding(0)
            };
            grid.ColumnHeadersHeight = (int)(Font.Height * 2.1);
            grid.RowTemplate.Height = (int)(Font.Height * 1.8);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9F);
            grid.ColumnHeadersDefaultCellStyle.Padding = grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", FillWeight = 5, SortMode = DataGridViewColumnSortMode.NotSortable });
            AddText("File", 30);
            AddText("What's in it", 34);
            AddText("Dates", 20);
            AddText("Folder", 30);
            for (int i = 1; i < grid.Columns.Count; i++) grid.Columns[i].ReadOnly = true;
            grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += (s, e) => UpdateImportButton();
            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Space) return;
                bool on = grid.SelectedRows.Cast<DataGridViewRow>().Any(r => !(r.Cells[0].Value is bool && (bool)r.Cells[0].Value));
                foreach (DataGridViewRow r in grid.SelectedRows) r.Cells[0].Value = on;
                e.Handled = true;
            };
            root.Controls.Add(grid, 0, 3);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 8, 0, 4) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var left = Ui.Flow();
            left.Controls.Add(Ui.Button("Tick all", (s, e) => TickAll(true)));
            left.Controls.Add(Ui.Button("Untick all", (s, e) => TickAll(false)));
            var right = Ui.Flow();
            btnImport = Ui.PrimaryButton("Import ticked", (s, e) => Finish());
            btnImport.Enabled = false;
            var cancel = Ui.Button("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            right.Controls.Add(btnImport);
            right.Controls.Add(cancel);
            bottom.Controls.Add(left, 0, 0);
            bottom.Controls.Add(right, 1, 0);
            root.Controls.Add(bottom, 0, 4);

            Controls.Add(root);
            AcceptButton = btnSearch;
            CancelButton = cancel;
            Load += (s, e) => StyleGrid();
            FormClosing += (s, e) => { if (worker != null && worker.IsBusy) worker.CancelAsync(); };
            ResumeLayout(false);
            PerformLayout();
        }

        void AddText(string name, float weight)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        }

        void StyleGrid()
        {
            var t = Theme.Current;
            grid.BackgroundColor = t.GridBack;
            grid.GridColor = t.GridLine;
            grid.DefaultCellStyle.BackColor = t.GridBack;
            grid.DefaultCellStyle.ForeColor = t.Text;
            grid.DefaultCellStyle.SelectionBackColor = t.Selection;
            grid.DefaultCellStyle.SelectionForeColor = t.SelectionText;
            var head = grid.ColumnHeadersDefaultCellStyle;
            head.BackColor = head.SelectionBackColor = t.HeaderBack;
            head.ForeColor = head.SelectionForeColor = t.HeaderText;
        }

        void PickFolder()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Which folder are your old timesheets in?";
                if (Directory.Exists(txtFolder.Text)) dlg.SelectedPath = txtFolder.Text;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                txtFolder.Text = dlg.SelectedPath;
            }
            StartOrStop();
        }

        void StartOrStop()
        {
            if (worker != null && worker.IsBusy)
            {
                worker.CancelAsync();
                return;
            }
            string folder = txtFolder.Text.Trim();
            if (!Directory.Exists(folder))
            {
                Ui.Error(this, "That folder doesn't exist. Click Browse... and pick one.");
                return;
            }
            if (!chkPdf.Checked && !chkDocx.Checked)
            {
                Ui.Error(this, "Tick PDF files, Word files, or both.");
                return;
            }

            found.Clear();
            grid.Rows.Clear();
            UpdateImportButton();
            btnSearch.Text = "Stop";
            lblStatus.Text = "Searching...";
            var exts = new List<string>();
            if (chkPdf.Checked) exts.Add(".pdf");
            if (chkDocx.Checked) exts.Add(".docx");
            bool sub = chkSub.Checked;

            worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            var w = worker;
            w.DoWork += (s, e) =>
            {
                int checkedCount = 0;
                foreach (var file in Importer.FindFiles(folder, sub, exts, () => w.CancellationPending))
                {
                    if (w.CancellationPending) { e.Cancel = true; return; }
                    var f = new FoundFile { Path = file };
                    try { f.Result = Importer.ReadAny(file); }
                    catch (Exception ex) { f.Error = ex.Message; }
                    checkedCount++;
                    w.ReportProgress(checkedCount, f);
                }
            };
            w.ProgressChanged += (s, e) => AddResult((FoundFile)e.UserState, e.ProgressPercentage);
            w.RunWorkerCompleted += (s, e) =>
            {
                btnSearch.Text = "Search";
                int sheets = found.Count(f => f.Result != null && f.Result.Entries.Count > 0);
                if (e.Error != null) lblStatus.Text = "Search stopped: " + e.Error.Message;
                else lblStatus.Text = string.Format("{0}{1} file{2} checked, {3} with timesheets.", e.Cancelled ? "Stopped. " : "",
                    found.Count, found.Count == 1 ? "" : "s", sheets);
                if (found.Count == 0 && !e.Cancelled && e.Error == null)
                    lblStatus.Text = "No " + string.Join(" or ", exts.Select(x => x.TrimStart('.').ToUpperInvariant())) + " files in there.";
            };
            w.RunWorkerAsync();
        }

        void AddResult(FoundFile f, int count)
        {
            found.Add(f);
            var t = Theme.Current;
            string what, dates = "";
            bool tick = false;
            if (f.Error != null) what = "Couldn't read: " + f.Error;
            else if (f.Result.Entries.Count == 0) what = "No timesheet table found";
            else
            {
                f.NewEntries = Importer.CountNew(data, f.Result);
                double hours = Math.Round(f.Result.Entries.Sum(x => x.Hours ?? 0), 2);
                what = string.Format("{0} entr{1}, {2} h", f.Result.Entries.Count, f.Result.Entries.Count == 1 ? "y" : "ies", TimeUtil.FormatHours(hours));
                what += f.NewEntries == 0 ? " (all already in the app)" : f.NewEntries < f.Result.Entries.Count ? " (" + f.NewEntries + " new)" : "";
                dates = TimeUtil.FormatDate(f.Result.Entries.Min(x => x.Date)) + " \u2013 " + TimeUtil.FormatDate(f.Result.Entries.Max(x => x.Date));
                tick = f.NewEntries > 0;
            }
            string folder = Path.GetDirectoryName(f.Path) ?? "";
            string root = txtFolder.Text.Trim().TrimEnd(Path.DirectorySeparatorChar);
            string rel = folder.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? "." + folder.Substring(root.Length) : folder;
            int i = grid.Rows.Add(tick, Path.GetFileName(f.Path), what, dates, rel.Length == 0 ? "." : rel);
            var row = grid.Rows[i];
            row.Tag = f;
            row.Cells[1].ToolTipText = f.Path;
            row.Cells[2].ToolTipText = what;
            if (f.Result == null || f.Result.Entries.Count == 0)
            {
                row.DefaultCellStyle.ForeColor = t.Muted;
                row.Cells[0].ReadOnly = true;
            }
            lblStatus.Text = string.Format("Searching... {0} file{1} checked", count, count == 1 ? "" : "s");
            UpdateImportButton();
        }

        void TickAll(bool on)
        {
            foreach (DataGridViewRow r in grid.Rows)
            {
                var f = r.Tag as FoundFile;
                if (f != null && f.Result != null && f.Result.Entries.Count > 0) r.Cells[0].Value = on;
            }
        }

        List<FoundFile> Ticked()
        {
            return grid.Rows.Cast<DataGridViewRow>()
                .Where(r => r.Cells[0].Value is bool && (bool)r.Cells[0].Value && r.Tag is FoundFile)
                .Select(r => (FoundFile)r.Tag).Where(f => f.Result != null && f.Result.Entries.Count > 0).ToList();
        }

        void UpdateImportButton()
        {
            var ticked = Ticked();
            btnImport.Enabled = ticked.Count > 0;
            btnImport.Text = ticked.Count > 0 ? string.Format(CultureInfo.CurrentCulture, "Import {0} file{1}", ticked.Count, ticked.Count == 1 ? "" : "s") : "Import ticked";
        }

        void Finish()
        {
            if (worker != null && worker.IsBusy) worker.CancelAsync();
            Selected = Ticked();
            DialogResult = DialogResult.OK;
        }
    }
}
