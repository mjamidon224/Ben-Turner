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
                    TimesheetPrintDocument.Draw(g, pages[0], logo);
                }
                bmp.Save(Path.Combine(outDir, "print-page1.png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            return failures == 0 ? 0 : 1;
        }
    }
}
