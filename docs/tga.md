# TGA

Truevision TGA (Targa), still common for game textures and video frames.

| | |
| --- | --- |
| Extensions | `.tga` `.icb` `.vda` `.vst` |
| MIME type | `image/x-tga` |
| Module | `TgaFormat` (namespace `PictureSharp.Formats.Tga`) |
| Read | colour-mapped, true-colour and greyscale; RLE; any origin |
| Write | 8-bit grey, 24-bit, 32-bit; RLE |

## Examples

```csharp
using PictureSharp.Formats.Tga;

using var image = Image.Load("texture.tga");
image.SaveAsTga("out.tga");                                               // RLE, bottom-up rows
image.SaveAsTga("raw.tga", new TgaEncoderOptions { Rle = false, Origin = TgaOrigin.TopLeft });
```

## Encoder options

| `TgaEncoderOptions` | Default | Meaning |
| --- | --- | --- |
| `Rle` | `true` | Run-length encoding; packets never cross rows |
| `Origin` | `BottomLeft` | Row order: `BottomLeft` (what most readers expect) or `TopLeft` |

The encoder writes 32-bit BGRA when some pixel is transparent, 8-bit greyscale when all pixels are grey, otherwise
24-bit BGR.

## Details

- TGA has no signature, so detection checks the header fields.
- Alpha follows the bit depth: 32-bit pixels, 16-bit greyscale, 32-bit colour maps, and 16-bit colour only with a
  declared alpha bit. A TGA 2.0 extension area can declare no alpha or premultiplied alpha.
- 15-bit colour widens as value × 255 / 31.
- Pixels match Pillow 10.2.0. TGA stores no ICC, EXIF or XMP.
