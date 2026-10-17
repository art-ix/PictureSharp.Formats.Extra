# PictureSharp.Formats.Extra

**More image formats for [PictureSharp](https://github.com/art-ix/PictureSharp), in 100% C#. No native dependencies.**

| Format | Extensions | Read | Write |
| --- | --- | :---: | :---: |
| QOI | `.qoi` | RGB, RGBA | RGB, RGBA |
| Netpbm | `.pbm` `.pgm` `.ppm` `.pnm` `.pam` | P1–P7, plain and binary, maxval up to 65535 | PBM, PGM, PPM (plain or binary), PAM |
| TGA | `.tga` `.icb` `.vda` `.vst` | colour-mapped, true-colour, greyscale; RLE; any origin | 8-bit grey, 24-bit, 32-bit; RLE |
| DDS | `.dds` | DX9 and DX10 headers; BC1–BC5, BC6H, BC7; bit-mask layouts (8–32 bit), luminance, 8-bit palette | 32-bit BGRA or BGRX |
| PSD | `.psd` `.psb` | merged image: bitmap, greyscale, duotone, indexed, RGB, CMYK; 8 and 16 bit; raw and PackBits | — |

Planned: SGI, Sun Raster, XBM/XPM.

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
- DDS decodes only the first surface: mip level 0 of the first array element, cube face or volume slice. BC1–BC5,
  BC7 and the bit-mask layouts decode exactly as in Pillow 10.2.0 (signed BC4/BC5 are shifted by +128, as Pillow does).
  BC6H follows the D3D11 spec, matching bcdec; half floats are clamped to [0, 1] and truncated after × 255. DXT2/DXT4
  and the DX10 premultiplied alpha mode are unpremultiplied. Float, YUV, ASTC and ETC formats throw
  `NotSupportedException`.
- PSD and PSB decode only the merged image; layers, masks and effects are skipped. 16-bit samples are reduced to
  round(value / 257); CMYK converts as Pillow 10.2.0 does, without the ICC profile; 1 bits of a bitmap are black.
  The first extra channel is the transparency when the layer count is negative or the file has no layers (indexed and
  bitmap images stay opaque), and RGB colours, which Photoshop composites over white, are then unblended exactly as
  ImageMagick does; greyscale and CMYK colours are kept, as in ImageMagick. ICC, EXIF, XMP and the resolution are read
  from the image resources. Lab, multichannel, 32-bit and ZIP-compressed image data throw `NotSupportedException`.
- Encoders keep the first frame and write no ICC, EXIF or XMP blocks. PBM, PGM and PPM drop alpha.
- Corrupt input throws `ImageFormatException`, valid input with unsupported features throws `NotSupportedException`,
  and `DecoderOptions.MaxPixels` is checked before any pixels are allocated.

## License

MIT. Ported sources and their licenses are listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
