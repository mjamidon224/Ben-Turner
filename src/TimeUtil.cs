using System;
using System.Globalization;

namespace TATimesheet
{
    public static class TimeUtil
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static readonly string[] DateFormats =
        {
            "M/d/yy", "M/d/yyyy", "MM/dd/yy", "MM/dd/yyyy", "yyyy-MM-dd", "yyyy-M-d",
            "M-d-yy", "M-d-yyyy", "M.d.yy", "M.d.yyyy", "ddd M/d/yy", "dddd M/d/yy"
        };

        /// <summary>
        /// Reads times the way people type them: 5, 0530, 530, 5:30, 17:15, 5:30p, 5:30 PM, 5.30, 24:00.
        /// Returns minutes after midnight (0..1440).
        /// </summary>
        public static bool TryParseTime(string s, out int minutes)
        {
            minutes = 0;
            if (s == null) return false;
            string t = s.Trim().ToLowerInvariant().Replace(" ", "");
            if (t.Length == 0) return false;

            bool am = false, pm = false;
            if (t.EndsWith("am")) { am = true; t = t.Substring(0, t.Length - 2); }
            else if (t.EndsWith("pm")) { pm = true; t = t.Substring(0, t.Length - 2); }
            else if (t.EndsWith("a")) { am = true; t = t.Substring(0, t.Length - 1); }
            else if (t.EndsWith("p")) { pm = true; t = t.Substring(0, t.Length - 1); }
            t = t.Replace('.', ':').Replace('h', ':');
            if (t.Length == 0) return false;

            int h, m;
            int colon = t.IndexOf(':');
            if (colon >= 0)
            {
                string hs = t.Substring(0, colon), ms = t.Substring(colon + 1);
                if (ms.Length == 0) ms = "0";
                if (hs.Length == 0 || hs.Length > 2 || ms.Length > 2) return false;
                if (!AllDigits(hs) || !AllDigits(ms)) return false;
                h = int.Parse(hs, Inv);
                m = int.Parse(ms, Inv);
            }
            else
            {
                if (!AllDigits(t)) return false;
                switch (t.Length)
                {
                    case 1:
                    case 2: h = int.Parse(t, Inv); m = 0; break;
                    case 3: h = int.Parse(t.Substring(0, 1), Inv); m = int.Parse(t.Substring(1), Inv); break;
                    case 4: h = int.Parse(t.Substring(0, 2), Inv); m = int.Parse(t.Substring(2), Inv); break;
                    default: return false;
                }
            }

            if (am || pm)
            {
                if (h < 1 || h > 12) return false;
                if (pm && h < 12) h += 12;
                if (am && h == 12) h = 0;
            }
            if (m > 59) return false;
            if (h == 24 && m == 0) { minutes = 1440; return true; }
            if (h > 23) return false;
            minutes = h * 60 + m;
            return true;
        }

        static bool AllDigits(string s)
        {
            foreach (char c in s)
                if (c < '0' || c > '9') return false;
            return true;
        }

        public static string FormatTime(int minutes)
        {
            return string.Format(Inv, "{0:00}:{1:00}", minutes / 60, minutes % 60);
        }

        /// <summary>Cleans up a typed time ("530p" -> "17:30"). Returns the input untouched if it can't be read.</summary>
        public static string NormalizeTime(string s)
        {
            int m;
            if (TryParseTime(s, out m)) return FormatTime(m);
            return s == null ? "" : s.Trim();
        }

        /// <summary>End minus start in hours. An end earlier than the start means the shift ran past midnight.</summary>
        public static double? ComputeHours(string start, string end)
        {
            int s, e;
            if (!TryParseTime(start, out s) || !TryParseTime(end, out e)) return null;
            int diff = e - s;
            if (diff < 0) diff += 1440;
            return Math.Round(diff / 60.0, 2);
        }

        public static string FormatHours(double? hours)
        {
            if (!hours.HasValue) return "";
            return Math.Round(hours.Value, 2).ToString("0.##", Inv);
        }

        public static bool TryParseHours(string s, out double hours)
        {
            hours = 0;
            if (s == null) return false;
            s = s.Trim().TrimEnd('h', 'H').Trim();
            if (s.Length == 0) return false;
            if (!double.TryParse(s, NumberStyles.Float, Inv, out hours)
                && !double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out hours))
                return false;
            return hours >= 0 && hours <= 48;
        }

        /// <summary>Date the way the timesheet prints it: 10/26/26.</summary>
        public static string FormatDate(DateTime d)
        {
            return d.ToString("M/d/yy", Inv);
        }

        public static bool TryParseDate(string s, out DateTime d)
        {
            d = DateTime.MinValue;
            if (s == null) return false;
            s = s.Trim();
            if (s.Length == 0) return false;
            if (DateTime.TryParseExact(s, DateFormats, Inv, DateTimeStyles.AllowWhiteSpaces, out d))
            {
                d = d.Date;
                return true;
            }
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out d))
            {
                d = d.Date;
                return d.Year > 1990 && d.Year < 2100;
            }
            return false;
        }
    }
}
