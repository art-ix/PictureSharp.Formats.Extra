#!/usr/bin/env python3
"""Records the expected RGBA of every corpus file in tests/assets/reference.json.

Pillow 10.2.0 decodes the files; files it cannot read (PAM, TGA with 32-bit colour maps) go through ImageMagick to an
8-bit PNG first. DDS files with a DX10 header for a format Pillow reads only from a DX9 header (BC1 sRGB, BC2, BC3,
16-bit and BGRA layouts) are decoded by Pillow after replacing the header with the equivalent DX9 one ("pillow-dx9").
BC6H files are decoded by bcdec (reference/bcdec/bcdec.h, not committed; see bc6h_reference.c) because Pillow 10.2.0
deviates from the BC6H spec ("bcdec"). The "oracle" field records which path was used.
16-bit greyscale (Pillow mode I) is reduced as round(v / 65535 * 255), ties to even, the decoder's documented rule.

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


def rgba(file):
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


if __name__ == "__main__":
    main()
