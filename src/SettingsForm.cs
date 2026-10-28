using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace TATimesheet
{
    public class SettingsForm : Form
    {
        readonly AppSettings settings;
        readonly TextBox txtName, txtFolder, txtPrefix, txtLogo;
        readonly ComboBox cboWeeks, cboStep, cboTheme;
        readonly ThemedDatePicker dtAnchor;
        readonly CheckBox chkOpen;
        readonly Label lblPreview;

        public SettingsForm(AppSettings settings, DataStore store)
        {
            this.settings = settings;
            Ui.SetupDialog(this, "Settings");
            SuspendLayout();

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Padding = new Padding(14, 12, 14, 6) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            txtName = new TextBox { Text = settings.DefaultName, Dock = DockStyle.Fill };
            Row(t, "Your name", txtName, null, "Used on new pay periods. You can still change it per period on the main screen.");

            cboTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            cboTheme.Items.AddRange(new object[] { "Match Windows", "Light", "Dark" });
            cboTheme.SelectedIndex = settings.Theme == Theme.LightName ? 1 : settings.Theme == Theme.DarkName ? 2 : 0;
            Row(t, "Look", cboTheme, null, "Match Windows follows your Windows light/dark setting.");

            cboWeeks = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cboWeeks.Items.AddRange(new object[] { "1 week", "2 weeks", "3 weeks", "4 weeks" });
            cboWeeks.SelectedIndex = Math.Max(0, Math.Min(3, settings.PeriodWeeks - 1));
            Row(t, "Pay period length", cboWeeks, null, null);

            dtAnchor = new ThemedDatePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dddd M/d/yyyy", Value = settings.PeriodAnchor, Width = 200 };
            Row(t, "A pay period starts on", dtAnchor, null, "Any date that begins a pay period, e.g. Monday 10/26/2026. Periods repeat from there.");

            cboStep = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cboStep.Items.AddRange(new object[] { "5 minutes", "10 minutes", "15 minutes", "30 minutes", "60 minutes" });
            cboStep.SelectedItem = settings.TimeStepMinutes + " minutes";
            if (cboStep.SelectedIndex < 0) cboStep.SelectedIndex = 2;
            Row(t, "Time dropdown steps", cboStep, null, "You can always type any time you want.");

            txtFolder = new TextBox { Text = settings.ExportFolder, Dock = DockStyle.Fill };
            var browseFolder = Ui.Button("Browse...", (s, e) => PickFolder());
            Row(t, "Save exports in", txtFolder, browseFolder, null);

            txtPrefix = new TextBox { Text = settings.FilePrefix, Dock = DockStyle.Fill };
            txtPrefix.TextChanged += (s, e) => UpdatePreview();
            lblPreview = Theme.SetRole(new Label { AutoSize = true, Margin = new Padding(3, 0, 3, 8) }, ThemeRole.Muted);
            Row(t, "File name starts with", txtPrefix, null, null);
            int previewRow = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(lblPreview, 1, previewRow);

            txtLogo = new TextBox { Text = settings.LogoPath, Dock = DockStyle.Fill };
            var logoButtons = Ui.Flow();
            logoButtons.Controls.Add(Ui.Button("Browse...", (s, e) => PickLogo()));
            logoButtons.Controls.Add(Ui.Button("Use TA logo", (s, e) => txtLogo.Text = ""));
            Row(t, "Logo on timesheets", txtLogo, logoButtons, "Blank = the Turner Automation logo.");

            chkOpen = new CheckBox { Text = "Open files right after exporting them", Checked = settings.OpenAfterExport, AutoSize = true };
            Row(t, "", chkOpen, null, null);

            var dataLink = new LinkLabel { Text = store.FilePath, AutoSize = true, MaximumSize = new Size(420, 0) };
            dataLink.LinkClicked += (s, e) =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", "\"" + store.Folder + "\""); } catch (Exception) { }
            };
            Row(t, "Your data lives in", dataLink, null, "Backed up automatically every day (Backups folder).");

            Button ok;
            var buttons = Ui.DialogButtons(this, out ok);
            ok.Click += (s, e) => Apply();

            Controls.Add(t);
            Controls.Add(buttons);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            UpdatePreview();
            ResumeLayout(false);
            PerformLayout();
        }

        static void Row(TableLayoutPanel t, string label, Control input, Control extra, string help)
        {
            int r = t.RowCount;
            t.RowCount = r + 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(Theme.SetRole(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 3) }, ThemeRole.Heading), 0, r);
            input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            if (input is CheckBox || input is LinkLabel) input.Anchor = AnchorStyles.Left;
            t.Controls.Add(input, 1, r);
            if (extra != null) t.Controls.Add(extra, 2, r);
            if (help != null)
            {
                t.RowCount++;
                t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var h = Theme.SetRole(new Label { Text = help, AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(3, 0, 3, 8) }, ThemeRole.Muted);
                t.Controls.Add(h, 1, r + 1);
                t.SetColumnSpan(h, 2);
            }
        }

        void UpdatePreview()
        {
            lblPreview.Text = "e.g. " + txtPrefix.Text + dtAnchor.Value.ToString("MMddyy", CultureInfo.InvariantCulture) + ".docx";
        }

        void PickFolder()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Where should exported timesheets go?";
                if (Directory.Exists(txtFolder.Text)) dlg.SelectedPath = txtFolder.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtFolder.Text = dlg.SelectedPath;
            }
        }

        void PickLogo()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Pick a logo image";
                dlg.Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
                if (dlg.ShowDialog(this) == DialogResult.OK) txtLogo.Text = dlg.FileName;
            }
        }

        void Apply()
        {
            string logo = txtLogo.Text.Trim();
            if (logo.Length > 0)
            {
                try
                {
                    using (var img = Image.FromFile(logo)) { }
                }
                catch (Exception)
                {
                    Ui.Error(this, "That logo file can't be opened as an image.");
                    DialogResult = DialogResult.None;
                    return;
                }
            }
            string prefix = txtPrefix.Text.Trim();
            if (prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                Ui.Error(this, "The file name can't contain any of these: \\ / : * ? \" < > |");
                DialogResult = DialogResult.None;
                return;
            }

            settings.DefaultName = txtName.Text.Trim();
            settings.PeriodWeeks = cboWeeks.SelectedIndex + 1;
            settings.PeriodAnchor = dtAnchor.Value.Date;
            settings.TimeStepMinutes = int.Parse(((string)cboStep.SelectedItem).Split(' ')[0], CultureInfo.InvariantCulture);
            settings.ExportFolder = txtFolder.Text.Trim();
            settings.FilePrefix = prefix;
            settings.LogoPath = logo;
            settings.OpenAfterExport = chkOpen.Checked;
            settings.Theme = cboTheme.SelectedIndex == 1 ? Theme.LightName : cboTheme.SelectedIndex == 2 ? Theme.DarkName : Theme.SystemName;
            DialogResult = DialogResult.OK;
        }
    }
}
