using System.Buffers;
using System.Buffers.Binary;
using PictureSharp.Formats.Extra.Common;

namespace PictureSharp.Formats.Sgi;

internal sealed class SgiEncoder : IImageFormatEncoder<SgiEncoderOptions>
{
    private const int MaxRun = 127;

    public static SgiEncoder Instance { get; } = new();

    public void Encode(Image image, Stream output, SgiEncoderOptions options, CancellationToken cancellationToken)
    {
        Guard.Encodable(image, output);
        ArgumentNullException.ThrowIfNull(options);
        if (image.Width > ushort.MaxValue || image.Height > ushort.MaxValue)
        {
            throw new NotSupportedException("SGI images are at most 65535 pixels wide and high.");
        }

        ReadOnlySpan<Rgba32> pixels = image.GetPixelSpan();
        (bool alpha, bool gray) = Analyze(pixels);
        int channels = alpha ? 4 : gray ? 1 : 3;
        int width = image.Width, height = image.Height;

        // A literal run of at most 127 samples costs one extra byte, plus the terminating zero.
        byte[] row = ArrayPool<byte>.Shared.Rent(width + width / MaxRun + 2);
        uint[]? lengths = options.Rle ? ArrayPool<uint>.Shared.Rent(height * channels) : null;
        try
        {
            using var buffer = new OutputBuffer(output);
            WriteHeader(buffer, options.Rle, gray && !alpha ? (height == 1 ? 1 : 2) : 3, width, height, channels);
            if (lengths is not null)
            {
                // The offset and length tables precede the rows, so a first pass measures every compressed row.
                long offset = SgiHeader.Size + 8L * height * channels;
                for (int z = 0; z < channels; z++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        lengths[z * height + y] = (uint)Compress(Samples(pixels, width, height, y, z, row), row);
                    }
                }

                for (int i = 0; i < height * channels; i++)
                {
                    if (offset > uint.MaxValue)
                    {
                        throw new NotSupportedException("The compressed SGI image is larger than 4 GiB.");
                    }

                    BinaryPrimitives.WriteUInt32BigEndian(buffer.GetSpan(4), (uint)offset);
                    buffer.Advance(4);
                    offset += lengths[i];
                }

                for (int i = 0; i < height * channels; i++)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(buffer.GetSpan(4), lengths[i]);
                    buffer.Advance(4);
                }
            }

            for (int z = 0; z < channels; z++)
            {
                for (int y = 0; y < height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Samples(pixels, width, height, y, z, row);
                    buffer.Write(lengths is null ? row.AsSpan(row.Length - count) : row.AsSpan(0, Compress(count, row)));
                }
            }

            buffer.Flush();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(row);
            if (lengths is not null)
            {
                ArrayPool<uint>.Shared.Return(lengths);
            }
        }
    }

    private static (bool Alpha, bool Gray) Analyze(ReadOnlySpan<Rgba32> pixels)
    {
        bool alpha = false, gray = true;
        foreach (Rgba32 px in pixels)
        {
            alpha |= px.A != 255;
            gray &= px.R == px.G && px.G == px.B;
        }

        return (alpha, gray);
    }

    private static void WriteHeader(OutputBuffer buffer, bool rle, int dimension, int width, int height, int channels)
    {
        // Magic, storage, bytes per channel, dimension, sizes, pixel range 0-255; the name and padding stay zero.
        Span<byte> header = buffer.GetSpan(SgiHeader.Size);
        header.Clear();
        BinaryPrimitives.WriteUInt16BigEndian(header, SgiHeader.Magic);
        header[2] = rle ? (byte)1 : (byte)0;
        header[3] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], (ushort)dimension);
        BinaryPrimitives.WriteUInt16BigEndian(header[6..], (ushort)width);
        BinaryPrimitives.WriteUInt16BigEndian(header[8..], (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(header[10..], (ushort)channels);
        BinaryPrimitives.WriteInt32BigEndian(header[16..], 255);
        buffer.Advance(SgiHeader.Size);
    }

    /// <summary>Copies channel <paramref name="z"/> of the stored row <paramref name="y"/> (bottom up) to the end of <paramref name="row"/>.</summary>
    private static int Samples(ReadOnlySpan<Rgba32> pixels, int width, int height, int y, int z, byte[] row)
    {
        ReadOnlySpan<Rgba32> source = pixels.Slice((height - 1 - y) * width, width);
        Span<byte> target = row.AsSpan(row.Length - width, width);
        for (int x = 0; x < width; x++)
        {
            Rgba32 px = source[x];
            target[x] = z switch
            {
                0 => px.R,
                1 => px.G,
                2 => px.B,
                _ => px.A,
            };
        }

        return width;
    }

    /// <summary>
    /// Compresses the <paramref name="count"/> samples at the end of <paramref name="row"/> to its start. Runs start at
    /// three equal samples, since a shorter one costs as much as staying in a literal run.
    /// </summary>
    private static int Compress(int count, byte[] row)
    {
        int s = row.Length - count, end = row.Length, d = 0;
        while (s < end)
        {
            int run = 1;
            while (s + run < end && run < MaxRun && row[s + run] == row[s])
            {
                run++;
            }

            if (run >= 3)
            {
                row[d++] = (byte)run;
                row[d++] = row[s];
                s += run;
                continue;
            }

            int literal = 0;
            while (s + literal < end && literal < MaxRun
                && !(s + literal + 2 < end && row[s + literal] == row[s + literal + 1] && row[s + literal] == row[s + literal + 2]))
            {
                literal++;
            }

            // In place: the gap before the samples is larger than the header bytes all literal runs add.
            row[d++] = (byte)(0x80 | literal);
            Array.Copy(row, s, row, d, literal);
            d += literal;
            s += literal;
        }

        row[d++] = 0;
        return d;
    }
}
