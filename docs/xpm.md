# XPM

X11 pixmap: a colour image stored as C source code with a palette of characters.

| | |
| --- | --- |
| Extensions | `.xpm` |
| MIME type | `image/x-xpixmap` |
| Module | `XpmFormat` (namespace `PictureSharp.Formats.Xpm`) |
| Read | XPM3, up to 4 characters per pixel, hexadecimal colours, X11 colour names, `None` |
| Write | — |

## Example

```csharp
using var icon = Image.Load("app.xpm");
icon.Save("app.png");
```

## Details

- Colours come from the `c` visual, then `g`, `g4` and `m`.
- Hexadecimal colours with 1–4 digits per channel are scaled to 8 bits with rounding.
- Colour names come from X.Org's `rgb.txt`, so they have X11 values (`gray` is 190, not the CSS 128).
- `None` is transparent black.
- HSV colours and unknown names throw `NotSupportedException`.
