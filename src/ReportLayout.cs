using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TATimesheet
{
    /// <summary>
    /// Bar chart of a report's hours as draw ops, so the same chart shows on screen, in the PDF
    /// and on paper. Time groupings get columns over a value axis; projects/locations/customers
    /// get horizontal bars with the value at the tip. One series, so no legend.
    /// </summary>
    public static class ChartLayout
    {
        public const int MaxCategories = 12;

        /// <summary>The rows the chart shows: categories past the top 12 fold into "Other".</summary>
        public static List<ReportRow> ChartRows(Report r)
        {
            if (r.IsTimeBased || r.Rows.Count <= MaxCategories + 1) return r.Rows;
            var top = r.Rows.Take(MaxCategories).ToList();
            var rest = r.Rows.Skip(MaxCategories).ToList();
            top.Add(new ReportRow
            {
                Label = "Other (" + rest.Count + ")",
                Hours = Math.Round(rest.Sum(x => x.Hours), 2),
                TravelHours = Math.Round(rest.Sum(x => x.TravelHours), 2),
                Days = rest.Sum(x => x.Days),
                Entries = rest.Sum(x => x.Entries)
            });
            return top;
        }

        public static List<DrawOp> Build(Report r, float x, float y, float w, float h, float font)
        {
            var ops = new List<DrawOp>();
            var rows = ChartRows(r);
            if (rows.Count == 0 || r.TotalHours <= 0)
            {
                ops.Add(new TextOp { X = x + w / 2, Y = y + h / 2, Text = "No hours in this range.", Size = font * 1.15f, Ink = Ink.Muted, Align = TextAnchor.Center });
                return ops;
            }
            if (r.IsTimeBased) Columns(ops, r, rows, x, y, w, h, font);
            else Bars(ops, rows, x, y, w, h, font);
            return ops;
        }

        static void Columns(List<DrawOp> ops, Report r, List<ReportRow> rows, float x, float y, float w, float h, float font)
        {
            double max = rows.Max(v => v.Hours);
            double step, top;
            NiceScale(max, 4, out step, out top);

            float tickW = 0;
            for (double t = 0; t <= top + step / 2; t += step) tickW = Math.Max(tickW, Fonts.Width(Tick(t), false, font));
            float px = x + tickW + 8, py = y + font * 1.6f, pw = w - (px - x), ph = h - font * 1.6f - font * 2.2f;

            // Recessive hairline gridlines with clean tick values.
            for (double t = 0; t <= top + step / 2; t += step)
            {
                float gy = py + ph - (float)(t / top) * ph;
                ops.Add(new LineOp { X1 = px, Y1 = gy, X2 = px + pw, Y2 = gy, Width = 1, Ink = t == 0 ? Ink.Muted : Ink.Grid });
                ops.Add(new TextOp { X = px - 6, Y = gy + font * 0.35f, Text = Tick(t), Size = font, Ink = Ink.Muted, Align = TextAnchor.Right });
            }

            float slot = pw / rows.Count;
            float barW = Math.Max(1f, Math.Min(24f, Math.Min(slot * 0.7f, slot - 2)));
            int busiest = rows.IndexOf(rows.OrderByDescending(v => v.Hours).First());
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Hours <= 0) continue;
                float bh = (float)(rows[i].Hours / top) * ph;
                float bx = px + i * slot + (slot - barW) / 2;
                ops.Add(new FillOp { X = bx, Y = py + ph - bh, W = barW, H = bh, Index = i, RoundTop = true });
                if (i == busiest)
                    ops.Add(new TextOp { X = bx + barW / 2, Y = py + ph - bh - font * 0.5f, Text = TimeUtil.FormatHours(rows[i].Hours),
                                         Size = font, Bold = true, Align = TextAnchor.Center });
            }

            // Axis labels, thinned so they never collide.
            float labelW = rows.Max(v => Fonts.Width(Report.ShortLabel(v, r.Grouping), false, font));
            int every = Math.Max(1, (int)Math.Ceiling((labelW + font) / slot));
            for (int i = 0; i < rows.Count; i += every)
                ops.Add(new TextOp { X = px + i * slot + slot / 2, Y = py + ph + font * 1.5f, Text = Report.ShortLabel(rows[i], r.Grouping),
                                     Size = font, Ink = Ink.Muted, Align = TextAnchor.Center });
        }

        static void Bars(List<DrawOp> ops, List<ReportRow> rows, float x, float y, float w, float h, float font)
        {
            double max = rows.Max(v => v.Hours);
            float labelW = Math.Min(w * 0.32f, rows.Max(v => Fonts.Width(v.Label, false, font)) + 10);
            float valueW = rows.Max(v => Fonts.Width(TimeUtil.FormatHours(v.Hours) + " h", true, font)) + 8;
            float px = x + labelW, pw = w - labelW - valueW;
            float slot = h / rows.Count;
            float barH = Math.Max(2f, Math.Min(24f, Math.Min(slot * 0.65f, slot - 2)));

            ops.Add(new LineOp { X1 = px, Y1 = y, X2 = px, Y2 = y + h, Width = 1, Ink = Ink.Muted });
            for (int i = 0; i < rows.Count; i++)
            {
                float cy = y + i * slot + slot / 2;
                ops.Add(new TextOp { X = px - 8, Y = cy + font * 0.35f, Text = Fit(rows[i].Label, font, labelW - 10), Size = font, Align = TextAnchor.Right });
                float bw = max > 0 ? (float)(rows[i].Hours / max) * pw : 0;
                if (bw > 0) ops.Add(new FillOp { X = px, Y = cy - barH / 2, W = bw, H = barH, Index = i, RoundRight = true });
                ops.Add(new TextOp { X = px + bw + 6, Y = cy + font * 0.35f, Text = TimeUtil.FormatHours(rows[i].Hours) + " h", Size = font, Bold = true });
            }
        }

        /// <summary>Shortens text with an ellipsis until it fits (measured, never clipped).</summary>
        public static string Fit(string text, float font, float width)
        {
            if (Fonts.Width(text, false, font) <= width) return text;
            for (int n = text.Length - 1; n > 0; n--)
            {
                string t = text.Substring(0, n).TrimEnd() + "...";
                if (Fonts.Width(t, false, font) <= width) return t;
            }
            return "...";
        }

        static string Tick(double v)
        {
            return v >= 1000 ? (v / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "K" : v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>Round tick step (1, 2, 2.5, 5 x 10^n) and the axis top it implies.</summary>
        static void NiceScale(double max, int ticks, out double step, out double top)
        {
            if (max <= 0) { step = 1; top = 1; return; }
            double raw = max / ticks;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double res = raw / mag;
            double nice = res <= 1 ? 1 : res <= 2 ? 2 : res <= 2.5 ? 2.5 : res <= 5 ? 5 : 10;
            step = nice * mag;
            top = Math.Ceiling(max / step) * step;
            if (top < max * 1.05) top += step; // headroom for the label on the tallest bar
        }
    }

    /// <summary>Landscape letter pages for a report: header, summary, chart, then the table.</summary>
    public static class ReportLayout
    {
        const float M = 54, PageW = SheetLayout.PageW, PageH = SheetLayout.PageH;
        const float RowH = 18, Font = 10;
        static readonly float[] Cols = { 250, 80, 80, 90, 90, 80 };

        public static List<string> SummaryLines(Report r)
        {
            var b = r.Busiest;
            var lines = new List<string>
            {
                string.Format("Total {0} h     Days worked {1}     Avg {2} h/day     Avg {3} h/week     Travel {4} h",
                    TimeUtil.FormatHours(r.TotalHours), r.DaysWorked, TimeUtil.FormatHours(r.AvgPerDay),
                    TimeUtil.FormatHours(r.AvgPerWeek), TimeUtil.FormatHours(r.TravelHours))
            };
            if (b != null)
                lines.Add(string.Format("Most hours: {0} ({1} h)", b.Label, TimeUtil.FormatHours(b.Hours)));
            return lines;
        }

        public static List<LayoutPage> Build(Report r, int logoW, int logoH)
        {
            var pages = new List<LayoutPage>();
            var page = NewPage(pages, r, logoW, logoH, false);
            float y = 112;
            foreach (var line in SummaryLines(r))
            {
                page.Ops.Add(new TextOp { X = M, Y = y, Text = line, Size = 11 });
                y += 16;
            }

            y += 10;
            float chartH = 190;
            page.Ops.AddRange(ChartLayout.Build(r, M, y, PageW - 2 * M, chartH, 9));
            y += chartH + 24;

            y = Header(page, y, r.GroupingName);
            var rows = r.TableRows;
            for (int i = 0; i < rows.Count; i++)
            {
                if (y + RowH > PageH - M)
                {
                    page = NewPage(pages, r, logoW, logoH, true);
                    y = Header(page, 104, r.GroupingName);
                }
                var row = rows[i];
                y = Row(page, y, new[]
                {
                    ChartLayout.Fit(row.Label, Font, Cols[0] - 8), TimeUtil.FormatHours(row.Hours), TimeUtil.FormatHours(row.TravelHours),
                    row.Days.ToString(CultureInfo.InvariantCulture), TimeUtil.FormatHours(row.AvgPerDay),
                    (r.TotalHours > 0 ? Math.Round(100 * row.Hours / r.TotalHours, 1) : 0).ToString("0.#", CultureInfo.InvariantCulture) + "%"
                }, false);
            }
            if (y + RowH > PageH - M) { page = NewPage(pages, r, logoW, logoH, true); y = Header(page, 104, r.GroupingName); }
            page.Ops.Add(new LineOp { X1 = M, Y1 = y, X2 = M + Cols.Sum(), Y2 = y, Width = 1 });
            Row(page, y, new[]
            {
                "Total", TimeUtil.FormatHours(r.TotalHours), TimeUtil.FormatHours(r.TravelHours),
                r.DaysWorked.ToString(CultureInfo.InvariantCulture), TimeUtil.FormatHours(r.AvgPerDay), r.TotalHours > 0 ? "100%" : "0%"
            }, true);
            return pages;
        }

        static LayoutPage NewPage(List<LayoutPage> pages, Report r, int logoW, int logoH, bool continued)
        {
            var page = new LayoutPage();
            pages.Add(page);
            if (logoW > 0 && logoH > 0)
            {
                float scale = Math.Min(150f / logoW, 100f / logoH);
                page.Ops.Add(new ImageOp { X = PageW - M - logoW * scale + 10, Y = 18, W = logoW * scale, H = logoH * scale });
            }
            page.Ops.Add(new TextOp { X = M, Y = M + 6, Text = continued ? "Hours Report (continued)" : "Hours Report", Size = 20, Bold = true });
            page.Ops.Add(new TextOp
            {
                X = M, Y = M + 26, Size = 11, Ink = Ink.Muted,
                Text = r.RangeText + "   \u00B7   by " + r.GroupingName.ToLowerInvariant() + (r.Name.Length > 0 ? "   \u00B7   " + r.Name : "")
            });
            return page;
        }

        static float Header(LayoutPage page, float y, string group)
        {
            var titles = new[] { group, "Hours", "Travel h", "Days worked", "Avg h/day", "% of total" };
            y = Row(page, y, titles, true);
            page.Ops.Add(new LineOp { X1 = M, Y1 = y, X2 = M + Cols.Sum(), Y2 = y, Width = 1 });
            return y;
        }

        static float Row(LayoutPage page, float y, string[] cells, bool bold)
        {
            float x = M;
            for (int i = 0; i < cells.Length; i++)
            {
                bool first = i == 0;
                page.Ops.Add(new TextOp
                {
                    X = first ? x + 4 : x + Cols[i] - 6, Y = y + RowH - 5, Text = cells[i], Size = Font, Bold = bold,
                    Align = first ? TextAnchor.Left : TextAnchor.Right
                });
                x += Cols[i];
            }
            page.Ops.Add(new LineOp { X1 = M, Y1 = y + RowH, X2 = M + Cols.Sum(), Y2 = y + RowH, Width = 0.5f, Ink = Ink.Grid });
            return y + RowH;
        }
    }
}
