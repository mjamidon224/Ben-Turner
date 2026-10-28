using System;

namespace TATimesheet
{
    /// <summary>
    /// Differences between the real app and the TEST copy (built with TEST_BUILD defined).
    /// The test copy keeps separate practice data, starts with sample entries and can run
    /// next to the real app.
    /// </summary>
    public static class BuildInfo
    {
#if TEST_BUILD
        public static readonly bool IsTest = true;
        public static readonly string AppName = "TA Timesheet TEST";
        public static readonly string DataFolderName = "TA Timesheet TEST";
        public static readonly string MutexName = "TATimesheet.Test.SingleInstance";
#else
        public static readonly bool IsTest = false;
        public static readonly string AppName = "TA Timesheet";
        public static readonly string DataFolderName = "TA Timesheet";
        public static readonly string MutexName = "TATimesheet.SingleInstance";
#endif

        /// <summary>The rows from TA_Timesheet_102626.docx, so the test copy has something to play with.</summary>
        public static void AddSampleEntries(AppData d)
        {
            var rows = new[]
            {
                new[] { "2026-10-26", "06:00", "17:15", "11.25" },
                new[] { "2026-10-27", "05:00", "19:30", "14.5" },
                new[] { "2026-10-28", "07:00", "15:00", "8" },
                new[] { "2026-10-29", "07:00", "11:00", "4" },
                new[] { "2026-11-02", "06:15", "16:15", "10" },
                new[] { "2026-11-03", "05:00", "18:45", "13.75" },
                new[] { "2026-11-04", "05:00", "17:45", "12.75" },
                new[] { "2026-11-05", "05:00", "19:25", "14.25" }, // hours don't match the times: shows the orange flag
                new[] { "2026-11-06", "05:00", "22:00", "17" }
            };
            foreach (var r in rows)
            {
                d.Entries.Add(new TimeEntry
                {
                    Date = DateTime.ParseExact(r[0], "yyyy-MM-dd", TimeUtil.Inv),
                    Project = "Motherson 260104-2",
                    Location = "Newark, CA",
                    Start = r[1],
                    End = r[2],
                    Hours = double.Parse(r[3], TimeUtil.Inv)
                });
            }
            d.Headers.Add(new PeriodHeader { Start = new DateTime(2026, 10, 26), Name = "Benjamin Turner", Customer = "Mission Automation" });
        }
    }
}
