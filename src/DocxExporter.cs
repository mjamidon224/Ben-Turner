using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TATimesheet
{
    /// <summary>
    /// Writes the customer timesheet as a Word file that matches the original TA_Timesheet layout:
    /// landscape letter, logo in the header, Name/Customer box, one table per week, totals.
    /// Everything except the body comes from the embedded template.
    /// </summary>
    public static class DocxExporter
    {
        // Measurements copied from the original document (twips).
        const int ColWidth = 2022;
        const int RowHeight = 578;
        const int InfoLabelWidth = 1447;
        const int InfoValueWidth = 5015;
        const int InfoRowHeight = 426;

        const string CellFont = "<w:rFonts w:ascii=\"Aptos Narrow\" w:eastAsia=\"Times New Roman\" w:hAnsi=\"Aptos Narrow\" w:cs=\"Times New Roman\"/>";
        const string Border = "w:val=\"single\" w:sz=\"8\" w:space=\"0\" w:color=\"auto\"";

        public static void Export(TimesheetSheet sheet, string path, byte[] customLogoPng)
        {
            string tmp = path + ".writing";
            using (var src = new ZipArchive(new MemoryStream(AppResources.TemplateDocx()), ZipArchiveMode.Read))
            {
                string headerRelId = FindHeaderRelId(src);
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                using (var dst = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    foreach (var entry in src.Entries)
                    {
                        string name = entry.FullName;
                        byte[] data;
                        if (name == "word/document.xml")
                            data = Encoding.UTF8.GetBytes(BuildDocument(sheet, headerRelId));
                        else if (name == "docProps/core.xml")
                            data = Encoding.UTF8.GetBytes(BuildCoreProps(sheet));
                        else if (customLogoPng != null && name == AppResources.LogoPart)
                            data = customLogoPng;
                        else if (customLogoPng != null && name == "word/header1.xml")
                            data = Encoding.UTF8.GetBytes(FitLogo(ReadText(entry), customLogoPng));
                        else
                            using (var s = entry.Open()) data = AppResources.ReadAll(s);

                        var outEntry = dst.CreateEntry(name, CompressionLevel.Optimal);
                        using (var o = outEntry.Open()) o.Write(data, 0, data.Length);
                    }
                }
            }
            ReplaceFile(tmp, path);
        }

        /// <summary>Moves a finished temp file over the target (fails cleanly if the target is open in Word).</summary>
        public static void ReplaceFile(string tmp, string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch
            {
                try { File.Delete(tmp); } catch (Exception) { }
                throw;
            }
        }

        static string ReadText(ZipArchiveEntry e)
        {
            using (var s = e.Open())
            using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }

        static string FindHeaderRelId(ZipArchive zip)
        {
            var rels = zip.GetEntry("word/_rels/document.xml.rels");
            if (rels != null)
            {
                var doc = XDocument.Parse(ReadText(rels));
                var header = doc.Root.Elements().FirstOrDefault(r => ((string)r.Attribute("Type") ?? "").EndsWith("/header"));
                if (header != null) return (string)header.Attribute("Id");
            }
            return null;
        }

        /// <summary>Resizes the header picture box so a custom logo keeps its own proportions.</summary>
        static string FitLogo(string headerXml, byte[] png)
        {
            var m = Regex.Match(headerXml, "<wp:extent cx=\"(\\d+)\" cy=\"(\\d+)\"/>");
            if (!m.Success) return headerXml;
            long boxW = long.Parse(m.Groups[1].Value), boxH = long.Parse(m.Groups[2].Value);
            int pw, ph;
            using (var img = Image.FromStream(new MemoryStream(png))) { pw = img.Width; ph = img.Height; }
            double scale = Math.Min((double)boxW / pw, (double)boxH / ph);
            long w = (long)(pw * scale), h = (long)(ph * scale);
            string oldExt = string.Format("cx=\"{0}\" cy=\"{1}\"", boxW, boxH);
            string newExt = string.Format("cx=\"{0}\" cy=\"{1}\"", w, h);
            return headerXml.Replace(oldExt, newExt);
        }

        static string Esc(string s)
        {
            return SecurityElement.Escape(s ?? "");
        }

        // ---- document.xml ----------------------------------------------------------------------

        static string BuildDocument(TimesheetSheet sheet, string headerRelId)
        {
            var b = new StringBuilder();
            b.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            b.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" ");
            b.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body>");

            for (int i = 0; i < sheet.Weeks.Count; i++)
            {
                var week = sheet.Weeks[i];
                bool first = i == 0, last = i == sheet.Weeks.Count - 1;
                Title(b, first ? TimesheetSheet.Title : TimesheetSheet.ContinuedTitle, !first);
                if (first)
                {
                    InfoTable(b, sheet.Name, sheet.Customer);
                    Spacer(b, 600);
                }
                else
                {
                    Spacer(b, 1450);
                }
                WeekTable(b, sheet, week, last);
            }

            b.Append("<w:sectPr>");
            if (headerRelId != null)
                b.AppendFormat("<w:headerReference w:type=\"default\" r:id=\"{0}\"/>", headerRelId);
            b.Append("<w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"landscape\"/>");
            b.Append("<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/>");
            b.Append("<w:cols w:space=\"720\"/><w:docGrid w:linePitch=\"360\"/></w:sectPr>");
            b.Append("</w:body></w:document>");
            return b.ToString();
        }

        static void Title(StringBuilder b, string text, bool pageBreakBefore)
        {
            const string rpr = "<w:b/><w:bCs/><w:sz w:val=\"50\"/><w:szCs w:val=\"50\"/><w:u w:val=\"single\"/>";
            b.Append("<w:p><w:pPr>");
            if (pageBreakBefore) b.Append("<w:pageBreakBefore/>");
            b.Append("<w:jc w:val=\"center\"/><w:rPr>").Append(rpr).Append("</w:rPr></w:pPr>");
            b.Append("<w:r><w:rPr>").Append(rpr).Append("</w:rPr><w:t>").Append(Esc(text)).Append("</w:t></w:r></w:p>");
        }

        static void Spacer(StringBuilder b, int twips)
        {
            b.AppendFormat("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"{0}\" w:lineRule=\"exact\"/></w:pPr></w:p>", twips);
        }

        static string RunProps(bool bold)
        {
            return "<w:rPr>" + CellFont + (bold ? "<w:b/><w:bCs/>" : "") +
                   "<w:color w:val=\"000000\"/><w:kern w:val=\"0\"/><w:sz w:val=\"30\"/><w:szCs w:val=\"30\"/></w:rPr>";
        }

        static void Cell(StringBuilder b, int width, string text, bool bold, bool center, bool bordered)
        {
            b.Append("<w:tc><w:tcPr>");
            b.AppendFormat("<w:tcW w:w=\"{0}\" w:type=\"dxa\"/>", width);
            if (bordered)
                b.AppendFormat("<w:tcBorders><w:top {0}/><w:left {0}/><w:bottom {0}/><w:right {0}/></w:tcBorders>", Border);
            else
                b.Append("<w:tcBorders><w:top w:val=\"nil\"/><w:left w:val=\"nil\"/><w:bottom w:val=\"nil\"/><w:right w:val=\"nil\"/></w:tcBorders>");
            b.Append("<w:vAlign w:val=\"center\"/></w:tcPr>");
            foreach (var line in (text ?? "").Split('\n'))
            {
                b.Append("<w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>");
                if (center) b.Append("<w:jc w:val=\"center\"/>");
                b.Append(RunProps(bold)).Append("</w:pPr>");
                if (line.Length > 0)
                    b.Append("<w:r>").Append(RunProps(bold)).Append("<w:t xml:space=\"preserve\">").Append(Esc(line)).Append("</w:t></w:r>");
                b.Append("</w:p>");
            }
            b.Append("</w:tc>");
        }

        static void TableStart(StringBuilder b, int[] widths)
        {
            b.Append("<w:tbl><w:tblPr>");
            b.AppendFormat("<w:tblW w:w=\"{0}\" w:type=\"dxa\"/>", widths.Sum());
            b.Append("<w:tblLayout w:type=\"fixed\"/><w:tblLook w:val=\"04A0\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"1\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\"/>");
            b.Append("</w:tblPr><w:tblGrid>");
            foreach (int w in widths) b.AppendFormat("<w:gridCol w:w=\"{0}\"/>", w);
            b.Append("</w:tblGrid>");
        }

        static void RowStart(StringBuilder b, int height, bool header)
        {
            b.Append("<w:tr><w:trPr><w:cantSplit/>");
            if (header) b.Append("<w:tblHeader/>");
            b.AppendFormat("<w:trHeight w:val=\"{0}\"/></w:trPr>", height);
        }

        static void InfoTable(StringBuilder b, string name, string customer)
        {
            TableStart(b, new[] { InfoLabelWidth, InfoValueWidth });
            RowStart(b, InfoRowHeight, false);
            Cell(b, InfoLabelWidth, "Name", true, false, true);
            Cell(b, InfoValueWidth, name, false, false, true);
            b.Append("</w:tr>");
            RowStart(b, InfoRowHeight, false);
            Cell(b, InfoLabelWidth, "Customer", true, false, true);
            Cell(b, InfoValueWidth, customer, false, false, true);
            b.Append("</w:tr></w:tbl>");
        }

        static readonly string[] Headings = { "Date", "Project", "Location", "Start Time", "End Time", "Hours" };

        static void WeekTable(StringBuilder b, TimesheetSheet sheet, SheetWeek week, bool lastWeek)
        {
            var widths = Enumerable.Repeat(ColWidth, 6).ToArray();
            TableStart(b, widths);

            RowStart(b, RowHeight, true);
            foreach (var h in Headings) Cell(b, ColWidth, h, true, true, true);
            b.Append("</w:tr>");

            foreach (var r in week.Rows)
            {
                RowStart(b, RowHeight, false);
                Cell(b, ColWidth, TimeUtil.FormatDate(r.Date), false, true, true);
                Cell(b, ColWidth, r.Project, false, true, true);
                Cell(b, ColWidth, r.Location, false, true, true);
                Cell(b, ColWidth, r.Start, false, true, true);
                Cell(b, ColWidth, r.End, false, true, true);
                Cell(b, ColWidth, r.Hours, false, true, true);
                b.Append("</w:tr>");
            }

            bool single = sheet.Weeks.Count == 1;
            if (!single)
                TotalRow(b, "Week " + week.Number + " Hours", week.Hours);
            if (lastWeek)
                TotalRow(b, "Total Hours", sheet.TotalHours);
            b.Append("</w:tbl>");
        }

        static void TotalRow(StringBuilder b, string label, double hours)
        {
            RowStart(b, RowHeight, false);
            for (int i = 0; i < 4; i++) Cell(b, ColWidth, "", false, true, false);
            Cell(b, ColWidth, label, true, true, true);
            Cell(b, ColWidth, TimeUtil.FormatHours(hours), true, true, true);
            b.Append("</w:tr>");
        }

        static string BuildCoreProps(TimesheetSheet sheet)
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", TimeUtil.Inv);
            string title = string.Format("{0} {1} - {2}", TimesheetSheet.Title,
                TimeUtil.FormatDate(sheet.Period.Start), TimeUtil.FormatDate(sheet.Period.End));
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                   "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
                   "xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                   "<dc:title>" + Esc(title) + "</dc:title>" +
                   "<dc:creator>" + Esc(sheet.Name) + "</dc:creator>" +
                   "<cp:lastModifiedBy>" + Esc(sheet.Name) + "</cp:lastModifiedBy>" +
                   "<cp:revision>1</cp:revision>" +
                   "<dcterms:created xsi:type=\"dcterms:W3CDTF\">" + now + "</dcterms:created>" +
                   "<dcterms:modified xsi:type=\"dcterms:W3CDTF\">" + now + "</dcterms:modified>" +
                   "</cp:coreProperties>";
        }
    }
}
