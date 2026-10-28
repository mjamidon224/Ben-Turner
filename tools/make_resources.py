"""Builds src/Resources/TimesheetTemplate.docx and app.ico from a real timesheet .docx.

The template keeps the original's header (logo), styles, theme and page setup.
The app regenerates word/document.xml on every export, so the body here is just a stub.

    python3 tools/make_resources.py path/to/TA_Timesheet_XXXXXX.docx
"""
import io, re, sys, zipfile
from pathlib import Path
from PIL import Image

src = Path(sys.argv[1])
out_dir = Path(__file__).resolve().parent.parent / "src" / "Resources"
out_dir.mkdir(parents=True, exist_ok=True)

STUB_DOC = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" '
            'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body><w:p/>'
            '<w:sectPr><w:headerReference w:type="default" r:id="rId7"/>'
            '<w:pgSz w:w="15840" w:h="12240" w:orient="landscape"/>'
            '<w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>'
            '<w:cols w:space="720"/><w:docGrid w:linePitch="360"/></w:sectPr></w:body></w:document>')

with zipfile.ZipFile(src) as zin, zipfile.ZipFile(out_dir / "TimesheetTemplate.docx", "w", zipfile.ZIP_DEFLATED) as zout:
    for item in zin.infolist():
        name = item.filename
        if name.startswith("customXml/"):
            continue  # Word bibliography junk, not needed
        data = zin.read(name)
        if name == "word/document.xml":
            data = STUB_DOC.encode("utf-8")
        elif name == "word/_rels/document.xml.rels":
            data = re.sub(rb'<Relationship [^>]*customXml[^>]*/>', b"", data)
        elif name == "[Content_Types].xml":
            data = re.sub(rb'<Override PartName="/customXml/[^>]*/>', b"", data)
        elif name == "word/settings.xml":
            data = re.sub(rb"<w:rsids>.*?</w:rsids>", b"", data, flags=re.S)
        zout.writestr(name, data)
    logo = Image.open(io.BytesIO(zin.read("word/media/image1.png"))).convert("RGBA")

# App icon: the round "TA" mark from the top of the logo, white made transparent.
px = logo.load()
w, h = logo.size
def ink(x, y):
    r, g, b, a = px[x, y]
    return a > 128 and not (r > 225 and g > 225 and b > 225)
rows = [any(ink(x, y) for x in range(w)) for y in range(h)]
top = rows.index(True)
bottom = top
while bottom < h and rows[bottom]:
    bottom += 1
cols = [x for x in range(w) if any(ink(x, y) for y in range(top, bottom))]
mark = logo.crop((cols[0], top, cols[-1] + 1, bottom))
data = [(r, g, b, 0) if r > 235 and g > 235 and b > 235 else (r, g, b, a) for (r, g, b, a) in list(mark.getdata())]
mark.putdata(data)
side = max(mark.size) + 8
square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
square.paste(mark, ((side - mark.size[0]) // 2, (side - mark.size[1]) // 2), mark)
ICO_SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
square.save(out_dir / "app.ico", sizes=ICO_SIZES)

# TEST copy icon: same mark with an orange badge so the two are easy to tell apart.
from PIL import ImageDraw
test = square.resize((256, 256), Image.LANCZOS)
d = ImageDraw.Draw(test)
d.ellipse((128, 128, 252, 252), fill=(255, 176, 0, 255), outline=(0, 0, 0, 255), width=10)
d.rectangle((166, 160, 214, 176), fill=(0, 0, 0, 255))   # a chunky "T"
d.rectangle((182, 160, 198, 222), fill=(0, 0, 0, 255))
test.save(out_dir / "app-test.ico", sizes=ICO_SIZES)
print("wrote", out_dir)
