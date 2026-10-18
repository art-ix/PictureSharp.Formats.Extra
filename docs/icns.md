# ICNS

Apple Icon Image, the icon format of macOS applications.

| | |
| --- | --- |
| Extensions | `.icns` |
| MIME type | `image/icns` |
| Module | `IcnsFormat` (namespace `PictureSharp.Formats.Icns`) |
| Read | the largest icon: PNG, RLE RGB with an 8-bit mask, ARGB, 1-bit with a mask |
| Write | — |

## Example

```csharp
using var icon = Image.Load("App.icns");                                 // the largest icon, e.g. 1024 × 1024
icon.Mutate(x => x.Resize(256, 256));
icon.Save("app-256.png");
```

## Details

- The largest icon is chosen in Pillow 10.2.0's order. Within a size, a PNG or ARGB element wins over an RGB element,
  which takes its alpha from the matching 8-bit mask.
- PNG elements decode through PictureSharp's PNG decoder and keep their ICC, EXIF and XMP.
- JPEG 2000 elements are skipped; a file with only JPEG 2000 icons throws `NotSupportedException`.
- 1-bit icons are used only when there are no colour icons. The 4- and 8-bit legacy icons are not read.
