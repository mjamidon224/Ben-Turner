using System;
using System.Collections.Generic;
using System.Linq;

namespace TATimesheet
{
    public class SheetRow
    {
        public DateTime Date;
        public string Project = "", Location = "", Start = "", End = "", Hours = "";
    }

    public class SheetWeek
    {
        public int Number;
        public DateTime Start;
        public List<SheetRow> Rows = new List<SheetRow>();
        public double Hours;
    }

    /// <summary>
    /// The customer timesheet for one pay period, laid out like the Word original:
    /// every day of every week gets a row (empty days stay blank), then the week totals.
    /// </summary>
    public class TimesheetSheet
    {
        public const string Title = "Customer Timesheet";
        public const string ContinuedTitle = "Customer Timesheet Continued";

        public string Name = "";
        public string Customer = "";
        public Period Period;
        public List<SheetWeek> Weeks = new List<SheetWeek>();
        public double TotalHours;

        public static TimesheetSheet Build(AppData data, Period p, string name, string customer)
        {
            var sheet = new TimesheetSheet { Name = name ?? "", Customer = customer ?? "", Period = p };
            var entries = data.Entries.Where(e => p.Contains(e.Date)).ToList();
            for (int w = 0; w < p.Weeks; w++)
            {
                var week = new SheetWeek { Number = w + 1, Start = p.WeekStart(w) };
                for (int d = 0; d < 7; d++)
                {
                    DateTime date = week.Start.AddDays(d);
                    var day = entries.Where(e => e.Date == date)
                                     .OrderBy(e => e.SortMinutes).ThenBy(e => e.Project).ToList();
                    if (day.Count == 0)
                    {
                        week.Rows.Add(new SheetRow { Date = date });
                        continue;
                    }
                    foreach (var e in day)
                    {
                        week.Rows.Add(new SheetRow
                        {
                            Date = date,
                            Project = e.Project,
                            Location = e.Location,
                            Start = e.Start,
                            End = e.End,
                            Hours = TimeUtil.FormatHours(e.Hours)
                        });
                        week.Hours += e.Hours ?? 0;
                    }
                }
                week.Hours = Math.Round(week.Hours, 2);
                sheet.TotalHours += week.Hours;
                sheet.Weeks.Add(week);
            }
            sheet.TotalHours = Math.Round(sheet.TotalHours, 2);
            return sheet;
        }
    }
}
