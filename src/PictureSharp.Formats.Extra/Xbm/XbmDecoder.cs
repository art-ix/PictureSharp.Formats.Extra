using PictureSharp.Formats.Extra.Common;

namespace PictureSharp.Formats.Xbm;

/// <summary>Decodes X10 and X11 bitmaps: bits are stored least significant first, 1 is black and 0 white.</summary>
internal sealed class XbmDecoder : IImageFormatDecoder
{
    private static readonly Rgba32 Black = new(0, 0, 0);
    private static readonly Rgba32 White = new(255, 255, 255);

    public static XbmDecoder Instance { get; } = new();

    public ImageInfo Identify(ReadOnlyMemory<byte> data, DecoderOptions options)
    {
        XbmParser header = XbmParser.Parse(data.Span);
        return new ImageInfo
        {
            Format = XbmFormat.Descriptor,
            Width = header.Width,
            Height = header.Height,
            BitsPerChannel = 1,
        };
    }

    public Image Decode(ReadOnlyMemory<byte> data, ImageDecodeContext context)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        XbmParser header = XbmParser.Parse(bytes);
        context.ReservePixels(header.Width, header.Height);

        // Every value takes at least two characters ("0," or "0}"), so a short file fails before the allocation.
        if ((long)header.UnitsPerRow * header.Height > (bytes.Length - header.DataOffset + 1) / 2)
        {
            throw Error("The XBM bit array is truncated.");
        }

        var image = new Image(header.Width, header.Height);
        try
        {
            Span<Rgba32> pixels = image.GetPixelSpan();
            var reader = new CSourceReader(bytes[header.DataOffset..]);
            int bits = header.IsX10 ? 16 : 8, max = (1 << bits) - 1;
            for (int y = 0; y < header.Height; y++)
            {
                context.ThrowIfCancellationRequested();
                Span<Rgba32> row = pixels.Slice(y * header.Width, header.Width);
                for (int unit = 0; unit < header.UnitsPerRow; unit++)
                {
                    int value = ReadValue(ref reader, max);
                    int first = unit * bits, count = Math.Min(bits, header.Width - first);
                    for (int b = 0; b < count; b++)
                    {
                        row[first + b] = (value >> b & 1) != 0 ? Black : White;
                    }
                }
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    public static ImageFormatException Error(string message) => new(XbmFormat.Descriptor, message);

    private static int ReadValue(ref CSourceReader reader, int max)
    {
        while (reader.SkipSpace() && !reader.AtEnd && reader.Current == ',')
        {
            reader.Advance();
        }

        if (reader.AtEnd || reader.Current == '}')
        {
            throw Error("The XBM bit array is truncated.");
        }

        if (!CSourceReader.TryParseNumber(reader.ReadWord(), max, out int value))
        {
            throw Error("The XBM bit array holds an invalid value.");
        }

        return value;
    }
}
