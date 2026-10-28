using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TATimesheet
{
    /// <summary>
    /// Keeps everything in one XML file under %APPDATA%\TA Timesheet, written atomically,
    /// with one automatic backup per day (last 30 kept) in the Backups folder.
    /// </summary>
    public class DataStore
    {
        const int BackupsToKeep = 30;

        public readonly string Folder;
        public readonly string FilePath;
        public string BackupFolder { get { return Path.Combine(Folder, "Backups"); } }
        public bool IsFirstRun { get; private set; }
        public DateTime LastSaved { get; private set; }

        public DataStore() : this(DefaultFolder()) { }

        public DataStore(string folder)
        {
            Folder = folder;
            FilePath = Path.Combine(folder, "timesheet-data.xml");
        }

        public static string DefaultFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), BuildInfo.DataFolderName);
        }

        public AppData Load()
        {
            Directory.CreateDirectory(Folder);
            if (!File.Exists(FilePath))
            {
                IsFirstRun = true;
                return AppData.CreateDefault();
            }
            return Read(FilePath);
        }

        /// <summary>Moves an unreadable data file out of the way so a fresh one can be started.</summary>
        public string QuarantineBrokenFile()
        {
            string dest = Path.Combine(Folder, "timesheet-data.broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".xml");
            File.Move(FilePath, dest);
            return dest;
        }

        public void Save(AppData data)
        {
            Directory.CreateDirectory(Folder);
            DailyBackup();
            string tmp = FilePath + ".tmp";
            Write(data, tmp);
            if (File.Exists(FilePath))
            {
                try
                {
                    File.Replace(tmp, FilePath, null);
                }
                catch (Exception)
                {
                    // File.Replace isn't supported everywhere (some network/sync folders).
                    File.Copy(tmp, FilePath, true);
                    File.Delete(tmp);
                }
            }
            else
            {
                File.Move(tmp, FilePath);
            }
            LastSaved = DateTime.Now;
        }

        void DailyBackup()
        {
            if (!File.Exists(FilePath)) return;
            try
            {
                Directory.CreateDirectory(BackupFolder);
                string today = Path.Combine(BackupFolder, "timesheet-data-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".xml");
                if (File.Exists(today)) return;
                File.Copy(FilePath, today);
                var old = new DirectoryInfo(BackupFolder).GetFiles("timesheet-data-*.xml")
                    .OrderByDescending(f => f.Name).Skip(BackupsToKeep);
                foreach (var f in old) f.Delete();
            }
            catch (Exception)
            {
                // A failed backup must never stop the real save.
            }
        }

        // ---- XML ----------------------------------------------------------------------------

        static string D(DateTime d) { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

        static DateTime ParseD(string s, DateTime fallback)
        {
            DateTime d;
            return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : fallback;
        }

        static string Attr(XElement e, string name)
        {
            var a = e.Attribute(name);
            return a == null ? "" : a.Value;
        }

        static XElement List(string name, List<string> items)
        {
            return new XElement(name, items.Select(i => new XElement("Item", i)));
        }

        static void ReadList(XElement parent, string name, List<string> into)
        {
            into.Clear();
            var e = parent == null ? null : parent.Element(name);
            if (e == null) return;
            foreach (var i in e.Elements("Item"))
                History.AddIfMissing(into, i.Value);
        }

        public static void Write(AppData d, string path)
        {
            var s = d.Settings;
            var doc = new XDocument(
                new XElement("TATimesheet", new XAttribute("version", 1),
                    new XElement("Settings",
                        new XAttribute("defaultName", s.DefaultName),
                        new XAttribute("periodWeeks", s.PeriodWeeks),
                        new XAttribute("periodAnchor", D(s.PeriodAnchor)),
                        new XAttribute("timeStep", s.TimeStepMinutes),
                        new XAttribute("exportFolder", s.ExportFolder),
                        new XAttribute("filePrefix", s.FilePrefix),
                        new XAttribute("logoPath", s.LogoPath),
                        new XAttribute("openAfterExport", s.OpenAfterExport),
                        new XAttribute("windowBounds", s.WindowBounds)),
                    new XElement("History",
                        List("Names", d.Names), List("Customers", d.Customers),
                        List("Projects", d.Projects), List("Locations", d.Locations)),
                    new XElement("Headers", d.Headers.OrderBy(h => h.Start).Select(h =>
                        new XElement("Header",
                            new XAttribute("start", D(h.Start)),
                            new XAttribute("name", h.Name),
                            new XAttribute("customer", h.Customer)))),
                    new XElement("Entries", d.Entries.OrderBy(e => e.Date).ThenBy(e => e.SortMinutes).Select(e =>
                        new XElement("Entry",
                            new XAttribute("id", e.Id.ToString()),
                            new XAttribute("date", D(e.Date)),
                            new XAttribute("project", e.Project),
                            new XAttribute("location", e.Location),
                            new XAttribute("start", e.Start),
                            new XAttribute("end", e.End),
                            new XAttribute("hours", e.Hours.HasValue ? e.Hours.Value.ToString("R", CultureInfo.InvariantCulture) : ""),
                            new XAttribute("notes", e.Notes))))));
            doc.Save(path);
        }

        public static AppData Read(string path)
        {
            var root = XDocument.Load(path).Root;
            if (root == null || root.Name != "TATimesheet")
                throw new InvalidDataException("This isn't a TA Timesheet data file.");

            var d = new AppData();
            var s = root.Element("Settings");
            if (s != null)
            {
                int n;
                bool b;
                d.Settings.DefaultName = Attr(s, "defaultName");
                if (int.TryParse(Attr(s, "periodWeeks"), out n) && n >= 1 && n <= 4) d.Settings.PeriodWeeks = n;
                d.Settings.PeriodAnchor = ParseD(Attr(s, "periodAnchor"), d.Settings.PeriodAnchor);
                if (int.TryParse(Attr(s, "timeStep"), out n) && n >= 1 && n <= 60) d.Settings.TimeStepMinutes = n;
                d.Settings.ExportFolder = Attr(s, "exportFolder");
                if (Attr(s, "filePrefix").Length > 0) d.Settings.FilePrefix = Attr(s, "filePrefix");
                d.Settings.LogoPath = Attr(s, "logoPath");
                if (bool.TryParse(Attr(s, "openAfterExport"), out b)) d.Settings.OpenAfterExport = b;
                d.Settings.WindowBounds = Attr(s, "windowBounds");
            }

            var hist = root.Element("History");
            ReadList(hist, "Names", d.Names);
            ReadList(hist, "Customers", d.Customers);
            ReadList(hist, "Projects", d.Projects);
            ReadList(hist, "Locations", d.Locations);

            var headers = root.Element("Headers");
            if (headers != null)
                foreach (var h in headers.Elements("Header"))
                    d.Headers.Add(new PeriodHeader
                    {
                        Start = ParseD(Attr(h, "start"), DateTime.Today),
                        Name = Attr(h, "name"),
                        Customer = Attr(h, "customer")
                    });

            var entries = root.Element("Entries");
            if (entries != null)
                foreach (var x in entries.Elements("Entry"))
                {
                    var e = new TimeEntry();
                    Guid id;
                    if (Guid.TryParse(Attr(x, "id"), out id)) e.Id = id;
                    e.Date = ParseD(Attr(x, "date"), DateTime.MinValue);
                    if (e.Date == DateTime.MinValue) continue;
                    e.Project = Attr(x, "project");
                    e.Location = Attr(x, "location");
                    e.Start = Attr(x, "start");
                    e.End = Attr(x, "end");
                    double h;
                    if (double.TryParse(Attr(x, "hours"), NumberStyles.Float, CultureInfo.InvariantCulture, out h)) e.Hours = h;
                    e.Notes = Attr(x, "notes");
                    d.Entries.Add(e);
                }
            return d;
        }
    }
}
