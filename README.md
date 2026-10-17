# PictureSharp.Formats.Extra

**More image formats for [PictureSharp](https://github.com/art-ix/PictureSharp), in 100% C#. No native dependencies.**

| Format | Extensions | Read | Write |
| --- | --- | :---: | :---: |
| QOI | `.qoi` | RGB, RGBA | RGB, RGBA |
| Netpbm | `.pbm` `.pgm` `.ppm` `.pnm` `.pam` | P1–P7, plain and binary, maxval up to 65535 | PBM, PGM, PPM (plain or binary), PAM |
| TGA | `.tga` `.icb` `.vda` `.vst` | colour-mapped, true-colour, greyscale; RLE; any origin | 8-bit grey, 24-bit, 32-bit; RLE |

Planned: DDS (BC1–BC7) and TIFF (read only).

```bash
dotnet add package PictureSharp.Formats.Extra
```

```csharp
using PictureSharp;
using PictureSharp.Formats.Extra;
using PictureSharp.Formats.Qoi;

ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddExtraFormats().Build();

using var image = Image.Load("sprite.tga");
image.SaveAsQoi("sprite.qoi");
image.Save("sprite.ppm");             // the format comes from the extension
```

Register a single format with `.AddFormat(new TgaFormat())`, or pass a configuration only to the calls that need it
through `DecoderOptions.Configuration` and `EncoderOptions.Configuration`.

## Behaviour

- Pixels decode exactly as in Pillow 10.2.0. Netpbm samples above 8 bits are reduced to
  round(value / maxval × 255) with ties to even, and 15-bit TGA colour to value × 255 / 31.
- The QOI encoder follows `qoi.h` `qoi_encode` chunk for chunk. A linear QOI colour space maps to
  `ImageMetadata.Gamma = 1` in both directions.
- TGA has no signature, so detection checks the header fields. Alpha follows the bit depth (32-bit pixels, 16-bit
  greyscale, 32-bit colour maps, and 16-bit colour only with a declared alpha bit). A TGA 2.0 extension area can
  declare no alpha or premultiplied alpha.
- Encoders keep the first frame and write no ICC, EXIF or XMP blocks. PBM, PGM and PPM drop alpha.
- Corrupt input throws `ImageFormatException`, valid input with unsupported features throws `NotSupportedException`,
  and `DecoderOptions.MaxPixels` is checked before any pixels are allocated.

## License

MIT. Ported sources and their licenses are listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
