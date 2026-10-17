using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace PictureSharp.Formats.Sgi;

/// <summary>
/// Decodes verbatim and RLE SGI images. Channels are planar and rows are stored bottom up; 16-bit samples become
/// <c>round(v / 257)</c>. One channel is grey, two are grey and alpha, three RGB and four RGBA.
/// </summary>
internal sealed class SgiDecoder : IImageFormatDecoder
{
    public static SgiDecoder Instance { get; } = new();

    public ImageInfo Identify(ReadOnlyMemory<byte> data, DecoderOptions options)
    {
        SgiHeader header = SgiHeader.Parse(data.Span);
        return new ImageInfo
        {
            Format = SgiFormat.Descriptor,
            Width = header.Width,
            Height = header.Height,
            HasAlpha = header.Channels is 2 or 4,
            BitsPerChannel = header.BytesPerChannel * 8,
        };
    }

    public Image Decode(ReadOnlyMemory<byte> data, ImageDecodeContext context)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        SgiHeader header = SgiHeader.Parse(bytes);
        if (!header.Rle && (bytes.Length - SgiHeader.Size) / header.BytesPerChannel / header.Width / header.Height < header.Channels)
        {
            throw Error("The SGI image data is truncated.");
        }

        context.ReservePixels(header.Width, header.Height);
        var image = new Image(header.Width, header.Height);
        byte[] row = ArrayPool<byte>.Shared.Rent(header.Width);
        try
        {
            Span<Rgba32> pixels = image.GetPixelSpan();
            Span<byte> samples = MemoryMarshal.AsBytes(pixels);
            if (header.Channels is 1 or 3)
            {
                pixels.Fill(new Rgba32(0, 0, 0, 255));
            }

            int rowBytes = header.Width * header.BytesPerChannel;
            for (int z = 0; z < header.Channels; z++)
            {
                int component = header.Channels == 2 && z == 1 ? 3 : z;
                for (int y = 0; y < header.Height; y++)
                {
                    context.ThrowIfCancellationRequested();
                    Span<byte> target = row.AsSpan(0, header.Width);
                    if (header.Rle)
                    {
                        ExpandRow(bytes, header, y, z, target);
                    }
                    else
                    {
                        Reduce(bytes.Slice(SgiHeader.Size + (z * header.Height + y) * rowBytes, rowBytes), header.BytesPerChannel, target);
                    }

                    int start = (header.Height - 1 - y) * header.Width * 4 + component;
                    for (int x = 0; x < target.Length; x++)
                    {
                        samples[start + x * 4] = target[x];
                    }
                }
            }

            if (header.Channels <= 2)
            {
                foreach (ref Rgba32 px in pixels)
                {
                    px = new Rgba32(px.R, px.R, px.R, px.A);
                }
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(row);
        }
    }

    public static ImageFormatException Error(string message) => new(SgiFormat.Descriptor, message);

    private static void Reduce(ReadOnlySpan<byte> source, int bytesPerChannel, Span<byte> target)
    {
        if (bytesPerChannel == 1)
        {
            source.CopyTo(target);
            return;
        }

        for (int x = 0; x < target.Length; x++)
        {
            target[x] = (byte)((BinaryPrimitives.ReadUInt16BigEndian(source[(2 * x)..]) + 128) / 257);
        }
    }

    /// <summary>
    /// Expands the RLE row <paramref name="y"/> of channel <paramref name="z"/>. The offset table lists every row of
    /// channel 0 first, then channel 1; the length table follows it. A row stops at a zero count or when it is full.
    /// </summary>
    private static void ExpandRow(ReadOnlySpan<byte> data, SgiHeader header, int y, int z, Span<byte> target)
    {
        long entries = (long)header.Height * header.Channels;
        long index = (long)z * header.Height + y;
        if (data.Length - SgiHeader.Size < entries * 8)
        {
            throw Error("The SGI RLE offset table is truncated.");
        }

        uint offset = BinaryPrimitives.ReadUInt32BigEndian(data[(int)(SgiHeader.Size + index * 4)..]);
        uint length = BinaryPrimitives.ReadUInt32BigEndian(data[(int)(SgiHeader.Size + (entries + index) * 4)..]);
        if (offset > data.Length || length > data.Length - offset)
        {
            throw Error("An SGI RLE row lies outside the file.");
        }

        ReadOnlySpan<byte> source = data.Slice((int)offset, (int)length);
        int size = header.BytesPerChannel;
        int s = 0, x = 0;
        while (x < target.Length)
        {
            if (source.Length - s < size)
            {
                throw Error("An SGI RLE row is shorter than the image width.");
            }

            int code = size == 1 ? source[s] : BinaryPrimitives.ReadUInt16BigEndian(source[s..]);
            s += size;
            int count = code & 0x7f;
            if (count == 0)
            {
                throw Error("An SGI RLE row is shorter than the image width.");
            }

            if (count > target.Length - x)
            {
                throw Error("An SGI RLE run passes the end of the row.");
            }

            if ((code & 0x80) != 0)
            {
                if (source.Length - s < count * size)
                {
                    throw Error("An SGI RLE literal run is truncated.");
                }

                Reduce(source.Slice(s, count * size), size, target.Slice(x, count));
                s += count * size;
            }
            else
            {
                if (source.Length - s < size)
                {
                    throw Error("An SGI RLE repeat run is truncated.");
                }

                byte value = size == 1 ? source[s] : (byte)((BinaryPrimitives.ReadUInt16BigEndian(source[s..]) + 128) / 257);
                target.Slice(x, count).Fill(value);
                s += size;
            }

            x += count;
        }
    }
}
