using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TATimesheet
{
    /// <summary>Shared look and small control factories.</summary>
    public static class Ui
    {
        public static readonly Color Green = Color.FromArgb(18, 160, 58);
        public static readonly Color Slate = Color.FromArgb(51, 61, 69);
        public static readonly Color GreenTint = Color.FromArgb(226, 244, 229);
        public static readonly Color Selection = Color.FromArgb(198, 234, 205);
        public static readonly Color WarnTint = Color.FromArgb(255, 232, 196);
        public static readonly Color WarnText = Color.FromArgb(176, 92, 0);
        public static readonly Color Muted = Color.FromArgb(120, 128, 134);
        public static readonly Color WeekendTint = Color.FromArgb(247, 248, 249);

        public static Font BaseFont()
        {
            return new Font("Segoe UI", 9F);
        }

        public static Label FieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Slate,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
                Margin = new Padding(3, 4, 3, 1)
            };
        }

        public static Label InlineLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Slate, Anchor = AnchorStyles.Left, Margin = new Padding(10, 0, 3, 0) };
        }

        /// <summary>Editable dropdown that remembers past values and autocompletes as you type.</summary>
        public static ComboBox HistoryCombo()
        {
            return new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                MaxDropDownItems = 16,
                IntegralHeight = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 3, 3)
            };
        }

        public static void Fill(ComboBox c, IEnumerable<string> items)
        {
            string text = c.Text;
            c.BeginUpdate();
            c.Items.Clear();
            foreach (var i in items) c.Items.Add(i);
            c.EndUpdate();
            c.Text = text;
        }

        public static FlowLayoutPanel Flow()
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        }

        public static Button Button(string text, EventHandler click)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 2, 8, 2),
                Margin = new Padding(3, 3, 3, 3),
                UseVisualStyleBackColor = true
            };
            if (click != null) b.Click += click;
            return b;
        }

        public static Button PrimaryButton(string text, EventHandler click)
        {
            var b = Button(text, click);
            b.Font = new Font("Segoe UI Semibold", 9F);
            return b;
        }

        public static void Error(IWin32Window owner, string message)
        {
            MessageBox.Show(owner, message, "TA Timesheet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static void Info(IWin32Window owner, string message)
        {
            MessageBox.Show(owner, message, "TA Timesheet", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static bool Confirm(IWin32Window owner, string message, string title)
        {
            return MessageBox.Show(owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        /// <summary>Standard OK / Cancel row for dialogs.</summary>
        public static FlowLayoutPanel DialogButtons(Form form, out Button ok)
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(8, 6, 8, 8)
            };
            var cancel = Button("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            ok = PrimaryButton("OK", null);
            ok.MinimumSize = cancel.MinimumSize = new Size(84, 0);
            row.Controls.Add(cancel);
            row.Controls.Add(ok);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            return row;
        }

        public static void SetupDialog(Form f, string title)
        {
            f.AutoScaleDimensions = new SizeF(96F, 96F);
            f.AutoScaleMode = AutoScaleMode.Dpi;
            f.Font = BaseFont();
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.CenterParent;
        }
    }
}
