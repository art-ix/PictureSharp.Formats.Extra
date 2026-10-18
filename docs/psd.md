# PSD and PSB

Adobe Photoshop documents (PSD) and large documents (PSB), read as their merged image.

| | |
| --- | --- |
| Extensions | `.psd` `.psb` |
| MIME type | `image/vnd.adobe.photoshop` |
| Module | `PsdFormat` (namespace `PictureSharp.Formats.Psd`) |
| Read | merged image: bitmap, greyscale, duotone, indexed, RGB, CMYK; 8 and 16 bit; raw and PackBits |
| Write | — |

## Examples

```csharp
using var document = Image.Load("poster.psd");
document.Save("poster.png");                                              // keeps ICC, EXIF and XMP

ImageInfo info = Image.Identify("poster.psb");
Console.WriteLine($"{info.Width} × {info.Height}, alpha: {info.HasAlpha}");
```

## Details

- Only the merged image (the composite Photoshop stores when "Maximize compatibility" is on) is read. Layers, masks
  and effects are skipped.
- 16-bit samples become round(value / 257). CMYK converts as Pillow 10.2.0 does, without the ICC profile. Set bits of
  a bitmap image are black.
- Transparency: the first extra channel, when the layer count is negative or the file has no layers. Indexed and
  bitmap images stay opaque. Photoshop composites RGB colours over white; they are unblended exactly as ImageMagick
  does, while greyscale and CMYK colours are kept.
- ICC, EXIF, XMP and the resolution come from the image resources.
- Lab, multichannel, 32-bit and ZIP-compressed image data throw `NotSupportedException`.
