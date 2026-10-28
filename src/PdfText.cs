using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TATimesheet
{
    /// <summary>A phrase of text on a page, in PDF points (origin bottom-left, y up).</summary>
    public class PdfTextRun
    {
        public int Page;
        public float X, Y, EndX, Size;
        public string Text = "";
        public float CenterX { get { return (X + EndX) / 2; } }
        public override string ToString() { return string.Format(CultureInfo.InvariantCulture, "p{0} ({1:0},{2:0}) {3}", Page, X, Y, Text); }
    }

    struct PdfMatrix
    {
        public float A, B, C, D, E, F;
        public static readonly PdfMatrix Identity = new PdfMatrix { A = 1, D = 1 };

        public static PdfMatrix Of(IList<object> v, int at)
        {
            Func<int, float> n = i => v[at + i] is double ? (float)(double)v[at + i] : 0f;
            return new PdfMatrix { A = n(0), B = n(1), C = n(2), D = n(3), E = n(4), F = n(5) };
        }

        public static PdfMatrix Translate(float x, float y) { return new PdfMatrix { A = 1, D = 1, E = x, F = y }; }

        /// <summary>this x m (apply this first, then m).</summary>
        public PdfMatrix Times(PdfMatrix m)
        {
            return new PdfMatrix
            {
                A = A * m.A + B * m.C, B = A * m.B + B * m.D,
                C = C * m.A + D * m.C, D = C * m.B + D * m.D,
                E = E * m.A + F * m.C + m.E, F = E * m.B + F * m.D + m.F
            };
        }

        public float X(float x, float y) { return x * A + y * C + E; }
        public float Y(float x, float y) { return x * B + y * D + F; }
        public float ScaleY { get { return (float)Math.Sqrt(C * C + D * D); } }
    }

    /// <summary>Maps a font's character codes to Unicode text and glyph widths.</summary>
    class PdfFont
    {
        public bool TwoByte;
        Dictionary<int, string> toUnicode;
        string[] simple;
        readonly Dictionary<int, float> widths = new Dictionary<int, float>();
        float defaultWidth = 500;
        bool helvetica, helveticaBold;

        public static PdfFont Load(PdfDocument doc, Dictionary<string, object> f)
        {
            var font = new PdfFont();
            string subtype = PdfDocument.Name(f, "Subtype") ?? "";
            string baseFont = PdfDocument.Name(f, "BaseFont") ?? "";
            font.TwoByte = subtype == "Type0";

            var tu = doc.Resolve(doc.Get(f, "ToUnicode")) as PdfStream;
            if (tu != null)
            {
                try { font.toUnicode = ParseCMap(doc.Decode(tu)); } catch (Exception) { }
            }

            if (font.TwoByte)
            {
                var desc = doc.Array(doc.Get(f, "DescendantFonts"));
                var cid = desc != null && desc.Count > 0 ? doc.Dict(desc[0]) : null;
                font.defaultWidth = (float)doc.Number(cid, "DW", 1000);
                var w = doc.Array(doc.Get(cid, "W"));
                if (w != null)
                {
                    for (int i = 0; i < w.Count;)
                    {
                        if (!(doc.Resolve(w[i]) is double)) break;
                        int first = (int)(double)doc.Resolve(w[i]);
                        var list = doc.Array(i + 1 < w.Count ? w[i + 1] : null);
                        if (list != null)
                        {
                            for (int k = 0; k < list.Count; k++)
                                if (doc.Resolve(list[k]) is double) font.widths[first + k] = (float)(double)doc.Resolve(list[k]);
                            i += 2;
                        }
                        else if (i + 2 < w.Count && doc.Resolve(w[i + 1]) is double && doc.Resolve(w[i + 2]) is double)
                        {
                            int last = (int)(double)doc.Resolve(w[i + 1]);
                            float width = (float)(double)doc.Resolve(w[i + 2]);
                            for (int c = first; c <= last && c - first < 65536; c++) font.widths[c] = width;
                            i += 3;
                        }
                        else break;
                    }
                }
                return font;
            }

            // Simple font: one byte per character.
            font.simple = BaseEncoding(PdfDocument.Name(f, "Encoding"));
            var enc = doc.Dict(doc.Get(f, "Encoding"));
            if (enc != null)
            {
                font.simple = BaseEncoding(PdfDocument.Name(enc, "BaseEncoding"));
                var diffs = doc.Array(doc.Get(enc, "Differences"));
                if (diffs != null)
                {
                    int code = 0;
                    foreach (var d in diffs.Select(doc.Resolve))
                    {
                        if (d is double) code = (int)(double)d;
                        else if (d is PdfName && code >= 0 && code < 256)
                        {
                            string u = GlyphToUnicode(((PdfName)d).Value);
                            if (u != null) font.simple[code] = u;
                            code++;
                        }
                    }
                }
            }
            int firstChar = (int)doc.Number(f, "FirstChar", 0);
            var ws = doc.Array(doc.Get(f, "Widths"));
            if (ws != null)
                for (int k = 0; k < ws.Count; k++)
                    if (doc.Resolve(ws[k]) is double) font.widths[firstChar + k] = (float)(double)doc.Resolve(ws[k]);
            var fd = doc.Dict(doc.Get(f, "FontDescriptor"));
            font.defaultWidth = (float)doc.Number(fd, "MissingWidth", ws == null ? 500 : 0);
            string plain = baseFont.Contains("+") ? baseFont.Substring(baseFont.IndexOf('+') + 1) : baseFont;
            if (ws == null && (plain.StartsWith("Helvetica") || plain.StartsWith("Arial")))
            {
                font.helvetica = true;
                font.helveticaBold = plain.Contains("Bold");
            }
            if (ws == null && plain.StartsWith("Courier")) font.defaultWidth = 600;
            return font;
        }

        static string[] BaseEncoding(string name)
        {
            var map = new string[256];
            Encoding enc = Encoding.GetEncoding(1252);
            if (name == "MacRomanEncoding")
            {
                try { enc = Encoding.GetEncoding(10000); } catch (Exception) { }
            }
            for (int i = 32; i < 256; i++) map[i] = enc.GetString(new[] { (byte)i });
            return map;
        }

        public string Text(int code)
        {
            string s;
            if (toUnicode != null && toUnicode.TryGetValue(code, out s)) return s;
            if (!TwoByte && simple != null && code < 256) return simple[code] ?? "";
            if (TwoByte) return toUnicode == null && code >= 32 && code < 0xD800 ? ((char)code).ToString() : "";
            return ((char)code).ToString();
        }

        public float Width(int code)
        {
            float w;
            if (widths.TryGetValue(code, out w)) return w;
            if (helvetica && code >= 32 && code < 256) return Fonts.Width(((char)code).ToString(), helveticaBold, 1000);
            return defaultWidth;
        }

        /// <summary>Reads a ToUnicode CMap: bfchar and bfrange entries, code -> text.</summary>
        static Dictionary<int, string> ParseCMap(byte[] data)
        {
            var map = new Dictionary<int, string>();
            var lex = new PdfLexer(data);
            var be = Encoding.BigEndianUnicode;
            Func<byte[], int> code = b => b.Aggregate(0, (acc, x) => (acc << 8) | x);
            while (!lex.AtEnd)
            {
                var kw = lex.ReadObject() as PdfKeyword;
                if (kw == null) continue;
                if (kw.Value == "beginbfchar")
                {
                    while (!lex.AtEnd)
                    {
                        object src = lex.ReadObject();
                        if (src is PdfKeyword) break;
                        object dst = lex.ReadObject();
                        var s = src as PdfString;
                        if (s == null) continue;
                        if (dst is PdfString) map[code(s.Bytes)] = be.GetString(((PdfString)dst).Bytes);
                        else if (dst is PdfName) map[code(s.Bytes)] = GlyphToUnicode(((PdfName)dst).Value) ?? "";
                    }
                }
                else if (kw.Value == "beginbfrange")
                {
                    while (!lex.AtEnd)
                    {
                        object lo = lex.ReadObject();
                        if (lo is PdfKeyword) break;
                        object hi = lex.ReadObject(), dst = lex.ReadObject();
                        var l = lo as PdfString;
                        var h = hi as PdfString;
                        if (l == null || h == null) continue;
                        int a = code(l.Bytes), z = Math.Min(code(h.Bytes), a + 65535);
                        var list = dst as List<object>;
                        var start = dst as PdfString;
                        for (int c = a; c <= z; c++)
                        {
                            if (list != null)
                            {
                                var item = c - a < list.Count ? list[c - a] as PdfString : null;
                                if (item != null) map[c] = be.GetString(item.Bytes);
                            }
                            else if (start != null && start.Bytes.Length >= 2)
                            {
                                string t = be.GetString(start.Bytes);
                                map[c] = t.Substring(0, t.Length - 1) + (char)(t[t.Length - 1] + (c - a));
                            }
                        }
                    }
                }
            }
            return map;
        }

        static readonly Dictionary<string, string> Glyphs = new Dictionary<string, string>
        {
            { "space", " " }, { "exclam", "!" }, { "quotedbl", "\"" }, { "numbersign", "#" }, { "dollar", "$" }, { "percent", "%" },
            { "ampersand", "&" }, { "quotesingle", "'" }, { "quoteright", "\u2019" }, { "quoteleft", "\u2018" }, { "parenleft", "(" },
            { "parenright", ")" }, { "asterisk", "*" }, { "plus", "+" }, { "comma", "," }, { "hyphen", "-" }, { "minus", "-" },
            { "period", "." }, { "slash", "/" }, { "zero", "0" }, { "one", "1" }, { "two", "2" }, { "three", "3" }, { "four", "4" },
            { "five", "5" }, { "six", "6" }, { "seven", "7" }, { "eight", "8" }, { "nine", "9" }, { "colon", ":" }, { "semicolon", ";" },
            { "less", "<" }, { "equal", "=" }, { "greater", ">" }, { "question", "?" }, { "at", "@" }, { "bracketleft", "[" },
            { "backslash", "\\" }, { "bracketright", "]" }, { "asciicircum", "^" }, { "underscore", "_" }, { "grave", "`" },
            { "braceleft", "{" }, { "bar", "|" }, { "braceright", "}" }, { "asciitilde", "~" }, { "endash", "\u2013" },
            { "emdash", "\u2014" }, { "bullet", "\u2022" }, { "ellipsis", "\u2026" }, { "quotedblleft", "\u201C" },
            { "quotedblright", "\u201D" }, { "nbspace", " " }, { "nonbreakingspace", " " }, { "degree", "\u00B0" },
            { "registered", "\u00AE" }, { "copyright", "\u00A9" }, { "trademark", "\u2122" }, { "periodcentered", "\u00B7" }
        };

        static string GlyphToUnicode(string name)
        {
            string s;
            if (Glyphs.TryGetValue(name, out s)) return s;
            if (name.Length == 1) return name;
            int v;
            if (name.StartsWith("uni") && name.Length >= 7 && int.TryParse(name.Substring(3, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
                return ((char)v).ToString();
            if (name.StartsWith("u") && name.Length >= 5 && name.Length <= 7 && int.TryParse(name.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) && v < 0x10000)
                return ((char)v).ToString();
            int dot = name.IndexOf('.');
            if (dot > 0) return GlyphToUnicode(name.Substring(0, dot)); // "a.sc", "one.oldstyle"
            return null;
        }
    }

    /// <summary>Runs page content streams and collects positioned text runs.</summary>
    public class PdfTextExtractor
    {
        class GState
        {
            public PdfMatrix Ctm = PdfMatrix.Identity;
            public PdfFont Font;
            public float Size = 12, CharSpace, WordSpace, Scale = 1, Leading, Rise;
            public GState Copy() { return (GState)MemberwiseClone(); }
        }

        readonly PdfDocument doc;
        readonly List<PdfTextRun> runs;
        readonly Dictionary<object, PdfFont> fonts = new Dictionary<object, PdfFont>();
        int page;
        PdfTextRun current;

        PdfTextExtractor(PdfDocument doc, List<PdfTextRun> runs)
        {
            this.doc = doc;
            this.runs = runs;
        }

        public static List<PdfTextRun> Extract(PdfDocument doc)
        {
            var runs = new List<PdfTextRun>();
            var x = new PdfTextExtractor(doc, runs);
            var pages = doc.Pages();
            for (int i = 0; i < pages.Count; i++)
            {
                x.page = i + 1;
                try
                {
                    x.Run(doc.PageContent(pages[i]), doc.Resources(pages[i]), new GState(), 0);
                }
                catch (Exception)
                {
                    // One unreadable page shouldn't sink the rest.
                }
                x.Flush();
            }
            foreach (var r in runs) r.Text = r.Text.Trim();
            runs.RemoveAll(r => r.Text.Length == 0);
            return runs;
        }

        void Run(byte[] content, Dictionary<string, object> resources, GState gs, int depth)
        {
            var stack = new Stack<GState>();
            var ops = new List<object>();
            var lex = new PdfLexer(content);
            PdfMatrix tm = PdfMatrix.Identity, tlm = PdfMatrix.Identity;
            var fontRes = doc.Dict(doc.Get(resources, "Font"));

            while (!lex.AtEnd)
            {
                object o = lex.ReadObject();
                var kw = o as PdfKeyword;
                if (kw == null) { ops.Add(o); continue; }
                switch (kw.Value)
                {
                    case "q": stack.Push(gs.Copy()); break;
                    case "Q": if (stack.Count > 0) gs = stack.Pop(); break;
                    case "cm": if (ops.Count >= 6) gs.Ctm = PdfMatrix.Of(ops, ops.Count - 6).Times(gs.Ctm); break;
                    case "BT": tm = tlm = PdfMatrix.Identity; break;
                    case "Tf":
                        if (ops.Count >= 2)
                        {
                            gs.Font = FontFor(fontRes, ops[ops.Count - 2] as PdfName);
                            gs.Size = Num(ops, ops.Count - 1);
                        }
                        break;
                    case "Tc": gs.CharSpace = Num(ops, ops.Count - 1); break;
                    case "Tw": gs.WordSpace = Num(ops, ops.Count - 1); break;
                    case "Tz": gs.Scale = Num(ops, ops.Count - 1) / 100f; break;
                    case "TL": gs.Leading = Num(ops, ops.Count - 1); break;
                    case "Ts": gs.Rise = Num(ops, ops.Count - 1); break;
                    case "Td":
                    case "TD":
                        if (ops.Count >= 2)
                        {
                            float tx = Num(ops, ops.Count - 2), ty = Num(ops, ops.Count - 1);
                            if (kw.Value == "TD") gs.Leading = -ty;
                            tlm = PdfMatrix.Translate(tx, ty).Times(tlm);
                            tm = tlm;
                        }
                        break;
                    case "Tm": if (ops.Count >= 6) tm = tlm = PdfMatrix.Of(ops, ops.Count - 6); break;
                    case "T*": tlm = PdfMatrix.Translate(0, -gs.Leading).Times(tlm); tm = tlm; break;
                    case "Tj": if (ops.Count >= 1) Show(ops[ops.Count - 1] as PdfString, gs, ref tm); break;
                    case "'":
                        tlm = PdfMatrix.Translate(0, -gs.Leading).Times(tlm); tm = tlm;
                        if (ops.Count >= 1) Show(ops[ops.Count - 1] as PdfString, gs, ref tm);
                        break;
                    case "\"":
                        if (ops.Count >= 3)
                        {
                            gs.WordSpace = Num(ops, ops.Count - 3);
                            gs.CharSpace = Num(ops, ops.Count - 2);
                            tlm = PdfMatrix.Translate(0, -gs.Leading).Times(tlm); tm = tlm;
                            Show(ops[ops.Count - 1] as PdfString, gs, ref tm);
                        }
                        break;
                    case "TJ":
                        var arr = ops.Count >= 1 ? ops[ops.Count - 1] as List<object> : null;
                        if (arr != null)
                            foreach (var item in arr)
                            {
                                if (item is PdfString) Show((PdfString)item, gs, ref tm);
                                else if (item is double) tm = PdfMatrix.Translate(-(float)(double)item / 1000f * gs.Size * gs.Scale, 0).Times(tm);
                            }
                        break;
                    case "Do":
                        if (depth < 6 && ops.Count >= 1) Form(resources, ops[ops.Count - 1] as PdfName, gs, depth);
                        break;
                    case "BI":
                        // Inline image: skip its dictionary and binary data.
                        while (!lex.AtEnd)
                        {
                            var k = lex.ReadObject() as PdfKeyword;
                            if (k != null && k.Value == "ID") { lex.SkipInlineImage(); break; }
                        }
                        break;
                }
                ops.Clear();
            }
        }

        void Form(Dictionary<string, object> resources, PdfName name, GState gs, int depth)
        {
            if (name == null) return;
            var xobjects = doc.Dict(doc.Get(resources, "XObject"));
            var form = doc.Resolve(doc.Get(xobjects, name.Value)) as PdfStream;
            if (form == null || PdfDocument.Name(form.Dict, "Subtype") != "Form") return;
            var inner = gs.Copy();
            var m = doc.Array(doc.Get(form.Dict, "Matrix"));
            if (m != null && m.Count == 6) inner.Ctm = PdfMatrix.Of(m.Select(doc.Resolve).ToList(), 0).Times(gs.Ctm);
            var res = doc.Dict(doc.Get(form.Dict, "Resources")) ?? resources;
            Run(doc.Decode(form), res, inner, depth + 1);
        }

        PdfFont FontFor(Dictionary<string, object> fontRes, PdfName name)
        {
            if (name == null || fontRes == null) return null;
            object key;
            if (!fontRes.TryGetValue(name.Value, out key)) return null;
            var dict = doc.Dict(key);
            if (dict == null) return null;
            PdfFont f;
            if (!fonts.TryGetValue(dict, out f))
            {
                try { f = PdfFont.Load(doc, dict); } catch (Exception) { f = null; }
                fonts[dict] = f;
            }
            return f;
        }

        static float Num(List<object> ops, int i)
        {
            return i >= 0 && i < ops.Count && ops[i] is double ? (float)(double)ops[i] : 0f;
        }

        void Show(PdfString s, GState gs, ref PdfMatrix tm)
        {
            if (s == null) return;
            var font = gs.Font;
            bool two = font != null && font.TwoByte;
            var bytes = s.Bytes;
            for (int i = 0; i < bytes.Length; i += two ? 2 : 1)
            {
                int code = two ? (i + 1 < bytes.Length ? (bytes[i] << 8) | bytes[i + 1] : bytes[i]) : bytes[i];
                string text = font != null ? font.Text(code) : Encoding.GetEncoding(1252).GetString(new[] { (byte)code });
                float w = font != null ? font.Width(code) : 500;
                var trm = PdfMatrix.Translate(0, gs.Rise).Times(tm).Times(gs.Ctm);
                float size = gs.Size * trm.ScaleY;
                float x = trm.X(0, 0), y = trm.Y(0, 0);
                float adv = (w / 1000f * gs.Size + gs.CharSpace + (!two && code == 32 ? gs.WordSpace : 0)) * gs.Scale;
                tm = PdfMatrix.Translate(adv, 0).Times(tm);
                var end = PdfMatrix.Translate(0, gs.Rise).Times(tm).Times(gs.Ctm);
                Glyph(text, x, y, end.X(0, 0), size);
            }
        }

        /// <summary>Joins glyphs into phrases. A gap wider than a generous word space starts a new run,
        /// so neighbouring table cells stay apart.</summary>
        void Glyph(string text, float x, float y, float endX, float size)
        {
            if (string.IsNullOrEmpty(text)) return;
            size = Math.Max(size, 1f);
            if (current != null && Math.Abs(y - current.Y) < size * 0.4f && x >= current.EndX - size * 0.6f && x <= current.EndX + size * 0.6f)
            {
                if (x - current.EndX > size * 0.18f && !current.Text.EndsWith(" ") && text != " ") current.Text += " ";
                if (!(text == " " && current.Text.EndsWith(" "))) current.Text += text;
                current.EndX = Math.Max(current.EndX, endX);
                return;
            }
            Flush();
            if (text.Trim().Length == 0) return;
            current = new PdfTextRun { Page = page, X = x, Y = y, EndX = endX, Size = size, Text = text };
        }

        void Flush()
        {
            if (current != null) runs.Add(current);
            current = null;
        }
    }
}
