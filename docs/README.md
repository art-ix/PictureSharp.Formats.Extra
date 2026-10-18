# PictureSharp.Formats.Extra documentation

## Getting started

Register every format once at startup:

```csharp
using PictureSharp;
using PictureSharp.Formats.Extra;

ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddExtraFormats().Build();
```

After that, `Image.Load`, `Image.Identify` and `Image.Save` handle the new formats like the built-in ones; `Save`
picks the format from the file extension. To add only some formats, register their modules:

```csharp
using PictureSharp.Formats.Qoi;
using PictureSharp.Formats.Tga;

ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder()
    .AddFormat(new QoiFormat())
    .AddFormat(new TgaFormat())
    .Build();
```

To keep the global default untouched, build a configuration and pass it only where needed, through
`DecoderOptions.Configuration` and `EncoderOptions.Configuration`:

```csharp
using PictureSharp.Formats.Qoi;

ImageConfiguration extra = ImageConfiguration.Default.ToBuilder().AddExtraFormats().Build();
using var image = Image.Load("sprite.tga", new DecoderOptions { Configuration = extra });
image.Save("sprite.qoi", new QoiEncoderOptions { Configuration = extra });
```

Every format that can be written has `SaveAsXxx`, `SaveAsXxxAsync` and `ToXxx` shortcuts (for example `SaveAsQoi`)
and an `XxxEncoderOptions` record. Every format has an `ImageFormat.Xxx` property (for example `ImageFormat.Qoi`).

## Formats

| Format | Read | Write |
| --- | :---: | :---: |
| [QOI](qoi.md) | ✓ | ✓ |
| [Netpbm](netpbm.md): PBM, PGM, PPM, PAM | ✓ | ✓ |
| [TGA](tga.md) | ✓ | ✓ |
| [DDS](dds.md) | ✓ | ✓ |
| [PSD and PSB](psd.md) | ✓ | |
| [SGI](sgi.md) | ✓ | ✓ |
| [Sun Raster](sun-raster.md) | ✓ | |
| [XBM](xbm.md) | ✓ | ✓ |
| [XPM](xpm.md) | ✓ | |
| [VTF](vtf.md) | ✓ | |
| [ICNS](icns.md) | ✓ | |

[Decoder options](decoder-options.md) select a mip level, layer or frame in DDS and VTF files.

## Common behaviour

- Images decode to 8-bit RGBA with straight alpha. Deeper samples are reduced to 8 bits with the rounding each page
  describes.
- Decoded pixels are checked against Pillow 10.2.0, ImageMagick or the project's own reference writers.
- Encoders write the first frame and no ICC, EXIF or XMP blocks.
- Corrupt files throw `ImageFormatException`, and valid files with unsupported features throw
  `NotSupportedException`. The decoders are fuzz-tested so that malicious input raises nothing else.
- `DecoderOptions.MaxPixels`, `MaxInputBytes` and cancellation apply as for the built-in formats; the pixel limit is
  checked before any pixels are allocated.
