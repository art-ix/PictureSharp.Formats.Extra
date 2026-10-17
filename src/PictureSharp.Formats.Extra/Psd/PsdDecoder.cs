using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace PictureSharp.Formats.Psd;

/// <summary>
/// Decodes the merged image. Channels are planar and stored row by row, raw or PackBits. 16-bit samples become
/// <c>round(v / 257)</c>; CMYK converts as Pillow does after the 8-bit reduction; bitmap 1 bits are black. With a
/// transparency channel, RGB colours, which Photoshop composites over white, are unblended as ImageMagick does;
/// greyscale and CMYK colours are kept, as ImageMagick keeps them.
/// </summary>
internal sealed class PsdDecoder : IImageFormatDecoder
{
    private const int Raw = 0;
    private const int PackBits = 1;

    public static PsdDecoder Instance { get; } = new();

    public ImageInfo Identify(ReadOnlyMemory<byte> data, DecoderOptions options)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        PsdHeader header = PsdHeader.Parse(bytes);
        (float horizontal, float vertical) = header.Resolution(bytes);
        return new ImageInfo
        {
            Format = PsdFormat.Descriptor,
            Width = header.Width,
            Height = header.Height,
            HasAlpha = header.MergedAlpha,
            BitsPerChannel = header.Depth,
            HorizontalResolution = horizontal > 0 ? horizontal : 96f,
            VerticalResolution = vertical > 0 ? vertical : 96f,
        };
    }

    public Image Decode(ReadOnlyMemory<byte> data, ImageDecodeContext context)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        PsdHeader header = PsdHeader.Parse(bytes);
        context.ReservePixels(header.Width, header.Height);

        var image = new Image(header.Width, header.Height);
        try
        {
            DecodePixels(bytes, header, image.GetPixelSpan(), context);
            ReadResources(bytes, header, image.Metadata, context);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    public static ImageFormatException Error(string message) => new(PsdFormat.Descriptor, message);

    private static void DecodePixels(ReadOnlySpan<byte> data, PsdHeader header, Span<Rgba32> pixels, ImageDecodeContext context)
    {
        int width = header.Width, height = header.Height;
        int rowBytes = header.Depth switch
        {
            1 => (width + 7) / 8,
            8 => width,
            _ => width * 2,
        };

        int pos = header.ImageDataOffset;
        if (data.Length - pos < 2)
        {
            throw Error("The PSD image data is missing.");
        }

        int compression = BinaryPrimitives.ReadUInt16BigEndian(data[pos..]);
        ReadOnlySpan<byte> body = data[(pos + 2)..];
        if (compression is 2 or 3)
        {
            throw new NotSupportedException("ZIP-compressed PSD image data is not supported.");
        }

        if (compression is not (Raw or PackBits))
        {
            throw Error($"The PSD compression {compression} is unknown.");
        }

        // CMYK has five components with alpha; the black plane lives outside the RGBA pixels until the conversion.
        byte[]? black = header.Mode == PsdColorMode.Cmyk ? ArrayPool<byte>.Shared.Rent(width * height) : null;
        byte[] row = ArrayPool<byte>.Shared.Rent(rowBytes);
        try
        {
            Span<byte> samples = MemoryMarshal.AsBytes(pixels);
            if (compression == Raw)
            {
                if (body.Length / rowBytes / height < header.UsedChannels)
                {
                    throw Error("The PSD image data is truncated.");
                }

                for (int c = 0; c < header.UsedChannels; c++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        context.ThrowIfCancellationRequested();
                        Store(header, c, y, body.Slice((c * height + y) * rowBytes, rowBytes), samples, black);
                    }
                }
            }
            else
            {
                DecodePackBits(body, header, rowBytes, row.AsSpan(0, rowBytes), samples, black, context);
            }

            Finish(header, data, pixels, black);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(row);
            if (black is not null)
            {
                ArrayPool<byte>.Shared.Return(black);
            }
        }
    }

    private static void DecodePackBits(ReadOnlySpan<byte> body, PsdHeader header, int rowBytes, Span<byte> row, Span<byte> samples, byte[]? black, ImageDecodeContext context)
    {
        // The table holds the compressed length of every row of every channel, two bytes each (four in PSB).
        int countSize = header.IsLarge ? 4 : 2;
        long tableSize = (long)header.Channels * header.Height * countSize;
        if (body.Length < tableSize)
        {
            throw Error("The PSD row length table is truncated.");
        }

        ReadOnlySpan<byte> table = body[..(int)tableSize];
        long offset = tableSize;
        int rows = header.UsedChannels * header.Height;
        for (int i = 0; i < rows; i++)
        {
            context.ThrowIfCancellationRequested();
            uint length = header.IsLarge ? BinaryPrimitives.ReadUInt32BigEndian(table[(i * 4)..]) : BinaryPrimitives.ReadUInt16BigEndian(table[(i * 2)..]);
            if (length > body.Length - offset)
            {
                throw Error("The PSD PackBits data is truncated.");
            }

            Unpack(body.Slice((int)offset, (int)length), row);
            offset += length;
            Store(header, i / header.Height, i % header.Height, row, samples, black);
        }
    }

    /// <summary>Expands one PackBits row; a run past the end of the row is cut, a row that ends early is an error.</summary>
    private static void Unpack(ReadOnlySpan<byte> source, Span<byte> row)
    {
        int s = 0, d = 0;
        while (d < row.Length)
        {
            if (s >= source.Length)
            {
                throw Error("A PSD PackBits row is shorter than the image width.");
            }

            int n = (sbyte)source[s++];
            if (n >= 0)
            {
                int count = Math.Min(n + 1, row.Length - d);
                if (source.Length - s < count)
                {
                    throw Error("A PSD PackBits literal run is truncated.");
                }

                source.Slice(s, count).CopyTo(row[d..]);
                s += n + 1;
                d += count;
            }
            else if (n != -128)
            {
                if (s >= source.Length)
                {
                    throw Error("A PSD PackBits repeat run is truncated.");
                }

                int count = Math.Min(1 - n, row.Length - d);
                row.Slice(d, count).Fill(source[s++]);
                d += count;
            }
        }
    }

    /// <summary>Writes one row of channel <paramref name="channel"/> as 8-bit samples into its RGBA component or the black plane.</summary>
    private static void Store(PsdHeader header, int channel, int y, ReadOnlySpan<byte> row, Span<byte> samples, byte[]? black)
    {
        int width = header.Width;
        int component = channel < header.BaseChannels ? channel : 3;
        Span<byte> plane = samples;
        int start = y * width * 4 + component, step = 4;
        if (header.Mode == PsdColorMode.Cmyk && channel == 3)
        {
            plane = black!;
            start = y * width;
            step = 1;
        }

        switch (header.Depth)
        {
            case 1:
                for (int x = 0; x < width; x++)
                {
                    plane[start + x * step] = (row[x >> 3] & (0x80 >> (x & 7))) != 0 ? (byte)0 : (byte)255;
                }

                break;
            case 8:
                for (int x = 0; x < width; x++)
                {
                    plane[start + x * step] = row[x];
                }

                break;
            default:
                for (int x = 0; x < width; x++)
                {
                    plane[start + x * step] = (byte)((BinaryPrimitives.ReadUInt16BigEndian(row[(2 * x)..]) + 128) / 257);
                }

                break;
        }
    }

    /// <summary>Turns the stored samples into straight RGBA: RGB unblending, palette lookup, greyscale and CMYK conversion.</summary>
    private static void Finish(PsdHeader header, ReadOnlySpan<byte> data, Span<Rgba32> pixels, byte[]? black)
    {
        bool alpha = header.MergedAlpha;
        ReadOnlySpan<byte> palette = data.Slice(header.ColorDataOffset, header.ColorDataLength);
        for (int i = 0; i < pixels.Length; i++)
        {
            ref Rgba32 px = ref pixels[i];
            byte a = alpha ? px.A : (byte)255;
            switch (header.Mode)
            {
                case PsdColorMode.Indexed:
                    // The palette is planar: 256 reds, 256 greens, 256 blues.
                    px = new Rgba32(palette[px.R], palette[256 + px.R], palette[512 + px.R]);
                    break;
                case PsdColorMode.Rgb:
                    px = new Rgba32(Unblend(px.R, a), Unblend(px.G, a), Unblend(px.B, a), a);
                    break;
                case PsdColorMode.Cmyk:
                    // Stored values are inverted: 255 is no ink.
                    int k = black![i];
                    px = new Rgba32(Ink(px.R, k), Ink(px.G, k), Ink(px.B, k), a);
                    break;
                default:
                    px = new Rgba32(px.R, px.R, px.R, a);
                    break;
            }
        }
    }

    // port: ImageMagick psd.c CorrectPSDAlphaBlend in its Q16 build: (v - (1 - a) * QuantumRange) / a in double
    // precision on 16-bit values, rounded to 16 bits and then to 8. The operation order keeps ties bit-identical.
    private static byte Unblend(byte value, byte alpha)
    {
        if (alpha is 0 or 255)
        {
            return value;
        }

        double gamma = 1.0 / 65535.0 * (alpha * 257);
        double unblended = (value * 257 - (1.0 - gamma) * 65535.0) / gamma;
        int q16 = (int)Math.Clamp(Math.Floor(unblended + 0.5), 0, 65535);
        return (byte)((q16 + 128) / 257);
    }

    // Pillow Convert.c cmyk2rgb: 255 - k scaled by the remaining colour, with MULDIV255 rounding.
    private static byte Ink(int stored, int storedBlack)
    {
        int ink = 255 - stored;
        int t = ink * storedBlack + 128;
        return (byte)(storedBlack - (((t >> 8) + t) >> 8));
    }

    private static void ReadResources(ReadOnlySpan<byte> data, PsdHeader header, ImageMetadata metadata, ImageDecodeContext context)
    {
        (float horizontal, float vertical) = header.Resolution(data);
        if (horizontal > 0 && float.IsFinite(horizontal) && vertical > 0 && float.IsFinite(vertical))
        {
            metadata.HorizontalResolution = horizontal;
            metadata.VerticalResolution = vertical;
        }

        if (context.Options.IgnoreMetadata)
        {
            return;
        }

        ReadOnlySpan<byte> resources = data.Slice(header.ResourcesOffset, header.ResourcesLength);
        int pos = 0;
        while (PsdHeader.NextResource(resources, ref pos, out int id, out int offset, out int length))
        {
            if (length == 0 || id is not (PsdResource.IccProfile or PsdResource.Exif or PsdResource.Xmp))
            {
                continue;
            }

            context.ReserveMetadata(length);
            byte[] block = resources.Slice(offset, length).ToArray();
            switch (id)
            {
                case PsdResource.IccProfile:
                    metadata.IccProfile = block;
                    break;
                case PsdResource.Exif:
                    metadata.Exif = block;
                    break;
                default:
                    metadata.Xmp = block;
                    break;
            }
        }
    }
}
