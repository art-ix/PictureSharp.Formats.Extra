#!/usr/bin/env python3
"""Records the expected RGBA of every corpus file in tests/assets/reference.json.

Pillow 10.2.0 decodes the files; files it cannot read (PAM, TGA with 32-bit colour maps) go through ImageMagick to an
8-bit PNG first. The "oracle" field records which path was used.
16-bit greyscale (Pillow mode I) is reduced as round(v / 65535 * 255), ties to even, the decoder's documented rule.

Run with:  python3 -I tests/assets/make_reference.py
"""
import hashlib
import json
import os
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


def rgba(file):
    try:
        im = Image.open(file)
        im.load()
    except (OSError, ValueError):
        return via_imagemagick(file)
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
