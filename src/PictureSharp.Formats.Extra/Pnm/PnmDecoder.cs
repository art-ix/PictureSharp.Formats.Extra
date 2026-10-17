using System.Buffers;

namespace PictureSharp.Formats.Pnm;

internal sealed class PnmDecoder : IImageFormatDecoder
{
    public static PnmDecoder Instance { get; } = new();

    public ImageInfo Identify(ReadOnlyMemory<byte> data, DecoderOptions options)
    {
        PnmHeader header = PnmHeader.Parse(data.Span);
        return new ImageInfo
        {
            Format = PnmFormat.Descriptor,
            Width = header.Width,
            Height = header.Height,
            HasAlpha = header.HasAlpha,
            BitsPerChannel = header.BitsPerChannel,
        };
    }

    public Image Decode(ReadOnlyMemory<byte> data, ImageDecodeContext context)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        PnmHeader header = PnmHeader.Parse(bytes);
        context.ReservePixels(header.Width, header.Height);

        // Binary samples above maxval are clamped as in Pillow, so the table covers every value the sample size holds.
        byte[] scale = ArrayPool<byte>.Shared.Rent(header.MaxValue < 256 ? 256 : 65536);
        try
        {
            BuildScale(scale, header.MaxValue, header.Tuple == PnmTuple.Bitmap);
            var image = new Image(header.Width, header.Height);
            Span<Rgba32> pixels = image.GetPixelSpan();
            if (header.IsPlain)
            {
                DecodePlain(bytes, header, scale, pixels, context);
            }
            else if (header.Tuple == PnmTuple.Bitmap)
            {
                DecodePackedBitmap(bytes, header, pixels, context);
            }
            else
            {
                DecodeBinary(bytes, header, scale, pixels, context);
            }

            return image;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scale);
        }
    }

    public static ImageFormatException Error(string message) => new(PnmFormat.Descriptor, message);

    // Pillow PpmDecoder: round(value / maxval * 255) in double precision, ties to even; PBM 1 is black.
    private static void BuildScale(Span<byte> scale, int maxValue, bool invert)
    {
        for (int v = 0; v < scale.Length; v++)
        {
            scale[v] = v >= maxValue ? (byte)255 : (byte)Math.Round((double)v / maxValue * 255, MidpointRounding.ToEven);
        }

        if (invert)
        {
            scale[0] = 255;
            scale[1] = 0;
        }
    }

    private static void DecodeBinary(ReadOnlySpan<byte> bytes, PnmHeader header, ReadOnlySpan<byte> scale, Span<Rgba32> pixels, ImageDecodeContext context)
    {
        int sampleSize = header.MaxValue < 256 ? 1 : 2;
        int depth = header.Depth;
        long rowBytes = (long)header.Width * depth * sampleSize;
        if (bytes.Length - header.DataOffset < rowBytes * header.Height)
        {
            throw Error("The Netpbm raster is truncated.");
        }

        int width = header.Width;
        for (int y = 0; y < header.Height; y++)
        {
            context.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> row = bytes.Slice(header.DataOffset + (int)(rowBytes * y), (int)rowBytes);
            Span<Rgba32> line = pixels.Slice(y * width, width);
            for (int x = 0, s = 0; x < width; x++, s += depth)
            {
                int c0 = Sample(row, s, sampleSize, scale);
                line[x] = header.Tuple switch
                {
                    PnmTuple.Gray or PnmTuple.BlackAndWhite => new Rgba32((byte)c0, (byte)c0, (byte)c0),
                    PnmTuple.GrayAlpha or PnmTuple.BlackAndWhiteAlpha => new Rgba32((byte)c0, (byte)c0, (byte)c0, Sample(row, s + 1, sampleSize, scale)),
                    PnmTuple.Rgb => new Rgba32((byte)c0, Sample(row, s + 1, sampleSize, scale), Sample(row, s + 2, sampleSize, scale)),
                    _ => new Rgba32((byte)c0, Sample(row, s + 1, sampleSize, scale), Sample(row, s + 2, sampleSize, scale), Sample(row, s + 3, sampleSize, scale)),
                };
            }
        }
    }

    private static byte Sample(ReadOnlySpan<byte> row, int index, int sampleSize, ReadOnlySpan<byte> scale) =>
        sampleSize == 1 ? scale[row[index]] : scale[(row[index * 2] << 8) | row[index * 2 + 1]];

    private static void DecodePackedBitmap(ReadOnlySpan<byte> bytes, PnmHeader header, Span<Rgba32> pixels, ImageDecodeContext context)
    {
        int width = header.Width;
        int rowBytes = (width + 7) >> 3;
        if (bytes.Length - header.DataOffset < (long)rowBytes * header.Height)
        {
            throw Error("The Netpbm raster is truncated.");
        }

        var black = new Rgba32(0, 0, 0);
        var white = new Rgba32(255, 255, 255);
        for (int y = 0; y < header.Height; y++)
        {
            context.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> row = bytes.Slice(header.DataOffset + rowBytes * y, rowBytes);
            Span<Rgba32> line = pixels.Slice(y * width, width);
            for (int x = 0; x < width; x++)
            {
                line[x] = (row[x >> 3] & (0x80 >> (x & 7))) != 0 ? black : white;
            }
        }
    }

    private static void DecodePlain(ReadOnlySpan<byte> bytes, PnmHeader header, ReadOnlySpan<byte> scale, Span<Rgba32> pixels, ImageDecodeContext context)
    {
        int pos = header.DataOffset;
        int depth = header.Depth;
        int width = header.Width;
        Span<byte> samples = stackalloc byte[3];
        for (int y = 0; y < header.Height; y++)
        {
            context.ThrowIfCancellationRequested();
            Span<Rgba32> line = pixels.Slice(y * width, width);
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < depth; c++)
                {
                    int value = header.Tuple == PnmTuple.Bitmap ? ReadBit(bytes, ref pos) : ReadSample(bytes, ref pos, header.MaxValue);
                    samples[c] = scale[value];
                }

                line[x] = depth == 1 ? new Rgba32(samples[0], samples[0], samples[0]) : new Rgba32(samples[0], samples[1], samples[2]);
            }
        }
    }

    // Plain PBM samples are single '0' or '1' characters; whitespace between them is optional.
    private static int ReadBit(ReadOnlySpan<byte> bytes, ref int pos)
    {
        PnmHeader.SkipWhitespaceAndComments(bytes, ref pos);
        if (pos >= bytes.Length)
        {
            throw Error("The Netpbm raster is truncated.");
        }

        return bytes[pos++] switch
        {
            (byte)'0' => 0,
            (byte)'1' => 1,
            _ => throw Error("The plain PBM raster has an invalid character."),
        };
    }

    private static int ReadSample(ReadOnlySpan<byte> bytes, ref int pos, int maxValue)
    {
        PnmHeader.SkipWhitespaceAndComments(bytes, ref pos);
        int start = pos;
        int value = 0;
        while (pos < bytes.Length && bytes[pos] is >= (byte)'0' and <= (byte)'9')
        {
            value = value * 10 + (bytes[pos] - '0');
            if (value > maxValue)
            {
                throw Error("A plain Netpbm sample exceeds maxval.");
            }

            pos++;
        }

        if (pos == start)
        {
            throw pos >= bytes.Length ? Error("The Netpbm raster is truncated.") : Error("The plain Netpbm raster has an invalid character.");
        }

        return value;
    }
}
