# Decoder options

DDS and VTF files hold more than one image: mip levels, array layers, cube faces, volume slices and animation frames.
By default the decoders read the first full-size image. To read another one, put a record in
`DecoderOptions.FormatOptions` (PictureSharp 0.7.6 or later):

```csharp
using PictureSharp.Formats.Dds;
using PictureSharp.Formats.Vtf;

var options = new DecoderOptions
{
    FormatOptions = [new DdsDecoderOptions { MipLevel = 1, Layer = 2 }, new VtfDecoderOptions { Frame = 3 }],
};
using var mip = Image.Load("texture.dds", options);
using var frame = Image.Load("water.vtf", options);
```

| Record | Properties | Page |
| --- | --- | --- |
| `DdsDecoderOptions` | `MipLevel`, `Layer` | [DDS](dds.md#decoder-options) |
| `VtfDecoderOptions` | `MipLevel`, `Frame`, `Face`, `Slice` | [VTF](vtf.md#decoder-options) |

- `Image.Identify` reports the size of the selected image, so it can be used to check a selection before loading.
- Each decoder reads only its own record. Other formats, including PictureSharp's built-in ones and other plugins,
  ignore it, so one `DecoderOptions` can carry settings for several formats.
- A negative value throws `ArgumentOutOfRangeException` when the record is created. An index the file does not store
  throws `ImageFormatException` when the file is read.
- `MaxPixels` and the other limits apply to the selected image.
