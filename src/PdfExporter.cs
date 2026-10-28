using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TATimesheet
{
    /// <summary>
    /// Tiny PDF writer for the timesheet layout: Helvetica text, lines and one JPEG logo.
    /// No external libraries needed.
    /// </summary>
    public static class PdfExporter
    {
        public static void Export(TimesheetSheet sheet, byte[] logoPng, string path)
        {
            string title = string.Format("{0} {1} - {2}", TimesheetSheet.Title,
                TimeUtil.FormatDate(sheet.Period.Start), TimeUtil.FormatDate(sheet.Period.End));
            ExportPages((w, h) => SheetLayout.Build(sheet, w, h), logoPng, path, title, sheet.Name);
        }

        /// <summary>Writes laid-out pages; build gets the logo's pixel size so it can keep its shape.</summary>
        public static void ExportPages(Func<int, int, List<LayoutPage>> build, byte[] logoPng, string path, string title, string author)
        {
            int lw = 0, lh = 0;
            byte[] jpeg = null;
            if (logoPng != null) jpeg = ToJpeg(logoPng, out lw, out lh);
            var pages = build(lw, lh);
            string tmp = path + ".writing";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                Write(fs, pages, jpeg, lw, lh, title, author);
            DocxExporter.ReplaceFile(tmp, path);
        }

        /// <summary>Flattens the logo onto white (PDF JPEGs have no transparency).</summary>
        static byte[] ToJpeg(byte[] png, out int w, out int h)
        {
            using (var src = Image.FromStream(new MemoryStream(png)))
            {
                // Big enough to print sharp, small enough to keep the file light.
                double scale = Math.Min(1.0, 900.0 / Math.Max(src.Width, src.Height));
                w = Math.Max(1, (int)(src.Width * scale));
                h = Math.Max(1, (int)(src.Height * scale));
                using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, 0, 0, w, h);
                    }
                    var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    var p = new EncoderParameters(1);
                    p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, codec, p);
                        return ms.ToArray();
                    }
                }
            }
        }

        /// <summary>Print colors for each ink (the same ones the printer path uses).</summary>
        public static System.Drawing.Color PrintColor(Ink ink)
        {
            switch (ink)
            {
                case Ink.Muted: return System.Drawing.Color.FromArgb(96, 104, 110);
                case Ink.Grid: return System.Drawing.Color.FromArgb(214, 218, 221);
                case Ink.Bar: return System.Drawing.Color.FromArgb(18, 160, 58);
                case Ink.TravelBar: return System.Drawing.Color.FromArgb(110, 91, 214);
                default: return System.Drawing.Color.Black;
            }
        }

        static string Rgb(Ink ink)
        {
            var c = PrintColor(ink);
            return string.Format("{0} {1} {2}", N(c.R / 255f), N(c.G / 255f), N(c.B / 255f));
        }

        static string N(float v)
        {
            return Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
        }

        static void PdfString(Stream s, string text)
        {
            s.WriteByte((byte)'(');
            foreach (byte b in Fonts.WinAnsi.GetBytes(text ?? ""))
            {
                if (b == '(' || b == ')' || b == '\\') s.WriteByte((byte)'\\');
                if (b < 32) continue;
                s.WriteByte(b);
            }
            s.WriteByte((byte)')');
        }

        static void Ascii(Stream s, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            s.Write(bytes, 0, bytes.Length);
        }

        static byte[] Content(LayoutPage page)
        {
            using (var s = new MemoryStream())
            {
                Ascii(s, "2 J 0 G 0 g\n");
                foreach (var op in page.Ops)
                {
                    var img = op as ImageOp;
                    if (img != null)
                    {
                        Ascii(s, string.Format("q {0} 0 0 {1} {2} {3} cm /Im1 Do Q\n",
                            N(img.W), N(img.H), N(img.X), N(SheetLayout.PageH - img.Y - img.H)));
                        continue;
                    }
                    var line = op as LineOp;
                    if (line != null)
                    {
                        Ascii(s, string.Format("{0} RG {1} w {2} {3} m {4} {5} l S\n", Rgb(line.Ink), N(line.Width),
                            N(line.X1), N(SheetLayout.PageH - line.Y1), N(line.X2), N(SheetLayout.PageH - line.Y2)));
                        continue;
                    }
                    var fill = op as FillOp;
                    if (fill != null)
                    {
                        Ascii(s, string.Format("{0} rg {1} {2} {3} {4} re f\n", Rgb(fill.Ink),
                            N(fill.X), N(SheetLayout.PageH - fill.Y - fill.H), N(fill.W), N(fill.H)));
                        continue;
                    }
                    var text = op as TextOp;
                    if (text != null)
                    {
                        float x = text.X;
                        float w = Fonts.Width(text.Text, text.Bold, text.Size);
                        if (text.Align == TextAnchor.Center) x -= w / 2;
                        else if (text.Align == TextAnchor.Right) x -= w;
                        Ascii(s, string.Format("{0} rg BT /{1} {2} Tf 1 0 0 1 {3} {4} Tm ", Rgb(text.Ink), text.Bold ? "F2" : "F1",
                            N(text.Size), N(x), N(SheetLayout.PageH - text.Y)));
                        PdfString(s, text.Text);
                        Ascii(s, " Tj ET\n");
                    }
                }
                return s.ToArray();
            }
        }

        static void Write(Stream output, List<LayoutPage> pages, byte[] jpeg, int jw, int jh, string title, string author)
        {
            var offsets = new List<long>();
            var s = new MemoryStream();
            Action<int> beginObj = id =>
            {
                while (offsets.Count < id) offsets.Add(0);
                offsets[id - 1] = s.Position;
                Ascii(s, id + " 0 obj\n");
            };

            Ascii(s, "%PDF-1.4\n%");
            s.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }, 0, 5); // marks the file as binary

            // 1 catalog, 2 pages, 3-4 fonts, 5 info, 6 image, then page/content pairs.
            const int firstPageObj = 7;
            var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => (firstPageObj + i * 2) + " 0 R"));

            beginObj(1); Ascii(s, "<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            beginObj(2); Ascii(s, "<< /Type /Pages /Kids [" + kids + "] /Count " + pages.Count + " >>\nendobj\n");
            beginObj(3); Ascii(s, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");
            beginObj(4); Ascii(s, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

            beginObj(5);
            Ascii(s, "<< /Title ");
            PdfString(s, title);
            Ascii(s, " /Author ");
            PdfString(s, author);
            Ascii(s, " /Creator (TA Timesheet) /CreationDate (D:" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ") >>\nendobj\n");

            beginObj(6);
            if (jpeg != null)
            {
                Ascii(s, string.Format("<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB " +
                                       "/BitsPerComponent 8 /Filter /DCTDecode /Length {2} >>\nstream\n", jw, jh, jpeg.Length));
                s.Write(jpeg, 0, jpeg.Length);
                Ascii(s, "\nendstream\nendobj\n");
            }
            else
            {
                Ascii(s, "null\nendobj\n");
            }

            for (int i = 0; i < pages.Count; i++)
            {
                int pageObj = firstPageObj + i * 2, contentObj = pageObj + 1;
                beginObj(pageObj);
                Ascii(s, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + N(SheetLayout.PageW) + " " + N(SheetLayout.PageH) + "] " +
                         "/Resources << /Font << /F1 3 0 R /F2 4 0 R >>" + (jpeg != null ? " /XObject << /Im1 6 0 R >>" : "") + " >> " +
                         "/Contents " + contentObj + " 0 R >>\nendobj\n");
                var content = Content(pages[i]);
                beginObj(contentObj);
                Ascii(s, "<< /Length " + content.Length + " >>\nstream\n");
                s.Write(content, 0, content.Length);
                Ascii(s, "\nendstream\nendobj\n");
            }

            long xref = s.Position;
            Ascii(s, "xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
            foreach (var o in offsets) Ascii(s, o.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
            Ascii(s, "trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R /Info 5 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");

            s.Position = 0;
            s.CopyTo(output);
        }
    }
}
