using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TATimesheet
{
    public abstract class DrawOp { }

    /// <summary>What a mark is, so each renderer (PDF/print vs. the themed screen) can pick its color.</summary>
    public enum Ink { Black, Muted, Grid, Bar }

    public enum TextAnchor { Left, Center, Right }

    /// <summary>Text drawn with its baseline at Y (points from the top-left of the page).</summary>
    public class TextOp : DrawOp
    {
        public float X, Y, Size;     // X is the left edge, centre or right edge depending on Align
        public bool Bold;
        public string Text;
        public Ink Ink;
        public TextAnchor Align;
    }

    public class LineOp : DrawOp
    {
        public float X1, Y1, X2, Y2, Width;
        public Ink Ink;
    }

    /// <summary>A filled rectangle (chart bars). Index ties a bar back to its report row.</summary>
    public class FillOp : DrawOp
    {
        public float X, Y, W, H;
        public Ink Ink = Ink.Bar;
        public int Index = -1;
        public bool RoundTop, RoundRight; // screen only: 4px rounded data-end
    }

    public class ImageOp : DrawOp
    {
        public float X, Y, W, H;
    }

    public class LayoutPage
    {
        public List<DrawOp> Ops = new List<DrawOp>();
    }

    /// <summary>
    /// Lays the customer timesheet out on landscape letter pages (792 x 612 pt) for the PDF
    /// export and for printing. Mirrors the Word layout; text uses Helvetica/Arial metrics.
    /// </summary>
    public static class SheetLayout
    {
        public const float PageW = 792, PageH = 612;
        const float Margin = 72;
        const float TitleSize = 22, CellSize = 12;
        const float RowH = 28.9f, InfoRowH = 21.3f;
        const float PadX = 5.4f;
        const float TableTop = 196;
        const float BottomLimit = PageH - 40;
        const float LineW = 1f;
        static readonly float[] InfoCols = { 72.35f, 250.75f };
        static readonly float[] WeekCols = { 101.1f, 101.1f, 101.1f, 101.1f, 101.1f, 101.1f };
        static readonly string[] Headings = { "Date", "Project", "Location", "Start Time", "End Time", "Hours" };

        public static List<LayoutPage> Build(TimesheetSheet sheet, int logoW, int logoH)
        {
            var pages = new List<LayoutPage>();
            LayoutPage page = null;
            float y = 0;

            for (int i = 0; i < sheet.Weeks.Count; i++)
            {
                var week = sheet.Weeks[i];
                bool lastWeek = i == sheet.Weeks.Count - 1;

                page = NewPage(pages, i == 0 ? TimesheetSheet.Title : TimesheetSheet.ContinuedTitle, logoW, logoH);
                if (i == 0)
                {
                    float iy = 108;
                    iy = Row(page, iy, Margin, InfoCols, new[] { "Name", sheet.Name }, new[] { true, false }, false, InfoRowH);
                    Row(page, iy, Margin, InfoCols, new[] { "Customer", sheet.Customer }, new[] { true, false }, false, InfoRowH);
                }
                y = HeaderRow(page, TableTop);

                foreach (var r in week.Rows)
                {
                    var cells = new[] { TimeUtil.FormatDate(r.Date), r.Project, r.Location, r.Start, r.End, r.Hours };
                    float h = RowHeight(WeekCols, cells, new bool[6], RowH);
                    if (y + h > BottomLimit)
                    {
                        page = NewPage(pages, TimesheetSheet.ContinuedTitle, logoW, logoH);
                        y = HeaderRow(page, TableTop);
                    }
                    y = Row(page, y, Margin, WeekCols, cells, new bool[6], true, RowH);
                }

                var totals = new List<string[]>();
                if (sheet.Weeks.Count > 1) totals.Add(new[] { "Week " + week.Number + " Hours", TimeUtil.FormatHours(week.Hours) });
                if (lastWeek) totals.Add(new[] { "Total Hours", TimeUtil.FormatHours(sheet.TotalHours) });
                foreach (var t in totals)
                {
                    if (y + RowH > BottomLimit)
                    {
                        page = NewPage(pages, TimesheetSheet.ContinuedTitle, logoW, logoH);
                        y = TableTop;
                    }
                    float x = Margin + WeekCols.Take(4).Sum();
                    y = Row(page, y, x, new[] { WeekCols[4], WeekCols[5] }, t, new[] { true, true }, true, RowH);
                }
            }
            return pages;
        }

        static LayoutPage NewPage(List<LayoutPage> pages, string title, int logoW, int logoH)
        {
            var page = new LayoutPage();
            pages.Add(page);
            if (logoW > 0 && logoH > 0)
            {
                // Same spot as the Word header: top right, kept clear of the tables.
                float boxW = 220, boxH = 160;
                float scale = Math.Min(boxW / logoW, boxH / logoH);
                float w = logoW * scale, h = logoH * scale;
                page.Ops.Add(new ImageOp { X = PageW - 22 - boxW + (boxW - w) / 2, Y = 12, W = w, H = h });
            }
            float tw = Fonts.Width(title, true, TitleSize);
            float tx = (PageW - tw) / 2, ty = Margin + 18;
            page.Ops.Add(new TextOp { X = tx, Y = ty, Text = title, Bold = true, Size = TitleSize });
            page.Ops.Add(new LineOp { X1 = tx, Y1 = ty + 3, X2 = tx + tw, Y2 = ty + 3, Width = 1.4f });
            return page;
        }

        static float HeaderRow(LayoutPage page, float y)
        {
            return Row(page, y, Margin, WeekCols, Headings, Enumerable.Repeat(true, 6).ToArray(), true, RowH);
        }

        static float RowHeight(float[] cols, string[] cells, bool[] bold, float minH)
        {
            float h = minH;
            for (int i = 0; i < cols.Length; i++)
            {
                int lines = Wrap(cells[i] ?? "", bold[i], CellSize, cols[i] - 2 * PadX).Count;
                h = Math.Max(h, lines * CellSize * 1.15f + 8);
            }
            return h;
        }

        /// <summary>Draws one bordered table row and returns the y of its bottom edge.</summary>
        static float Row(LayoutPage page, float y, float x, float[] cols, string[] cells, bool[] bold, bool center, float minH)
        {
            float h = RowHeight(cols, cells, bold, minH);
            float right = x + cols.Sum();
            page.Ops.Add(new LineOp { X1 = x, Y1 = y, X2 = right, Y2 = y, Width = LineW });
            page.Ops.Add(new LineOp { X1 = x, Y1 = y + h, X2 = right, Y2 = y + h, Width = LineW });
            float cx = x;
            for (int i = 0; i <= cols.Length; i++)
            {
                page.Ops.Add(new LineOp { X1 = cx, Y1 = y, X2 = cx, Y2 = y + h, Width = LineW });
                if (i == cols.Length) break;

                var lines = Wrap(cells[i] ?? "", bold[i], CellSize, cols[i] - 2 * PadX);
                float lineH = CellSize * 1.15f;
                float cap = CellSize * 0.72f;
                float baseline = y + h / 2 - (lines.Count - 1) * lineH / 2 + cap / 2;
                foreach (var line in lines)
                {
                    float w = Fonts.Width(line, bold[i], CellSize);
                    float tx = center ? cx + (cols[i] - w) / 2 : cx + PadX;
                    page.Ops.Add(new TextOp { X = tx, Y = baseline, Text = line, Bold = bold[i], Size = CellSize });
                    baseline += lineH;
                }
                cx += cols[i];
            }
            return y + h;
        }

        /// <summary>Greedy word wrap; words longer than the cell get broken by character.</summary>
        public static List<string> Wrap(string text, bool bold, float size, float width)
        {
            var lines = new List<string>();
            text = (text ?? "").Trim();
            if (text.Length == 0) return lines;
            if (text.IndexOf('\n') >= 0)
            {
                foreach (var part in text.Split('\n')) lines.AddRange(Wrap(part, bold, size, width));
                return lines;
            }
            string current = "";
            foreach (var word in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string attempt = current.Length == 0 ? word : current + " " + word;
                if (Fonts.Width(attempt, bold, size) <= width) { current = attempt; continue; }
                if (current.Length > 0) lines.Add(current);
                current = word;
                while (Fonts.Width(current, bold, size) > width && current.Length > 1)
                {
                    int n = current.Length - 1;
                    while (n > 1 && Fonts.Width(current.Substring(0, n), bold, size) > width) n--;
                    lines.Add(current.Substring(0, n));
                    current = current.Substring(n);
                }
            }
            if (current.Length > 0) lines.Add(current);
            return lines;
        }
    }

    /// <summary>Helvetica / Helvetica-Bold widths (WinAnsi 32..255); Arial has the same metrics.</summary>
    public static class Fonts
    {
        public static readonly Encoding WinAnsi = Encoding.GetEncoding(1252);

        static readonly short[] Regular =
        {
            278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278,556,556,556,556,556,556,556,556,556,556,278,278,584,584,584,556,
            1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778,667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556,
            333,556,556,500,556,556,278,556,556,222,222,500,222,833,556,556,556,556,333,500,278,556,500,722,500,500,500,334,260,334,584,761,
            556,556,222,556,333,1000,556,556,333,1000,667,333,1000,556,611,556,556,222,222,333,333,350,556,1000,333,1000,500,333,944,556,500,667,
            278,333,556,556,556,556,260,556,333,737,370,556,584,333,737,333,400,584,333,333,333,556,537,278,333,333,365,556,834,834,834,611,
            667,667,667,667,667,667,1000,722,667,667,667,667,278,278,278,278,722,722,778,778,778,778,778,584,778,722,722,722,722,667,667,611,
            556,556,556,556,556,556,889,500,556,556,556,556,278,278,278,278,556,556,556,556,556,556,556,584,611,556,556,556,556,500,556,500
        };

        static readonly short[] BoldW =
        {
            278,333,474,556,556,889,722,238,333,333,389,584,278,333,278,278,556,556,556,556,556,556,556,556,556,556,333,333,584,584,584,611,
            975,722,722,722,722,667,611,778,722,278,556,722,611,833,722,778,667,778,722,667,611,722,667,944,667,667,611,333,278,333,584,556,
            333,556,611,556,611,556,333,611,611,278,278,556,278,889,611,611,611,611,389,556,333,611,556,778,556,556,500,389,280,389,584,761,
            556,556,278,556,500,1000,556,556,333,1000,667,333,1000,556,611,556,556,278,278,500,500,350,556,1000,333,1000,556,333,944,556,500,667,
            278,333,556,556,556,556,280,556,333,737,370,556,584,333,737,333,400,584,333,333,333,611,556,278,333,333,365,556,834,834,834,611,
            722,722,722,722,722,722,1000,722,667,667,667,667,278,278,278,278,722,722,778,778,778,778,778,584,778,722,722,722,722,667,667,611,
            556,556,556,556,556,556,889,556,556,556,556,556,278,278,278,278,611,611,611,611,611,611,611,584,611,611,611,611,611,556,611,556
        };

        public static float Width(string s, bool bold, float size)
        {
            var table = bold ? BoldW : Regular;
            int total = 0;
            foreach (byte c in WinAnsi.GetBytes(s ?? ""))
                total += c >= 32 ? table[c - 32] : 0;
            return total * size / 1000f;
        }
    }
}
