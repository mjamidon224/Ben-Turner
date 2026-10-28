using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace TATimesheet
{
    /// <summary>Menu and button actions: exports, printing, imports, backups, dialogs.</summary>
    public partial class MainForm
    {
        // ---- Exports ----------------------------------------------------------------------------

        TimesheetSheet CurrentSheet()
        {
            CommitHeader();
            return TimesheetSheet.Build(data, period, cboName.Text.Trim(), cboCustomer.Text.Trim());
        }

        bool ConfirmIfEmpty(TimesheetSheet sheet)
        {
            if (data.Entries.Any(e => period.Contains(e.Date))) return true;
            return Ui.Confirm(this, "There are no entries in this pay period (" + period + ").\n\nExport a blank timesheet anyway?", "Empty timesheet");
        }

        string DefaultFileName(string extension)
        {
            return data.Settings.FilePrefix + period.Start.ToString("MMddyy", CultureInfo.InvariantCulture) + extension;
        }

        string ExportFolder()
        {
            string f = data.Settings.ExportFolder;
            return !string.IsNullOrEmpty(f) && Directory.Exists(f) ? f : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        string AskSavePath(string title, string filter, string fileName)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = title;
                dlg.Filter = filter;
                dlg.FileName = fileName;
                dlg.InitialDirectory = ExportFolder();
                dlg.OverwritePrompt = true;
                dlg.AddExtension = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                data.Settings.ExportFolder = Path.GetDirectoryName(dlg.FileName);
                return dlg.FileName;
            }
        }

        void RunExport(string path, Action write)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                write();
            }
            catch (IOException ex)
            {
                Ui.Error(this, "Couldn't write " + Path.GetFileName(path) + ".\n\nIf it's open in Word, Excel or a PDF viewer, close it and try again.\n\n(" + ex.Message + ")");
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                Ui.Error(this, "Windows won't let me write there. Pick another folder.\n\n(" + ex.Message + ")");
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            Persist("Exported " + Path.GetFileName(path));
            if (data.Settings.OpenAfterExport) OpenPath(path);
        }

        byte[] CustomLogoOrNull()
        {
            try
            {
                return AppResources.CustomLogoPng(data.Settings);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Couldn't read your custom logo (" + ex.Message + "), used the standard one.";
                return null;
            }
        }

        void ExportWord()
        {
            var sheet = CurrentSheet();
            if (!ConfirmIfEmpty(sheet)) return;
            string path = AskSavePath("Save Word timesheet", "Word document (*.docx)|*.docx", DefaultFileName(".docx"));
            if (path == null) return;
            var logo = CustomLogoOrNull();
            RunExport(path, () => DocxExporter.Export(sheet, path, logo));
        }

        void ExportPdf()
        {
            var sheet = CurrentSheet();
            if (!ConfirmIfEmpty(sheet)) return;
            string path = AskSavePath("Save PDF timesheet", "PDF file (*.pdf)|*.pdf", DefaultFileName(".pdf"));
            if (path == null) return;
            var logo = CustomLogoOrNull() ?? AppResources.DefaultLogoPng();
            RunExport(path, () => PdfExporter.Export(sheet, logo, path));
        }

        void ExportData(bool excel)
        {
            DateTime from, to;
            using (var dlg = new RangeForm(period, data, excel ? "Export to Excel" : "Export to CSV"))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                from = dlg.From;
                to = dlg.To;
            }
            string name = string.Format("Timesheet_{0}_to_{1}", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            string path = excel
                ? AskSavePath("Save Excel workbook", "Excel workbook (*.xlsx)|*.xlsx", name + ".xlsx")
                : AskSavePath("Save CSV file", "CSV file (*.csv)|*.csv", name + ".csv");
            if (path == null) return;
            if (excel) RunExport(path, () => DataExporters.ExportXlsx(data, from, to, path));
            else RunExport(path, () => DataExporters.ExportCsv(data, from, to, path));
        }

        TimesheetPrintDocument PrintDocumentForPeriod()
        {
            return new TimesheetPrintDocument(CurrentSheet(), CustomLogoOrNull() ?? AppResources.DefaultLogoPng());
        }

        void PrintPreview()
        {
            using (var doc = PrintDocumentForPeriod())
            using (var dlg = new PrintPreviewDialog())
            {
                dlg.Document = doc;
                dlg.UseAntiAlias = true;
                dlg.Width = Math.Min(1200, Screen.FromControl(this).WorkingArea.Width - 80);
                dlg.Height = Math.Min(900, Screen.FromControl(this).WorkingArea.Height - 80);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Text = "Print preview \u2013 " + period;
                dlg.ShowDialog(this);
            }
        }

        void PrintNow()
        {
            using (var doc = PrintDocumentForPeriod())
            using (var dlg = new PrintDialog())
            {
                dlg.Document = doc;
                dlg.UseEXDialog = true;
                if (dlg.ShowDialog(this) == DialogResult.OK) doc.Print();
            }
        }

        static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // No program registered for the file type; the file is still saved.
            }
        }

        // ---- Imports ----------------------------------------------------------------------------

        void ImportDocx()
        {
            RunImport("Import Word timesheets", "Word timesheets (*.docx)|*.docx", Importer.ReadDocx);
        }

        void ImportCsv()
        {
            RunImport("Import CSV", "CSV files (*.csv)|*.csv", Importer.ReadCsv);
        }

        void RunImport(string title, string filter, Func<string, ImportResult> read)
        {
            string[] files;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = title;
                dlg.Filter = filter;
                dlg.Multiselect = true;
                dlg.InitialDirectory = ExportFolder();
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                files = dlg.FileNames;
            }

            var summary = new MergeSummary();
            int before = data.Entries.Count;
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (var f in files)
                {
                    try
                    {
                        var r = read(f);
                        if (r.Entries.Count == 0) summary.Problems.Add(Path.GetFileName(f) + ": no timesheet rows found");
                        Importer.Merge(data, r, summary);
                    }
                    catch (Exception ex)
                    {
                        summary.Problems.Add(Path.GetFileName(f) + ": " + ex.Message);
                    }
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            Persist(string.Format("Imported {0} entries", summary.Added));
            int mismatched = data.Entries.Skip(before).Count(e => e.HoursMismatch);
            LoadPeriod(true);

            string msg = string.Format("Imported {0} entr{1} from {2} file{3}.", summary.Added, summary.Added == 1 ? "y" : "ies",
                summary.Files, summary.Files == 1 ? "" : "s");
            if (summary.Duplicates > 0) msg += string.Format("\nSkipped {0} already in here.", summary.Duplicates);
            if (mismatched > 0)
                msg += string.Format("\n\nHeads up: {0} imported row{1} bill different hours than the start/end times add up to. " +
                                     "They're highlighted orange in the Hours column \u2013 hover for details.", mismatched, mismatched == 1 ? "" : "s");
            if (summary.Problems.Count > 0) msg += "\n\nProblems:\n" + string.Join("\n", summary.Problems);
            if (summary.Added > 0)
            {
                var latest = data.Entries.Skip(before).Max(e => e.Date);
                msg += "\n\nGo to the most recent imported pay period?";
                if (Ui.Confirm(this, msg, title)) GoToPeriod(Period.Containing(latest, data.Settings));
            }
            else
            {
                Ui.Info(this, msg);
            }
        }

        // ---- Backups ----------------------------------------------------------------------------

        void BackupData()
        {
            string path = AskSavePath("Back up timesheet data", "TA Timesheet data (*.xml)|*.xml",
                "TA Timesheet backup " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".xml");
            if (path == null) return;
            try
            {
                DataStore.Write(data, path);
                lblStatus.Text = "Backed up to " + path;
            }
            catch (Exception ex)
            {
                Ui.Error(this, "Backup failed:\n\n" + ex.Message);
            }
        }

        void RestoreData()
        {
            string path;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Restore from backup";
                dlg.Filter = "TA Timesheet data (*.xml)|*.xml";
                dlg.InitialDirectory = Directory.Exists(store.BackupFolder) ? store.BackupFolder : store.Folder;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }
            AppData restored;
            try
            {
                restored = DataStore.Read(path);
            }
            catch (Exception ex)
            {
                Ui.Error(this, "That file can't be restored:\n\n" + ex.Message);
                return;
            }
            string range = restored.Entries.Count == 0 ? "no entries"
                : string.Format("{0} entries, {1} to {2}", restored.Entries.Count,
                    TimeUtil.FormatDate(restored.Entries.Min(e => e.Date)), TimeUtil.FormatDate(restored.Entries.Max(e => e.Date)));
            if (!Ui.Confirm(this, "Replace everything in the app with this backup?\n\n" + Path.GetFileName(path) + "\n" + range +
                                  "\n\nWhat's in the app right now gets saved to the Backups folder first.", "Restore from backup"))
                return;
            try
            {
                Directory.CreateDirectory(store.BackupFolder);
                DataStore.Write(data, Path.Combine(store.BackupFolder,
                    "before-restore-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture) + ".xml"));
            }
            catch (Exception ex)
            {
                Ui.Error(this, "Couldn't save a safety copy first, so nothing was changed:\n\n" + ex.Message);
                return;
            }
            restored.Settings.WindowBounds = data.Settings.WindowBounds;
            data = restored;
            Persist("Restored " + Path.GetFileName(path));
            FillTimeCombos();
            period = Period.Containing(DateTime.Today, data.Settings);
            LoadPeriod(true);
        }

        void OpenDataFolder()
        {
            try
            {
                Process.Start("explorer.exe", "\"" + store.Folder + "\"");
            }
            catch (Exception)
            {
                Ui.Info(this, "Your data is in:\n" + store.FilePath);
            }
        }

        // ---- Dialogs ----------------------------------------------------------------------------

        void ManageHistory()
        {
            using (var dlg = new HistoryForm(data))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
            }
            Persist("Dropdown history updated");
            suppress++;
            try
            {
                Ui.Fill(cboName, data.Names);
                Ui.Fill(cboCustomer, data.Customers);
                FillEditorCombos();
            }
            finally
            {
                suppress--;
            }
        }

        void ShowSettings()
        {
            int oldWeeks = data.Settings.PeriodWeeks;
            DateTime oldAnchor = data.Settings.PeriodAnchor;
            using (var dlg = new SettingsForm(data.Settings, store))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
            }
            Persist("Settings saved");
            FillTimeCombos();
            if (oldWeeks != data.Settings.PeriodWeeks || oldAnchor != data.Settings.PeriodAnchor)
            {
                period = Period.Containing(period.Contains(DateTime.Today) ? DateTime.Today : period.Start, data.Settings);
                LoadPeriod(true);
            }
        }

        void ShowHelp()
        {
            Ui.Info(this,
                "LOGGING A DAY\n" +
                "  1. Pick the date (it jumps to the next empty day by itself).\n" +
                "  2. Project and Location are dropdowns of everything you've used. Start typing and they fill in.\n" +
                "     Picking a project fills in the location you last used with it.\n" +
                "  3. Start/End: pick from the list or just type 6, 0630, 6:30 or 6:30p.\n" +
                "     Hours fill in by themselves. Type over them if you bill something different.\n" +
                "  4. Hit Enter. Done. It saves instantly and moves to the next day.\n\n" +
                "EDITING\n" +
                "  Click a row to edit it. Delete key removes it. Right-click > Copy to next day.\n" +
                "  Esc starts a fresh entry. Orange hours = hours don't match the start/end times.\n\n" +
                "SENDING THE TIMESHEET\n" +
                "  Word timesheet = the exact customer timesheet you used to make by hand (TA_Timesheet_MMDDYY.docx).\n" +
                "  PDF = same thing, ready to email. Print... = preview and print.\n" +
                "  Excel / CSV = all your entries for any date range, with totals by project and week.\n\n" +
                "SHORTCUTS\n" +
                "  Enter save    Esc clear    Ctrl+L copy last entry    Ctrl+PgUp/PgDn change period\n" +
                "  Ctrl+T this period    Ctrl+E Word    Ctrl+Shift+E PDF    Ctrl+P print preview\n\n" +
                "Everything is saved to:\n  " + store.FilePath + "\nwith a backup every day in the Backups folder next to it.");
        }

        void ShowAbout()
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            Ui.Info(this, "TA Timesheet " + v.Major + "." + v.Minor + "\nTurner Automation\n\n" +
                          data.Entries.Count + " entries on file.\nData: " + store.FilePath);
        }
    }
}
