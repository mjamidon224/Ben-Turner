using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

namespace TATimesheet
{
    /// <summary>Spreadsheet-friendly exports of raw entries for any date range (CSV and Excel).</summary>
    public static class DataExporters
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static readonly string[] Columns =
            { "Date", "Day", "Project", "Location", "Start", "End", "Hours", "Travel", "Notes", "Customer", "Name" };

        // ---- CSV -------------------------------------------------------------------------------

        static string Csv(string s)
        {
            s = s ?? "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        public static void ExportCsv(AppData data, DateTime from, DateTime to, string path)
        {
            var b = new StringBuilder();
            b.AppendLine(string.Join(",", Columns));
            foreach (var e in data.EntriesBetween(from, to))
            {
                b.AppendLine(string.Join(",", new[]
                {
                    e.Date.ToString("M/d/yyyy", Inv), e.Date.ToString("ddd", Inv), Csv(e.Project), Csv(e.Location),
                    e.Start, e.End, TimeUtil.FormatHours(e.Hours), e.Travel ? "Yes" : "", Csv(e.Notes),
                    Csv(data.CustomerOn(e.Date)), Csv(data.NameOn(e.Date))
                }));
            }
            string tmp = path + ".writing";
            File.WriteAllText(tmp, b.ToString(), new UTF8Encoding(true)); // BOM so Excel reads it as UTF-8
            DocxExporter.ReplaceFile(tmp, path);
        }

        // ---- Excel (.xlsx) -----------------------------------------------------------------

        // Style indexes into the cellXfs list in StylesXml.
        const int SBold = 1, SHeader = 2, SDate = 3, STime = 4, SText = 5, SNum = 6, STotalLabel = 7, STotalNum = 8, STitle = 9;

        class Sheet
        {
            public string Name;
            public StringBuilder Rows = new StringBuilder();
            public int RowCount;
            public double[] Widths;
            public int FreezeRows;
            StringBuilder current;

            public void StartRow()
            {
                RowCount++;
                current = new StringBuilder();
            }

            public void EndRow()
            {
                Rows.AppendFormat("<row r=\"{0}\">{1}</row>", RowCount, current);
            }

            string Ref(int col) { return ColName(col) + RowCount; }

            public void Text(int col, string s, int style)
            {
                if (string.IsNullOrEmpty(s)) { Blank(col, style); return; }
                current.AppendFormat("<c r=\"{0}\" s=\"{1}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{2}</t></is></c>",
                    Ref(col), style, SecurityElement.Escape(s));
            }

            public void Number(int col, double v, int style)
            {
                current.AppendFormat("<c r=\"{0}\" s=\"{1}\"><v>{2}</v></c>", Ref(col), style, v.ToString("R", Inv));
            }

            public void Formula(int col, string f, double cached, int style)
            {
                current.AppendFormat("<c r=\"{0}\" s=\"{1}\"><f>{2}</f><v>{3}</v></c>", Ref(col), style, f, cached.ToString("R", Inv));
            }

            public void Blank(int col, int style)
            {
                if (style != 0) current.AppendFormat("<c r=\"{0}\" s=\"{1}\"/>", Ref(col), style);
            }
        }

        static string ColName(int col)
        {
            string s = "";
            for (col++; col > 0; col = (col - 1) / 26) s = (char)('A' + (col - 1) % 26) + s;
            return s;
        }

        static double ExcelDate(DateTime d) { return (d.Date - new DateTime(1899, 12, 30)).TotalDays; }

        public static void ExportXlsx(AppData data, DateTime from, DateTime to, string path)
        {
            var entries = data.EntriesBetween(from, to).ToList();

            // Sheet 1: every entry.
            var sh = new Sheet { Name = "Entries", Widths = new double[] { 11, 6, 26, 18, 8, 8, 8, 8, 40, 22, 20 } };
            sh.StartRow(); sh.Text(0, "Timesheet entries", STitle); sh.EndRow();
            sh.StartRow();
            sh.Text(0, string.Format("{0} to {1}", from.ToString("M/d/yyyy", Inv), to.ToString("M/d/yyyy", Inv)), 0);
            sh.EndRow();
            sh.StartRow(); sh.EndRow();
            sh.StartRow();
            for (int i = 0; i < Columns.Length; i++) sh.Text(i, Columns[i], SHeader);
            sh.EndRow();
            sh.FreezeRows = sh.RowCount;
            int firstData = sh.RowCount + 1;
            foreach (var e in entries)
            {
                sh.StartRow();
                sh.Number(0, ExcelDate(e.Date), SDate);
                sh.Text(1, e.Date.ToString("ddd", Inv), SText);
                sh.Text(2, e.Project, SText);
                sh.Text(3, e.Location, SText);
                int m;
                if (TimeUtil.TryParseTime(e.Start, out m)) sh.Number(4, m / 1440.0, STime); else sh.Text(4, e.Start, SText);
                if (TimeUtil.TryParseTime(e.End, out m)) sh.Number(5, m / 1440.0, STime); else sh.Text(5, e.End, SText);
                if (e.Hours.HasValue) sh.Number(6, e.Hours.Value, SNum); else sh.Blank(6, SNum);
                if (e.Travel) sh.Text(7, "Yes", SNum); else sh.Blank(7, SNum);
                sh.Text(8, e.Notes, SText);
                sh.Text(9, data.CustomerOn(e.Date), SText);
                sh.Text(10, data.NameOn(e.Date), SText);
                sh.EndRow();
            }
            int lastData = sh.RowCount;
            double total = Math.Round(entries.Sum(e => e.Hours ?? 0), 2);
            sh.StartRow();
            sh.Text(5, "Total", STotalLabel);
            if (lastData >= firstData) sh.Formula(6, string.Format("SUM(G{0}:G{1})", firstData, lastData), total, STotalNum);
            else sh.Number(6, 0, STotalNum);
            sh.EndRow();

            // Sheet 2: totals by project and by week (handy for invoicing).
            var byProject = Report.Build(data, from, to, ReportGrouping.Project);
            var byWeek = Report.Build(data, from, to, ReportGrouping.Week);
            var sum = new Sheet { Name = "Summary", Widths = new double[] { 26, 8, 10, 10, 4, 18, 10, 10 } };
            sum.StartRow(); sum.Text(0, "Hours summary", STitle); sum.EndRow();
            sum.StartRow(); sum.EndRow();
            sum.StartRow();
            sum.Text(0, "Project", SHeader); sum.Text(1, "Days", SHeader); sum.Text(2, "Hours", SHeader); sum.Text(3, "Travel h", SHeader);
            sum.Text(5, "Week", SHeader); sum.Text(6, "Hours", SHeader); sum.Text(7, "Travel h", SHeader);
            sum.EndRow();
            var weeks = byWeek.Rows.Where(r => r.Hours > 0).ToList();
            int rows = Math.Max(byProject.Rows.Count, weeks.Count);
            for (int i = 0; i < rows; i++)
            {
                sum.StartRow();
                if (i < byProject.Rows.Count)
                {
                    var r = byProject.Rows[i];
                    sum.Text(0, r.Label, SText);
                    sum.Number(1, r.Days, SNum);
                    sum.Number(2, r.Hours, SNum);
                    sum.Number(3, r.TravelHours, SNum);
                }
                if (i < weeks.Count)
                {
                    sum.Text(5, weeks[i].Label, SText);
                    sum.Number(6, weeks[i].Hours, SNum);
                    sum.Number(7, weeks[i].TravelHours, SNum);
                }
                sum.EndRow();
            }
            sum.StartRow();
            sum.Text(1, "Total", STotalLabel);
            sum.Number(2, total, STotalNum);
            sum.Number(3, byProject.TravelHours, STotalNum);
            sum.Text(5, "Total", STotalLabel);
            sum.Number(6, total, STotalNum);
            sum.Number(7, byWeek.TravelHours, STotalNum);
            sum.EndRow();

            WriteWorkbook(path, new[] { sh, sum });
        }

        // ---- Reports ---------------------------------------------------------------------------

        public static readonly string[] ReportColumns = { "Hours", "On-site h", "Travel h", "Days worked", "Avg h/day", "% of total" };

        static double Share(Report r, ReportRow row)
        {
            return r.TotalHours > 0 ? Math.Round(100 * row.Hours / r.TotalHours, 1) : 0;
        }

        public static void ExportReportCsv(Report r, string path)
        {
            var b = new StringBuilder();
            b.AppendLine(Csv(r.GroupingName) + "," + string.Join(",", ReportColumns));
            foreach (var row in r.TableRows)
                b.AppendLine(string.Join(",", new[]
                {
                    Csv(row.Label), TimeUtil.FormatHours(row.Hours), TimeUtil.FormatHours(row.OnSiteHours), TimeUtil.FormatHours(row.TravelHours),
                    row.Days.ToString(Inv), TimeUtil.FormatHours(row.AvgPerDay), Share(r, row).ToString("0.#", Inv)
                }));
            b.AppendLine(string.Join(",", new[]
            {
                "Total", TimeUtil.FormatHours(r.TotalHours), TimeUtil.FormatHours(r.OnSiteHours), TimeUtil.FormatHours(r.TravelHours),
                r.DaysWorked.ToString(Inv), TimeUtil.FormatHours(r.AvgPerDay), r.TotalHours > 0 ? "100" : "0"
            }));
            string tmp = path + ".writing";
            File.WriteAllText(tmp, b.ToString(), new UTF8Encoding(true));
            DocxExporter.ReplaceFile(tmp, path);
        }

        /// <summary>The report table, plus every entry it was built from on a second sheet.</summary>
        public static void ExportReportXlsx(AppData data, Report r, string path)
        {
            var sh = new Sheet { Name = "Report", Widths = new double[] { 26, 10, 10, 10, 12, 11, 11 } };
            sh.StartRow(); sh.Text(0, "Hours report by " + r.GroupingName.ToLowerInvariant(), STitle); sh.EndRow();
            sh.StartRow(); sh.Text(0, r.RangeText + (r.Name.Length > 0 ? "   " + r.Name : ""), 0); sh.EndRow();
            sh.StartRow(); sh.EndRow();
            sh.StartRow();
            sh.Text(0, r.GroupingName, SHeader);
            for (int i = 0; i < ReportColumns.Length; i++) sh.Text(i + 1, ReportColumns[i], SHeader);
            sh.EndRow();
            sh.FreezeRows = sh.RowCount;
            int first = sh.RowCount + 1;
            foreach (var row in r.TableRows)
            {
                sh.StartRow();
                sh.Text(0, row.Label, SText);
                sh.Number(1, row.Hours, SNum);
                sh.Number(2, row.OnSiteHours, SNum);
                sh.Number(3, row.TravelHours, SNum);
                sh.Number(4, row.Days, SNum);
                sh.Number(5, row.AvgPerDay, SNum);
                sh.Number(6, Share(r, row), SNum);
                sh.EndRow();
            }
            int last = sh.RowCount;
            sh.StartRow();
            sh.Text(0, "Total", STotalLabel);
            if (last >= first)
            {
                sh.Formula(1, string.Format("SUM(B{0}:B{1})", first, last), r.TotalHours, STotalNum);
                sh.Formula(2, string.Format("SUM(C{0}:C{1})", first, last), r.OnSiteHours, STotalNum);
                sh.Formula(3, string.Format("SUM(D{0}:D{1})", first, last), r.TravelHours, STotalNum);
            }
            else
            {
                sh.Number(1, 0, STotalNum);
                sh.Number(2, 0, STotalNum);
                sh.Number(3, 0, STotalNum);
            }
            sh.Number(4, r.DaysWorked, STotalNum);
            sh.Number(5, r.AvgPerDay, STotalNum);
            sh.Number(6, r.TotalHours > 0 ? 100 : 0, STotalNum);
            sh.EndRow();

            var entries = new Sheet { Name = "Entries", Widths = new double[] { 11, 6, 26, 18, 8, 8, 8, 8, 40 } };
            entries.StartRow();
            for (int i = 0; i < 9; i++) entries.Text(i, Columns[i], SHeader);
            entries.EndRow();
            entries.FreezeRows = 1;
            foreach (var e in data.EntriesBetween(r.From, r.To))
            {
                entries.StartRow();
                entries.Number(0, ExcelDate(e.Date), SDate);
                entries.Text(1, e.Date.ToString("ddd", Inv), SText);
                entries.Text(2, e.Project, SText);
                entries.Text(3, e.Location, SText);
                entries.Text(4, e.Start, SNum);
                entries.Text(5, e.End, SNum);
                if (e.Hours.HasValue) entries.Number(6, e.Hours.Value, SNum); else entries.Blank(6, SNum);
                if (e.Travel) entries.Text(7, "Yes", SNum); else entries.Blank(7, SNum);
                entries.Text(8, e.Notes, SText);
                entries.EndRow();
            }
            WriteWorkbook(path, new[] { sh, entries });
        }

        static string SheetXml(Sheet s)
        {
            var b = new StringBuilder();
            b.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            b.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            b.Append("<sheetViews><sheetView workbookViewId=\"0\">");
            if (s.FreezeRows > 0)
                b.AppendFormat("<pane ySplit=\"{0}\" topLeftCell=\"A{1}\" activePane=\"bottomLeft\" state=\"frozen\"/>", s.FreezeRows, s.FreezeRows + 1);
            b.Append("</sheetView></sheetViews>");
            b.Append("<cols>");
            for (int i = 0; i < s.Widths.Length; i++)
                b.AppendFormat(Inv, "<col min=\"{0}\" max=\"{0}\" width=\"{1}\" customWidth=\"1\"/>", i + 1, s.Widths[i]);
            b.Append("</cols>");
            b.Append("<sheetData>").Append(s.Rows).Append("</sheetData>");
            b.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");
            b.Append("<pageSetup orientation=\"landscape\"/>");
            b.Append("</worksheet>");
            return b.ToString();
        }

        const string StylesXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"m/d/yy\"/><numFmt numFmtId=\"165\" formatCode=\"hh:mm\"/></numFmts>" +
            "<fonts count=\"4\">" +
            "<font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
            "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
            "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
            "<font><b/><sz val=\"14\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
            "</fonts>" +
            "<fills count=\"4\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF333D45\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE3F5E6\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
            "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
            "<border><left style=\"thin\"><color rgb=\"FFBFBFBF\"/></left><right style=\"thin\"><color rgb=\"FFBFBFBF\"/></right>" +
            "<top style=\"thin\"><color rgb=\"FFBFBFBF\"/></top><bottom style=\"thin\"><color rgb=\"FFBFBFBF\"/></bottom><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"10\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +                                   // 0 default
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                     // 1 bold
            "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\"/></xf>" + // 2 header
            "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\"/></xf>" + // 3 date
            "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\"/></xf>" + // 4 time
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\"/>" +                   // 5 text
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\"/></xf>" + // 6 number
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\"/></xf>" + // 7 total label
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\"/></xf>" + // 8 total number
            "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                     // 9 title
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";

        static void WriteWorkbook(string path, Sheet[] sheets)
        {
            var parts = new List<KeyValuePair<string, string>>();
            var ct = new StringBuilder();
            ct.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            ct.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            ct.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            ct.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            ct.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            ct.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 0; i < sheets.Length; i++)
                ct.AppendFormat("<Override PartName=\"/xl/worksheets/sheet{0}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>", i + 1);
            ct.Append("</Types>");
            parts.Add(new KeyValuePair<string, string>("[Content_Types].xml", ct.ToString()));

            parts.Add(new KeyValuePair<string, string>("_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>"));

            var wb = new StringBuilder();
            wb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            wb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            for (int i = 0; i < sheets.Length; i++)
                wb.AppendFormat("<sheet name=\"{0}\" sheetId=\"{1}\" r:id=\"rId{1}\"/>", SecurityElement.Escape(sheets[i].Name), i + 1);
            wb.Append("</sheets><calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/></workbook>");
            parts.Add(new KeyValuePair<string, string>("xl/workbook.xml", wb.ToString()));

            var rels = new StringBuilder();
            rels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            rels.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 0; i < sheets.Length; i++)
                rels.AppendFormat("<Relationship Id=\"rId{0}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{0}.xml\"/>", i + 1);
            rels.AppendFormat("<Relationship Id=\"rId{0}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>", sheets.Length + 1);
            rels.Append("</Relationships>");
            parts.Add(new KeyValuePair<string, string>("xl/_rels/workbook.xml.rels", rels.ToString()));
            parts.Add(new KeyValuePair<string, string>("xl/styles.xml", StylesXml));
            for (int i = 0; i < sheets.Length; i++)
                parts.Add(new KeyValuePair<string, string>("xl/worksheets/sheet" + (i + 1) + ".xml", SheetXml(sheets[i])));

            string tmp = path + ".writing";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var p in parts)
                {
                    var bytes = Encoding.UTF8.GetBytes(p.Value);
                    using (var s = zip.CreateEntry(p.Key, CompressionLevel.Optimal).Open()) s.Write(bytes, 0, bytes.Length);
                }
            }
            DocxExporter.ReplaceFile(tmp, path);
        }
    }
}
