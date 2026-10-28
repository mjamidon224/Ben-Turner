using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TATimesheet
{
    /// <summary>
    /// Rebuilds timesheet rows from a PDF's positioned text: finds the Date ... Hours header row,
    /// anchors one row on each date in the Date column, then drops every other piece of text into
    /// the nearest row and column. Works for PDFs saved from Word, printed to PDF, or exported here.
    /// </summary>
    public static class PdfTimesheetReader
    {
        public const long MaxBytes = 30 * 1024 * 1024;

        public static ImportResult Read(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > MaxBytes) throw new InvalidDataException("Too big to be a timesheet (" + (info.Length / 1048576) + " MB).");
            var doc = PdfDocument.Load(File.ReadAllBytes(path));
            if (doc.Encrypted) throw new InvalidDataException("This PDF is password-protected.");
            var runs = PdfTextExtractor.Extract(doc);
            if (runs.Count == 0) throw new InvalidDataException("No text in this PDF (probably a scan or picture).");
            return FromRuns(runs);
        }

        public static ImportResult FromRuns(List<PdfTextRun> runs)
        {
            var result = new ImportResult();
            foreach (var page in runs.Select(r => r.Page).Distinct().OrderBy(p => p))
            {
                var pageRuns = runs.Where(r => r.Page == page).ToList();
                if (result.Name.Length == 0) result.Name = LabelValue(pageRuns, IsNameLabel, "Name");
                if (result.Customer.Length == 0) result.Customer = LabelValue(pageRuns, IsCustomerLabel, "Customer");
                ReadTables(pageRuns, result);
            }
            return result;
        }

        static bool IsNameLabel(string t) { return t == "name" || t == "name:"; }
        static bool IsCustomerLabel(string t) { return t == "customer" || t == "customer:" || t == "custom"; }

        /// <summary>Text to the right of a label like "Name" (or after it, if the PDF ran them together).</summary>
        static string LabelValue(List<PdfTextRun> runs, Func<string, bool> isLabel, string word)
        {
            var ordered = runs.OrderByDescending(r => r.Y).ThenBy(r => r.X).ToList();
            foreach (var label in ordered.Where(r => isLabel(r.Text.Trim().ToLowerInvariant())))
            {
                // The value sits right of the label, level with it, or centred beside a label that wrapped.
                var candidates = runs.Where(r => r != label && r.X > label.EndX && r.X < label.EndX + 420
                                                 && Math.Abs(r.Y - label.Y) <= label.Size * 1.1f).ToList();
                if (candidates.Count == 0) continue;
                var nearest = candidates.OrderBy(r => Math.Abs(r.Y - label.Y)).ThenBy(r => r.X).First();
                var line = candidates.Where(r => Math.Abs(r.Y - nearest.Y) < nearest.Size * 0.4f).OrderBy(r => r.X);
                return string.Join(" ", line.Select(r => r.Text)).Trim();
            }
            // Label and value run together ("Customer Mission Automation"), but not the page title.
            string prefix = word.ToLowerInvariant() + " ";
            foreach (var r in ordered)
            {
                string t = r.Text.Trim();
                string lower = t.ToLowerInvariant();
                if (lower.StartsWith(prefix) && !lower.Contains("timesheet"))
                    return t.Substring(word.Length).TrimStart(':', ' ').Trim();
            }
            return "";
        }

        static void ReadTables(List<PdfTextRun> runs, ImportResult result)
        {
            var headers = runs.Where(r => r.Text.Trim().TrimEnd(':').Equals("Date", StringComparison.OrdinalIgnoreCase))
                              .OrderByDescending(r => r.Y).ToList();
            for (int h = 0; h < headers.Count; h++)
            {
                var header = headers[h];
                var headerLine = runs.Where(r => Math.Abs(r.Y - header.Y) < header.Size * 0.5f).OrderBy(r => r.X).ToList();
                var cols = Importer.HeaderMap(headerLine.Select(r => r.Text.Trim()).ToList());
                if (cols == null) continue;
                var centers = headerLine.Select(r => r.CenterX).ToArray();
                float floor = h + 1 < headers.Count ? headers[h + 1].Y + header.Size : float.MinValue;
                float top = header.Y - header.Size * 0.5f;
                Func<PdfTextRun, int> column = r =>
                {
                    int best = 0;
                    for (int i = 1; i < centers.Length; i++)
                        if (Math.Abs(r.CenterX - centers[i]) < Math.Abs(r.CenterX - centers[best])) best = i;
                    return best;
                };

                int dateCol = cols["date"];
                var body = runs.Where(r => r.Y < top && r.Y > floor).ToList();
                DateTime ignored;
                var anchors = body.Where(r => column(r) == dateCol && TimeUtil.TryParseDate(r.Text, out ignored))
                                  .OrderByDescending(r => r.Y).ToList();
                if (anchors.Count == 0) continue;

                // Rows are evenly spaced; anything further than about half a row from a date isn't in it.
                float spacing = anchors.Count > 1
                    ? Median(anchors.Zip(anchors.Skip(1), (a, b) => a.Y - b.Y).ToList())
                    : header.Y - anchors[0].Y;
                float maxDist = Math.Max(header.Size * 0.8f, Math.Min(spacing * 0.55f, header.Size * 2.5f));

                var cells = anchors.ToDictionary(a => a, a => new List<PdfTextRun>[centers.Length]);
                foreach (var r in body)
                {
                    var anchor = anchors.OrderBy(a => Math.Abs(a.Y - r.Y)).First();
                    if (Math.Abs(anchor.Y - r.Y) > maxDist) continue;
                    int c = column(r);
                    if (cells[anchor][c] == null) cells[anchor][c] = new List<PdfTextRun>();
                    cells[anchor][c].Add(r);
                }

                foreach (var a in anchors)
                {
                    var row = cells[a].Select(list => list == null ? ""
                        : string.Join(" ", list.OrderByDescending(r => Math.Round(r.Y, 0)).ThenBy(r => r.X).Select(r => r.Text.Trim()))).ToList();
                    row[dateCol] = a.Text.Trim();
                    var e = Importer.RowToEntry(row, cols);
                    if (e != null) result.Entries.Add(e);
                }
            }
        }

        static float Median(List<float> values)
        {
            values.Sort();
            return values.Count == 0 ? 0 : values[values.Count / 2];
        }
    }
}
