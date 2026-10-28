using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TATimesheet
{
    /// <summary>Picks the date range for the Excel/CSV exports.</summary>
    public class RangeForm : Form
    {
        public DateTime From { get; private set; }
        public DateTime To { get; private set; }

        readonly RadioButton rbCustom;
        readonly ThemedDatePicker dtFrom, dtTo;

        public RangeForm(Period current, AppData data, string title)
        {
            SuspendLayout(); // before SetupDialog: controls added below must be DPI-scaled with the form
            Ui.SetupDialog(this, title);

            var today = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var lastMonthStart = monthStart.AddMonths(-1);
            DateTime first = data.Entries.Count > 0 ? data.Entries.Min(e => e.Date) : today;
            DateTime last = data.Entries.Count > 0 ? data.Entries.Max(e => e.Date) : today;

            var list = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 4), WrapContents = false };
            list.Controls.Add(Theme.SetRole(new Label { Text = "Which entries?", AutoSize = true, Margin = new Padding(3, 0, 3, 8) }, ThemeRole.Heading));

            Option(list, "Pay period on screen  (" + current + ")", current.Start, current.End, true);
            Option(list, "Previous pay period  (" + current.Previous + ")", current.Previous.Start, current.Previous.End, false);
            Option(list, "This month  (" + monthStart.ToString("MMMM yyyy") + ")", monthStart, monthStart.AddMonths(1).AddDays(-1), false);
            Option(list, "Last month  (" + lastMonthStart.ToString("MMMM yyyy") + ")", lastMonthStart, monthStart.AddDays(-1), false);
            Option(list, "This year  (" + today.Year + ")", new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31), false);
            Option(list, "Everything  (" + data.Entries.Count + " entries)", first, last, false);

            var customRow = Ui.Flow();
            rbCustom = new RadioButton { Text = "From", AutoSize = true, Anchor = AnchorStyles.Left };
            dtFrom = new ThemedDatePicker { Format = DateTimePickerFormat.Short, Width = 120, Value = current.Start, Anchor = AnchorStyles.Left };
            dtTo = new ThemedDatePicker { Format = DateTimePickerFormat.Short, Width = 120, Value = current.End, Anchor = AnchorStyles.Left };
            dtFrom.ValueChanged += (s, e) => rbCustom.Checked = true;
            dtTo.ValueChanged += (s, e) => rbCustom.Checked = true;
            customRow.Controls.AddRange(new Control[] { rbCustom, dtFrom, new Label { Text = "to", AutoSize = true, Anchor = AnchorStyles.Left }, dtTo });
            list.Controls.Add(customRow);

            // The custom option sits in its own row, so keep it exclusive with the others by hand.
            var presets = list.Controls.OfType<RadioButton>().ToList();
            rbCustom.CheckedChanged += (s, e) => { if (rbCustom.Checked) presets.ForEach(r => r.Checked = false); };
            foreach (var r in presets)
            {
                var radio = r;
                radio.CheckedChanged += (s, e) => { if (radio.Checked) rbCustom.Checked = false; };
            }

            Button ok;
            var buttons = Ui.DialogButtons(this, out ok);
            ok.Click += (s, e) =>
            {
                if (rbCustom.Checked)
                {
                    From = dtFrom.Value.Date;
                    To = dtTo.Value.Date;
                }
                else
                {
                    var picked = list.Controls.OfType<RadioButton>().First(r => r.Checked);
                    var range = (DateTime[])picked.Tag;
                    From = range[0];
                    To = range[1];
                }
                if (To < From)
                {
                    var t = From; From = To; To = t;
                }
                DialogResult = DialogResult.OK;
            };

            Controls.Add(list);
            Controls.Add(buttons);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ResumeLayout(false);
            PerformLayout();
        }

        static void Option(FlowLayoutPanel list, string text, DateTime from, DateTime to, bool selected)
        {
            list.Controls.Add(new RadioButton
            {
                Text = text,
                AutoSize = true,
                Checked = selected,
                Tag = new[] { from.Date, to.Date },
                Margin = new Padding(3, 2, 3, 2)
            });
        }
    }
}
