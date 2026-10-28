using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

namespace TATimesheet
{
    public static class AppResources
    {
        public const string TemplateResource = "TATimesheet.TimesheetTemplate.docx";
        public const string LogoPart = "word/media/image1.png";

        static byte[] template;

        /// <summary>The Word template (header logo, styles, page setup) built from Ben's real timesheet.</summary>
        public static byte[] TemplateDocx()
        {
            if (template == null)
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(TemplateResource))
                {
                    if (s == null) throw new InvalidOperationException("The timesheet template is missing from the program.");
                    template = ReadAll(s);
                }
            }
            return template;
        }

        public static byte[] DefaultLogoPng()
        {
            using (var zip = new ZipArchive(new MemoryStream(TemplateDocx()), ZipArchiveMode.Read))
            {
                var e = zip.GetEntry(LogoPart);
                using (var s = e.Open()) return ReadAll(s);
            }
        }

        /// <summary>The custom logo from Settings as PNG, or null when using the default one.</summary>
        public static byte[] CustomLogoPng(AppSettings s)
        {
            if (string.IsNullOrEmpty(s.LogoPath) || !File.Exists(s.LogoPath)) return null;
            using (var img = Image.FromFile(s.LogoPath))
            using (var ms = new MemoryStream())
            {
                img.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        public static byte[] LogoPng(AppSettings s)
        {
            return CustomLogoPng(s) ?? DefaultLogoPng();
        }

        public static Icon AppIcon()
        {
            try
            {
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static byte[] ReadAll(Stream s)
        {
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}
