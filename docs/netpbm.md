# Netpbm (PBM, PGM, PPM, PAM)

The Netpbm family: plain-text and binary bitmaps (PBM), greymaps (PGM), pixmaps (PPM) and the general PAM format.

| | |
| --- | --- |
| Extensions | `.pbm` `.pgm` `.ppm` `.pnm` `.pam` |
| MIME type | `image/x-portable-anymap` |
| Module | `PnmFormat` (namespace `PictureSharp.Formats.Pnm`) |
| Read | P1–P7, plain and binary, maxval up to 65535 |
| Write | PBM, PGM, PPM (plain or binary), PAM |

## Examples

```csharp
using PictureSharp.Formats.Pnm;

using var image = Image.Load("scan.pgm");
image.Save("scan.ppm");                                                   // binary PPM, chosen by the extension
image.SaveAsPnm("scan.pbm", new PnmEncoderOptions { Kind = PnmKind.Bitmap });
image.SaveAsPnm("scan.txt.ppm", new PnmEncoderOptions { Kind = PnmKind.Pixmap, Plain = true });
```

## Encoder options

| `PnmEncoderOptions` | Default | Meaning |
| --- | --- | --- |
| `Kind` | `Auto` | `Auto` picks PAM when some pixel is transparent, PGM when all pixels are grey, otherwise PPM. `Bitmap` (PBM), `Graymap` (PGM), `Pixmap` (PPM) or `Pam` force one |
| `Plain` | `false` | Writes the ASCII variants P1–P3. PAM has no plain variant, so `Plain` with `Pam` is rejected |

## Details

- Samples above 8 bits become round(value / maxval × 255), with ties to even.
- PBM is white where the BT.601 luma is at least 128. PGM uses the BT.601 luma.
- PBM, PGM and PPM drop alpha; PAM writes `RGB_ALPHA`, `RGB`, `GRAYSCALE_ALPHA` or `GRAYSCALE`, whichever keeps every
  pixel.
- Pixels match Pillow 10.2.0.
