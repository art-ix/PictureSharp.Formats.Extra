# PictureSharp.Formats.Extra

**More image formats for [PictureSharp](https://github.com/art-ix/PictureSharp), in 100% C#. No native dependencies.**

| Format | Read | Write |
| --- | --- | --- |
| [QOI](https://github.com/art-ix/PictureSharp.Formats.Extra/blob/main/docs/qoi.md) | RGB, RGBA | RGB, RGBA |
| [Netpbm](https://github.com/art-ix/PictureSharp.Formats.Extra/blob/main/docs/netpbm.md) (PBM, PGM, PPM, PAM) | P1–P7, up to 16 bit | PBM, PGM, PPM, PAM |
| [TGA](https://github.com/art-ix/PictureSharp.Formats.Extra/blob/main/docs/tga.md) | colour-mapped, true-colour, grey; RLE | 8, 24, 32 bit; RLE |
| [DDS](https://github.com/art-ix/PictureSharp.Formats.Extra/blob/main/docs/dds.md) | BC1–BC7, uncompressed; mips, arrays, cube maps, volumes | 32-bit BGRA/BGRX |
| [PSD, PSB](https://github.com/art-ix/PictureSharp.Formats.Extra/blob/main/docs/psd.md) | merged image, 8 and 16 bit | — |
| [SGI](https://github.com/art-ix/PictureSharp.Formats.Extra/blob/main/docs/sgi.md) | 1–4 channels, 8 and 16 bit, RLE | 8 bit, RLE |
| [Sun Raster](https://github.com/art-ix/PictureSharp.Formats.Extra/blob/main/docs/sun-raster.md) | 1–32 bit, RLE | — |
| [XBM](https://github.com/art-ix/PictureSharp.Formats.Extra/blob/main/docs/xbm.md) | X10, X11 | X11 |
| [XPM](https://github.com/art-ix/PictureSharp.Formats.Extra/blob/main/docs/xpm.md) | XPM3 | — |
| [VTF](https://github.com/art-ix/PictureSharp.Formats.Extra/blob/main/docs/vtf.md) | 7.0–7.6, DXT and BC, Deflate; mips, frames, faces | — |
| [ICNS](https://github.com/art-ix/PictureSharp.Formats.Extra/blob/main/docs/icns.md) | largest icon: PNG, RLE, ARGB, 1 bit | — |

Requires .NET 10 and PictureSharp 0.7.6 or later.

```bash
dotnet add package PictureSharp.Formats.Extra
```

## Usage

```csharp
using PictureSharp;
using PictureSharp.Formats.Extra;
using PictureSharp.Formats.Qoi;

ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddExtraFormats().Build();

using var image = Image.Load("sprite.tga");
image.SaveAsQoi("sprite.qoi");
image.Save("sprite.ppm");                 // the format comes from the extension
```

DDS and VTF files hold several images; choose one with a decoder options record:

```csharp
using PictureSharp.Formats.Dds;

var options = new DecoderOptions { FormatOptions = [new DdsDecoderOptions { MipLevel = 1, Layer = 2 }] };
using var mip = Image.Load("texture.dds", options);
```

## Documentation

- [Getting started](https://github.com/art-ix/PictureSharp.Formats.Extra/blob/main/docs/README.md): registering all or some formats, shortcuts, common behaviour.
- One page per format (linked in the table above): what is read and written, options, examples and exact behaviour.
- [Decoder options](https://github.com/art-ix/PictureSharp.Formats.Extra/blob/main/docs/decoder-options.md): mip levels, layers and frames in DDS and VTF.

Decoded pixels are checked against Pillow 10.2.0, ImageMagick or reference writers. Corrupt files throw
`ImageFormatException`, unsupported features `NotSupportedException`.

## License

MIT. Ported sources and their licenses are listed in [THIRD-PARTY-NOTICES.txt](https://github.com/art-ix/PictureSharp.Formats.Extra/blob/main/THIRD-PARTY-NOTICES.txt).
