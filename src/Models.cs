using System;
using System.Collections.Generic;
using System.Linq;

namespace TATimesheet
{
    /// <summary>One line on the timesheet: a block of time on one day.</summary>
    public class TimeEntry
    {
        public Guid Id = Guid.NewGuid();
        public DateTime Date;
        public string Project = "";
        public string Location = "";
        public string Start = "";   // "HH:mm" or empty
        public string End = "";     // "HH:mm" or empty
        public double? Hours;       // what gets billed; normally End - Start, but can be typed by hand
        public string Notes = "";   // private notes, never printed on the customer timesheet

        public TimeEntry Clone()
        {
            return (TimeEntry)MemberwiseClone();
        }

        /// <summary>Hours implied by Start/End, or null when either is missing.</summary>
        public double? ComputedHours
        {
            get { return TimeUtil.ComputeHours(Start, End); }
        }

        /// <summary>True when the billed hours don't match the start/end times.</summary>
        public bool HoursMismatch
        {
            get
            {
                double? c = ComputedHours;
                return c.HasValue && Hours.HasValue && Math.Abs(c.Value - Hours.Value) > 0.005;
            }
        }

        public int SortMinutes
        {
            get
            {
                int m;
                return TimeUtil.TryParseTime(Start, out m) ? m : 9999;
            }
        }

        public bool IsBlank
        {
            get
            {
                return Project.Trim().Length == 0 && Location.Trim().Length == 0 && Start.Length == 0
                    && End.Length == 0 && !Hours.HasValue && Notes.Trim().Length == 0;
            }
        }
    }

    /// <summary>Name/customer printed at the top of one pay period's timesheet.</summary>
    public class PeriodHeader
    {
        public DateTime Start;
        public string Name = "";
        public string Customer = "";
    }

    public class AppSettings
    {
        public string DefaultName = "";
        public int PeriodWeeks = 2;
        public DateTime PeriodAnchor = new DateTime(2026, 10, 26); // a Monday that starts a pay period
        public int TimeStepMinutes = 15;
        public string ExportFolder = "";
        public string FilePrefix = "TA_Timesheet_";
        public string LogoPath = "";
        public bool OpenAfterExport = true;
        public string WindowBounds = "";  // "x,y,w,h,maximized"
    }

    public class AppData
    {
        public List<TimeEntry> Entries = new List<TimeEntry>();
        public List<PeriodHeader> Headers = new List<PeriodHeader>();
        public AppSettings Settings = new AppSettings();

        // Dropdown history, most recently used first.
        public List<string> Names = new List<string>();
        public List<string> Customers = new List<string>();
        public List<string> Projects = new List<string>();
        public List<string> Locations = new List<string>();

        /// <summary>What a brand new install starts with (taken from Ben's current timesheet).</summary>
        public static AppData CreateDefault()
        {
            var d = new AppData();
            d.Settings.DefaultName = "Benjamin Turner";
            d.Names.Add("Benjamin Turner");
            d.Customers.Add("Mission Automation");
            d.Projects.Add("Motherson 260104-2");
            d.Locations.Add("Newark, CA");
            return d;
        }

        public PeriodHeader HeaderFor(Period p)
        {
            return Headers.FirstOrDefault(h => p.Contains(h.Start));
        }

        /// <summary>Name/customer to show for a period that has no header saved yet.</summary>
        public PeriodHeader DefaultHeaderFor(Period p)
        {
            var h = new PeriodHeader();
            h.Start = p.Start;
            var previous = Headers.Where(x => x.Start <= p.Start).OrderByDescending(x => x.Start).FirstOrDefault()
                        ?? Headers.OrderByDescending(x => x.Start).FirstOrDefault();
            h.Name = previous != null && previous.Name.Length > 0 ? previous.Name
                   : Settings.DefaultName.Length > 0 ? Settings.DefaultName
                   : Names.FirstOrDefault() ?? "";
            h.Customer = previous != null && previous.Customer.Length > 0 ? previous.Customer
                       : Customers.FirstOrDefault() ?? "";
            return h;
        }

        public string LastLocationFor(string project)
        {
            var e = Entries.Where(x => string.Equals(x.Project, project, StringComparison.OrdinalIgnoreCase)
                                       && x.Location.Length > 0)
                           .OrderByDescending(x => x.Date).FirstOrDefault();
            return e != null ? e.Location : null;
        }

        public TimeEntry LastEntryBefore(DateTime date)
        {
            return Entries.Where(x => x.Date < date.Date)
                          .OrderByDescending(x => x.Date).ThenByDescending(x => x.SortMinutes)
                          .FirstOrDefault();
        }

        public IEnumerable<TimeEntry> EntriesBetween(DateTime from, DateTime to)
        {
            return Entries.Where(e => e.Date >= from.Date && e.Date <= to.Date)
                          .OrderBy(e => e.Date).ThenBy(e => e.SortMinutes).ThenBy(e => e.Project);
        }

        /// <summary>Customer billed for a given day (from that pay period's header).</summary>
        public string CustomerOn(DateTime date)
        {
            var p = Period.Containing(date, Settings);
            var h = HeaderFor(p) ?? DefaultHeaderFor(p);
            return h.Customer;
        }

        public string NameOn(DateTime date)
        {
            var p = Period.Containing(date, Settings);
            var h = HeaderFor(p) ?? DefaultHeaderFor(p);
            return h.Name;
        }
    }

    /// <summary>A pay period: one or more Monday-to-Sunday style weeks starting at Start.</summary>
    public struct Period
    {
        public readonly DateTime Start;
        public readonly int Weeks;

        public Period(DateTime start, int weeks)
        {
            Start = start.Date;
            Weeks = Math.Max(1, weeks);
        }

        public DateTime End { get { return Start.AddDays(Weeks * 7 - 1); } }
        public int Days { get { return Weeks * 7; } }
        public Period Next { get { return new Period(Start.AddDays(Days), Weeks); } }
        public Period Previous { get { return new Period(Start.AddDays(-Days), Weeks); } }

        public bool Contains(DateTime d)
        {
            d = d.Date;
            return d >= Start && d <= End;
        }

        public DateTime WeekStart(int week)
        {
            return Start.AddDays(7 * week);
        }

        public static Period Containing(DateTime date, AppSettings s)
        {
            int len = 7 * Math.Max(1, s.PeriodWeeks);
            int diff = (int)(date.Date - s.PeriodAnchor.Date).TotalDays;
            int k = diff >= 0 ? diff / len : -((-diff + len - 1) / len);
            return new Period(s.PeriodAnchor.Date.AddDays((double)k * len), s.PeriodWeeks);
        }

        public override string ToString()
        {
            return string.Format("{0:ddd} {1} \u2013 {2:ddd} {3}", Start, TimeUtil.FormatDate(Start), End, TimeUtil.FormatDate(End));
        }
    }

    /// <summary>Most-recently-used lists that feed the dropdowns.</summary>
    public static class History
    {
        public const int Max = 75;

        /// <summary>Moves value to the top of the list (adding it if new).</summary>
        public static void Touch(List<string> list, string value)
        {
            if (value == null) return;
            value = value.Trim();
            if (value.Length == 0) return;
            list.RemoveAll(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, value);
            if (list.Count > Max) list.RemoveRange(Max, list.Count - Max);
        }

        /// <summary>Adds value to the bottom of the list if it isn't there already.</summary>
        public static void AddIfMissing(List<string> list, string value)
        {
            if (value == null) return;
            value = value.Trim();
            if (value.Length == 0) return;
            if (list.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))) return;
            if (list.Count < Max) list.Add(value);
        }
    }
}
