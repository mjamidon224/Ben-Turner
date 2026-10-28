using System;
using System.IO;
using System.Linq;

namespace TATimesheet.Tests
{
    /// <summary>
    /// Headless checks for the non-UI code. Build with build.sh, then:
    ///   mono SmokeTest.exe path\to\TA_Timesheet_102626.docx outputFolder
    /// </summary>
    static class SmokeTest
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) failures++;
        }

        static int Main(string[] args)
        {
            string docx = args[0];
            string outDir = args[1];
            Directory.CreateDirectory(outDir);

            Console.WriteLine("Time parsing");
            int m;
            Check(TimeUtil.TryParseTime("5", out m) && m == 300, "5 -> 05:00");
            Check(TimeUtil.TryParseTime("530", out m) && m == 330, "530 -> 05:30");
            Check(TimeUtil.TryParseTime("1715", out m) && m == 1035, "1715 -> 17:15");
            Check(TimeUtil.TryParseTime("5:30p", out m) && m == 1050, "5:30p -> 17:30");
            Check(TimeUtil.TryParseTime("12 am", out m) && m == 0, "12 am -> 00:00");
            Check(TimeUtil.TryParseTime("24:00", out m) && m == 1440, "24:00");
            Check(!TimeUtil.TryParseTime("25:00", out m), "25:00 rejected");
            Check(!TimeUtil.TryParseTime("abc", out m), "abc rejected");
            Check(TimeUtil.ComputeHours("06:00", "17:15") == 11.25, "06:00-17:15 = 11.25");
            Check(TimeUtil.ComputeHours("22:00", "06:00") == 8, "overnight 22:00-06:00 = 8");
            Check(TimeUtil.FormatHours(14.5) == "14.5" && TimeUtil.FormatHours(8) == "8", "hours format");

            Console.WriteLine("Periods");
            var settings = new AppSettings();
            var p = Period.Containing(new DateTime(2026, 10, 28), settings);
            Check(p.Start == new DateTime(2026, 10, 26) && p.End == new DateTime(2026, 11, 8), "10/28/26 is in 10/26-11/8");
            p = Period.Containing(new DateTime(2026, 10, 25), settings);
            Check(p.Start == new DateTime(2026, 10, 12), "10/25/26 is in the period before");

            Console.WriteLine("Import " + Path.GetFileName(docx));
            var data = AppData.CreateDefault();
            data.Entries.Clear();
            var r = Importer.ReadDocx(docx);
            Check(r.Name == "Benjamin Turner", "name: " + r.Name);
            Check(r.Customer == "Mission Automation", "customer: " + r.Customer);
            Check(r.Entries.Count == 10, "entries read: " + r.Entries.Count);
            var first = r.Entries.OrderBy(e => e.Date).First();
            Check(first.Project == "Motherson 260104-2" && first.Location == "Newark, CA" && first.Start == "06:00"
                  && first.End == "17:15" && first.Hours == 11.25, "first row intact");
            var summary = new MergeSummary();
            Importer.Merge(data, r, summary);
            Importer.Merge(data, Importer.ReadDocx(docx), summary);
            Check(summary.Added == 10 && summary.Duplicates == 10, "re-import skips duplicates");
            var mismatch = data.Entries.Where(e => e.HoursMismatch).ToList();
            Check(mismatch.Count == 1 && mismatch[0].Date == new DateTime(2026, 11, 5), "flags 11/5 (05:00-19:25 is not 14.25 h)");

            p = Period.Containing(new DateTime(2026, 10, 26), data.Settings);
            var header = data.HeaderFor(p);
            Check(header != null && header.Customer == "Mission Automation", "period header saved");
            var sheet = TimesheetSheet.Build(data, p, header.Name, header.Customer);
            Check(sheet.Weeks.Count == 2 && sheet.Weeks[0].Rows.Count == 7, "two weeks, seven rows each");
            Check(sheet.Weeks[0].Hours == 37.75 && sheet.Weeks[1].Hours == 67.75 && sheet.TotalHours == 105.5,
                  string.Format("totals {0} / {1} / {2}", sheet.Weeks[0].Hours, sheet.Weeks[1].Hours, sheet.TotalHours));

            Console.WriteLine("Save / load");
            string dataFile = Path.Combine(outDir, "roundtrip.xml");
            DataStore.Write(data, dataFile);
            var back = DataStore.Read(dataFile);
            Check(back.Entries.Count == data.Entries.Count && back.Headers.Count == data.Headers.Count
                  && back.Projects.SequenceEqual(data.Projects), "xml round trip");

            Console.WriteLine("Exports");
            string docxOut = Path.Combine(outDir, "TA_Timesheet_102626.docx");
            DocxExporter.Export(sheet, docxOut, null);
            Check(File.Exists(docxOut), "docx written");
            var again = Importer.ReadDocx(docxOut);
            Check(again.Entries.Count == 10 && again.Name == "Benjamin Turner", "exported docx reads back");

            var one = TimesheetSheet.Build(data, new Period(p.Start, 1), header.Name, header.Customer);
            DocxExporter.Export(one, Path.Combine(outDir, "one_week.docx"), null);

            string pdfOut = Path.Combine(outDir, "TA_Timesheet_102626.pdf");
            PdfExporter.Export(sheet, AppResources.DefaultLogoPng(), pdfOut);
            Check(File.ReadAllBytes(pdfOut).Length > 1000, "pdf written");

            // Lots of entries on one day forces the PDF table onto extra pages.
            var busy = AppData.CreateDefault();
            for (int i = 0; i < 12; i++)
                busy.Entries.Add(new TimeEntry { Date = p.Start.AddDays(1), Project = "Long project name number " + i, Location = "Somewhere, CA",
                                                 Start = "08:00", End = "09:00", Hours = 1 });
            PdfExporter.Export(TimesheetSheet.Build(busy, p, "Benjamin Turner", "A customer with a long name, Inc."), null,
                               Path.Combine(outDir, "busy.pdf"));

            string csvOut = Path.Combine(outDir, "entries.csv");
            DataExporters.ExportCsv(data, p.Start, p.End, csvOut);
            var csvBack = Importer.ReadCsv(csvOut);
            Check(csvBack.Entries.Count == 10 && csvBack.Customer == "Mission Automation"
                  && csvBack.Entries.Sum(e => e.Hours ?? 0) == 105.5, "csv round trip");

            string xlsxOut = Path.Combine(outDir, "entries.xlsx");
            DataExporters.ExportXlsx(data, p.Start, p.End, xlsxOut);
            Check(File.Exists(xlsxOut), "xlsx written");

            string logoFile = Path.Combine(outDir, "wide-logo.png");
            using (var bmp = new System.Drawing.Bitmap(400, 100))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp)) g.Clear(System.Drawing.Color.SteelBlue);
                bmp.Save(logoFile, System.Drawing.Imaging.ImageFormat.Png);
            }
            data.Settings.LogoPath = logoFile;
            DocxExporter.Export(sheet, Path.Combine(outDir, "custom_logo.docx"), AppResources.CustomLogoPng(data.Settings));

            // What Print / Print Preview draws, rendered to an image at 2x.
            byte[] logoPng = AppResources.DefaultLogoPng();
            using (var logo = System.Drawing.Image.FromStream(new MemoryStream(logoPng)))
            using (var bmp = new System.Drawing.Bitmap(1584, 1224))
            {
                var pages = SheetLayout.Build(sheet, logo.Width, logo.Height);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.White);
                    g.ScaleTransform(2, 2); // 2 px per point
                    LayoutPrintDocument.Draw(g, pages[0].Ops, logo, PdfExporter.PrintColor, "Arial", false);
                }
                bmp.Save(Path.Combine(outDir, "print-page1.png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            Console.WriteLine("Travel");
            var trip = data.Entries.OrderBy(e => e.Date).First();
            trip.Travel = true;
            DataStore.Write(data, dataFile);
            Check(DataStore.Read(dataFile).Entries.Count(e => e.Travel) == 1, "travel flag saved and loaded");
            var travelSheet = TimesheetSheet.Build(data, p, header.Name, header.Customer);
            Check(travelSheet.Weeks[0].Rows[0].Location == "Newark, CA\n(Travel)", "timesheet marks the travel row");
            string travelDocx = Path.Combine(outDir, "travel.docx");
            DocxExporter.Export(travelSheet, travelDocx, null);
            var travelBack = Importer.ReadDocx(travelDocx).Entries.OrderBy(e => e.Date).ToList();
            Check(travelBack[0].Travel && travelBack[0].Location == "Newark, CA" && !travelBack[1].Travel, "Word timesheet round-trips travel");
            PdfExporter.Export(travelSheet, AppResources.DefaultLogoPng(), Path.Combine(outDir, "travel.pdf"));
            DataExporters.ExportCsv(data, p.Start, p.End, csvOut);
            Check(Importer.ReadCsv(csvOut).Entries.Count(e => e.Travel) == 1, "CSV round-trips travel");

            Console.WriteLine("Weekly / bi-weekly");
            var weekly = new AppSettings { PeriodWeeks = 1 };
            var wp = Period.Containing(new DateTime(2026, 11, 4), weekly);
            Check(wp.Start == new DateTime(2026, 11, 2) && wp.End == new DateTime(2026, 11, 8), "weekly period 11/2-11/8");
            var weeklySheet = TimesheetSheet.Build(data, wp, header.Name, header.Customer);
            Check(weeklySheet.Weeks.Count == 1 && weeklySheet.TotalHours == 67.75, "weekly timesheet total 67.75");

            Console.WriteLine("Reports");
            var byPeriod = Report.Build(data, new DateTime(2026, 10, 1), new DateTime(2026, 11, 30), ReportGrouping.PayPeriod);
            Check(byPeriod.TotalHours == 105.5 && byPeriod.Rows.Sum(x => x.Hours) == 105.5, "report total 105.5");
            Check(byPeriod.Rows.Count == 5 && byPeriod.Rows.Count(x => x.Hours > 0) == 1, "pay-period buckets include empty periods");
            Check(byPeriod.TravelHours == 11.25 && byPeriod.DaysWorked == 9, "travel 11.25 h, 9 days worked");
            var byWeek = Report.Build(data, p.Start, p.End, ReportGrouping.Week);
            Check(byWeek.Rows.Count == 2 && byWeek.Rows[0].Hours == 37.75 && byWeek.Rows[1].Hours == 67.75, "by week 37.75 / 67.75");
            Check(byWeek.Busiest.Label == "Week of 11/2/26", "busiest week");
            var byProjectReport = Report.Build(data, p.Start, p.End, ReportGrouping.Project);
            Check(byProjectReport.Rows.Count == 1 && byProjectReport.Rows[0].Label == "Motherson 260104-2", "by project");
            var byMonth = Report.Build(data, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), ReportGrouping.Month);
            Check(byMonth.Rows.Count == 12 && byMonth.Rows[9].Hours == 37.75 && byMonth.Rows[10].Hours == 67.75, "by month Oct 37.75 / Nov 67.75");
            PdfExporter.ExportPages((w, h) => ReportLayout.Build(byMonth, w, h), AppResources.DefaultLogoPng(),
                Path.Combine(outDir, "report-month.pdf"), "Hours report", "Benjamin Turner");
            PdfExporter.ExportPages((w, h) => ReportLayout.Build(byProjectReport, w, h), AppResources.DefaultLogoPng(),
                Path.Combine(outDir, "report-project.pdf"), "Hours report", "Benjamin Turner");
            var longWeeks = Report.Build(data, new DateTime(2025, 1, 1), new DateTime(2026, 12, 31), ReportGrouping.Week);
            PdfExporter.ExportPages((w, h) => ReportLayout.Build(longWeeks, w, h), AppResources.DefaultLogoPng(),
                Path.Combine(outDir, "report-weeks-2y.pdf"), "Hours report", "Benjamin Turner");
            Check(ReportLayout.Build(longWeeks, 0, 0).Count > 1, "long report paginates");
            DataExporters.ExportReportXlsx(data, byMonth, Path.Combine(outDir, "report.xlsx"));
            DataExporters.ExportReportCsv(byMonth, Path.Combine(outDir, "report.csv"));
            Check(File.ReadAllLines(Path.Combine(outDir, "report.csv")).Length == 14, "report csv: header + 12 months + total");
            DataExporters.ExportXlsx(data, p.Start, p.End, Path.Combine(outDir, "entries-travel.xlsx"));

            var travelReport = Report.Build(data, p.Start, p.End, ReportGrouping.Travel);
            Check(travelReport.Rows.Count == 2 && travelReport.Rows[0].Label == "On site" && travelReport.Rows[0].Hours == 94.25
                  && travelReport.Rows[1].Label == "Travel" && travelReport.Rows[1].Hours == 11.25, "travel vs on-site: 94.25 / 11.25");
            Check(byWeek.Rows[0].OnSiteHours == 26.5 && byWeek.Rows[0].TravelHours == 11.25, "week 1 splits 26.5 on site / 11.25 travel");
            Check(ChartLayout.Build(byWeek, 0, 0, 600, 200, 9).OfType<FillOp>().Count(f => f.Ink == Ink.TravelBar) == 2,
                  "chart splits bars and adds a legend");
            PdfExporter.ExportPages((w, h) => ReportLayout.Build(travelReport, w, h), AppResources.DefaultLogoPng(),
                Path.Combine(outDir, "report-travel.pdf"), "Hours report", "Benjamin Turner");
            PdfExporter.ExportPages((w, h) => ReportLayout.Build(byWeek, w, h), AppResources.DefaultLogoPng(),
                Path.Combine(outDir, "report-week-split.pdf"), "Hours report", "Benjamin Turner");
            DataExporters.ExportReportCsv(travelReport, Path.Combine(outDir, "report-travel.csv"));
            Check(File.ReadAllLines(Path.Combine(outDir, "report-travel.csv"))[1].StartsWith("On site,94.25,94.25,0,"), "report csv has on-site column");

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            return failures == 0 ? 0 : 1;
        }
    }
}
