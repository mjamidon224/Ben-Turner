using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;

namespace TATimesheet
{
    /// <summary>Prints the same page layout the PDF uses (also drives Print Preview).</summary>
    public class TimesheetPrintDocument : PrintDocument
    {
        readonly List<LayoutPage> pages;
        readonly Image logo;
        int next;

        public TimesheetPrintDocument(TimesheetSheet sheet, byte[] logoPng)
        {
            if (logoPng != null) logo = Image.FromStream(new MemoryStream(logoPng));
            pages = SheetLayout.Build(sheet, logo != null ? logo.Width : 0, logo != null ? logo.Height : 0);
            DocumentName = string.Format("{0} {1}", TimesheetSheet.Title, TimeUtil.FormatDate(sheet.Period.Start));
            DefaultPageSettings.Landscape = true;
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

            Draw(g, pages[next], logo);
            next++;
            e.HasMorePages = next < pages.Count;
        }

        /// <summary>Draws a page; the Graphics transform must map one world unit to one point.</summary>
        public static void Draw(Graphics g, LayoutPage page, Image logo)
        {
            using (var regular = new FontFamily("Arial"))
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.NoWrap;
                foreach (var op in page.Ops)
                {
                    var img = op as ImageOp;
                    if (img != null && logo != null)
                    {
                        g.DrawImage(logo, img.X, img.Y, img.W, img.H);
                        continue;
                    }
                    var line = op as LineOp;
                    if (line != null)
                    {
                        using (var pen = new Pen(Color.Black, line.Width))
                        {
                            pen.StartCap = pen.EndCap = LineCap.Square;
                            g.DrawLine(pen, line.X1, line.Y1, line.X2, line.Y2);
                        }
                        continue;
                    }
                    var text = op as TextOp;
                    if (text != null)
                    {
                        var style = text.Bold ? FontStyle.Bold : FontStyle.Regular;
                        using (var font = new Font(regular, text.Size, style, GraphicsUnit.World))
                        {
                            float ascent = text.Size * regular.GetCellAscent(style) / regular.GetEmHeight(style);
                            g.DrawString(text.Text, font, Brushes.Black, text.X, text.Y - ascent, format);
                        }
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && logo != null) logo.Dispose();
            base.Dispose(disposing);
        }
    }
}
