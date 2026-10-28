using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TATimesheet
{
    // A small, read-only PDF parser: just enough to pull positioned text out of ordinary PDFs
    // (Word "Save as PDF", Microsoft Print to PDF, LibreOffice, this app's own exports).

    public sealed class PdfName
    {
        public readonly string Value;
        public PdfName(string v) { Value = v; }
        public override string ToString() { return "/" + Value; }
    }

    public sealed class PdfRef
    {
        public readonly int Num, Gen;
        public PdfRef(int num, int gen) { Num = num; Gen = gen; }
    }

    public sealed class PdfString
    {
        public readonly byte[] Bytes;
        public PdfString(byte[] b) { Bytes = b; }
    }

    public sealed class PdfStream
    {
        public readonly Dictionary<string, object> Dict;
        public readonly byte[] Raw;
        public PdfStream(Dictionary<string, object> dict, byte[] raw) { Dict = dict; Raw = raw; }
    }

    public sealed class PdfKeyword
    {
        public readonly string Value;
        public PdfKeyword(string v) { Value = v; }
        public override string ToString() { return Value; }
    }

    /// <summary>Tokenizer/parser for PDF object syntax (also used for content streams and CMaps).</summary>
    public class PdfLexer
    {
        readonly byte[] b;
        public int Pos;
        readonly int end;
        static readonly object ArrayEnd = new object(), DictEnd = new object();

        public PdfLexer(byte[] data) : this(data, 0, data.Length) { }

        public PdfLexer(byte[] data, int start, int end)
        {
            b = data;
            Pos = start;
            this.end = Math.Min(end, data.Length);
        }

        public bool AtEnd { get { SkipSpace(); return Pos >= end; } }

        static bool IsSpace(byte c) { return c == 0 || c == 9 || c == 10 || c == 12 || c == 13 || c == 32; }
        static bool IsDelim(byte c) { return c == '(' || c == ')' || c == '<' || c == '>' || c == '[' || c == ']' || c == '{' || c == '}' || c == '/' || c == '%'; }

        public void SkipSpace()
        {
            while (Pos < end)
            {
                byte c = b[Pos];
                if (IsSpace(c)) { Pos++; continue; }
                if (c == '%') { while (Pos < end && b[Pos] != 10 && b[Pos] != 13) Pos++; continue; }
                break;
            }
        }

        /// <summary>Next complete object (numbers, names, strings, arrays, dicts, refs, keywords); null at the end.</summary>
        public object ReadObject()
        {
            object t = Token();
            if (t is double)
            {
                // "12 0 R" is a reference.
                double n = (double)t;
                int save = Pos;
                object t2 = Token();
                if (t2 is double && n >= 0 && n == Math.Floor(n))
                {
                    object t3 = Token();
                    var kw = t3 as PdfKeyword;
                    if (kw != null && kw.Value == "R") return new PdfRef((int)n, (int)(double)t2);
                }
                Pos = save;
                return n;
            }
            if (t == (object)ArrayStart)
            {
                var list = new List<object>();
                while (true)
                {
                    object o = ReadObjectOrEnd();
                    if (o == ArrayEnd || o == null) break;
                    list.Add(o);
                }
                return list;
            }
            if (t == (object)DictStart)
            {
                var dict = new Dictionary<string, object>();
                while (true)
                {
                    object k = ReadObjectOrEnd();
                    if (k == DictEnd || k == null) break;
                    var name = k as PdfName;
                    object v = ReadObjectOrEnd();
                    if (v == DictEnd || v == null) break;
                    if (name != null) dict[name.Value] = v;
                }
                return dict;
            }
            return t;
        }

        object ReadObjectOrEnd()
        {
            int save = Pos;
            object t = Token();
            if (t == ArrayEnd || t == DictEnd) return t;
            Pos = save;
            return ReadObject();
        }

        static readonly object ArrayStart = new object(), DictStart = new object();

        object Token()
        {
            SkipSpace();
            if (Pos >= end) return null;
            byte c = b[Pos];
            if (c == '[') { Pos++; return ArrayStart; }
            if (c == ']') { Pos++; return ArrayEnd; }
            if (c == '<' && Pos + 1 < end && b[Pos + 1] == '<') { Pos += 2; return DictStart; }
            if (c == '>' && Pos + 1 < end && b[Pos + 1] == '>') { Pos += 2; return DictEnd; }
            if (c == '<') return HexString();
            if (c == '(') return LiteralString();
            if (c == '/') return Name();
            if (c == '{' || c == '}' || c == '>' || c == ')') { Pos++; return new PdfKeyword(((char)c).ToString()); }
            if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.')
            {
                int s = Pos;
                Pos++;
                while (Pos < end && ((b[Pos] >= '0' && b[Pos] <= '9') || b[Pos] == '.')) Pos++;
                double v;
                string txt = Encoding.ASCII.GetString(b, s, Pos - s);
                if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
                return 0.0;
            }
            int st = Pos;
            while (Pos < end && !IsSpace(b[Pos]) && !IsDelim(b[Pos])) Pos++;
            if (Pos == st) { Pos++; return new PdfKeyword(((char)c).ToString()); }
            string word = Encoding.ASCII.GetString(b, st, Pos - st);
            if (word == "true") return true;
            if (word == "false") return false;
            if (word == "null") return null;
            return new PdfKeyword(word);
        }

        PdfName Name()
        {
            Pos++;
            var sb = new StringBuilder();
            while (Pos < end && !IsSpace(b[Pos]) && !IsDelim(b[Pos]))
            {
                if (b[Pos] == '#' && Pos + 2 < end)
                {
                    int h;
                    if (int.TryParse(Encoding.ASCII.GetString(b, Pos + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out h))
                    {
                        sb.Append((char)h);
                        Pos += 3;
                        continue;
                    }
                }
                sb.Append((char)b[Pos]);
                Pos++;
            }
            return new PdfName(sb.ToString());
        }

        PdfString HexString()
        {
            Pos++;
            var bytes = new List<byte>();
            int hi = -1;
            while (Pos < end && b[Pos] != '>')
            {
                int v = HexVal(b[Pos++]);
                if (v < 0) continue;
                if (hi < 0) hi = v;
                else { bytes.Add((byte)(hi * 16 + v)); hi = -1; }
            }
            if (hi >= 0) bytes.Add((byte)(hi * 16));
            Pos++;
            return new PdfString(bytes.ToArray());
        }

        static int HexVal(byte c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        PdfString LiteralString()
        {
            Pos++;
            var bytes = new List<byte>();
            int depth = 1;
            while (Pos < end)
            {
                byte c = b[Pos++];
                if (c == '\\' && Pos < end)
                {
                    byte e = b[Pos++];
                    switch (e)
                    {
                        case (byte)'n': bytes.Add(10); break;
                        case (byte)'r': bytes.Add(13); break;
                        case (byte)'t': bytes.Add(9); break;
                        case (byte)'b': bytes.Add(8); break;
                        case (byte)'f': bytes.Add(12); break;
                        case 13: if (Pos < end && b[Pos] == 10) Pos++; break; // line continuation
                        case 10: break;
                        default:
                            if (e >= '0' && e <= '7')
                            {
                                int v = e - '0';
                                for (int i = 0; i < 2 && Pos < end && b[Pos] >= '0' && b[Pos] <= '7'; i++) v = v * 8 + (b[Pos++] - '0');
                                bytes.Add((byte)v);
                            }
                            else bytes.Add(e);
                            break;
                    }
                    continue;
                }
                if (c == '(') depth++;
                else if (c == ')' && --depth == 0) break;
                bytes.Add(c);
            }
            return new PdfString(bytes.ToArray());
        }

        /// <summary>Skips an inline image's binary data (after the ID operator) up to EI.</summary>
        public void SkipInlineImage()
        {
            Pos++;
            while (Pos + 2 < end)
            {
                if (b[Pos] == 'E' && b[Pos + 1] == 'I' && IsSpace(b[Pos - 1]) && (Pos + 2 >= end || IsSpace(b[Pos + 2]) || IsDelim(b[Pos + 2])))
                {
                    Pos += 2;
                    return;
                }
                Pos++;
            }
            Pos = end;
        }
    }

    public class PdfDocument
    {
        readonly Dictionary<int, object> objects = new Dictionary<int, object>();
        readonly List<Dictionary<string, object>> trailers = new List<Dictionary<string, object>>();
        public bool Encrypted { get; private set; }

        static readonly Regex ObjHeader = new Regex(@"(?<![0-9])(\d{1,7})\s+(\d{1,5})\s+obj\b", RegexOptions.Compiled);

        /// <summary>
        /// Reads every "n g obj" in file order (later definitions win, like incremental updates),
        /// then unpacks object streams. Doesn't rely on the xref table, so damaged files still load.
        /// </summary>
        public static PdfDocument Load(byte[] data)
        {
            var doc = new PdfDocument();
            string text = Encoding.GetEncoding(28591).GetString(data); // byte-for-byte
            if (!text.StartsWith("%PDF", StringComparison.Ordinal) && text.IndexOf("%PDF", 0, Math.Min(text.Length, 1024), StringComparison.Ordinal) < 0)
                throw new InvalidDataException("Not a PDF file.");

            int skipUntil = 0;
            foreach (Match m in ObjHeader.Matches(text))
            {
                if (m.Index < skipUntil) continue; // inside a stream we already read
                int num = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var lex = new PdfLexer(data, m.Index + m.Length, data.Length);
                object obj;
                try { obj = lex.ReadObject(); }
                catch (Exception) { continue; }
                var dict = obj as Dictionary<string, object>;
                int after = lex.Pos;
                if (dict != null)
                {
                    lex.SkipSpace();
                    if (lex.Pos + 6 <= data.Length && text.Substring(lex.Pos, 6) == "stream")
                    {
                        int start = lex.Pos + 6;
                        if (start < data.Length && data[start] == 13) start++;
                        if (start < data.Length && data[start] == 10) start++;
                        int len = -1;
                        object l;
                        if (dict.TryGetValue("Length", out l) && l is double) len = (int)(double)l;
                        int endPos;
                        if (len >= 0 && start + len <= data.Length && LooksLikeEndstream(text, start + len)) endPos = start + len;
                        else
                        {
                            int es = text.IndexOf("endstream", start, StringComparison.Ordinal);
                            if (es < 0) es = data.Length;
                            endPos = es;
                            while (endPos > start && (data[endPos - 1] == 10 || data[endPos - 1] == 13)) endPos--;
                        }
                        var raw = new byte[endPos - start];
                        Buffer.BlockCopy(data, start, raw, 0, raw.Length);
                        obj = new PdfStream(dict, raw);
                        after = endPos;
                    }
                }
                doc.objects[num] = obj;
                skipUntil = after;

                var st = obj as PdfStream;
                if (st != null && Name(st.Dict, "Type") == "XRef") doc.trailers.Add(st.Dict);
            }

            int t = 0;
            while ((t = text.IndexOf("trailer", t, StringComparison.Ordinal)) >= 0)
            {
                var lex = new PdfLexer(data, t + 7, data.Length);
                var d = lex.ReadObject() as Dictionary<string, object>;
                if (d != null) doc.trailers.Add(d);
                t += 7;
            }
            doc.Encrypted = doc.trailers.Any(d => d.ContainsKey("Encrypt"));

            // Objects packed inside object streams (PDF 1.5+).
            foreach (var st in doc.objects.Values.OfType<PdfStream>().Where(s => Name(s.Dict, "Type") == "ObjStm").ToList())
            {
                try
                {
                    byte[] dec = doc.Decode(st);
                    int n = (int)doc.Number(st.Dict, "N", 0), first = (int)doc.Number(st.Dict, "First", 0);
                    var head = new PdfLexer(dec, 0, first);
                    var pairs = new List<int[]>();
                    for (int i = 0; i < n; i++)
                    {
                        object a = head.ReadObject(), o = head.ReadObject();
                        if (!(a is double) || !(o is double)) break;
                        pairs.Add(new[] { (int)(double)a, (int)(double)o });
                    }
                    foreach (var p in pairs)
                    {
                        if (doc.objects.ContainsKey(p[0])) continue; // a directly-written object wins
                        var lex = new PdfLexer(dec, first + p[1], dec.Length);
                        doc.objects[p[0]] = lex.ReadObject();
                    }
                }
                catch (Exception)
                {
                    // Skip a broken object stream; whatever else is readable still is.
                }
            }
            return doc;
        }

        static bool LooksLikeEndstream(string text, int pos)
        {
            int i = pos;
            while (i < text.Length && (text[i] == '\r' || text[i] == '\n' || text[i] == ' ')) i++;
            return i + 9 <= text.Length && string.CompareOrdinal(text, i, "endstream", 0, 9) == 0;
        }

        public object Resolve(object o)
        {
            for (int depth = 0; depth < 32; depth++)
            {
                var r = o as PdfRef;
                if (r == null) return o;
                object v;
                if (!objects.TryGetValue(r.Num, out v)) return null;
                o = v;
            }
            return null;
        }

        public Dictionary<string, object> Dict(object o)
        {
            o = Resolve(o);
            var s = o as PdfStream;
            return s != null ? s.Dict : o as Dictionary<string, object>;
        }

        public object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? Resolve(v) : null;
        }

        public double Number(Dictionary<string, object> d, string key, double fallback)
        {
            var v = Get(d, key);
            return v is double ? (double)v : fallback;
        }

        public static string Name(Dictionary<string, object> d, string key)
        {
            object v;
            var n = d != null && d.TryGetValue(key, out v) ? v as PdfName : null;
            return n != null ? n.Value : null;
        }

        public List<object> Array(object o)
        {
            return Resolve(o) as List<object>;
        }

        /// <summary>Pages in reading order (falls back to every /Type /Page object if the tree is broken).</summary>
        public List<Dictionary<string, object>> Pages()
        {
            var pages = new List<Dictionary<string, object>>();
            Dictionary<string, object> root = null;
            foreach (var t in trailers)
            {
                root = Dict(Get(t, "Root"));
                if (root != null) break;
            }
            if (root == null)
                root = objects.Values.Select(Dict).FirstOrDefault(d => d != null && Name(d, "Type") == "Catalog");
            if (root != null) Walk(Dict(Get(root, "Pages")), pages, 0);
            if (pages.Count == 0)
                pages = objects.OrderBy(kv => kv.Key).Select(kv => Dict(kv.Value)).Where(d => d != null && Name(d, "Type") == "Page").ToList();
            return pages;
        }

        void Walk(Dictionary<string, object> node, List<Dictionary<string, object>> pages, int depth)
        {
            if (node == null || depth > 64) return;
            if (Name(node, "Type") == "Page" || (!node.ContainsKey("Kids") && node.ContainsKey("Contents")))
            {
                pages.Add(node);
                return;
            }
            var kids = Array(Get(node, "Kids"));
            if (kids == null) return;
            foreach (var k in kids) Walk(Dict(k), pages, depth + 1);
        }

        /// <summary>A page's Resources, inherited from its parents if needed.</summary>
        public Dictionary<string, object> Resources(Dictionary<string, object> page)
        {
            for (var n = page; n != null; n = Dict(Get(n, "Parent")))
            {
                var r = Dict(Get(n, "Resources"));
                if (r != null) return r;
            }
            return new Dictionary<string, object>();
        }

        public byte[] PageContent(Dictionary<string, object> page)
        {
            object c = Get(page, "Contents");
            var list = c as List<object>;
            var parts = new List<byte[]>();
            if (list != null)
            {
                foreach (var item in list)
                {
                    var s = Resolve(item) as PdfStream;
                    if (s != null) parts.Add(Decode(s));
                }
            }
            else if (c is PdfStream) parts.Add(Decode((PdfStream)c));
            using (var ms = new MemoryStream())
            {
                foreach (var p in parts)
                {
                    ms.Write(p, 0, p.Length);
                    ms.WriteByte(10);
                }
                return ms.ToArray();
            }
        }

        public byte[] Decode(PdfStream s)
        {
            byte[] data = s.Raw;
            object f = Get(s.Dict, "Filter");
            var filters = f is List<object> ? ((List<object>)f).Select(x => Resolve(x) as PdfName).ToList() : new List<PdfName> { f as PdfName };
            object p = Get(s.Dict, "DecodeParms");
            var parms = p is List<object> ? ((List<object>)p).Select(x => Dict(x)).ToList() : new List<Dictionary<string, object>> { Dict(p) };
            for (int i = 0; i < filters.Count; i++)
            {
                if (filters[i] == null) continue;
                var parm = i < parms.Count ? parms[i] : null;
                switch (filters[i].Value)
                {
                    case "FlateDecode":
                    case "Fl":
                        data = Predict(Inflate(data), parm);
                        break;
                    case "ASCIIHexDecode":
                    case "AHx":
                        data = HexDecode(data);
                        break;
                    case "ASCII85Decode":
                    case "A85":
                        data = Ascii85(data);
                        break;
                    default:
                        throw new NotSupportedException("PDF filter " + filters[i].Value);
                }
            }
            return data;
        }

        static byte[] Inflate(byte[] data)
        {
            int skip = data.Length > 2 && (data[0] & 0x0F) == 8 && ((data[0] << 8) | data[1]) % 31 == 0 ? 2 : 0;
            using (var input = new MemoryStream(data, skip, data.Length - skip))
            using (var z = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buf = new byte[16384];
                try
                {
                    int n;
                    while ((n = z.Read(buf, 0, buf.Length)) > 0) output.Write(buf, 0, n);
                }
                catch (InvalidDataException)
                {
                    // Truncated/damaged stream: keep what decoded fine.
                }
                return output.ToArray();
            }
        }

        /// <summary>PNG row predictors (used by xref/object streams).</summary>
        byte[] Predict(byte[] data, Dictionary<string, object> parm)
        {
            int predictor = (int)Number(parm, "Predictor", 1);
            if (predictor < 10) return data;
            int colors = (int)Number(parm, "Colors", 1), bpc = (int)Number(parm, "BitsPerComponent", 8), columns = (int)Number(parm, "Columns", 1);
            int bpp = Math.Max(1, colors * bpc / 8), rowLen = (colors * bpc * columns + 7) / 8;
            var output = new MemoryStream();
            var prev = new byte[rowLen];
            for (int pos = 0; pos + 1 + rowLen <= data.Length; pos += rowLen + 1)
            {
                int type = data[pos];
                var row = new byte[rowLen];
                Buffer.BlockCopy(data, pos + 1, row, 0, rowLen);
                for (int i = 0; i < rowLen; i++)
                {
                    int left = i >= bpp ? row[i - bpp] : 0, up = prev[i], upLeft = i >= bpp ? prev[i - bpp] : 0;
                    switch (type)
                    {
                        case 1: row[i] = (byte)(row[i] + left); break;
                        case 2: row[i] = (byte)(row[i] + up); break;
                        case 3: row[i] = (byte)(row[i] + (left + up) / 2); break;
                        case 4:
                            int pa = Math.Abs(up - upLeft), pb = Math.Abs(left - upLeft), pc = Math.Abs(left + up - 2 * upLeft);
                            row[i] = (byte)(row[i] + (pa <= pb && pa <= pc ? left : pb <= pc ? up : upLeft));
                            break;
                    }
                }
                output.Write(row, 0, rowLen);
                prev = row;
            }
            return output.ToArray();
        }

        static byte[] HexDecode(byte[] data)
        {
            var output = new List<byte>();
            int hi = -1;
            foreach (byte c in data)
            {
                if (c == '>') break;
                int v = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                if (v < 0) continue;
                if (hi < 0) hi = v;
                else { output.Add((byte)(hi * 16 + v)); hi = -1; }
            }
            if (hi >= 0) output.Add((byte)(hi * 16));
            return output.ToArray();
        }

        static byte[] Ascii85(byte[] data)
        {
            var output = new List<byte>();
            uint tuple = 0;
            int count = 0;
            foreach (byte c in data)
            {
                if (c == '~') break;
                if (c == 'z' && count == 0) { output.AddRange(new byte[4]); continue; }
                if (c < '!' || c > 'u') continue;
                tuple = tuple * 85 + (uint)(c - '!');
                if (++count == 5)
                {
                    output.Add((byte)(tuple >> 24)); output.Add((byte)(tuple >> 16)); output.Add((byte)(tuple >> 8)); output.Add((byte)tuple);
                    tuple = 0;
                    count = 0;
                }
            }
            if (count > 1)
            {
                for (int i = count; i < 5; i++) tuple = tuple * 85 + 84;
                for (int i = 0; i < count - 1; i++) output.Add((byte)(tuple >> (24 - 8 * i)));
            }
            return output.ToArray();
        }
    }
}
