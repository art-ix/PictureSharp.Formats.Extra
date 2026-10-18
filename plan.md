# PictureSharp.Formats.Extra: plan

A plugin with extra formats for PictureSharp: QOI, PNM and TGA (read and write), DDS, PSD/PSB, SGI, Sun Raster and
XBM/XPM. It has its own repository and SemVer, modelled on `PictureSharp.Heif`, and depends only on the public
`PictureSharp` package (minimum 0.7.6), with no copies of core code.

TIFF moved to the separate `PictureSharp.Tiff` package (repository `../PictureSharp.Tiff`, user decision of
2026-10-17): it is a larger format for a different audience, with its own release cadence.

User decision (2026-10-17): plugins may use `System.IO.Compression` for decompression (zlib-ng ships with the runtime on
every platform). The core will drop this rule in the future.

## Versions

| Version | Scope | Status |
| --- | --- | --- |
| 0.1.0 | Repository skeleton, QOI, PNM, TGA (read and write) | Done (b891c11); released as part of v0.3.0 |
| 0.2.0 | DDS: DX9/DX10, uncompressed, BC1–BC5, BC7, BC6H; BGRA8/BGRX8 writing | Done (268087c); released as part of v0.3.0 |
| 0.3.0 | PSD/PSB (merged image, read only) | Done (f62011b); first GitHub release v0.3.0, not yet on NuGet |
| 0.4.0 | SGI (read and write), Sun Raster (read), XBM (read and write), XPM (read) | Done (525fb10); released as part of v0.5.0 |
| 0.5.0 | VTF (read), ICNS (read) | Done (ee4c86a); GitHub release v0.5.0, not yet on NuGet |
| 0.6.0 | Decoder options (`DdsDecoderOptions`, `VtfDecoderOptions`) through `DecoderOptions.FormatOptions`; PictureSharp 0.7.6 | Done locally, not committed |

0.1.0 and 0.2.0 were never released on their own: the first release, v0.3.0, contains all three versions. 0.4.0 was
released together with 0.5.0 as v0.5.0.

User decision (2026-10-17): after DDS come PSD/PSB, SGI, Sun Raster and XBM/XPM. Out of scope: CUR (belongs in the
core next to ICO), HDR formats (Radiance, OpenEXR — waiting for the HDR SPI), AVIF/JPEG XL/JPEG 2000 (separate
packages like `PictureSharp.Heif`), BLP, FLI/FLC, DCX. KTX/KTX2 was dropped by user decision (2026-10-17).

User decision (2026-10-17): 0.5.0 adds VTF and ICNS. PCX is built into the core (0.4.7). Doom/Quake graphics (WAD lumps
without a signature, with an external palette) would need a separate WAD package, not decoders in this plugin.

## Release

- GitHub Releases are published from the `vX.Y.Z` tag matching `VERSION`. The release workflow runs CI, packs the
  plugin and attaches the `.nupkg` to the release.
- Publishing to nuget.org is off until the user decides otherwise: the push step runs only when the repository
  variable `PUBLISH_NUGET` is `true` (Trusted Publishing, `nuget` environment, secret `NUGET_USER`).

## 0.1.0 — QOI, PNM, TGA

Done:
- `ExtraFormats.AddExtraFormats()` and `CreateModules()`; `QoiFormat`, `PnmFormat`, `TgaFormat` in
  `PictureSharp.Formats.<Format>`, encoder options, `SaveAsXxx`/`ToXxx`/`ImageFormat.Xxx` shortcuts.
- QOI: port of `qoi.h` (`qoi_decode`, `qoi_encode`); linear colour space ↔ `ImageMetadata.Gamma = 1`; a 3-channel
  file is always opaque.
- PNM: P1–P6 (plain and binary), PAM P7 (BLACKANDWHITE, GRAYSCALE, RGB and the `_ALPHA` variants), maxval 1–65535,
  scaled as Pillow does (`round(v / maxval * 255)` in double, ties to even); PBM/PGM/PPM/PAM writing, `PnmKind.Auto`.
- TGA: types 1/2/3 and RLE (packets may cross rows), 8/15/16/24/32 bpp, 15/16/24/32-bit colour maps, orientation,
  TGA 2.0 extension area (no alpha, premultiplied alpha); 8-bit grey/24/32 writing with RLE within a row.
- Tests (279): a 44-file corpus = `reference.json` (Pillow 10.2.0, ImageMagick for PAM and 32-bit colour maps),
  byte-exact QOI encoder against the `qoi_encode` port, round trips, edge cases, fuzzing (`Slow`), `MaxPixels`,
  cancellation, architecture, API snapshot, Oracle: encoder output read by Pillow 10.2.0 (10 cases).

Remaining for the DoD:
- A `qoi.h` oracle (the original `qoiconv`) instead of the Python port — needs approval to download `qoi.h` into
  `reference/`.
- Consumer smoke test with trimming and Native AOT (model: the core's `verify-package-consumer.sh`).

## 0.2.0 — DDS

- DX9 and DX10 headers; only mip level 0 of the first face/layer (choosing one: `DdsDecoderOptions`, 0.6.0).
- BC1–BC5, BC7, BC6H block decoders (→ 8 bit, clamped to [0, 1]); scalar, SIMD only with an equality test.
- DoD: pixels = Pillow (BC1–BC5, BC7) and a reference decoder (bcdec) for BC6H; BGRA8 writing round trip; BC6H/BC7
  samples from `texconv`/`nvcompress`, or an external corpus with the user's approval.

Done (2026-10-17):
- `DdsFormat`, `DdsEncoderOptions` (`DdsPixelFormat` Auto/Bgra32/Bgrx32), `SaveAsDds`/`ToDds`/`ImageFormat.Dds`
  shortcuts; block decoders in `Common/BlockCompression/`.
- DX9: RGB/luminance/alpha masks of 8–32 bits (scaled as Pillow does, `int(v / max * 255)`), L8, LA8, P8 palette,
  FourCC DXT1–DXT5 (DXT2/DXT4 unpremultiplied), ATI1/ATI2, BC4U/BC4S/BC5U/BC5S. DX10: BC1–BC7, BC6H UF16/SF16,
  RGBA8/BGRA8/BGRX8 (UNORM, SRGB, TYPELESS), B5G6R5, B5G5R5A1, B4G4R4A4, R10G10B10A2, R8, R8G8, A8; DX10 alpha mode
  (premultiplied, opaque). Float, YUV, ASTC, ETC → `NotSupportedException`.
- Behaviour as Pillow 10.2.0: BC1 with 565 bit replication and truncating division, BC4/BC5 SNORM shifted by +128,
  blue 128 in BC5 SNORM, the reserved BC7 mode → opaque black. BC6H follows the spec (like bcdec): Pillow interpolates
  without `+32` and does not sign-extend after the transform in the delta modes (checked by emulation: reproducing
  both deviations gives Pillow's result in all 14 modes).
- A 41-file DDS corpus: Pillow, ImageMagick (DXT1/3/5 with mipmaps), random blocks with an even spread of BC6H/BC7
  modes (129×127, partial blocks), a cube map, a DX10 array, mask layouts; references: Pillow, "pillow-dx9" (the DX10
  header replaced by the equivalent DX9 one for formats Pillow does not read from DX10) and bcdec for BC6H
  (`tests/assets/bc6h_reference.c`, header in `reference/bcdec/`, downloaded with the user's approval).
- Tests: 513/513 (including Slow and Oracle; Oracle of DDS BGRA/BGRX writing read by Pillow).

Remaining for the DoD: BC6H/BC7 samples from real encoders (`texconv`/`nvcompress`, not available on this machine; the
corpus covers every mode with random blocks), trimming/AOT smoke test together with 0.1.0.

## 0.3.0 — PSD/PSB

- Only the merged image (Image Data section); layers, masks and effects are skipped (their sections are read as
  lengths). Raw and PackBits compression; ZIP (used only in layers) is not needed. PSB: 64-bit section lengths and
  dimensions up to 300,000.
- Modes: Bitmap, Grayscale, Indexed, RGB, CMYK (naive conversion as in Pillow, no ICC), Duotone as grey; 8 and 16 bit
  (16 → 8 with the documented rounding), 32-bit → `NotSupportedException`. Lab and Multichannel →
  `NotSupportedException`.
- Metadata: ICC (resource 1039), EXIF (1058), XMP (1060) into `ImageMetadata`; resolution (1005).
- DoD: pixels = Pillow 10.2.0 (8 bit) and ImageMagick (PSB, 16 bit); samples from GIMP/ImageMagick/Krita, an external
  corpus only with the user's approval.

Done (2026-10-17):
- `PsdFormat` (read only, `.psd`/`.psb`), `ImageFormat.Psd`; no encoder options or save shortcuts.
- Modes Bitmap (1 bit, 1 = black as in the spec and ImageMagick; Pillow inverts it), Grayscale, Duotone (as grey),
  Indexed (768-byte planar palette), RGB, CMYK (Pillow's `cmyk2rgb` formula after the 8-bit reduction); 8 and 16 bit
  (`round(v / 257)`); raw and PackBits (2-byte row lengths in PSD, 4-byte in PSB). Lab, Multichannel, 32 bit, ZIP →
  `NotSupportedException`.
- Alpha (user decision: "as Photoshop"): the first extra channel when the layer count is negative (also in the
  Lr16/Lr32/Layr blocks) or the file has no layers; Indexed and Bitmap are always opaque. RGB colours are unblended
  from white exactly as ImageMagick Q16 does (double, the same operation order, then 16 → 8 bit); greyscale and CMYK
  are not unblended, as in ImageMagick.
- Metadata: ICC 1039, EXIF 1058, XMP 1060 (with `ReserveMetadata`, skipped with `IgnoreMetadata`), resolution 1005.
- A 30-file corpus (ImageMagick plus an own writer: no layers, a positive layer count with a selection channel, two
  extra channels, 16-bit raw/PackBits, raw PSB, duotone, resources with a name and an odd length). References: Pillow
  when it agrees with ImageMagick, otherwise ImageMagick's 16-bit output rounded in Python (ImageMagick's `-depth 8`
  truncates); files without layers through a copy with layer count −1 ("imagemagick-layers").
- Tests: 684/684 (including Slow).

Remaining for the DoD: samples from GIMP/Krita/Photoshop (no such tools on this machine; an external corpus only with
approval), including a check of greyscale/CMYK unblending on Photoshop files.

## 0.4.0 — SGI, Sun Raster, XBM, XPM

- SGI (`.sgi`, `.rgb`, `.rgba`, `.bw`): verbatim and RLE, 1–4 channels, 8 and 16 bit; 8-bit writing with and without
  RLE (as Pillow).
- Sun Raster (`.ras`): 1/8/24/32 bpp, standard/RLE/RGB types, colour map; read only.
- XBM: X10 and X11 reading (`#define`, `static char`), hotspot into metadata; X11 writing.
- XPM: XPM3, `c` keys (with `m`/`g` as a fallback), `#RGB`/`#RRGGBB`/`#RRRRGGGGBBBB` colours, `None`, X11 colour names
  (built-in table, no system files); up to 4 characters per pixel; read only.
- DoD: pixels = Pillow 10.2.0 (SGI, Sun, XBM, XPM), ImageMagick for variants Pillow does not read (24-bit Sun RLE, XPM
  with names); an oracle for SGI and XBM writing.

Done (2026-10-17):
- `SgiFormat` (`SgiEncoderOptions.Rle`, default true), `SunRasterFormat` (read only), `XbmFormat` (`XbmEncoderOptions`
  `Name` and `Hotspot`), `XpmFormat` (read only), with `ImageFormat.Sgi`/`SunRaster`/`Xbm`/`Xpm` and save shortcuts for
  SGI and XBM. `Common/CSourceReader` reads the C text of XBM and XPM.
- SGI: dimensions 1–3, 1–4 channels (2 = grey + alpha, as GIMP; Pillow rejects it and ImageMagick drops the alpha),
  8/16 bit (`round(v / 257)`; Pillow truncates), verbatim and RLE (offset table, shared rows); colour-map types →
  `NotSupportedException`. Writing: grey, RGB or RGBA by content, 8 bit, RLE with runs of three or more.
- Sun Raster: depths 1/4/8 (map or grey; 1 = black without a map, index with a map), 24/32 (first byte padding), types
  0/1/3/4/5 and 2 (RLE over the padded stream, runs across rows); raw maps and experimental type →
  `NotSupportedException`. Pillow leaves the padding out of RLE and takes the last byte of 32-bit pixels as padding;
  ImageMagick swaps the colours of byte-encoded 24-bit files and inverts 1-bit indices, so references use Pillow on a
  file expanded to type 1, or ImageMagick with `-alpha off`.
- XBM: X10 (`short`) and X11, any `#define` name, comments, decimal and upper-case hex values; 1 = black (ImageMagick;
  Pillow inverts). Writing X11 with 12 values per line, threshold luma < 128 and alpha ≥ 128, optional hotspot. The
  hotspot cannot be read into `ImageMetadata` (no field for it); see the requests to the core.
- XPM: XPM3 strings with C comments, 1–4 characters per pixel, visuals c > g > g4 > m, `#` colours with 1–4 digits per
  channel (rounded), the 658 X11 names of X.Org's `rgb.txt` (`Xpm/X11Colors.cs`, generated by
  `tests/assets/make_x11_colors.py`), `None`; HSV, unknown names and more than four characters → `NotSupportedException`.
- A 53-file corpus (Pillow, ImageMagick, own writers); disputed variants (SGI grey + alpha, Sun 1-bit with a map, XPM
  with four characters, XBM comments) are covered by unit tests. Oracle: SGI writing (4 cases) and XBM writing (Pillow,
  inverted).
- Tests: 1002/1002 (including Slow and Oracle).

Remaining for the DoD: a check of the rgb.txt notice text against the
upstream `COPYING` (written from the standard X11 licence).

Every version adds its `PictureSharp.Formats.<Format>` namespace to `ArchitectureTests` and updates the API snapshot,
the README and `ExtraFormats.AddExtraFormats`.

## 0.5.0 — VTF, ICNS

- VTF (Valve Texture Format, `.vtf`), read only: signature `VTF\0`, versions 7.0–7.6 (7.3+ with the resource
  directory, 7.6 with Deflate-compressed mip levels from the `AXC` resource; other methods → `NotSupportedException`). Only the largest mip level of the first frame, face and slice; the low-resolution thumbnail is skipped.
  Image formats: RGBA8888, ABGR8888, ARGB8888, BGRA8888, BGRX8888, RGB888, BGR888 (and the bluescreen variants as
  opaque colours), RGB565, BGR565, BGRA4444, BGRA5551, BGRX5551, I8, IA88, A8, UV88, UVWQ8888, UVLX8888, RGBA16161616
  (→ 8 bit with the documented rounding), DXT1, DXT1_ONEBITALPHA, DXT3, DXT5, ATI1N, ATI2N and the Strata Source BC4, BC5, BC6H and BC7
  formats (the existing BC decoders from `Common/BlockCompression`). Float formats (RGBA16161616F, R32F, RGB323232F,
  RGBA32323232F) and P8 → `NotSupportedException`.
- ICNS (Apple Icon Image, `.icns`), read only: decodes the largest element. PNG elements (`ic07`–`ic14`, `icp4`–`icp6`,
  `ic04`/`ic05` when PNG) through the core PNG decoder via the public API; RGB elements with PackBits-like RLE
  (`is32`, `il32`, `ih32`, `it32` with its 4-byte prefix) combined with the 8-bit masks (`s8mk`, `l8mk`, `h8mk`,
  `t8mk`); ARGB `ic04`/`ic05`; 1-bit `ICN#`/`ics#` with their masks as a fallback. JPEG 2000 elements →
  `NotSupportedException` (a separate package); the 4- and 8-bit legacy icons (`icl4`, `icl8`, ...) only if a fixed Mac
  palette is added. ICC, EXIF and XMP of an embedded PNG are kept.
- DoD: ICNS pixels = Pillow 10.2.0 (it reads PNG and RLE elements and writes PNG-based files); VTF has no oracle in
  Pillow or ImageMagick, so the corpus comes from an own writer whose blocks and raw layouts are checked against the
  same data decoded from DDS by Pillow; real VTF files (VTFEdit, Source SDK) only as an external corpus with the user's
  approval.

Done:
- VTF: `Vtf/` with the header for every version (pre-7.3 thumbnail skip, resource directory with inline resources,
  seventh sphere-map face in 7.1–7.4, volume slices, frames) and the 7.6 `AXC` resource (Deflate through `ZLibStream`).
  27-file corpus from the own writer in `generate_corpus.py`; references from `make_reference.py` (numpy for raw
  layouts, Pillow's DDS decoder for the DXT blocks). Packed 16-bit formats, bluescreen, 16-bit channels, 7.6 and the
  error paths are covered by unit tests.
- ICNS: `Icns/` ranks sizes as Pillow does; PNG through `ImageDecodeContext.DecodeEmbedded`, RLE and raw RGB with
  masks, `it32` prefix, ARGB, `ich#`/`ICN#`/`ics#` fallback, JPEG 2000 skipped. 5-file corpus checked against
  Pillow 10.2.0 (Pillow-written PNG icons, RGB elements of every size, a TOC, PNG over RGB).
- Tests: 1172/1172 (including Slow and Oracle); the corpus and `reference.json` regenerate byte for byte.

## 0.6.0 — decoder options

The core added per-plugin decoder options in 0.7.6 (`FormatDecoderOptions`, `DecoderOptions.FormatOptions`,
`GetFormatOptions<T>()`); the minimum PictureSharp version is now 0.7.6.

- `DdsDecoderOptions { MipLevel, Layer }`: the layer is the DX10 array element (six faces per cube element), the stored
  face of a DX9 cube map or the slice of the selected level of a volume texture. Arrays and cubes store each layer
  with its whole mip chain; volumes store all slices of a level before the next. The mip count follows DirectXTex
  (0 means 1, the flag is ignored), capped at the full chain. The palette of 8-bit paletted files precedes every
  surface.
- `VtfDecoderOptions { MipLevel, Frame, Face, Slice }`: the existing layout walk with the selected indices; 7.6 files
  inflate the selected mip/frame/face (slices are compressed together) up to the selected slice.
- `Identify` reports the selected size; out-of-range indices → `ImageFormatException`, negative values →
  `ArgumentOutOfRangeException`. Without a record the decoders behave exactly as in 0.5.0 (the default selection needs
  none of the new header fields).
- DoD: synthetic DDS (L8 chain, DX10 array, partial DX9 cube, DX10 cube array, volume, BC1 and paletted mips,
  truncation) and VTF (frames, faces with the sphere map, volume slices, 7.6 Deflate) tests; README section
  "Decoder options"; API snapshot. ICNS (choosing an icon size) and PSD layers stay out of scope.
- Tests: 1183/1183 (including Slow and Oracle).

## Requests to the core

- Per-plugin decoder options: done in PictureSharp 0.7.6, adopted in 0.6.0.
- A place for format-specific metadata such as the XBM hotspot (or cursor hotspots in general) in `ImageMetadata`.
