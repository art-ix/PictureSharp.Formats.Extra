# QOI

[Quite OK Image](https://qoiformat.org/) is a simple, fast lossless format for RGB and RGBA images.

| | |
| --- | --- |
| Extensions | `.qoi` |
| MIME type | `image/qoi` |
| Module | `QoiFormat` (namespace `PictureSharp.Formats.Qoi`) |
| Read | RGB, RGBA |
| Write | RGB, RGBA |

## Examples

```csharp
using PictureSharp.Formats.Qoi;

using var image = Image.Load("sprite.png");
image.SaveAsQoi("sprite.qoi");                                            // alpha only when needed
image.SaveAsQoi("sprite-rgb.qoi", new QoiEncoderOptions { IncludeAlpha = false });
byte[] bytes = image.ToQoi();
```

## Encoder options

| `QoiEncoderOptions` | Default | Meaning |
| --- | --- | --- |
| `IncludeAlpha` | `null` | `true` writes 4 channels, `false` 3 (alpha dropped); `null` writes alpha only when some pixel is not opaque |
| `ColorSpace` | `null` | `Srgb` or `Linear` in the header; `null` writes `Linear` when `ImageMetadata.Gamma` is 1 |

## Details

- The encoder follows the reference `qoi.h` (`qoi_encode`) chunk for chunk.
- The header's colour space does not change the pixels. A linear file loads with `ImageMetadata.Gamma = 1`.
- QOI stores no ICC, EXIF or XMP.
