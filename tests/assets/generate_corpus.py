#!/usr/bin/env python3
"""Generates the deterministic test corpus in tests/assets/corpus.

Sources are synthetic. Variants come from Pillow 10.2.0 and ImageMagick `convert`; small writers below produce the
layouts no tool writes (QOI, unusual PNM maxvals and comments, 15/16-bit and right-to-left TGA, RLE across rows).
The QOI writer is a line-by-line port of qoi.h qoi_encode, so its files also check the C# encoder byte for byte.

Run with:  python3 -I tests/assets/generate_corpus.py
Then:      python3 -I tests/assets/make_reference.py
"""
import os
import shutil
import struct
import subprocess

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(ROOT, "corpus")
SRC = os.path.join(CORPUS, "src")


def run(*args):
    subprocess.run(args, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def path(*parts):
    p = os.path.join(CORPUS, *parts)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    return p


# --------------------------------------------------------------------------- sources

def make_photo(w, h, seed):
    """Smooth gradients with noise: exercises QOI diff/luma chunks and short TGA runs."""
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w]
    img = np.zeros((h, w, 3))
    img[..., 0] = 128 + 100 * np.sin(xx / 7.0 + seed)
    img[..., 1] = 128 + 100 * np.cos(yy / 5.0)
    img[..., 2] = (xx * 255 // max(1, w - 1) + yy * 3) % 256
    img += rng.normal(0, 6, img.shape)
    return np.clip(np.rint(img), 0, 255).astype(np.uint8)


def make_flat(w, h):
    """Flat blocks and long runs: exercises QOI runs longer than 62 and TGA run packets up to 128."""
    img = np.zeros((h, w, 3), np.uint8)
    img[:, : w // 3] = (200, 30, 30)
    img[:, w // 3: 2 * w // 3] = (30, 200, 30)
    img[:, 2 * w // 3:] = (30, 30, 200)
    img[h // 2:, :] = 255 - img[h // 2:, :]
    return img


def with_alpha(rgb, seed):
    h, w, _ = rgb.shape
    yy, xx = np.mgrid[0:h, 0:w]
    a = ((xx * 7 + yy * 13 + seed) % 256).astype(np.uint8)
    a[: h // 4, :] = 255
    a[h // 4: h // 3, :] = 0
    return np.dstack([rgb, a])


def sources():
    photo = make_photo(37, 23, 1)
    big = make_photo(160, 120, 2)
    flat = make_flat(150, 40)
    rgba = with_alpha(photo, 3)
    gray = np.asarray(Image.fromarray(photo).convert("L"))
    graya = np.dstack([gray, rgba[..., 3]])
    rng = np.random.default_rng(7)
    indexed = rng.integers(0, 16, (23, 37), dtype=np.uint8)
    return dict(photo=photo, big=big, flat=flat, rgba=rgba, gray=gray, graya=graya, indexed=indexed)


def palette_image(indexed):
    im = Image.fromarray(indexed, "P")
    colors = [(i * 16, 255 - i * 13, (i * 71) % 256) for i in range(16)]
    im.putpalette([c for rgb in colors for c in rgb])
    return im, colors


# --------------------------------------------------------------------------- QOI

def qoi_encode(rgba, channels, colorspace=0):
    """port: qoi.h qoi_encode"""
    h, w, _ = rgba.shape
    out = bytearray(b"qoif" + struct.pack(">IIBB", w, h, channels, colorspace))
    index = [(0, 0, 0, 0)] * 64
    prev = (0, 0, 0, 255)
    run = 0
    pixels = [tuple(int(v) for v in p) for p in rgba.reshape(-1, 4)]
    if channels == 3:
        pixels = [(r, g, b, 255) for r, g, b, _ in pixels]
    last = len(pixels) - 1
    for i, px in enumerate(pixels):
        if px == prev:
            run += 1
            if run == 62 or i == last:
                out.append(0xC0 | (run - 1))
                run = 0
            continue
        if run > 0:
            out.append(0xC0 | (run - 1))
            run = 0
        r, g, b, a = px
        pos = (r * 3 + g * 5 + b * 7 + a * 11) % 64
        if index[pos] == px:
            out.append(pos)
        else:
            index[pos] = px
            if a == prev[3]:
                def sc(v):
                    v &= 0xFF
                    return v - 256 if v > 127 else v
                vr, vg, vb = sc(r - prev[0]), sc(g - prev[1]), sc(b - prev[2])
                vg_r, vg_b = vr - vg, vb - vg
                if -3 < vr < 2 and -3 < vg < 2 and -3 < vb < 2:
                    out.append(0x40 | (vr + 2) << 4 | (vg + 2) << 2 | (vb + 2))
                elif -9 < vg_r < 8 and -33 < vg < 32 and -9 < vg_b < 8:
                    out += bytes([0x80 | (vg + 32), (vg_r + 8) << 4 | (vg_b + 8)])
                else:
                    out += bytes([0xFE, r, g, b])
            else:
                out += bytes([0xFF, r, g, b, a])
        prev = px
    out += b"\0" * 7 + b"\1"
    return bytes(out)


def rgba_of(arr):
    if arr.ndim == 2:
        arr = np.dstack([arr, arr, arr])
    if arr.shape[2] == 3:
        arr = np.dstack([arr, np.full(arr.shape[:2], 255, np.uint8)])
    return arr


def qoi(s):
    write = lambda name, data: open(path("qoi", name), "wb").write(data)
    write("photo-rgb.qoi", qoi_encode(rgba_of(s["photo"]), 3))
    write("photo-rgba.qoi", qoi_encode(s["rgba"], 4))
    write("big-rgb.qoi", qoi_encode(rgba_of(s["big"]), 3))
    write("flat-runs.qoi", qoi_encode(rgba_of(s["flat"]), 3))
    write("gray-linear.qoi", qoi_encode(rgba_of(s["gray"]), 3, 1))
    write("one-pixel.qoi", qoi_encode(np.array([[[10, 20, 30, 40]]], np.uint8), 4))


# --------------------------------------------------------------------------- PNM

def pnm_custom(name, magic, w, h, maxval, samples, plain, comment=False):
    """Writes P2/P3/P5/P6 with any maxval; samples is a flat list of ints."""
    head = f"P{magic}\n" + ("# made by generate_corpus.py\n" if comment else "") + f"{w} {h}\n{maxval}\n"
    if plain:
        lines, line = [], []
        for v in samples:
            line.append(str(v))
            if len(line) == 12:
                lines.append(" ".join(line))
                line = []
        if line:
            lines.append(" ".join(line))
        body = ("\n".join(lines) + "\n").encode()
    elif maxval < 256:
        body = bytes(samples)
    else:
        body = b"".join(struct.pack(">H", v) for v in samples)
    open(path("pnm", name), "wb").write(head.encode() + body)


def pnm(s):
    photo, gray = Image.fromarray(s["photo"]), Image.fromarray(s["gray"])
    photo.save(path("pnm", "photo.ppm"))
    gray.save(path("pnm", "gray.pgm"))
    gray.convert("1", dither=Image.Dither.NONE).save(path("pnm", "bitmap.pbm"))
    Image.fromarray(s["gray"].astype(np.uint16) * 257).convert("I").save(path("pnm", "gray16.pgm"))

    src = path("src", "photo.png")
    photo.save(src)
    gray.save(path("src", "gray.png"))
    Image.fromarray(s["rgba"], "RGBA").save(path("src", "rgba.png"))
    Image.fromarray(s["graya"], "LA").save(path("src", "graya.png"))
    run("convert", src, "-compress", "none", path("pnm", "photo-plain.ppm"))
    run("convert", path("src", "gray.png"), "-compress", "none", path("pnm", "gray-plain.pgm"))
    run("convert", path("src", "gray.png"), "-threshold", "50%", "-compress", "none", path("pnm", "bitmap-plain.pbm"))
    run("convert", src, "-depth", "16", path("pnm", "photo16.ppm"))
    run("convert", src, path("pnm", "photo.pam"))
    run("convert", path("src", "gray.png"), path("pnm", "gray.pam"))
    run("convert", path("src", "rgba.png"), path("pnm", "rgba.pam"))
    run("convert", path("src", "graya.png"), path("pnm", "graya.pam"))

    h, w, _ = s["photo"].shape
    flat = [int(v) for v in s["photo"].reshape(-1)]
    pnm_custom("photo-maxval1023.ppm", 6, w, h, 1023, [v * 1023 // 255 for v in flat], False)
    pnm_custom("photo-maxval1023-plain.ppm", 3, w, h, 1023, [v * 1023 // 255 for v in flat], True, comment=True)
    pnm_custom("gray-maxval7-plain.pgm", 2, w, h, 7, [int(v) >> 5 for v in s["gray"].reshape(-1)], True, comment=True)
    pnm_custom("gray-maxval100.pgm", 5, w, h, 100, [int(v) * 100 // 255 for v in s["gray"].reshape(-1)], False, comment=True)


# --------------------------------------------------------------------------- TGA

def tga_custom(name, image_type, w, h, bpp, descriptor, body, cmap=b"", cm=(0, 0, 0), ident=b""):
    first, length, depth = cm
    header = struct.pack("<BBBHHBHHHHBB", len(ident), 1 if cmap else 0, image_type, first, length, depth, 0, 0, w, h, bpp, descriptor)
    open(path("tga", name), "wb").write(header + ident + cmap + body)


def rle_across_rows(pixels):
    """RLE packets over the whole raster, crossing row ends, as many writers do."""
    out = bytearray()
    i = 0
    while i < len(pixels):
        run = 1
        while i + run < len(pixels) and run < 128 and pixels[i + run] == pixels[i]:
            run += 1
        if run > 1:
            out.append(0x80 | (run - 1))
            out += pixels[i]
            i += run
        else:
            k = 1
            while i + k < len(pixels) and k < 128 and not (i + k + 1 < len(pixels) and pixels[i + k] == pixels[i + k + 1]):
                k += 1
            out.append(k - 1)
            for p in pixels[i:i + k]:
                out += p
            i += k
    return bytes(out)


def tga(s):
    photo, rgba = Image.fromarray(s["photo"]), Image.fromarray(s["rgba"], "RGBA")
    gray, graya = Image.fromarray(s["gray"]), Image.fromarray(s["graya"], "LA")
    pal, colors = palette_image(s["indexed"])
    for name, im in [("rgb", photo), ("rgba", rgba), ("gray", gray), ("graya", graya), ("palette", pal), ("flat", Image.fromarray(s["flat"]))]:
        im.save(path("tga", f"{name}.tga"))
        im.save(path("tga", f"{name}-rle.tga"), rle=True)
    rgba.save(path("tga", "rgba-topleft.tga"), orientation=1)
    rgba.save(path("tga", "rgba-rle-topleft.tga"), orientation=1, rle=True)

    run("convert", path("src", "photo.png"), "-compress", "RLE", path("tga", "im-rgb-rle.tga"))
    run("convert", path("src", "rgba.png"), "-compress", "none", path("tga", "im-rgba.tga"))
    run("convert", path("src", "photo.png"), "-colors", "64", "-type", "Palette", path("tga", "im-palette.tga"))

    # 15-bit colour (alpha bits 0, bit 15 set in some pixels to show it is ignored), right to left, top down.
    p = s["photo"]
    h, w, _ = p.shape
    words = [((r >> 3) << 10 | (g >> 3) << 5 | (b >> 3)) | ((x & 1) << 15) for y in range(h) for x in range(w) for r, g, b in [p[y, w - 1 - x]]]
    tga_custom("rgb15-righttoleft-top.tga", 2, w, h, 16, 0x30, b"".join(struct.pack("<H", v) for v in words))

    # 32-bit BGRA with an ID field, RLE packets crossing rows, bottom-up.
    a = s["rgba"]
    pixels = [bytes([a[y, x, 2], a[y, x, 1], a[y, x, 0], a[y, x, 3]]) for y in range(h - 1, -1, -1) for x in range(w)]
    tga_custom("rgba-rle-crossrows-id.tga", 10, w, h, 32, 8, rle_across_rows(pixels), ident=b"PictureSharp test")
    f = s["flat"]
    fh, fw, _ = f.shape
    pixels = [bytes([f[y, x, 2], f[y, x, 1], f[y, x, 0]]) for y in range(fh - 1, -1, -1) for x in range(fw)]
    tga_custom("flat-rle-crossrows.tga", 10, fw, fh, 24, 0, rle_across_rows(pixels))

    # Colour maps: 32-bit BGRA entries, and 16-bit entries starting at index 4.
    idx = s["indexed"]
    cmap32 = b"".join(bytes([c[2], c[1], c[0], (i * 17) % 256]) for i, c in enumerate(colors))
    tga_custom("palette32-alpha.tga", 1, w, h, 8, 0x28, idx.tobytes(), cmap32, (0, 16, 32))
    cmap16 = b"".join(struct.pack("<H", (c[0] >> 3) << 10 | (c[1] >> 3) << 5 | (c[2] >> 3)) for c in colors)
    tga_custom("palette16-first4.tga", 1, w, h, 8, 0x20, (idx + 4).astype(np.uint8).tobytes(), cmap16, (4, 16, 16))


def main():
    shutil.rmtree(CORPUS, ignore_errors=True)
    os.makedirs(SRC)
    s = sources()
    qoi(s)
    pnm(s)
    tga(s)
    shutil.rmtree(SRC)


if __name__ == "__main__":
    main()
