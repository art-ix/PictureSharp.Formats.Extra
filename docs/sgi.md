# SGI

Silicon Graphics image (also known as RGB or IRIS), used by older 3D and VFX tools.

| | |
| --- | --- |
| Extensions | `.sgi` `.rgb` `.rgba` `.bw` |
| MIME type | `image/sgi` |
| Module | `SgiFormat` (namespace `PictureSharp.Formats.Sgi`) |
| Read | 1–4 channels, 8 and 16 bit, verbatim and RLE |
| Write | grey, RGB, RGBA (8 bit); verbatim or RLE |

## Examples

```csharp
using PictureSharp.Formats.Sgi;

using var image = Image.Load("render.rgb");
image.SaveAsSgi("render.sgi");                                            // RLE
image.SaveAsSgi("render-raw.sgi", new SgiEncoderOptions { Rle = false });
```

## Encoder options

| `SgiEncoderOptions` | Default | Meaning |
| --- | --- | --- |
| `Rle` | `true` | Run-length encoding of each row |

The encoder writes RGBA when some pixel is transparent, greyscale when all pixels are grey, otherwise RGB.

## Details

- 16-bit samples become round(value / 257). Two channels are grey and alpha.
- Colour-map files (dithered, screen, colour map) throw `NotSupportedException`.
- Pixels match Pillow 10.2.0. SGI stores no ICC, EXIF or XMP.
