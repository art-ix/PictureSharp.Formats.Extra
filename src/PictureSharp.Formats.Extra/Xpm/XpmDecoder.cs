using PictureSharp.Formats.Extra.Common;

namespace PictureSharp.Formats.Xpm;

/// <summary>
/// Decodes XPM3 files. Each colour takes its <c>c</c> value, falling back to <c>g</c>, <c>g4</c> and <c>m</c>;
/// <c>None</c> is transparent black. Hexadecimal colours have one to four digits per channel and are scaled to 8 bits
/// with rounding; names come from X11's <c>rgb.txt</c>. The hotspot and extensions are ignored.
/// </summary>
internal sealed class XpmDecoder : IImageFormatDecoder
{
    private const int MaxDimension = 1 << 24;
    private const int MaxCharsPerPixel = 4;

    public static XpmDecoder Instance { get; } = new();

    public ImageInfo Identify(ReadOnlyMemory<byte> data, DecoderOptions options)
    {
        var parser = new XpmParser(data.Span);
        (int width, int height, int charsPerPixel, int colors) = ReadValues(parser.NextString());
        using XpmPalette palette = ReadPalette(ref parser, colors, charsPerPixel);
        return new ImageInfo
        {
            Format = XpmFormat.Descriptor,
            Width = width,
            Height = height,
            HasAlpha = palette.HasAlpha,
        };
    }

    public Image Decode(ReadOnlyMemory<byte> data, ImageDecodeContext context)
    {
        var parser = new XpmParser(data.Span);
        (int width, int height, int charsPerPixel, int colors) = ReadValues(parser.NextString());
        context.ReservePixels(width, height);
        using XpmPalette palette = ReadPalette(ref parser, colors, charsPerPixel);

        // Every row needs width * charsPerPixel characters, so a short file fails before the allocation.
        if ((long)width * charsPerPixel * height > data.Length)
        {
            throw Error("The XPM pixels are truncated.");
        }

        var image = new Image(width, height);
        try
        {
            Span<Rgba32> pixels = image.GetPixelSpan();
            for (int y = 0; y < height; y++)
            {
                context.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> row = parser.NextString();
                if (row.Length < width * charsPerPixel)
                {
                    throw Error("An XPM row is shorter than the image width.");
                }

                Span<Rgba32> target = pixels.Slice(y * width, width);
                for (int x = 0; x < width; x++)
                {
                    if (!palette.TryGet(row.Slice(x * charsPerPixel, charsPerPixel), out target[x]))
                    {
                        throw Error("An XPM pixel uses an undefined colour.");
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

    public static ImageFormatException Error(string message) => new(XpmFormat.Descriptor, message);

    /// <summary>The values line: width, height, number of colours, characters per pixel, then an optional hotspot and <c>XPMEXT</c>.</summary>
    private static (int Width, int Height, int CharsPerPixel, int Colors) ReadValues(ReadOnlySpan<byte> line)
    {
        Span<int> values = stackalloc int[4];
        int count = 0;
        foreach (Range range in line.SplitAny(" \t"u8))
        {
            ReadOnlySpan<byte> word = line[range];
            if (word.IsEmpty)
            {
                continue;
            }

            if (count == 4)
            {
                break;
            }

            if (!CSourceReader.TryParseNumber(word, MaxDimension, out values[count]) || word[0] == '0' && word.Length > 1 && word[1] is (byte)'x' or (byte)'X')
            {
                throw Error("The XPM values line is invalid.");
            }

            count++;
        }

        if (count < 4 || values[0] == 0 || values[1] == 0 || values[2] == 0 || values[3] == 0)
        {
            throw Error("The XPM values line is invalid.");
        }

        if (values[3] > MaxCharsPerPixel)
        {
            throw new NotSupportedException($"XPM files with {values[3]} characters per pixel are not supported.");
        }

        return (values[0], values[1], values[3], values[2]);
    }

    private static XpmPalette ReadPalette(ref XpmParser parser, int colors, int charsPerPixel)
    {
        var palette = new XpmPalette(charsPerPixel);
        try
        {
            for (int i = 0; i < colors; i++)
            {
                ReadOnlySpan<byte> line = parser.NextString();
                if (line.Length < charsPerPixel)
                {
                    throw Error("An XPM colour line is shorter than its key.");
                }

                palette.Add(line[..charsPerPixel], ParseColor(line[charsPerPixel..]));
            }

            return palette;
        }
        catch
        {
            palette.Dispose();
            throw;
        }
    }

    /// <summary>Picks the colour visual (<c>c</c>, else <c>g</c>, <c>g4</c>, <c>m</c>) from the key and value pairs; a value may hold spaces.</summary>
    private static Rgba32 ParseColor(ReadOnlySpan<byte> pairs)
    {
        // Rank of a visual: c = 4, g = 3, g4 = 2, m = 1; s (a symbolic name) is 0 and never chosen.
        int bestRank = 0, rank = -1, valueStart = -1, valueEnd = -1;
        ReadOnlySpan<byte> best = default;
        foreach (Range range in pairs.SplitAny(" \t"u8))
        {
            (int start, int length) = range.GetOffsetAndLength(pairs.Length);
            if (length == 0)
            {
                continue;
            }

            int wordRank = Rank(pairs.Slice(start, length));
            if (wordRank >= 0 && (rank < 0 || valueStart >= 0))
            {
                if (rank > bestRank && valueStart >= 0)
                {
                    bestRank = rank;
                    best = pairs[valueStart..valueEnd];
                }

                (rank, valueStart) = (wordRank, -1);
            }
            else if (rank >= 0)
            {
                valueStart = valueStart < 0 ? start : valueStart;
                valueEnd = start + length;
            }
            else
            {
                throw Error("An XPM colour line does not start with a key.");
            }
        }

        if (rank > bestRank && valueStart >= 0)
        {
            bestRank = rank;
            best = pairs[valueStart..valueEnd];
        }

        return bestRank > 0 ? ToColor(best) : throw Error("An XPM colour has no colour value.");
    }

    private static int Rank(ReadOnlySpan<byte> word) => word switch
    {
        [(byte)'c'] => 4,
        [(byte)'g'] => 3,
        [(byte)'g', (byte)'4'] => 2,
        [(byte)'m'] => 1,
        [(byte)'s'] => 0,
        _ => -1,
    };

    private static Rgba32 ToColor(ReadOnlySpan<byte> value)
    {
        if (value.Length == 4 && System.Text.Ascii.EqualsIgnoreCase(value, "none"u8))
        {
            return default;
        }

        if (value[0] == '#')
        {
            ReadOnlySpan<byte> hex = value[1..];
            int digits = hex.Length / 3;
            if (hex.Length % 3 != 0 || digits is 0 or > 4)
            {
                throw Error("An XPM hexadecimal colour is invalid.");
            }

            Span<byte> channels = stackalloc byte[3];
            int max = (1 << (4 * digits)) - 1;
            for (int i = 0; i < 3; i++)
            {
                if (!CSourceReader.TryParseNumber([(byte)'0', (byte)'x', .. hex.Slice(i * digits, digits)], max, out int v))
                {
                    throw Error("An XPM hexadecimal colour is invalid.");
                }

                channels[i] = (byte)((v * 255 * 2 + max) / (2 * max));
            }

            return new Rgba32(channels[0], channels[1], channels[2]);
        }

        if (value[0] == '%')
        {
            throw new NotSupportedException("XPM HSV colours are not supported.");
        }

        return X11Colors.TryGet(value, out Rgba32 color) ? color : throw new NotSupportedException("The XPM colour name is not an X11 colour.");
    }
}
