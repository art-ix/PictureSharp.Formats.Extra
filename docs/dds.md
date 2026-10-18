# DDS

DirectDraw Surface, the texture container of DirectX games and tools.

| | |
| --- | --- |
| Extensions | `.dds` |
| MIME type | `image/vnd-ms.dds` |
| Module | `DdsFormat` (namespace `PictureSharp.Formats.Dds`) |
| Read | DX9 and DX10 headers; BC1–BC5, BC6H, BC7; bit-mask layouts (8–32 bit), luminance, 8-bit palette; any mip level, array element, cube face or volume slice |
| Write | uncompressed 32-bit BGRA or BGRX, one surface |

## Examples

```csharp
using PictureSharp.Formats.Dds;

using var texture = Image.Load("diffuse.dds");                           // mip level 0 of the first layer
texture.SaveAsDds("copy.dds");

// Mip level 2 of the fourth cube face.
var options = new DecoderOptions { FormatOptions = [new DdsDecoderOptions { MipLevel = 2, Layer = 3 }] };
using var face = Image.Load("skybox.dds", options);
ImageInfo info = Image.Identify("skybox.dds", options);                  // the size of that surface
```

## Decoder options

| `DdsDecoderOptions` | Default | Selects |
| --- | --- | --- |
| `MipLevel` | 0 | Mip level; 0 is the full size, each level halves both sides (at least 1) |
| `Layer` | 0 | Texture array element; cube face (+X, −X, +Y, −Y, +Z, −Z; element `n` of a cube array starts at `6 × n`; a DX9 cube map counts only its stored faces); or volume slice of the selected mip level |

A layer or level the file does not store throws `ImageFormatException`. See [decoder options](decoder-options.md).

## Encoder options

| `DdsEncoderOptions` | Default | Meaning |
| --- | --- | --- |
| `PixelFormat` | `Auto` | `Bgra32` (with alpha), `Bgrx32` (without), or `Auto`: BGRA when some pixel is transparent |

The encoder writes one 2D surface without mipmaps and a DX9 header, which every DDS reader understands.

## Details

- BC1–BC5, BC7 and the bit-mask layouts decode exactly as in Pillow 10.2.0. Signed BC4/BC5 are shifted by +128, as
  Pillow does.
- BC6H follows the D3D11 specification and matches bcdec; half floats are clamped to [0, 1] and truncated after
  × 255.
- DXT2/DXT4 and the DX10 premultiplied alpha mode are unpremultiplied; the DX10 opaque mode ignores alpha.
- The mip count is read as DirectXTex does (0 means 1).
- Float, YUV, ASTC and ETC formats throw `NotSupportedException`. DDS stores no ICC, EXIF or XMP.
