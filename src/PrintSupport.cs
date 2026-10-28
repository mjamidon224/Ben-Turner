using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;

namespace TATimesheet
{
    /// <summary>Prints laid-out pages (the same ones the PDF uses); also drives Print Preview.</summary>
    public class LayoutPrintDocument : PrintDocument
    {
        readonly List<LayoutPage> pages;
        readonly Image logo;
        int next;

        public LayoutPrintDocument(Func<int, int, List<LayoutPage>> build, byte[] logoPng, string name)
        {
            if (logoPng != null) logo = Image.FromStream(new MemoryStream(logoPng));
            pages = build(logo != null ? logo.Width : 0, logo != null ? logo.Height : 0);
            DocumentName = name;
            DefaultPageSettings.Landscape = true;
        }

        public static LayoutPrintDocument ForSheet(TimesheetSheet sheet, byte[] logoPng)
        {
            return new LayoutPrintDocument((w, h) => SheetLayout.Build(sheet, w, h), logoPng,
                string.Format("{0} {1}", TimesheetSheet.Title, TimeUtil.FormatDate(sheet.Period.Start)));
        }

        protected override void OnBeginPrint(PrintEventArgs e)
        {
            next = 0;
            base.OnBeginPrint(e);
        }

        protected override void OnPrintPage(PrintPageEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Work in points: printer "Display" units are 1/100 inch, so scale by 100/72.
            g.PageUnit = GraphicsUnit.Display;
            g.ScaleTransform(100f / 72f, 100f / 72f);

            // Real printers can't print to the paper edge; their origin is the printable area.
            if (!PrintController.IsPreview)
                g.TranslateTransform(-e.PageSettings.HardMarginX * 0.72f, -e.PageSettings.HardMarginY * 0.72f);

            // The layout is for US Letter; shrink it to fit anything smaller (e.g. A4).
            float pageW = e.PageBounds.Width * 0.72f, pageH = e.PageBounds.Height * 0.72f;
            float scale = Math.Min(1f, Math.Min(pageW / SheetLayout.PageW, pageH / SheetLayout.PageH));
            if (scale < 1f) g.ScaleTransform(scale, scale);

            if (pages.Count > 0) Draw(g, pages[next].Ops, logo, PdfExporter.PrintColor, "Arial", false);
            next++;
            e.HasMorePages = next < pages.Count;
        }

        /// <summary>
        /// Draws ops; the Graphics transform must map one world unit to one layout unit
        /// (points on paper, pixels on screen). Rounded bar ends are for the screen only.
        /// </summary>
        public static void Draw(Graphics g, IEnumerable<DrawOp> ops, Image logo, Func<Ink, Color> color, string fontName, bool roundEnds)
        {
            using (var family = new FontFamily(fontName))
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                foreach (var op in ops)
                {
                    var img = op as ImageOp;
                    if (img != null)
                    {
                        if (logo != null) g.DrawImage(logo, img.X, img.Y, img.W, img.H);
                        continue;
                    }
                    var line = op as LineOp;
                    if (line != null)
                    {
                        using (var pen = new Pen(color(line.Ink), line.Width))
                        {
                            pen.StartCap = pen.EndCap = LineCap.Square;
                            g.DrawLine(pen, line.X1, line.Y1, line.X2, line.Y2);
                        }
                        continue;
                    }
                    var fill = op as FillOp;
                    if (fill != null)
                    {
                        using (var brush = new SolidBrush(color(fill.Ink)))
                        {
                            if (roundEnds && (fill.RoundTop || fill.RoundRight))
                                using (var path = RoundedEnd(fill)) g.FillPath(brush, path);
                            else
                                g.FillRectangle(brush, fill.X, fill.Y, fill.W, fill.H);
                        }
                        continue;
                    }
                    var text = op as TextOp;
                    if (text != null)
                    {
                        var style = text.Bold ? FontStyle.Bold : FontStyle.Regular;
                        using (var font = new Font(family, text.Size, style, GraphicsUnit.World))
                        using (var brush = new SolidBrush(color(text.Ink)))
                        {
                            float ascent = text.Size * family.GetCellAscent(style) / family.GetEmHeight(style);
                            float x = text.X;
                            if (text.Align != TextAnchor.Left)
                            {
                                float w = g.MeasureString(text.Text, font, PointF.Empty, format).Width;
                                x -= text.Align == TextAnchor.Center ? w / 2 : w;
                            }
                            g.DrawString(text.Text, font, brush, x, text.Y - ascent, format);
                        }
                    }
                }
            }
        }

        /// <summary>A bar with a 4-unit rounded data end and a square baseline end.</summary>
        static GraphicsPath RoundedEnd(FillOp f)
        {
            var p = new GraphicsPath();
            float r = Math.Min(4f, Math.Min(f.W, f.H) / 2f);
            if (r < 0.5f)
            {
                p.AddRectangle(new RectangleF(f.X, f.Y, f.W, f.H));
                return p;
            }
            float d = 2 * r;
            if (f.RoundTop)
            {
                p.AddLine(f.X, f.Y + f.H, f.X, f.Y + r);
                p.AddArc(f.X, f.Y, d, d, 180, 90);
                p.AddArc(f.X + f.W - d, f.Y, d, d, 270, 90);
                p.AddLine(f.X + f.W, f.Y + r, f.X + f.W, f.Y + f.H);
            }
            else
            {
                p.AddLine(f.X, f.Y, f.X + f.W - r, f.Y);
                p.AddArc(f.X + f.W - d, f.Y, d, d, 270, 90);
                p.AddArc(f.X + f.W - d, f.Y + f.H - d, d, d, 0, 90);
                p.AddLine(f.X + f.W - r, f.Y + f.H, f.X, f.Y + f.H);
            }
            p.CloseFigure();
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && logo != null) logo.Dispose();
            base.Dispose(disposing);
        }
    }
}
