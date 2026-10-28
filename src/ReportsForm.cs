using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace TATimesheet
{
    /// <summary>What the Reports window borrows from the main window (save dialogs, logo, printing).</summary>
    public interface IReportHost
    {
        string AskSavePath(IWin32Window owner, string title, string filter, string fileName);
        void RunExport(IWin32Window owner, string path, Action write);
        byte[] LogoPng();
        void ShowPreview(IWin32Window owner, PrintDocument doc, string title);
        void SaveAllTimesheets(IWin32Window owner, DateTime from, DateTime to, bool pdf);
        void OpenPeriodAt(DateTime date);
    }

    /// <summary>Historical reports: hours over any range, grouped by time or by project/location/customer.</summary>
    public class ReportsForm : Form
    {
        readonly AppData data;
        readonly IReportHost host;
        readonly ComboBox cboRange, cboGroup;
        readonly CheckBox chkHideEmpty;
        readonly ThemedDatePicker dtFrom, dtTo;
        readonly Label lblTotal, lblStats;
        readonly BarChart chart;
        readonly DataGridView grid;
        Report report;
        int suppress;

        static readonly string[] Ranges = { "All time", "This year", "Last year", "Last 12 months", "Last 3 months", "Last 4 weeks", "Custom" };
        const int Custom = 6;

        public ReportsForm(AppData data, IReportHost host)
        {
            this.data = data;
            this.host = host;
            Ui.SetupDialog(this, "Reports");
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimumSize = new Size(860, 600);
            Size = new Size(1100, 780);
            SuspendLayout();

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12, 10, 12, 4) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Filters, all in one row above the chart.
            var filters = Ui.Flow();
            cboRange = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Anchor = AnchorStyles.Left };
            cboRange.Items.AddRange(Ranges.Cast<object>().ToArray());
            dtFrom = new ThemedDatePicker { Format = DateTimePickerFormat.Short, Width = 115, Anchor = AnchorStyles.Left };
            dtTo = new ThemedDatePicker { Format = DateTimePickerFormat.Short, Width = 115, Anchor = AnchorStyles.Left };
            cboGroup = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Anchor = AnchorStyles.Left };
            foreach (ReportGrouping g in Enum.GetValues(typeof(ReportGrouping))) cboGroup.Items.Add(Report.NameOf(g));
            chkHideEmpty = new CheckBox { Text = "Hide empty rows", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(16, 3, 3, 3) };
            chkHideEmpty.CheckedChanged += (s, e) => { if (report != null) { report.HideEmpty = chkHideEmpty.Checked; Fill(); } };
            filters.Controls.AddRange(new Control[]
            {
                Ui.InlineLabel("Show"), cboRange, Ui.InlineLabel("from"), dtFrom, Ui.InlineLabel("to"), dtTo,
                Ui.InlineLabel("Group by"), cboGroup, chkHideEmpty
            });
            filters.Controls[0].Margin = new Padding(0, 0, 3, 0);
            filters.Margin = new Padding(0, 0, 0, 8);

            // Summary: one hero number, the rest as plain stats.
            var summary = Ui.Flow();
            summary.Margin = new Padding(0, 0, 0, 8);
            lblTotal = Theme.SetRole(new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 22F), Anchor = AnchorStyles.Left, Margin = new Padding(0) }, ThemeRole.Heading);
            lblStats = Theme.SetRole(new Label { AutoSize = true, Font = new Font("Segoe UI", 10F), Anchor = AnchorStyles.Left, Margin = new Padding(16, 0, 0, 0) }, ThemeRole.Muted);
            summary.Controls.Add(lblTotal);
            summary.Controls.Add(lblStats);

            chart = new BarChart { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), Font = new Font("Segoe UI", 9F) };

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                Margin = new Padding(0)
            };
            grid.ColumnHeadersHeight = (int)(Font.Height * 2.1);
            grid.RowTemplate.Height = (int)(Font.Height * 1.8);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9F);
            grid.ColumnHeadersDefaultCellStyle.Padding = grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            AddColumn("Group", 34, false);
            foreach (var c in DataExporters.ReportColumns) AddColumn(c, 13, true);
            grid.CellDoubleClick += (s, e) => OpenRow(e.RowIndex);

            var buttons = Ui.Flow();
            buttons.Dock = DockStyle.Fill;
            buttons.Margin = new Padding(0, 8, 0, 4);
            buttons.Controls.Add(Ui.InlineLabel("Export report:"));
            buttons.Controls[0].Margin = new Padding(0, 0, 3, 0);
            buttons.Controls.Add(Ui.PrimaryButton("PDF", (s, e) => ExportPdf()));
            buttons.Controls.Add(Ui.Button("Excel", (s, e) => ExportExcel()));
            buttons.Controls.Add(Ui.Button("CSV", (s, e) => ExportCsv()));
            buttons.Controls.Add(Ui.Button("Print...", (s, e) => Print()));
            var all = Ui.Button("Save every timesheet in this range \u25BE", null);
            all.Margin = new Padding(24, 3, 3, 3);
            var allMenu = new ContextMenuStrip();
            allMenu.Items.Add("As Word files (.docx)...", null, (s, e) => host.SaveAllTimesheets(this, report.From, report.To, false));
            allMenu.Items.Add("As PDF files...", null, (s, e) => host.SaveAllTimesheets(this, report.From, report.To, true));
            all.Click += (s, e) => allMenu.Show(all, new Point(0, all.Height));
            buttons.Controls.Add(all);
            var close = Ui.Button("Close", (s, e) => Close());
            close.Anchor = AnchorStyles.Right;
            CancelButton = close;

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.Controls.Add(buttons, 0, 0);
            bottom.Controls.Add(close, 1, 0);

            root.Controls.Add(filters, 0, 0);
            root.Controls.Add(summary, 0, 1);
            root.Controls.Add(chart, 0, 2);
            root.Controls.Add(grid, 0, 3);
            root.Controls.Add(bottom, 0, 4);
            Controls.Add(root);

            cboRange.SelectedIndexChanged += (s, e) => { if (suppress == 0) ApplyPreset(); };
            cboGroup.SelectedIndexChanged += (s, e) => { if (suppress == 0) Rebuild(); };
            dtFrom.ValueChanged += (s, e) => DatesEdited();
            dtTo.ValueChanged += (s, e) => DatesEdited();

            suppress++;
            cboRange.SelectedIndex = 0;
            cboGroup.SelectedIndex = 0;
            suppress--;
            ApplyPreset();
            Load += (s, e) => StyleGrid();
            ResumeLayout(false);
            PerformLayout();
        }

        void AddColumn(string name, float weight, bool numeric)
        {
            var c = new DataGridViewTextBoxColumn { HeaderText = name, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable };
            if (numeric)
            {
                c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            grid.Columns.Add(c);
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
            Fill();
        }

        void ApplyPreset()
        {
            DateTime today = DateTime.Today, from, to = today;
            DateTime first = data.Entries.Count > 0 ? data.Entries.Min(e => e.Date) : today;
            DateTime last = data.Entries.Count > 0 ? data.Entries.Max(e => e.Date) : today;
            switch (cboRange.SelectedIndex)
            {
                case 0: from = first < today ? first : today; to = last > today ? last : today; break;
                case 1: from = new DateTime(today.Year, 1, 1); break;
                case 2: from = new DateTime(today.Year - 1, 1, 1); to = new DateTime(today.Year - 1, 12, 31); break;
                case 3: from = today.AddMonths(-12).AddDays(1); break;
                case 4: from = today.AddMonths(-3).AddDays(1); break;
                case 5: from = today.AddDays(-27); break;
                default: return; // Custom: keep whatever dates are in the boxes
            }
            suppress++;
            dtFrom.Value = from;
            dtTo.Value = to;
            suppress--;
            Rebuild();
        }

        void DatesEdited()
        {
            if (suppress > 0) return;
            suppress++;
            cboRange.SelectedIndex = Custom;
            suppress--;
            Rebuild();
        }

        void Rebuild()
        {
            report = Report.Build(data, dtFrom.Value.Date, dtTo.Value.Date, (ReportGrouping)Math.Max(0, cboGroup.SelectedIndex));
            report.HideEmpty = chkHideEmpty.Checked;
            lblTotal.Text = TimeUtil.FormatHours(report.TotalHours) + " h";
            var b = report.Busiest;
            lblStats.Text = string.Format("{0} days worked   \u00B7   {1} h/day   \u00B7   {2} h/week   \u00B7   {3} h travel{4}",
                report.DaysWorked, TimeUtil.FormatHours(report.AvgPerDay), TimeUtil.FormatHours(report.AvgPerWeek),
                TimeUtil.FormatHours(report.TravelHours),
                b != null ? "\n Most hours: " + b.Label + " (" + TimeUtil.FormatHours(b.Hours) + " h)" : "");
            chart.Report = report;
            grid.Columns[0].HeaderText = report.GroupingName;
            Fill();
        }

        void Fill()
        {
            if (report == null) return;
            var t = Theme.Current;
            grid.Rows.Clear();
            foreach (var row in report.TableRows)
            {
                int i = grid.Rows.Add(row.Label, TimeUtil.FormatHours(row.Hours), TimeUtil.FormatHours(row.TravelHours),
                    row.Days.ToString(CultureInfo.CurrentCulture), TimeUtil.FormatHours(row.AvgPerDay), Share(row));
                if (row.Hours <= 0) grid.Rows[i].DefaultCellStyle.ForeColor = t.Muted;
                if (report.IsTimeBased) grid.Rows[i].Cells[0].ToolTipText = "Double-click to open this time in the main window";
            }
            int total = grid.Rows.Add("Total", TimeUtil.FormatHours(report.TotalHours), TimeUtil.FormatHours(report.TravelHours),
                report.DaysWorked.ToString(CultureInfo.CurrentCulture), TimeUtil.FormatHours(report.AvgPerDay), report.TotalHours > 0 ? "100%" : "0%");
            var style = grid.Rows[total].DefaultCellStyle;
            style.BackColor = style.SelectionBackColor = t.AccentTint;
            style.SelectionForeColor = t.Text;
            style.Font = new Font("Segoe UI Semibold", 9F);
            grid.ClearSelection();
        }

        string Share(ReportRow row)
        {
            return report.TotalHours > 0 ? (100 * row.Hours / report.TotalHours).ToString("0.#", CultureInfo.CurrentCulture) + "%" : "0%";
        }

        void OpenRow(int index)
        {
            var rows = report == null ? null : report.TableRows;
            if (rows == null || !report.IsTimeBased || index < 0 || index >= rows.Count) return;
            var start = rows[index].Start;
            if (!start.HasValue) return;
            host.OpenPeriodAt(start.Value < report.From ? report.From : start.Value);
            Close();
        }

        string FileName(string ext)
        {
            return string.Format("TA_Hours_Report_{0}_{1}_to_{2}{3}", report.GroupingName.Replace(" ", ""),
                report.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), report.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ext);
        }

        string Title { get { return "Hours report " + report.RangeText; } }

        void ExportPdf()
        {
            var r = report;
            string path = host.AskSavePath(this, "Save report as PDF", "PDF file (*.pdf)|*.pdf", FileName(".pdf"));
            if (path == null) return;
            var logo = host.LogoPng();
            host.RunExport(this, path, () => PdfExporter.ExportPages((w, h) => ReportLayout.Build(r, w, h), logo, path, Title, r.Name));
        }

        void ExportExcel()
        {
            var r = report;
            string path = host.AskSavePath(this, "Save report as Excel", "Excel workbook (*.xlsx)|*.xlsx", FileName(".xlsx"));
            if (path == null) return;
            host.RunExport(this, path, () => DataExporters.ExportReportXlsx(data, r, path));
        }

        void ExportCsv()
        {
            var r = report;
            string path = host.AskSavePath(this, "Save report as CSV", "CSV file (*.csv)|*.csv", FileName(".csv"));
            if (path == null) return;
            host.RunExport(this, path, () => DataExporters.ExportReportCsv(r, path));
        }

        void Print()
        {
            var r = report;
            using (var doc = new LayoutPrintDocument((w, h) => ReportLayout.Build(r, w, h), host.LogoPng(), Title))
                host.ShowPreview(this, doc, Title);
        }
    }
}
