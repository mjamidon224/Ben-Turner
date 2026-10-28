using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TATimesheet
{
    public enum ReportGrouping { PayPeriod, Week, Month, Year, Project, Location, Customer, Travel }

    public class ReportRow
    {
        public string Label = "";
        public DateTime? Start;     // set for time-based groups
        public int Days;            // distinct days with hours
        public int Entries;
        public double Hours;
        public double TravelHours;

        public double AvgPerDay { get { return Days > 0 ? Math.Round(Hours / Days, 2) : 0; } }
        public double OnSiteHours { get { return Math.Round(Hours - TravelHours, 2); } }
    }

    /// <summary>Hours over any date range, grouped by time or by project/location/customer.</summary>
    public class Report
    {
        public DateTime From, To;
        public ReportGrouping Grouping;
        public string Name = "";
        public List<ReportRow> Rows = new List<ReportRow>();
        public bool HideEmpty;      // tables/exports skip rows with no entries; the chart still shows the gaps

        public List<ReportRow> TableRows
        {
            get { return HideEmpty ? Rows.Where(r => r.Entries > 0).ToList() : Rows; }
        }
        public double TotalHours, TravelHours;
        public int DaysWorked, Entries;

        public bool IsTimeBased { get { return Grouping <= ReportGrouping.Year; } }
        public double AvgPerDay { get { return DaysWorked > 0 ? Math.Round(TotalHours / DaysWorked, 2) : 0; } }
        public double OnSiteHours { get { return Math.Round(TotalHours - TravelHours, 2); } }
        public double AvgPerWeek { get { return Math.Round(TotalHours / Math.Max(1.0, ((To - From).TotalDays + 1) / 7.0), 2); } }

        public ReportRow Busiest
        {
            get { return Rows.Where(r => r.Hours > 0).OrderByDescending(r => r.Hours).FirstOrDefault(); }
        }

        public string GroupingName { get { return NameOf(Grouping); } }

        public static string NameOf(ReportGrouping g)
        {
            switch (g)
            {
                case ReportGrouping.PayPeriod: return "Pay period";
                case ReportGrouping.Week: return "Week";
                case ReportGrouping.Month: return "Month";
                case ReportGrouping.Year: return "Year";
                case ReportGrouping.Project: return "Project";
                case ReportGrouping.Location: return "Location";
                case ReportGrouping.Customer: return "Customer";
                default: return "Travel vs on-site";
            }
        }

        public string RangeText
        {
            get { return TimeUtil.FormatDate(From) + " \u2013 " + TimeUtil.FormatDate(To); }
        }

        public static Report Build(AppData data, DateTime from, DateTime to, ReportGrouping grouping)
        {
            from = from.Date;
            to = to.Date;
            if (to < from) { var t = from; from = to; to = t; }
            var r = new Report { From = from, To = to, Grouping = grouping, Name = data.Settings.DefaultName };
            var entries = data.EntriesBetween(from, to).ToList();

            if (r.IsTimeBased)
            {
                foreach (var bucket in Buckets(data.Settings, from, to, grouping))
                {
                    var inBucket = entries.Where(e => e.Date >= bucket.Item1 && e.Date <= bucket.Item2).ToList();
                    var row = Summarize(inBucket);
                    row.Label = bucket.Item3;
                    row.Start = bucket.Item1;
                    r.Rows.Add(row);
                }
            }
            else if (grouping == ReportGrouping.Travel)
            {
                // Fixed order (on site, then travel) so each keeps its place and color.
                var onSite = Summarize(entries.Where(e => !e.Travel).ToList());
                onSite.Label = "On site";
                var travel = Summarize(entries.Where(e => e.Travel).ToList());
                travel.Label = "Travel";
                r.Rows.Add(onSite);
                r.Rows.Add(travel);
            }
            else
            {
                Func<TimeEntry, string> key;
                if (grouping == ReportGrouping.Project) key = e => e.Project.Trim();
                else if (grouping == ReportGrouping.Location) key = e => e.Location.Trim();
                else key = e => data.CustomerOn(e.Date).Trim();
                foreach (var g in entries.GroupBy(key, StringComparer.OrdinalIgnoreCase))
                {
                    var row = Summarize(g.ToList());
                    row.Label = g.Key.Length > 0 ? g.Key : "(none)";
                    r.Rows.Add(row);
                }
                r.Rows = r.Rows.OrderByDescending(x => x.Hours).ThenBy(x => x.Label).ToList();
            }

            var all = Summarize(entries);
            r.TotalHours = all.Hours;
            r.TravelHours = all.TravelHours;
            r.DaysWorked = all.Days;
            r.Entries = all.Entries;
            return r;
        }

        static ReportRow Summarize(List<TimeEntry> entries)
        {
            return new ReportRow
            {
                Entries = entries.Count,
                Days = entries.Where(e => (e.Hours ?? 0) > 0).Select(e => e.Date).Distinct().Count(),
                Hours = Math.Round(entries.Sum(e => e.Hours ?? 0), 2),
                TravelHours = Math.Round(entries.Where(e => e.Travel).Sum(e => e.Hours ?? 0), 2)
            };
        }

        /// <summary>Consecutive (start, end, label) spans covering the range, empty ones included.</summary>
        static IEnumerable<Tuple<DateTime, DateTime, string>> Buckets(AppSettings s, DateTime from, DateTime to, ReportGrouping g)
        {
            var c = CultureInfo.CurrentCulture;
            DateTime start;
            switch (g)
            {
                case ReportGrouping.PayPeriod:
                    for (var p = Period.Containing(from, s); p.Start <= to; p = p.Next)
                        yield return Tuple.Create(p.Start, p.End, TimeUtil.FormatDate(p.Start) + " \u2013 " + TimeUtil.FormatDate(p.End));
                    break;
                case ReportGrouping.Week:
                    var weekly = new AppSettings { PeriodAnchor = s.PeriodAnchor, PeriodWeeks = 1 };
                    for (var p = Period.Containing(from, weekly); p.Start <= to; p = p.Next)
                        yield return Tuple.Create(p.Start, p.End, "Week of " + TimeUtil.FormatDate(p.Start));
                    break;
                case ReportGrouping.Month:
                    for (start = new DateTime(from.Year, from.Month, 1); start <= to; start = start.AddMonths(1))
                        yield return Tuple.Create(start, start.AddMonths(1).AddDays(-1), start.ToString("MMM yyyy", c));
                    break;
                default:
                    for (start = new DateTime(from.Year, 1, 1); start <= to; start = start.AddYears(1))
                        yield return Tuple.Create(start, start.AddYears(1).AddDays(-1), start.Year.ToString(CultureInfo.InvariantCulture));
                    break;
            }
        }

        /// <summary>Short label for chart axes (the full label is in the table).</summary>
        public static string ShortLabel(ReportRow row, ReportGrouping g)
        {
            if (!row.Start.HasValue) return row.Label;
            var d = row.Start.Value;
            switch (g)
            {
                case ReportGrouping.PayPeriod:
                case ReportGrouping.Week: return d.ToString("M/d", CultureInfo.InvariantCulture);
                case ReportGrouping.Month: return d.ToString("MMM yy", CultureInfo.CurrentCulture);
                default: return row.Label;
            }
        }
    }
}
