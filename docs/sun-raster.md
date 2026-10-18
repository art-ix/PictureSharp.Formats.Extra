# Sun Raster

The Sun Microsystems raster format from SunOS workstations.

| | |
| --- | --- |
| Extensions | `.ras` `.sun` |
| MIME type | `image/x-sun-raster` |
| Module | `SunRasterFormat` (namespace `PictureSharp.Formats.SunRaster`) |
| Read | 1, 4 and 8 bit with or without a colour map; 24 and 32 bit; RLE |
| Write | — |

## Example

```csharp
using var image = Image.Load("screen.ras");
image.Save("screen.png");
```

## Details

The format has a few ambiguities; the decoder follows the specification:

- RLE runs cover the row padding and may cross rows, as in ImageMagick and Netpbm (Pillow 10.2.0 leaves the padding
  out).
- The first byte of a 32-bit pixel is padding, as in Netpbm (Pillow takes the last byte, ImageMagick reads it as
  alpha).
- Type 3 stores RGB and every other type BGR, also when byte-encoded (ImageMagick swaps those colours).
- A 1-bit image with a colour map indexes the map.
