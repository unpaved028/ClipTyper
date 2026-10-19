"""Build ClipTyper's multi-size application icon.

16-48 px: simplified "draft B" (clipboard + typed line + keyboard), drawn per size so edges stay crisp.
64-256 px: the existing detailed artwork (256 px PNG frame of media/ClipTyper_256.ico).

Small frames are stored as 32-bit BMP (with AND mask), 64 px and up as PNG, which every
Windows version since Vista and the .NET Icon class read.

usage (from the repo root): python media/build_icon.py media media/ClipTyper.ico icon-preview.png
"""
import io
import os
import struct
import sys
from PIL import Image, ImageDraw

MEDIA, OUT_ICO, PREVIEW = sys.argv[1], sys.argv[2], sys.argv[3]

BOARD = (52, 110, 170, 255)
PAPER = (255, 255, 255, 255)
CLIP = (128, 136, 145, 255)
TEXT = (43, 110, 168, 255)
KEYS = (70, 78, 88, 255)
SS = 4


def snap(v, s):
    return round(v * s)


def rrect(d, box, r, fill, s):
    x0, y0, x1, y1 = (snap(c, s) * SS for c in box)
    d.rounded_rectangle([x0, y0, x1 - 1, y1 - 1], radius=max(0, round(r * s * SS)), fill=fill)


def small_icon(s):
    img = Image.new("RGBA", (s * SS, s * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rrect(d, (0.06, 0.12, 0.94, 1.00), 0.16, BOARD, s)      # clipboard
    rrect(d, (0.19, 0.25, 0.81, 0.88), 0.04, PAPER, s)      # paper
    rrect(d, (0.31, 0.03, 0.69, 0.25), 0.06, CLIP, s)       # clip
    rrect(d, (0.25, 0.34, 0.75, 0.40), 0, TEXT, s)          # typed line
    rrect(d, (0.25, 0.50, 0.75, 0.80), 0.04, KEYS, s)       # keyboard
    kw = max(1 / s, 0.07)
    cols = 3 if s < 24 else 4
    left, right = 0.30, 0.70 - kw          # keep every key inside the keyboard body (0.25-0.75)
    for y in (0.55, 0.67):
        for i in range(cols):
            x = left + i * ((right - left) / (cols - 1))
            rrect(d, (x, y, x + kw, y + kw), 0, PAPER, s)
    return img.resize((s, s), Image.LANCZOS)


def artwork_png_bytes():
    d = open(os.path.join(MEDIA, "ClipTyper_256.ico"), "rb").read()
    w, h, cc, _, planes, bpp, size, off = struct.unpack("<BBBBHHII", d[6:22])
    return d[off:off + size]


def bmp_frame(img):
    """32-bit BMP icon frame: BITMAPINFOHEADER, bottom-up BGRA, 1-bit AND mask."""
    w, h = img.size
    px = img.load()
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    xor = bytearray()
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b, a = px[x, y]
            xor += bytes((b, g, r, a))
    row = ((w + 31) // 32) * 4
    mask = bytearray()
    for y in range(h - 1, -1, -1):
        bits = bytearray(row)
        for x in range(w):
            if px[x, y][3] == 0:
                bits[x // 8] |= 0x80 >> (x % 8)
        mask += bits
    return header + bytes(xor) + bytes(mask)


def png_frame(img):
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


art256_bytes = artwork_png_bytes()
art256 = Image.open(io.BytesIO(art256_bytes)).convert("RGBA")

frames = []  # (size, bytes)
for s in (16, 20, 24, 32, 40, 48):
    frames.append((s, bmp_frame(small_icon(s))))
for s in (64, 128):
    frames.append((s, png_frame(art256.resize((s, s), Image.LANCZOS))))
frames.append((256, art256_bytes))  # original artwork, untouched

offset = 6 + 16 * len(frames)
entries, data = b"", b""
for s, blob in frames:
    entries += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(blob), offset + len(data))
    data += blob
with open(OUT_ICO, "wb") as f:
    f.write(struct.pack("<HHH", 0, 1, len(frames)) + entries + data)
print(f"{OUT_ICO}: {len(frames)} frames, {os.path.getsize(OUT_ICO)} bytes")

# Preview: every frame at actual size and 4x, on light and dark backgrounds
MAG = 4
cols = [16, 20, 24, 32, 40, 48, 64]
cw = 64 * MAG + 24
sheet = Image.new("RGB", (20 + cw * len(cols), 2 * (64 * MAG + 40) + 20), (255, 255, 255))
d = ImageDraw.Draw(sheet)
ico = Image.open(OUT_ICO)
for row, bg in enumerate([(243, 243, 243), (32, 32, 32)]):
    y = 20 + row * (64 * MAG + 40)
    for i, s in enumerate(cols):
        ico.size = (s, s)
        frame = ico.convert("RGBA") if hasattr(ico, "convert") else ico
        frame = Image.open(OUT_ICO)
        frame.size = (s, s)
        frame = frame.convert("RGBA")
        tile = Image.new("RGBA", (s, s), bg + (255,))
        tile.alpha_composite(frame)
        x = 10 + i * cw
        d.text((x, y - 16), f"{s}px", fill=(90, 90, 90))
        sheet.paste(tile.convert("RGB"), (x, y))
        sheet.paste(tile.resize((s * MAG, s * MAG), Image.NEAREST).convert("RGB"), (x + s + 8, y))
sheet.save(PREVIEW)
print("preview", PREVIEW)
