# XBM

X11 bitmap: a 1-bit image stored as C source code, used for cursors and icons in X.

| | |
| --- | --- |
| Extensions | `.xbm` |
| MIME type | `image/x-xbitmap` |
| Module | `XbmFormat` (namespace `PictureSharp.Formats.Xbm`) |
| Read | X10 and X11 |
| Write | X11, optional hotspot |

## Examples

```csharp
using PictureSharp.Formats.Xbm;

using var cursor = Image.Load("cursor.png");
cursor.SaveAsXbm("cursor.xbm", new XbmEncoderOptions { Name = "cursor", Hotspot = (4, 4) });
```

The file declares `cursor_width`, `cursor_height`, `cursor_x_hot`, `cursor_y_hot` and `cursor_bits`.

## Encoder options

| `XbmEncoderOptions` | Default | Meaning |
| --- | --- | --- |
| `Name` | `"image"` | C identifier that prefixes the declarations |
| `Hotspot` | `null` | `(X, Y)` written as `_x_hot` and `_y_hot`; `null` writes none |

A pixel becomes a set (black) bit when it is at least half opaque and its luma is below 128.

## Details

- Set bits decode as black, as in X11 and ImageMagick (Pillow decodes them as white).
- The decoder skips the hotspot: PictureSharp's metadata has no place for it.
