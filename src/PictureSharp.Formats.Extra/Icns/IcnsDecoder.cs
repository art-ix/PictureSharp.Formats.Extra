using System.Buffers.Binary;

namespace PictureSharp.Formats.Icns;

/// <summary>
/// Decodes the largest icon of an ICNS file. Sizes are ranked as Pillow 10.2.0 ranks them (points, then scale); within
/// a size a PNG (or ARGB) element wins over an RGB element, which takes its alpha from the matching 8-bit mask. Sizes
/// whose only image is JPEG 2000 are skipped. Files without colour icons fall back to a 1-bit icon and its mask.
/// </summary>
internal sealed class IcnsDecoder : IImageFormatDecoder
{
    private const int HeaderSize = 8;

    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0d, 0x0a, 0x1a, 0x0a];

    // Pillow's order: (points, scale) descending; within a size the elements in order of preference.
    private static readonly IconSize[] Sizes =
    [
        new(512, 2, "ic10"), new(512, 1, "ic09"), new(256, 2, "ic14"), new(256, 1, "ic08"), new(128, 2, "ic13"),
        new(128, 1, "ic07", Rgb: "it32", Mask: "t8mk"), new(64, 1, "icp6"), new(48, 1, null, Rgb: "ih32", Mask: "h8mk"),
        new(32, 2, "ic12"), new(32, 1, "icp5", "ic05", "il32", "l8mk"), new(24, 2, "SB24"), new(24, 1, "sb24"),
        new(18, 2, "icsB"), new(18, 1, "icsb"), new(16, 2, "ic11"), new(16, 1, "icp4", "ic04", "is32", "s8mk"),
    ];

    private static readonly (string Type, int Size)[] Monochrome = [("ich#", 48), ("ICN#", 32), ("ics#", 16)];

    public static IcnsDecoder Instance { get; } = new();

    public ImageInfo Identify(ReadOnlyMemory<byte> data, DecoderOptions options)
    {
        Choice choice = Choose(data);
        (int width, int height) = choice.Kind == ElementKind.Png ? PngSize(choice.Data.Span) : (choice.Pixels, choice.Pixels);
        return new ImageInfo
        {
            Format = IcnsFormat.Descriptor,
            Width = width,
            Height = height,
            HasAlpha = choice.Kind != ElementKind.Rgb || !choice.Mask.IsEmpty,
        };
    }

    public Image Decode(ReadOnlyMemory<byte> data, ImageDecodeContext context)
    {
        Choice choice = Choose(data);
        if (choice.Kind == ElementKind.Png)
        {
            return context.DecodeEmbedded(choice.Data);
        }

        int size = choice.Pixels;
        context.ReservePixels(size, size);
        var image = new Image(size, size);
        try
        {
            Span<Rgba32> pixels = image.GetPixelSpan();
            ReadOnlySpan<byte> element = choice.Data.Span;
            switch (choice.Kind)
            {
                case ElementKind.Rgb:
                    DecodeRgb(element, pixels);
                    ApplyMask(choice.Mask.Span, pixels);
                    break;
                case ElementKind.Argb:
                    DecodePlanes(element[4..], pixels, [3, 0, 1, 2]);
                    break;
                default:
                    DecodeMonochrome(element, size, pixels);
                    break;
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    public static ImageFormatException Error(string message) => new(IcnsFormat.Descriptor, message);

    private static Choice Choose(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> data = file.Span;
        Dictionary<int, (int Offset, int Length)> elements = ReadElements(data);
        bool skippedJpeg2000 = false;
        foreach (IconSize size in Sizes)
        {
            int pixels = size.Points * size.Scale;
            foreach (string? type in (string?[])[size.Png, size.Argb])
            {
                if (type is null || !elements.TryGetValue(Key(type), out var at))
                {
                    continue;
                }

                ReadOnlySpan<byte> element = data.Slice(at.Offset, at.Length);
                if (element.StartsWith(PngSignature))
                {
                    return new Choice(ElementKind.Png, pixels, file.Slice(at.Offset, at.Length), default);
                }

                if (element.StartsWith("ARGB"u8))
                {
                    return new Choice(ElementKind.Argb, pixels, file.Slice(at.Offset, at.Length), default);
                }

                // Anything else in these slots is JPEG 2000, which needs a separate codec.
                skippedJpeg2000 = true;
            }

            if (size.Rgb is not null && elements.TryGetValue(Key(size.Rgb), out var rgb))
            {
                if (size.Rgb == "it32")
                {
                    // The 128-pixel RGB element starts with four zero bytes.
                    if (rgb.Length < 4 || BinaryPrimitives.ReadUInt32BigEndian(data[rgb.Offset..]) != 0)
                    {
                        throw Error("The ICNS it32 element lacks its zero prefix.");
                    }

                    rgb = (rgb.Offset + 4, rgb.Length - 4);
                }

                (int Offset, int Length) mask = elements.TryGetValue(Key(size.Mask!), out var m) ? m : default;
                if (mask.Length != 0 && mask.Length < pixels * pixels)
                {
                    throw Error("An ICNS mask is truncated.");
                }

                return new Choice(ElementKind.Rgb, pixels, file.Slice(rgb.Offset, rgb.Length), file.Slice(mask.Offset, mask.Length));
            }
        }

        foreach ((string type, int size) in Monochrome)
        {
            if (elements.TryGetValue(Key(type), out var at))
            {
                if (at.Length < size * size / 4)
                {
                    throw Error("An ICNS 1-bit icon is truncated.");
                }

                return new Choice(ElementKind.Monochrome, size, file.Slice(at.Offset, at.Length), default);
            }
        }

        throw skippedJpeg2000
            ? new NotSupportedException("The ICNS file holds only JPEG 2000 icons.")
            : Error("The ICNS file holds no icon this decoder reads.");
    }

    /// <summary>The elements after the 8-byte file header: a type, a length that includes this 8-byte header, the data.</summary>
    private static Dictionary<int, (int Offset, int Length)> ReadElements(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || !data.StartsWith("icns"u8))
        {
            throw Error("Not an ICNS file.");
        }

        uint total = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        if (total < HeaderSize || total > data.Length)
        {
            throw Error("The ICNS file length is invalid.");
        }

        var elements = new Dictionary<int, (int, int)>();
        int pos = HeaderSize;
        while (pos < total)
        {
            if (total - pos < HeaderSize)
            {
                throw Error("An ICNS element header is truncated.");
            }

            int type = BinaryPrimitives.ReadInt32BigEndian(data[pos..]);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(data[(pos + 4)..]);
            if (length < HeaderSize || length > total - pos)
            {
                throw Error("An ICNS element length is invalid.");
            }

            elements[type] = (pos + HeaderSize, (int)length - HeaderSize);
            pos += (int)length;
        }

        return elements;
    }

    private static int Key(string type) => type[0] << 24 | type[1] << 16 | type[2] << 8 | type[3];

    private static (int Width, int Height) PngSize(ReadOnlySpan<byte> png)
    {
        if (png.Length < 24)
        {
            throw Error("An ICNS PNG element is truncated.");
        }

        return ((int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(png[16..]), int.MaxValue), (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(png[20..]), int.MaxValue));
    }

    // port: Pillow IcnsImagePlugin read_32: interleaved RGB when the element holds exactly that many bytes, otherwise
    // each channel compressed on its own.
    private static void DecodeRgb(ReadOnlySpan<byte> element, Span<Rgba32> pixels)
    {
        if (element.Length == pixels.Length * 3)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Rgba32(element[3 * i], element[3 * i + 1], element[3 * i + 2]);
            }

            return;
        }

        pixels.Fill(new Rgba32(0, 0, 0, 255));
        DecodePlanes(element, pixels, [0, 1, 2]);
    }

    /// <summary>
    /// Expands consecutive compressed planes into the given components: a byte below 0x80 starts n + 1 literal bytes,
    /// any other byte repeats the next one n - 125 times. A run never continues into the next plane.
    /// </summary>
    private static void DecodePlanes(ReadOnlySpan<byte> source, Span<Rgba32> pixels, ReadOnlySpan<int> components)
    {
        Span<byte> samples = System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels);
        int s = 0;
        foreach (int component in components)
        {
            int x = 0;
            while (x < pixels.Length)
            {
                if (s >= source.Length)
                {
                    throw Error("An ICNS RLE plane is truncated.");
                }

                int code = source[s++];
                int count = code < 0x80 ? code + 1 : code - 125;
                if (count > pixels.Length - x)
                {
                    throw Error("An ICNS RLE run passes the end of a plane.");
                }

                if (code < 0x80)
                {
                    if (source.Length - s < count)
                    {
                        throw Error("An ICNS RLE literal run is truncated.");
                    }

                    for (int i = 0; i < count; i++)
                    {
                        samples[(x + i) * 4 + component] = source[s + i];
                    }

                    s += count;
                }
                else
                {
                    if (s >= source.Length)
                    {
                        throw Error("An ICNS RLE repeat run is truncated.");
                    }

                    for (int i = 0; i < count; i++)
                    {
                        samples[(x + i) * 4 + component] = source[s];
                    }

                    s++;
                }

                x += count;
            }
        }
    }

    private static void ApplyMask(ReadOnlySpan<byte> mask, Span<Rgba32> pixels)
    {
        if (mask.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i].A = mask[i];
        }
    }

    /// <summary>A 1-bit icon followed by its 1-bit mask, rows of bits most significant first; set icon bits are black.</summary>
    private static void DecodeMonochrome(ReadOnlySpan<byte> element, int size, Span<Rgba32> pixels)
    {
        int bytes = size * size / 8;
        for (int i = 0; i < pixels.Length; i++)
        {
            int bit = 0x80 >> (i & 7);
            byte v = (element[i >> 3] & bit) != 0 ? (byte)0 : (byte)255;
            byte a = (element[bytes + (i >> 3)] & bit) != 0 ? (byte)255 : (byte)0;
            pixels[i] = new Rgba32(v, v, v, a);
        }
    }

    private enum ElementKind
    {
        Png,
        Argb,
        Rgb,
        Monochrome,
    }

    private sealed record IconSize(int Points, int Scale, string? Png, string? Argb = null, string? Rgb = null, string? Mask = null);

    private readonly record struct Choice(ElementKind Kind, int Pixels, ReadOnlyMemory<byte> Data, ReadOnlyMemory<byte> Mask);
}
