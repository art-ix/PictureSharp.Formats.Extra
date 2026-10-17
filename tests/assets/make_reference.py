#!/usr/bin/env python3
"""Records the expected RGBA of every corpus file in tests/assets/reference.json.

Pillow 10.2.0 decodes the files; files it cannot read (PAM, TGA with 32-bit colour maps) go through ImageMagick to an
8-bit PNG first. DDS files with a DX10 header for a format Pillow reads only from a DX9 header (BC1 sRGB, BC2, BC3,
16-bit and BGRA layouts) are decoded by Pillow after replacing the header with the equivalent DX9 one ("pillow-dx9").
BC6H files are decoded by bcdec (reference/bcdec/bcdec.h, not committed; see bc6h_reference.c) because Pillow 10.2.0
deviates from the BC6H spec ("bcdec"). PSD files use Pillow when it agrees with ImageMagick and ImageMagick otherwise:
Pillow inverts 1-bit bitmaps, ignores alpha except in 4-channel RGB, does not unblend the merged image from white and
reads neither 16-bit nor PSB. ImageMagick takes alpha only from a negative layer count, so files without a layer
section are decoded after inserting one with count -1 ("imagemagick-layers"). The "oracle" field records the path.
16-bit greyscale (Pillow mode I) is reduced as round(v / 65535 * 255), ties to even, the decoder's documented rule.
SGI, Sun Raster, XBM and XPM files use Pillow when it agrees with ImageMagick's 16-bit output and ImageMagick otherwise
(Pillow truncates 16-bit SGI samples and misreads 32-bit Sun pixels and XPM comments before the pixels); the script
lists every such difference. Sun 32-bit files go through ImageMagick with -alpha off (the first byte is padding), and
byte-encoded Sun files are first expanded to type 1 ("-expanded"). Pillow's XBM result is inverted: it decodes set
bits as white.

Run with:  python3 -I tests/assets/make_reference.py
"""
import hashlib
import io
import json
import os
import struct
import subprocess
import tempfile

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(ROOT, "corpus")


def via_imagemagick(file):
    with tempfile.TemporaryDirectory() as tmp:
        png = os.path.join(tmp, "x.png")
        subprocess.run(["convert", file, "-depth", "8", "PNG32:" + png], check=True)
        with Image.open(png) as im:
            return im.size, np.asarray(im.convert("RGBA")).tobytes(), "imagemagick"


# DXGI format -> (pixel format flags, FourCC, bit count, masks) of the equivalent DX9 header.
DX9_EQUIVALENT = {
    72: (0x4, b"DXT1", 0, (0, 0, 0, 0)),
    74: (0x4, b"DXT3", 0, (0, 0, 0, 0)),
    77: (0x4, b"DXT5", 0, (0, 0, 0, 0)),
    87: (0x41, b"\0\0\0\0", 32, (0xFF0000, 0xFF00, 0xFF, 0xFF000000)),
    88: (0x40, b"\0\0\0\0", 32, (0xFF0000, 0xFF00, 0xFF, 0)),
    85: (0x40, b"\0\0\0\0", 16, (0xF800, 0x7E0, 0x1F, 0)),
    86: (0x41, b"\0\0\0\0", 16, (0x7C00, 0x3E0, 0x1F, 0x8000)),
    115: (0x41, b"\0\0\0\0", 16, (0xF00, 0xF0, 0xF, 0xF000)),
    24: (0x41, b"\0\0\0\0", 32, (0x3FF, 0xFFC00, 0x3FF00000, 0xC0000000)),
}


def via_dx9_header(file):
    data = open(file, "rb").read()
    if data[84:88] != b"DX10":
        return None
    dxgi = struct.unpack_from("<I", data, 128)[0]
    if dxgi not in DX9_EQUIVALENT:
        return None
    flags, fourcc, bits, masks = DX9_EQUIVALENT[dxgi]
    pf = struct.pack("<I", 32) + struct.pack("<I", flags) + fourcc + struct.pack("<5I", bits, *masks)
    with Image.open(io.BytesIO(data[:76] + pf + data[108:128] + data[148:])) as im:
        im.load()
        return im.size, np.asarray(im.convert("RGBA")).tobytes(), "pillow-dx9"


BCDEC_TOOL = None


def via_bcdec(file):
    global BCDEC_TOOL
    data = open(file, "rb").read(132)
    if data[84:88] != b"DX10" or struct.unpack_from("<I", data, 128)[0] not in (94, 95, 96):
        return None
    if BCDEC_TOOL is None:
        header = os.path.join(ROOT, "..", "..", "reference", "bcdec")
        if not os.path.exists(os.path.join(header, "bcdec.h")):
            raise SystemExit("BC6H references need reference/bcdec/bcdec.h (https://github.com/iOrange/bcdec)")
        BCDEC_TOOL = os.path.join(tempfile.mkdtemp(), "bc6h_reference")
        subprocess.run(["cc", "-O2", "-I", header, os.path.join(ROOT, "bc6h_reference.c"), "-o", BCDEC_TOOL, "-lm"], check=True)
    rgba_bytes = subprocess.run([BCDEC_TOOL, file], check=True, capture_output=True).stdout
    w, h = struct.unpack_from("<I", data, 16)[0], struct.unpack_from("<I", data, 12)[0]
    return (w, h), rgba_bytes, "bcdec"


PSD_BASE_CHANNELS = {0: 1, 1: 1, 2: 1, 3: 3, 4: 4, 8: 1}


def psd_with_negative_layer_count(data):
    """The file with an empty layer section replaced by one holding a single empty layer and the count -1."""
    version, channels, mode = struct.unpack_from(">H", data, 4)[0], struct.unpack_from(">H", data, 12)[0], struct.unpack_from(">H", data, 24)[0]
    if channels <= PSD_BASE_CHANNELS.get(mode, 99) or mode in (0, 2):
        return None
    pos = 26
    pos += 4 + struct.unpack_from(">I", data, pos)[0]
    pos += 4 + struct.unpack_from(">I", data, pos)[0]
    wide = ">Q" if version == 2 else ">I"
    if struct.unpack_from(wide, data, pos)[0] != 0:
        return None
    record = struct.pack(">4iH", 0, 0, 0, 0, 3) + b"".join(struct.pack(">hI", c, 2) for c in (0, 1, 2))
    record += b"8BIMnorm" + bytes([255, 0, 0, 0]) + struct.pack(">I", 12) + struct.pack(">II", 0, 0) + b"\0\0\0\0"
    info = struct.pack(">h", -1) + record + b"\0\0" * 3
    section = struct.pack(wide, len(info)) + info + struct.pack(">I", 0)
    return data[:pos] + struct.pack(wide, len(section)) + section + data[pos + struct.calcsize(wide):]


def via_imagemagick16(file, *args):
    """ImageMagick's 16-bit RGBA reduced as round(v / 257): its own -depth 8 output truncates."""
    with tempfile.TemporaryDirectory() as tmp:
        raw = os.path.join(tmp, "x.rgba")
        subprocess.run(["convert", file, *args, "-depth", "16", "RGBA:" + raw], check=True)
        size = subprocess.run(["identify", "-format", "%w %h", file], check=True, capture_output=True, text=True).stdout.split()
        v = np.fromfile(raw, "<u2").astype(np.int64)
        return (int(size[0]), int(size[1])), ((v + 128) // 257).astype(np.uint8).tobytes(), "imagemagick"


def psd(file):
    try:
        with Image.open(file) as im:
            im.load()
            pillow = im.size, np.asarray(im.convert("RGBA")).tobytes(), "pillow"
    except (OSError, ValueError, KeyError, SyntaxError):
        pillow = None
    changed = psd_with_negative_layer_count(open(file, "rb").read())
    if changed is not None:
        with tempfile.TemporaryDirectory() as tmp:
            copy = os.path.join(tmp, "x.psd")
            open(copy, "wb").write(changed)
            size, data, _ = via_imagemagick16(copy + "[0]")
            return size, data, "imagemagick-layers"
    magick = via_imagemagick16(file + "[0]")
    if pillow and pillow[:2] == magick[:2]:
        return pillow
    return magick


def pillow_rgba(file, invert=False):
    try:
        with Image.open(file) as im:
            im.load()
            data = np.asarray(im.convert("RGBA"))
    except (OSError, ValueError, KeyError, SyntaxError):
        return None
    if invert:
        data = data.copy()
        data[..., :3] = 255 - data[..., :3]
    return (data.shape[1], data.shape[0]), data.tobytes(), "pillow"


DIFFERENCES = []


def consensus(file, magick_args=(), invert=False):
    """Pillow when it reads the file and agrees with ImageMagick, otherwise ImageMagick (the difference is listed)."""
    pillow = pillow_rgba(file, invert)
    try:
        magick = via_imagemagick16(file, *magick_args)
    except subprocess.CalledProcessError:
        # ImageMagick rejects some valid variants (Sun type 0); Pillow alone decides them.
        assert pillow, file
        return pillow
    if pillow and pillow[:2] == magick[:2]:
        return pillow
    if pillow:
        DIFFERENCES.append(os.path.basename(file))
    return magick


def sun_without_rle(data):
    """A byte-encoded (type 2) Sun Raster file expanded to type 1. The stream covers the row padding, which Pillow
    leaves out, and ImageMagick swaps the colours of encoded 24-bit files; both read the expanded file correctly."""
    w, h, depth, _, ras_type, map_type, map_length = struct.unpack_from(">7I", data, 4)
    if ras_type != 2:
        return None
    size = ((w * depth + 15) // 16) * 2 * h
    src, out, i = data[32 + map_length:], bytearray(), 0
    while len(out) < size:
        if src[i] != 0x80:
            out.append(src[i])
            i += 1
        elif src[i + 1] == 0:
            out.append(0x80)
            i += 2
        else:
            out += bytes([src[i + 2]]) * (src[i + 1] + 1)
            i += 3
    return struct.pack(">8I", 0x59A66A95, w, h, depth, size, 1, map_type, map_length) + data[32:32 + map_length] + bytes(out[:size])


def sun(file):
    expanded = sun_without_rle(open(file, "rb").read())
    if expanded is None:
        # The first byte of a 32-bit pixel is padding; ImageMagick reads it as alpha.
        return consensus(file, ["-alpha", "off"])
    with tempfile.TemporaryDirectory() as tmp:
        copy = os.path.join(tmp, "x.ras")
        open(copy, "wb").write(expanded)
        size, data, oracle = consensus(copy, ["-alpha", "off"])
        return size, data, oracle + "-expanded"


def rgba(file):
    lower = file.lower()
    if lower.endswith((".psd", ".psb")):
        return psd(file)
    if lower.endswith((".sgi", ".rgb", ".rgba", ".bw")):
        return consensus(file)
    if lower.endswith(".ras"):
        return sun(file)
    if lower.endswith(".xbm"):
        # Pillow decodes set bits as white; they are the black foreground.
        return consensus(file, invert=True)
    if lower.endswith(".xpm"):
        return consensus(file)
    special = via_bcdec(file)
    if special:
        return special
    try:
        im = Image.open(file)
        im.load()
    except (OSError, ValueError, NotImplementedError):
        return via_dx9_header(file) or via_imagemagick(file)
    with im:
        if im.mode in ("I", "I;16", "I;16B"):
            v = np.asarray(im, dtype=np.float64)
            g = np.array([round(x / 65535 * 255) for x in v.reshape(-1)], np.uint8).reshape(v.shape)
            return im.size, np.dstack([g, g, g, np.full(g.shape, 255, np.uint8)]).tobytes(), "pillow"
        return im.size, np.asarray(im.convert("RGBA")).tobytes(), "pillow"


def main():
    reference = {}
    for folder, _, files in sorted(os.walk(CORPUS)):
        for name in sorted(files):
            file = os.path.join(folder, name)
            (w, h), data, oracle = rgba(file)
            assert len(data) == w * h * 4, file
            reference[os.path.relpath(file, CORPUS).replace(os.sep, "/")] = {
                "width": w, "height": h, "oracle": oracle, "sha256": hashlib.sha256(data).hexdigest()}
    with open(os.path.join(ROOT, "reference.json"), "w") as f:
        json.dump(reference, f, indent=1, sort_keys=True)
        f.write("\n")
    print(len(reference), "files")
    for name in DIFFERENCES:
        print("Pillow differs from ImageMagick:", name)


if __name__ == "__main__":
    main()
