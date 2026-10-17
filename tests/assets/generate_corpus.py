#!/usr/bin/env python3
"""Generates the deterministic test corpus in tests/assets/corpus.

Sources are synthetic. Variants come from Pillow 10.2.0 and ImageMagick `convert`; small writers below produce the
layouts no tool writes (QOI, unusual PNM maxvals and comments, 15/16-bit and right-to-left TGA, RLE across rows, DDS
headers for every BC format, PSD layouts ImageMagick does not write, SGI grey/alpha and shared RLE rows, Sun
Raster byte encoding and 32-bit pixels, X10 bitmaps, XPM visuals, names and up to three characters per pixel). DDS block data is random with the BC6H/BC7 mode bits chosen evenly, so every mode,
partition and anchor is decoded; no encoder for BC4-BC7 is needed.
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


# --------------------------------------------------------------------------- DDS

DDPF_ALPHAPIXELS, DDPF_FOURCC, DDPF_PAL8, DDPF_RGB = 0x1, 0x4, 0x20, 0x40


def dds_custom(name, w, h, body, fourcc=None, dxgi=None, masks=None, bitcount=0, flags=0, mipmaps=0, caps2=0, dx10_extra=(3, 0, 1, 0)):
    if dxgi is not None:
        fourcc = b"DX10"
    if fourcc is not None:
        flags |= DDPF_FOURCC
    pf = struct.pack("<4I", 32, flags, struct.unpack("<I", fourcc)[0] if fourcc else 0, bitcount) + struct.pack("<4I", *(masks or (0, 0, 0, 0)))
    caps = 0x1000 | (0x400008 if mipmaps > 1 else 0) | (0x8 if caps2 else 0)
    header = b"DDS " + struct.pack("<7I", 124, 0x1007 | (0x20000 if mipmaps > 1 else 0), h, w, 0, 0, mipmaps) + b"\0" * 44 + pf
    header += struct.pack("<5I", caps, caps2, 0, 0, 0)
    if dxgi is not None:
        header += struct.pack("<5I", dxgi, *dx10_extra)
    open(path("dds", name), "wb").write(header + body)


def random_blocks(rng, count, size):
    return rng.integers(0, 256, (count, size), dtype=np.uint8)


def bc7_blocks(rng, count):
    blocks = random_blocks(rng, count, 16)
    for i in range(count):
        mode = i % 9                       # 8 = the reserved mode (first byte zero)
        if mode == 8:
            blocks[i, 0] = 0
        else:
            blocks[i, 0] = (blocks[i, 0] & ~((1 << (mode + 1)) - 1) | (1 << mode)) & 0xFF
    return blocks.tobytes()


BC6H_MODES = [0, 1, 2, 6, 10, 14, 18, 22, 26, 30, 3, 7, 11, 15, 19, 23, 27, 31]   # the last four are reserved


def bc6h_blocks(rng, count):
    blocks = random_blocks(rng, count, 16)
    for i in range(count):
        mode = BC6H_MODES[i % len(BC6H_MODES)]
        bits = 2 if mode < 2 else 5
        blocks[i, 0] = (blocks[i, 0] & ~((1 << bits) - 1) | mode) & 0xFF
    return blocks.tobytes()


def bc1_blocks(rng, count):
    """Random BC1 blocks, every second one with c0 <= c1 (three colours and transparency)."""
    blocks = random_blocks(rng, count, 8)
    for i in range(0, count, 2):
        c0, c1 = blocks[i, 0] | blocks[i, 1] << 8, blocks[i, 2] | blocks[i, 3] << 8
        if int(c0) > int(c1):
            blocks[i, [0, 1, 2, 3]] = blocks[i, [2, 3, 0, 1]]
    return blocks.tobytes()


def masked(pixels, masks, bitcount):
    """Packs RGBA pixels into words with the given masks (values scaled down by truncation)."""
    out = bytearray()
    for r, g, b, a in pixels.reshape(-1, 4):
        word = 0
        for value, mask in zip((r, g, b, a), masks):
            if mask:
                shift = (mask & -mask).bit_length() - 1
                word |= (int(value) * (mask >> shift) // 255) << shift
        out += word.to_bytes(bitcount // 8, "little")
    return bytes(out)


def dds(s):
    rng = np.random.default_rng(11)
    photo, rgba = Image.fromarray(s["photo"]), Image.fromarray(s["rgba"], "RGBA")
    photo.save(path("dds", "rgb.dds"))
    rgba.save(path("dds", "rgba.dds"))
    Image.fromarray(s["gray"]).save(path("dds", "gray.dds"))
    Image.fromarray(s["graya"], "LA").save(path("dds", "graya.dds"))

    # ImageMagick writes mipmaps by default: only level 0 is decoded.
    run("convert", path("src", "photo.png"), "-define", "dds:compression=dxt1", path("dds", "im-dxt1-mipmaps.dds"))
    run("convert", path("src", "rgba.png"), "-define", "dds:compression=dxt5", path("dds", "im-dxt5-mipmaps.dds"))
    run("convert", path("src", "rgba.png"), "-define", "dds:compression=dxt3", path("dds", "im-dxt3.dds"))
    run("convert", path("src", "rgba.png"), "-define", "dds:compression=none", "-define", "dds:mipmaps=0", path("dds", "im-rgba.dds"))

    w, h = 129, 127                        # 33 x 32 blocks, partial blocks on two edges
    n = ((w + 3) // 4) * ((h + 3) // 4)
    dds_custom("bc1-random.dds", w, h, bc1_blocks(rng, n), fourcc=b"DXT1")
    dds_custom("bc1-random-dx10.dds", w, h, bc1_blocks(rng, n), dxgi=71)
    dds_custom("bc1-random-srgb-dx10.dds", w, h, bc1_blocks(rng, n), dxgi=72)
    dds_custom("bc2-random.dds", w, h, random_blocks(rng, n, 16).tobytes(), fourcc=b"DXT3")
    dds_custom("bc2-random-dx10.dds", w, h, random_blocks(rng, n, 16).tobytes(), dxgi=74)
    dds_custom("bc3-random.dds", w, h, random_blocks(rng, n, 16).tobytes(), fourcc=b"DXT5")
    dds_custom("bc3-random-dx10.dds", w, h, random_blocks(rng, n, 16).tobytes(), dxgi=77)
    dds_custom("bc4-random-ati1.dds", w, h, random_blocks(rng, n, 8).tobytes(), fourcc=b"ATI1")
    dds_custom("bc4-random-dx10.dds", w, h, random_blocks(rng, n, 8).tobytes(), dxgi=80)
    dds_custom("bc5-random-ati2.dds", w, h, random_blocks(rng, n, 16).tobytes(), fourcc=b"ATI2")
    dds_custom("bc5-random-dx10.dds", w, h, random_blocks(rng, n, 16).tobytes(), dxgi=83)
    dds_custom("bc5s-random.dds", w, h, random_blocks(rng, n, 16).tobytes(), fourcc=b"BC5S")
    dds_custom("bc5s-random-dx10.dds", w, h, random_blocks(rng, n, 16).tobytes(), dxgi=84)
    dds_custom("bc6h-random.dds", w, h, bc6h_blocks(rng, n), dxgi=95)
    dds_custom("bc6h-signed-random.dds", w, h, bc6h_blocks(rng, n), dxgi=96)
    dds_custom("bc7-random.dds", w, h, bc7_blocks(rng, n), dxgi=98)
    dds_custom("bc7-random-srgb.dds", w, h, bc7_blocks(rng, n), dxgi=99)

    # A cube map with six DXT1 faces and two mip levels: the first face's level 0 is decoded.
    faces = b"".join(bc1_blocks(rng, 16) + bc1_blocks(rng, 4) for _ in range(6))
    dds_custom("cubemap-dxt1.dds", 16, 16, faces, fourcc=b"DXT1", mipmaps=2, caps2=0xFE00)
    # A texture array of three BC7 elements.
    dds_custom("array-bc7-dx10.dds", 8, 8, bc7_blocks(rng, 12), dxgi=98, dx10_extra=(3, 0, 3, 0))

    # Uncompressed layouts described by masks, DX9 and DX10.
    a = s["rgba"]
    ph, pw, _ = a.shape
    opaque = rgba_of(s["photo"])
    dds_custom("rgb565.dds", pw, ph, masked(opaque, (0xF800, 0x7E0, 0x1F, 0), 16), masks=(0xF800, 0x7E0, 0x1F, 0), bitcount=16, flags=DDPF_RGB)
    dds_custom("argb1555.dds", pw, ph, masked(a, (0x7C00, 0x3E0, 0x1F, 0x8000), 16), masks=(0x7C00, 0x3E0, 0x1F, 0x8000), bitcount=16, flags=DDPF_RGB | DDPF_ALPHAPIXELS)
    dds_custom("argb4444.dds", pw, ph, masked(a, (0xF00, 0xF0, 0xF, 0xF000), 16), masks=(0xF00, 0xF0, 0xF, 0xF000), bitcount=16, flags=DDPF_RGB | DDPF_ALPHAPIXELS)
    dds_custom("rgb332.dds", pw, ph, masked(opaque, (0xE0, 0x1C, 0x3, 0), 8), masks=(0xE0, 0x1C, 0x3, 0), bitcount=8, flags=DDPF_RGB)
    dds_custom("a2b10g10r10.dds", pw, ph, masked(a, (0x3FF, 0xFFC00, 0x3FF00000, 0xC0000000), 32), masks=(0x3FF, 0xFFC00, 0x3FF00000, 0xC0000000), bitcount=32, flags=DDPF_RGB | DDPF_ALPHAPIXELS)
    dds_custom("xrgb8888.dds", pw, ph, masked(opaque, (0xFF0000, 0xFF00, 0xFF, 0), 32), masks=(0xFF0000, 0xFF00, 0xFF, 0), bitcount=32, flags=DDPF_RGB)
    dds_custom("bgra8-dx10.dds", pw, ph, masked(a, (0xFF0000, 0xFF00, 0xFF, 0xFF000000), 32), dxgi=87)
    dds_custom("bgrx8-dx10.dds", pw, ph, masked(a, (0xFF0000, 0xFF00, 0xFF, 0xFF000000), 32), dxgi=88)
    dds_custom("rgba8-dx10.dds", pw, ph, masked(a, (0xFF, 0xFF00, 0xFF0000, 0xFF000000), 32), dxgi=28)
    dds_custom("b5g6r5-dx10.dds", pw, ph, masked(opaque, (0xF800, 0x7E0, 0x1F, 0), 16), dxgi=85)
    dds_custom("b5g5r5a1-dx10.dds", pw, ph, masked(a, (0x7C00, 0x3E0, 0x1F, 0x8000), 16), dxgi=86)
    dds_custom("b4g4r4a4-dx10.dds", pw, ph, masked(a, (0xF00, 0xF0, 0xF, 0xF000), 16), dxgi=115)
    dds_custom("r10g10b10a2-dx10.dds", pw, ph, masked(a, (0x3FF, 0xFFC00, 0x3FF00000, 0xC0000000), 32), dxgi=24)

    # An 8-bit palette with 256 RGBA entries.
    palette = rng.integers(0, 256, 1024, dtype=np.uint8).tobytes()
    dds_custom("palette8.dds", pw, ph, palette + rng.integers(0, 256, pw * ph, dtype=np.uint8).tobytes(), bitcount=8, flags=DDPF_PAL8)


# --------------------------------------------------------------------------- PSD

def packbits(row):
    """PackBits as Photoshop writes it: runs of 3+ equal bytes as repeats, the rest as literals of up to 128 bytes."""
    out, i, n = bytearray(), 0, len(row)
    while i < n:
        j = i
        while j + 1 < n and row[j + 1] == row[i] and j - i < 127:
            j += 1
        if j - i >= 2:
            out += bytes([257 - (j - i + 1), row[i]])
            i = j + 1
            continue
        j = i
        while j < n and j - i < 128 and not (j + 2 < n and row[j] == row[j + 1] == row[j + 2]):
            j += 1
        out += bytes([j - i - 1]) + bytes(row[i:j])
        i = j
    return bytes(out)


def psd_layer_info(count):
    """A layer info block with one empty layer (0x0 bounds, three empty channels) and the given signed count."""
    record = struct.pack(">4iH", 0, 0, 0, 0, 3) + b"".join(struct.pack(">hI", c, 2) for c in (0, 1, 2))
    record += b"8BIMnorm" + bytes([255, 0, 0, 0]) + struct.pack(">I", 12) + struct.pack(">II", 0, 0) + b"\0\0\0\0"
    body = struct.pack(">h", count) + record + b"\0\0" * 3
    if len(body) & 1:
        body += b"\0"
    return body


def psd_custom(name, mode, planes, depth=8, compression=1, version=1, layers=None, color_data=b"", resources=b""):
    """Writes a PSD (version 1) or PSB (version 2) whose merged image holds the given planes (uint8 or big-endian uint16)."""
    h, w = planes[0].shape
    wide = ">Q" if version == 2 else ">I"
    header = b"8BPS" + struct.pack(">H", version) + b"\0" * 6 + struct.pack(">HIIHH", len(planes), h, w, depth, mode)
    header += struct.pack(">I", len(color_data)) + color_data + struct.pack(">I", len(resources)) + resources
    if layers is None:
        header += struct.pack(wide, 0)
    else:
        info = psd_layer_info(layers)
        section = struct.pack(wide, len(info)) + info + struct.pack(">I", 0)
        header += struct.pack(wide, len(section)) + section
    rows = []
    for plane in planes:
        if depth == 1:
            rows += [np.packbits(r).tobytes() for r in plane]
        else:
            rows += [r.astype(">u2" if depth == 16 else np.uint8).tobytes() for r in plane]
    if compression == 0:
        body = struct.pack(">H", 0) + b"".join(rows)
    else:
        packed = [packbits(r) for r in rows]
        counts = b"".join(struct.pack(">I" if version == 2 else ">H", len(p)) for p in packed)
        body = struct.pack(">H", 1) + counts + b"".join(packed)
    open(path("psd", name), "wb").write(header + body)


def psd_resource(rid, data):
    return b"8BIM" + struct.pack(">H", rid) + b"\0\0" + struct.pack(">I", len(data)) + data + b"\0" * (len(data) & 1)


def psd(s):
    src = lambda name: path("src", name)
    Image.fromarray(s["big"]).save(src("big.png"))
    for name, args in [
        ("im-rgb.psd", [src("photo.png")]),
        ("im-rgb-raw.psd", [src("photo.png"), "-compress", "none"]),
        ("im-big-rgb.psd", [src("big.png")]),
        ("im-rgba.psd", [src("rgba.png")]),
        ("im-gray.psd", [src("gray.png")]),
        ("im-graya.psd", [src("graya.png")]),
        ("im-bitmap.psd", [src("gray.png"), "-monochrome", "-depth", "1"]),
        ("im-indexed.psd", [src("photo.png"), "-colors", "16"]),
        ("im-cmyk.psd", [src("photo.png"), "-colorspace", "CMYK"]),
        ("im-cmyka.psd", [src("rgba.png"), "-colorspace", "CMYK"]),
        ("im-rgb16.psd", [src("photo.png"), "-depth", "16"]),
        ("im-gray16.psd", [src("gray.png"), "-depth", "16"]),
        ("im-rgba.psb", [src("rgba.png")]),
        ("im-rgb16.psb", [src("photo.png"), "-depth", "16"]),
    ]:
        depth = [] if "-depth" in args else ["-depth", "8"]
        target = ("PSB:" if name.endswith(".psb") else "") + path("psd", name)
        run("convert", *args, *depth, target)

    rng = np.random.default_rng(13)
    photo, rgba, gray = s["photo"], s["rgba"], s["gray"]
    rgb_planes = [photo[..., c] for c in range(3)]
    rgba_planes = [rgba[..., c] for c in range(4)]
    wide16 = lambda p: p.astype(np.uint16) * 257 + rng.integers(0, 257, p.shape).astype(np.uint16) % 257
    # No layer section: the extra channel is the merged transparency.
    psd_custom("rgba-nolayers.psd", 3, rgba_planes)
    psd_custom("graya-nolayers-raw.psd", 1, [gray, rgba[..., 3]], compression=0)
    psd_custom("cmyka-nolayers.psd", 4, [255 - photo[..., 0], 255 - photo[..., 1], 255 - photo[..., 2], gray, rgba[..., 3]])
    # A positive layer count: the extra channel is a saved selection, the image stays opaque.
    psd_custom("rgb-selection.psd", 3, rgba_planes, layers=1)
    psd_custom("rgba-negative-count.psd", 3, rgba_planes, layers=-1)
    # Two extra channels: only the first is alpha.
    psd_custom("rgba-two-extra.psd", 3, rgba_planes + [gray])
    # 16-bit samples with low bits that test the rounding, raw and PackBits, PSD and PSB.
    psd_custom("rgb16-raw.psd", 3, [wide16(p) for p in rgb_planes], depth=16, compression=0)
    # ImageMagick unblends in 16 bits, the decoder after the reduction: multiples of 257 keep them equal.
    psd_custom("rgba16.psd", 3, [p.astype(np.uint16) * 257 for p in rgba_planes], depth=16)
    # ImageMagick converts CMYK in 16 bits, the decoder after the reduction: multiples of 257 keep them equal.
    psd_custom("cmyk16.psd", 4, [(255 - p).astype(np.uint16) * 257 for p in rgb_planes + [gray]], depth=16)
    psd_custom("gray16-raw.psb", 1, [wide16(gray)], depth=16, compression=0, version=2)
    psd_custom("rgba.psb", 3, rgba_planes, version=2, layers=-1)
    # Duotone: the colour data is opaque to readers; the channel is grey.
    psd_custom("duotone.psd", 8, [gray], color_data=bytes(rng.integers(0, 256, 1172, dtype=np.uint8)))
    # Indexed with a full planar palette and a 1-bit bitmap with an odd width, both raw.
    palette = bytes(rng.integers(0, 256, 768, dtype=np.uint8))
    psd_custom("indexed-raw.psd", 2, [rng.integers(0, 256, gray.shape, dtype=np.uint8)], color_data=palette, compression=0)
    psd_custom("bitmap-raw.psd", 0, [(gray < 128).astype(np.uint8)], depth=1, compression=0)
    # Resources that must be skipped, including an odd-sized one and a named one.
    res = psd_resource(1005, struct.pack(">IHHIHH", 300 << 16, 1, 1, 150 << 16, 1, 1)) + psd_resource(1028, b"\x1c\x02\x00\x00\x01")
    res += b"8BIM" + struct.pack(">H", 2000) + b"\x03abc" + struct.pack(">I", 3) + b"xyz\0"
    psd_custom("rgb-resources.psd", 3, rgb_planes, resources=res)
    psd_custom("one-pixel.psd", 3, [np.array([[v]], np.uint8) for v in (10, 20, 30)])


# --------------------------------------------------------------------------- SGI

def sgi_rle_row(samples, bpc):
    """Standard SGI RLE: literal runs (0x80 | n) and repeat runs of at least three, a zero count ends the row."""
    out, i, n = [], 0, len(samples)
    while i < n:
        run = 1
        while i + run < n and run < 127 and samples[i + run] == samples[i]:
            run += 1
        if run >= 3:
            out += [run, samples[i]]
            i += run
            continue
        j = i
        while j < n and j - i < 127 and not (j + 2 < n and samples[j] == samples[j + 1] == samples[j + 2]):
            j += 1
        out += [0x80 | (j - i)] + list(samples[i:j])
        i = j
    out.append(0)
    return b"".join(struct.pack(">H" if bpc == 2 else "B", int(v)) for v in out)


def sgi_custom(name, planes, bpc=1, rle=False, dimension=3, zsize=None, shared=False, reverse=False):
    """planes: list of 2-D arrays, top row first; written bottom up."""
    h, w = planes[0].shape
    head = struct.pack(">hBBHHHHii4s80si404s", 474, int(rle), bpc, dimension, w, h, zsize or len(planes), 0, (1 << (8 * bpc)) - 1, b"", b"test", 0, b"")
    rows = [plane[h - 1 - y] for plane in planes for y in range(h)]
    if not rle:
        dt = ">u2" if bpc == 2 else "u1"
        body = b"".join(r.astype(dt).tobytes() for r in rows)
    else:
        table = 512 + 8 * len(rows)
        encoded = [sgi_rle_row(r, bpc) for r in rows]
        # Optionally store the rows in reverse order, and share identical rows, as some writers do.
        order = list(reversed(range(len(rows)))) if reverse else list(range(len(rows)))
        offsets, lengths, data, seen = [0] * len(rows), [0] * len(rows), b"", {}
        for i in order:
            if shared and encoded[i] in seen:
                offsets[i] = seen[encoded[i]]
            else:
                offsets[i] = table + len(data)
                seen[encoded[i]] = offsets[i]
                data += encoded[i]
            lengths[i] = len(encoded[i])
        body = struct.pack(f">{len(rows)}I", *offsets) + struct.pack(f">{len(rows)}I", *lengths) + data
    open(path("sgi", name), "wb").write(head + body)


def sgi(s):
    src = lambda name: path("src", name)
    photo, rgba, gray, flat = s["photo"], s["rgba"], s["gray"], s["flat"]
    for name, im in [("rgb", Image.fromarray(photo)), ("rgba", Image.fromarray(rgba, "RGBA")), ("gray", Image.fromarray(gray))]:
        im.save(path("sgi", f"pil-{name}.sgi"))
    Image.fromarray(gray[:1]).save(path("sgi", "pil-row.bw"))
    Image.fromarray(photo).save(path("sgi", "pil-rgb16.rgb"), bpc=2)
    for name, args in [
        ("im-rgb-rle.sgi", [src("photo.png")]),
        ("im-rgba-rle.rgba", [src("rgba.png")]),
        ("im-gray-rle.bw", [src("gray.png")]),
        ("im-big-rgb-rle.sgi", [src("big.png")]),
        ("im-rgb-verbatim.sgi", [src("photo.png"), "-compress", "none"]),
        ("im-rgb16-rle.sgi", [src("photo.png"), "-depth", "16"]),
        ("im-rgba16.sgi", [src("rgba.png"), "-depth", "16", "-compress", "none"]),
    ]:
        depth = [] if "-depth" in args else ["-depth", "8"]
        run("convert", *args, *depth, "SGI:" + path("sgi", name))

    rng = np.random.default_rng(17)
    wide16 = lambda p: p.astype(np.uint16) * 257 + rng.integers(0, 257, p.shape).astype(np.uint16) % 257
    flat_planes = [flat[..., c] for c in range(3)]
    # Two channels (grey and alpha) are covered by SgiTests: Pillow rejects them and ImageMagick drops the alpha.
    sgi_custom("rgba16-rle.sgi", [wide16(rgba[..., c]) for c in range(4)], bpc=2, rle=True)
    sgi_custom("gray16-verbatim.sgi", [wide16(gray)], bpc=2, dimension=2)
    sgi_custom("flat-rle-shared-reversed.sgi", flat_planes, rle=True, shared=True, reverse=True)
    # Dimension 2 ignores zsize, dimension 1 also the height: only the first channel and row are read.
    sgi_custom("dim2-zsize3.sgi", [gray, gray, gray], dimension=2, zsize=3)
    sgi_custom("dim1-row.sgi", [gray[:1]], dimension=1, zsize=1)
    sgi_custom("one-pixel.sgi", [np.array([[v]], np.uint8) for v in (10, 20, 30)], rle=True)


# --------------------------------------------------------------------------- Sun Raster

def sun_rle(data):
    """Sun byte encoding over the whole stream (row padding included): runs may cross rows."""
    out, i, n = bytearray(), 0, len(data)
    while i < n:
        run = 1
        while i + run < n and run < 256 and data[i + run] == data[i]:
            run += 1
        if run >= 3 or data[i] == 0x80:
            if run >= 2:
                out += bytes([0x80, run - 1, data[i]])
            else:
                out += b"\x80\x00"
            i += run
        else:
            out.append(data[i])
            i += 1
    return bytes(out)


def sun_rows(rows, depth):
    """Pads every row (bytes) to 16 bits."""
    return b"".join(r + b"\0" * (len(r) % 2) for r in rows)


def sun_custom(name, w, h, depth, rows, ras_type=1, cmap=b"", map_type=None):
    data = sun_rows(rows, depth)
    if ras_type == 2:
        data = sun_rle(data)
    if map_type is None:
        map_type = 1 if cmap else 0
    head = struct.pack(">8I", 0x59A66A95, w, h, depth, len(data), ras_type, map_type, len(cmap))
    open(path("sun", name), "wb").write(head + cmap + data)


def pack_bits(values, depth):
    """Packs a row of small ints MSB first."""
    out, acc, bits = bytearray(), 0, 0
    for v in values:
        acc = (acc << depth) | int(v)
        bits += depth
        if bits == 8:
            out.append(acc)
            acc, bits = 0, 0
    if bits:
        out.append(acc << (8 - bits))
    return bytes(out)


def sun(s):
    src = lambda name: path("src", name)
    photo, rgba, gray, flat = s["photo"], s["rgba"], s["gray"], s["flat"]
    for name, args in [
        ("im-rgb.ras", [src("photo.png")]),
        ("im-palette.ras", [src("photo.png"), "-colors", "16"]),
        ("im-gray.ras", [src("gray.png")]),
        ("im-mono.ras", [src("gray.png"), "-monochrome"]),
        ("im-big-rgb.ras", [src("big.png")]),
        ("im-rgba.ras", [src("rgba.png")]),
    ]:
        run("convert", *args, "-depth", "8", "SUN:" + path("sun", name))

    h, w, _ = photo.shape
    fh, fw, _ = flat.shape
    bgr = lambda img, y: img[y, :, ::-1].tobytes()
    sun_custom("bgr-standard.ras", w, h, 24, [bgr(photo, y) for y in range(h)])
    sun_custom("bgr-old.ras", w, h, 24, [bgr(photo, y) for y in range(h)], ras_type=0)
    sun_custom("xbgr-standard.ras", w, h, 32, [np.dstack([rgba[..., 3], photo[..., ::-1]])[y].tobytes() for y in range(h)])
    sun_custom("xrgb-rgb.ras", w, h, 32, [np.dstack([np.zeros_like(gray), photo])[y].tobytes() for y in range(h)], ras_type=3)
    sun_custom("gray-nomap-rle.ras", w, h, 8, [gray[y].tobytes() for y in range(h)], ras_type=2)
    sun_custom("flat-bgr-rle.ras", fw, fh, 24, [bgr(flat, y) for y in range(fh)], ras_type=2)
    sun_custom("photo-bgr-rle.ras", w, h, 24, [bgr(photo, y) for y in range(h)], ras_type=2)
    idx = s["indexed"]
    _, colors = palette_image(idx)
    cmap = bytes(c[0] for c in colors) + bytes(c[1] for c in colors) + bytes(c[2] for c in colors)
    sun_custom("palette-rle.ras", w, h, 8, [idx[y].tobytes() for y in range(h)], ras_type=2, cmap=cmap)
    sun_custom("palette4.ras", w, h, 4, [pack_bits(idx[y], 4) for y in range(h)], cmap=cmap)
    sun_custom("gray4-nomap.ras", w, h, 4, [pack_bits(idx[y], 4) for y in range(h)])
    mono = (gray < 128).astype(np.uint8)
    sun_custom("mono-rle.ras", w, h, 1, [pack_bits(mono[y], 1) for y in range(h)], ras_type=2)
    # A 1-bit image with a colour map is covered by SunRasterTests: Pillow ignores the map and ImageMagick inverts the bits.
    # 0x80 bytes need the two-byte escape.
    esc = np.where(idx % 3 == 0, 0x80, gray).astype(np.uint8)
    sun_custom("gray-escape-rle.ras", w, h, 8, [esc[y].tobytes() for y in range(h)], ras_type=2)
    sun_custom("one-pixel.ras", 1, 1, 24, [bytes([30, 20, 10])])


# --------------------------------------------------------------------------- XBM

def xbm_custom(name, text):
    open(path("xbm", name), "w", newline="").write(text)


def xbm(s):
    src = lambda name: path("src", name)
    gray, big = s["gray"], np.asarray(Image.open(src("big.png")).convert("L"))
    mono = Image.fromarray(gray).convert("1", dither=Image.Dither.NONE)
    mono.save(path("xbm", "pil.xbm"))
    mono.save(path("xbm", "pil-hotspot.xbm"), hotspot=(3, 4))
    run("convert", src("gray.png"), "-monochrome", path("xbm", "im.xbm"))
    run("convert", src("big.png"), "-monochrome", path("xbm", "im-big.xbm"))

    bits = (gray < 128).astype(np.uint8)
    h, w = bits.shape

    def values(unit):
        out = []
        for y in range(h):
            for x0 in range(0, w, unit):
                v = 0
                for b in range(unit):
                    if x0 + b < w and bits[y, x0 + b]:
                        v |= 1 << b
                out.append(v)
        return out

    x10 = values(16)
    lines = ",\n".join(", ".join(f"0x{v:04x}" for v in x10[i:i + 8]) for i in range(0, len(x10), 8))
    xbm_custom("x10.xbm", f"#define x10_width {w}\n#define x10_height {h}\nstatic short x10_bits[] = {{\n{lines}}};\n")
    x11 = values(8)
    # Comments, upper-case hex and decimal values are covered by XbmTests: neither oracle reads them.
    crlf = ", ".join(f"0x{v:02X}" for v in x11)
    xbm_custom("hotspot-crlf.xbm",
               f"#define pic_width {w}\r\n#define pic_height {h}\r\n#define pic_x_hot 1\r\n#define pic_y_hot 2\r\n"
               f"static unsigned char pic_bits[] = {{ {crlf} }};")
    xbm_custom("one-pixel.xbm", "#define o_width 1\n#define o_height 1\nstatic char o_bits[] = { 0x01 };\n")


# --------------------------------------------------------------------------- XPM

def xpm_text(name, w, h, colors, rows, extra="", comment=True):
    lines = [f'"{w} {h} {len(colors)} {len(rows[0]) // w}{extra}"']
    lines += [f'"{key} {spec}"' for key, spec in colors]
    if comment:
        lines.insert(1, "/* colors */")
        lines.insert(len(colors) + 2, "/* pixels */")
    lines += [f'"{r}"' for r in rows]
    return f"/* XPM */\nstatic char *{name}[] = {{\n" + ",\n".join(lines) + "\n};\n"


def xpm(s):
    src = lambda name: path("src", name)
    for name, args in [
        ("im-rgb.xpm", [src("photo.png")]),
        ("im-rgba.xpm", [src("rgba.png")]),
        ("im-gray.xpm", [src("gray.png")]),
        ("im-palette.xpm", [src("photo.png"), "-colors", "16"]),
    ]:
        run("convert", *args, "XPM:" + path("xpm", name))

    idx = s["indexed"]
    h, w = idx.shape
    write = lambda name, text: open(path("xpm", name), "w").write(text)
    keys1 = ".#abcdefghijklmn"
    named = ["black", "white", "red", "light blue", "DarkOliveGreen", "gray50", "navajo white", "medium sea green",
             "None", "grey", "maroon", "purple", "ghost white", "LightGoldenrodYellow", "steel blue", "Gold"]
    write("names.xpm", xpm_text("names", w, h, [(k, f"c {n}") for k, n in zip(keys1, named)], ["".join(keys1[v] for v in r) for r in idx]))
    hexes = ["#000", "#fff", "#f80", "#123456", "#abcdef", "#00ff7f", "#123456789abc", "#fedcba987654", "#8000ffff0001",
             "#123abc789", "#fff000888", "#7f7f7f", "#0a0b0c", "#c0ffee", "#bad", "#000000000000"]
    keys2 = [f"{a}{b}" for a in "Ab" for b in "+-*/&@?!"]
    write("hex-cpp2.xpm", xpm_text("hex", w, h, [(k, f"c {v}") for k, v in zip(keys2, hexes)], ["".join(keys2[v] for v in r) for r in idx]))
    # Visual fallback: no c key (g, then g4, then m), a symbolic name first, and a c key after the others.
    visuals = ["s sym m black", "m white g4 #808080", "g #102030 m black", "s s1 c #ff0000", "m black c #00ff00 g white",
               "g gray20", "g4 #333", "c None", "m #00f", "c #ff00ff s magenta", "g white", "c yellow", "m gray", "c #010203",
               "c orange", "g black"]
    keys3 = [f"x{a}{b}" for a in "01" for b in "01234567"]
    write("visuals-cpp3.xpm", xpm_text("vis", w, h, [(k, v) for k, v in zip(keys3, visuals)], ["".join(keys3[v] for v in r) for r in idx],
                                     extra=" 2 3 XPMEXT"))
    # ImageMagick reads at most three characters per pixel; XpmTests cover four.
    keys4 = [f"{v:02d}" for v in range(16)]
    tail = '"XPMEXT ext1 data",\n"XPMENDEXT"\n};\n'
    text = xpm_text("ext", w, h, [(k, f"c #{v * 16:02x}{255 - v * 13:02x}{v * 71 % 256:02x}") for v, k in enumerate(keys4)],
                    ["".join(keys4[v] for v in r) for r in idx], comment=False)
    write("cpp2-ext.xpm", text.replace("\n};\n", ",\n" + tail))
    write("one-pixel.xpm", '/* XPM */\nstatic char *o[] = {"1 1 1 1", "a c #102030", "a"};\n')


def main():
    shutil.rmtree(CORPUS, ignore_errors=True)
    os.makedirs(SRC)
    s = sources()
    qoi(s)
    pnm(s)
    tga(s)
    dds(s)
    psd(s)
    sgi(s)
    sun(s)
    xbm(s)
    xpm(s)
    shutil.rmtree(SRC)


if __name__ == "__main__":
    main()
