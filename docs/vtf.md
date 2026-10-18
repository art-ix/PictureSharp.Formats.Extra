# VTF

Valve Texture Format, the texture format of the Source engine (Half-Life 2, Portal, Counter-Strike, Garry's Mod).

| | |
| --- | --- |
| Extensions | `.vtf` |
| MIME type | `image/x-vtf` |
| Module | `VtfFormat` (namespace `PictureSharp.Formats.Vtf`) |
| Read | versions 7.0–7.6; 8- and 16-bit layouts, DXT1/3/5, ATI1N/ATI2N, Strata Source BC4–BC7; Deflate (7.6); any mip level, frame, face or slice |
| Write | — |

## Examples

```csharp
using PictureSharp.Formats.Vtf;

using var texture = Image.Load("brick.vtf");                             // largest mip level, first frame
texture.Save("brick.png");

// Frame 3 of an animated texture, at half size.
var options = new DecoderOptions { FormatOptions = [new VtfDecoderOptions { Frame = 3, MipLevel = 1 }] };
using var frame = Image.Load("water.vtf", options);
frame.Save("water-3.png");
```

## Decoder options

| `VtfDecoderOptions` | Default | Selects |
| --- | --- | --- |
| `MipLevel` | 0 | Mip level; 0 is the full size |
| `Frame` | 0 | Animation frame |
| `Face` | 0 | Environment map face: 0–5 (right, left, back, front, up, down), 6 for the sphere map of some 7.1–7.4 files |
| `Slice` | 0 | Volume slice; each mip level has half the slices of the previous one (at least 1) |

An index the file does not store throws `ImageFormatException`. See [decoder options](decoder-options.md).

## Details

- The low-resolution thumbnail is skipped.
- 4-, 5- and 6-bit fields widen as value × 255 / max (truncating); 16-bit channels become round(value / 257).
- Bluescreen formats make pure blue transparent. A8 is black with alpha, I8 grey, and UV88 and UVLX8888 fill red and
  green (and blue).
- DXT and BC blocks use the DDS decoders.
- Float formats, P8 and compression methods other than Deflate throw `NotSupportedException`.
