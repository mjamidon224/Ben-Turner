"""Test fixture: synthetic PDF shaped like Word / Microsoft Print to PDF output: Type0 Identity-H font with
glyph ids that aren't Unicode (so a ToUnicode CMap is required), Flate streams, a y-flipped
scaled CTM, kerned TJ arrays and a date split into several positioned pieces.

    python3 tests/make_wordlike_pdf.py tests/fixtures/wordlike-identity-h.pdf
    qpdf --object-streams=generate tests/fixtures/wordlike-identity-h.pdf tests/fixtures/wordlike-objstm.pdf
"""
import sys, zlib
out = sys.argv[1]
chars = sorted(set("Customer Timesheet Name Benjamin Turner Mission Automation Date Project Location Start Time End Hours "
                   "0123456789/:.,-() Motherson Newark CA Week Total Travel"))
gid = {c: 40 + i * 3 for i, c in enumerate(chars)}           # glyph ids unrelated to Unicode
def hexs(s): return "<" + "".join("%04X" % gid[c] for c in s) + ">"
# ToUnicode: half via bfrange, half via bfchar
items = sorted((gid[c], c) for c in chars)
cmap = ["/CIDInit /ProcSet findresource begin 12 dict begin begincmap",
        "/CMapName /Adobe-Identity-UCS def 1 begincodespacerange <0000> <FFFF> endcodespacerange"]
half = len(items) // 2
cmap.append("%d beginbfchar" % half)
for g, c in items[:half]: cmap.append("<%04X> <%04X>" % (g, ord(c)))
cmap.append("endbfchar")
cmap.append("%d beginbfrange" % (len(items) - half))
for g, c in items[half:]: cmap.append("<%04X> <%04X> <%04X>" % (g, g, ord(c)))
cmap.append("endbfrange endcmap CMapName currentdict /CMap defineresource pop end end")
tounicode = zlib.compress("\n".join(cmap).encode())

# Page coordinates are in a y-down space scaled by 0.5 (like Print to PDF), so double everything.
H = 612
def place(x, y_top, size=15):  # x, y_top in points from top-left
    return "1 0 0 -1 %.2f %.2f Tm" % (x * 2, y_top * 2)
lines = ["q 0.5 0 0 -0.5 0 %d cm" % H, "BT"]
def text(x, y, s, size=15, kern=False):
    lines.append("/F1 %d Tf" % (size * 2))
    lines.append(place(x, y))
    if kern and len(s) > 3:
        a, b = s[: len(s) // 2], s[len(s) // 2 :]
        lines.append("[%s -12 %s] TJ" % (hexs(a), hexs(b)))
    else:
        lines.append("%s Tj" % hexs(s))
text(268, 96, "Customer Timesheet", 25)
text(77, 128, "Name"); text(152, 128, "Benjamin Turner", kern=True)
text(77, 149, "Customer"); text(152, 158 - 9, "Mission Automation", kern=True)
cols = [(105, "Date"), (197, "Project"), (293, "Location"), (388, "Start Time"), (492, "End Time"), (606, "Hours")]
for x, h in cols: text(x, 219, h, kern=True)
rows = [("10/26/26", "06:00", "17:15", "11.25", False), ("10/27/26", "05:00", "19:30", "14.5", True),
        ("10/28/26", "07:00", "15:00", "8", False), ("10/29/26", "07:00", "11:00", "4", False)]
y = 252
for d, s, e, hrs, travel in rows:
    # the date in pieces, each placed separately (Word does this when the source has several runs)
    lines.append("/F1 30 Tf"); lines.append(place(91, y)); lines.append("%s Tj" % hexs(d[:2]))
    lines.append("16 0 Td %s Tj" % hexs(d[2:3]))   # Td is in text space (already x2): 8pt later
    lines.append("8.4 0 Td %s Tj" % hexs(d[3:5]))
    lines.append("16 0 Td %s Tj" % hexs(d[5:]))
    text(185, y - 9, "Motherson"); text(189, y + 9, "260104-2")
    text(282, y if not travel else y - 9, "Newark, CA")
    if travel: text(287, y + 9, "(Travel)")
    text(404, y, s); text(509, y, e); text(612, y, hrs)
    y += 38
text(492, y + 4, "Week 1"); text(497, y + 22, "Hours"); text(612, y + 13, "37.75")
lines += ["ET", "Q"]
content = zlib.compress("\n".join(lines).encode("latin-1"))

objs = []
objs.append(b"<< /Type /Catalog /Pages 2 0 R >>")
objs.append(b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
objs.append(b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 792 612] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>")
objs.append(b"<< /Length %d /Filter /FlateDecode >>\nstream\n" % len(content) + content + b"\nendstream")
objs.append(b"<< /Type /Font /Subtype /Type0 /BaseFont /ABCDEF+AptosNarrow /Encoding /Identity-H /DescendantFonts [6 0 R] /ToUnicode 7 0 R >>")
w = " ".join("%d [%d]" % (gid[c], 230 if c in " .:/,-()1" else 520) for c in chars)
objs.append(("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /ABCDEF+AptosNarrow /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /DW 500 /W [%s] >>" % w).encode())
objs.append(b"<< /Length %d /Filter /FlateDecode >>\nstream\n" % len(tounicode) + tounicode + b"\nendstream")
pdf = bytearray(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n")
offs = []
for i, o in enumerate(objs, 1):
    offs.append(len(pdf)); pdf += b"%d 0 obj\n" % i + o + b"\nendobj\n"
x = len(pdf)
pdf += b"xref\n0 %d\n0000000000 65535 f \n" % (len(objs) + 1) + b"".join(b"%010d 00000 n \n" % o for o in offs)
pdf += b"trailer\n<< /Size %d /Root 1 0 R >>\nstartxref\n%d\n%%%%EOF\n" % (len(objs) + 1, x)
open(out, "wb").write(pdf)
