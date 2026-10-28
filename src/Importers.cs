using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TATimesheet
{
    public class ImportResult
    {
        public List<TimeEntry> Entries = new List<TimeEntry>();
        public string Name = "";
        public string Customer = "";
    }

    public class MergeSummary
    {
        public int Added, Duplicates, Files;
        public List<string> Problems = new List<string>();
    }

    public static class Importer
    {
        static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        /// <summary>
        /// Reads an existing Word timesheet (the TA_Timesheet layout): Name/Customer box plus
        /// any table with Date ... Hours columns. Blank days and total rows are skipped.
        /// </summary>
        public static ImportResult ReadDocx(string path)
        {
            XDocument doc;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var part = zip.GetEntry("word/document.xml");
                if (part == null) throw new InvalidDataException("Not a Word document.");
                using (var s = part.Open()) doc = XDocument.Load(s);
            }

            var result = new ImportResult();
            foreach (var table in doc.Descendants(W + "tbl"))
            {
                Dictionary<string, int> cols = null;
                foreach (var tr in table.Elements(W + "tr"))
                {
                    var cells = tr.Elements(W + "tc").Select(CellText).ToList();
                    if (cells.Count == 0) continue;

                    if (cells.Count >= 2)
                    {
                        string label = cells[0].TrimEnd(':').Trim().ToLowerInvariant();
                        if (label == "name" && cells[1].Length > 0) result.Name = cells[1];
                        else if (label == "customer" && cells[1].Length > 0) result.Customer = cells[1];
                    }

                    var header = HeaderMap(cells);
                    if (header != null) { cols = header; continue; }
                    if (cols == null) continue;

                    var e = RowToEntry(cells, cols);
                    if (e != null) result.Entries.Add(e);
                }
            }
            return result;
        }

        static string CellText(XElement tc)
        {
            var paras = tc.Descendants(W + "p")
                          .Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value)))
                          .Where(t => t.Trim().Length > 0);
            return Regex.Replace(string.Join(" ", paras), "\\s+", " ").Trim();
        }

        /// <summary>Recognises a header row and maps column names to positions.</summary>
        static Dictionary<string, int> HeaderMap(List<string> cells)
        {
            var map = new Dictionary<string, int>();
            for (int i = 0; i < cells.Count; i++)
            {
                string c = cells[i].ToLowerInvariant();
                string key = null;
                if (c == "date" || c == "day/date") key = "date";
                else if (c.StartsWith("project") || c == "job") key = "project";
                else if (c.StartsWith("location") || c == "site") key = "location";
                else if (c.StartsWith("start") || c == "time in" || c == "in") key = "start";
                else if (c.StartsWith("end") || c.StartsWith("stop") || c == "time out" || c == "out") key = "end";
                else if (c == "hours" || c == "hrs" || c == "total hours") key = "hours";
                else if (c.StartsWith("note") || c == "description") key = "notes";
                else if (c == "customer") key = "customer";
                else if (c == "name") key = "name";
                if (key != null && !map.ContainsKey(key)) map[key] = i;
            }
            return map.ContainsKey("date") && (map.ContainsKey("hours") || map.ContainsKey("start")) ? map : null;
        }

        static string Get(List<string> cells, Dictionary<string, int> cols, string key)
        {
            int i;
            return cols.TryGetValue(key, out i) && i < cells.Count ? (cells[i] ?? "").Trim() : "";
        }

        static TimeEntry RowToEntry(List<string> cells, Dictionary<string, int> cols)
        {
            DateTime date;
            if (!TimeUtil.TryParseDate(Get(cells, cols, "date"), out date)) return null;

            var e = new TimeEntry
            {
                Date = date,
                Project = Get(cells, cols, "project"),
                Location = Get(cells, cols, "location"),
                Start = TimeUtil.NormalizeTime(Get(cells, cols, "start")),
                End = TimeUtil.NormalizeTime(Get(cells, cols, "end")),
                Notes = Get(cells, cols, "notes")
            };
            double h;
            if (TimeUtil.TryParseHours(Get(cells, cols, "hours"), out h)) e.Hours = h;
            else e.Hours = e.ComputedHours;
            return e.IsBlank ? null : e;
        }

        // ---- CSV -------------------------------------------------------------------------------

        public static ImportResult ReadCsv(string path)
        {
            var result = new ImportResult();
            var rows = ParseCsv(File.ReadAllText(path, Encoding.UTF8));
            Dictionary<string, int> cols = null;
            foreach (var row in rows)
            {
                var cells = row.Select(c => c.Trim()).ToList();
                if (cols == null)
                {
                    cols = HeaderMap(cells);
                    continue;
                }
                var e = RowToEntry(cells, cols);
                if (e == null) continue;
                result.Entries.Add(e);
                if (result.Customer.Length == 0) result.Customer = Get(cells, cols, "customer");
                if (result.Name.Length == 0) result.Name = Get(cells, cols, "name");
            }
            if (cols == null) throw new InvalidDataException("Couldn't find a header row with Date and Hours columns.");
            return result;
        }

        public static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Length = 0; }
                else if (c == '\r') { }
                else if (c == '\n')
                {
                    row.Add(cell.ToString()); cell.Length = 0;
                    if (row.Any(x => x.Length > 0)) rows.Add(row);
                    row = new List<string>();
                }
                else if (c == '\uFEFF') { }
                else cell.Append(c);
            }
            row.Add(cell.ToString());
            if (row.Any(x => x.Length > 0)) rows.Add(row);
            return rows;
        }

        // ---- Merge -----------------------------------------------------------------------------

        static bool SameEntry(TimeEntry a, TimeEntry b)
        {
            return a.Date == b.Date
                && string.Equals(a.Start, b.Start, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.End, b.End, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Project.Trim(), b.Project.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Adds imported entries (skipping ones already there) and feeds the dropdown history.</summary>
        public static void Merge(AppData data, ImportResult r, MergeSummary summary)
        {
            summary.Files++;
            foreach (var e in r.Entries.OrderBy(x => x.Date))
            {
                if (data.Entries.Any(x => SameEntry(x, e))) { summary.Duplicates++; continue; }
                data.Entries.Add(e);
                summary.Added++;
                History.Touch(data.Projects, e.Project);
                History.Touch(data.Locations, e.Location);
            }

            if (r.Name.Length > 0)
            {
                History.Touch(data.Names, r.Name);
                if (data.Settings.DefaultName.Length == 0) data.Settings.DefaultName = r.Name;
            }
            History.Touch(data.Customers, r.Customer);

            // Remember name/customer for every pay period the file covers.
            if (r.Name.Length > 0 || r.Customer.Length > 0)
            {
                foreach (var start in r.Entries.Select(e => Period.Containing(e.Date, data.Settings).Start).Distinct())
                {
                    var p = new Period(start, data.Settings.PeriodWeeks);
                    if (data.HeaderFor(p) != null) continue;
                    data.Headers.Add(new PeriodHeader { Start = start, Name = r.Name, Customer = r.Customer });
                }
            }
        }
    }
}
