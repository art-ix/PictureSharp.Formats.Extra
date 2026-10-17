using System.Buffers;

namespace PictureSharp.Formats.SunRaster;

/// <summary>
/// Decodes Sun Raster images. Rows are padded to 16 bits; the RLE stream covers the padding as well and its runs may
/// cross rows. Without a colour map 1 bits are black and 4- and 8-bit pixels grey; the first byte of a 32-bit pixel is
/// padding, as in the specification and Netpbm. Type 3 (RGB) stores colours as RGB, every other type as BGR.
/// </summary>
internal sealed class SunRasterDecoder : IImageFormatDecoder
{
    public static SunRasterDecoder Instance { get; } = new();

    public ImageInfo Identify(ReadOnlyMemory<byte> data, DecoderOptions options)
    {
        SunRasterHeader header = SunRasterHeader.Parse(data.Span);
        return new ImageInfo
        {
            Format = SunRasterFormat.Descriptor,
            Width = header.Width,
            Height = header.Height,
            BitsPerChannel = header.Depth < 8 ? header.Depth : 8,
        };
    }

    public Image Decode(ReadOnlyMemory<byte> data, ImageDecodeContext context)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        SunRasterHeader header = SunRasterHeader.Parse(bytes);
        ReadOnlySpan<byte> body = bytes[header.DataOffset..];
        bool rle = header.Type == SunRasterHeader.ByteEncoded;
        if (!rle && body.Length / header.Stride < header.Height)
        {
            throw Error("The Sun Raster image data is truncated.");
        }

        context.ReservePixels(header.Width, header.Height);
        int stride = (int)header.Stride;
        Span<Rgba32> palette = stackalloc Rgba32[256];
        int colors = ReadPalette(bytes.Slice(SunRasterHeader.Size, header.ColorMapLength), header, palette);

        var image = new Image(header.Width, header.Height);
        byte[] row = ArrayPool<byte>.Shared.Rent(stride);
        try
        {
            Span<Rgba32> pixels = image.GetPixelSpan();
            var reader = new RleReader(body);
            for (int y = 0; y < header.Height; y++)
            {
                context.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> source;
                if (rle)
                {
                    reader.Read(row.AsSpan(0, stride));
                    source = row.AsSpan(0, stride);
                }
                else
                {
                    source = body.Slice(y * stride, stride);
                }

                Convert(source, header, palette[..colors], pixels.Slice(y * header.Width, header.Width));
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

    public static ImageFormatException Error(string message) => new(SunRasterFormat.Descriptor, message);

    /// <summary>Fills <paramref name="palette"/> for indexed depths and returns the number of indices allowed.</summary>
    private static int ReadPalette(ReadOnlySpan<byte> map, SunRasterHeader header, Span<Rgba32> palette)
    {
        if (header.Depth > 8)
        {
            return 0;
        }

        if (map.IsEmpty)
        {
            // No map: 1 bits are black, 4- and 8-bit values grey.
            int count = 1 << header.Depth;
            int scale = 255 / (count - 1);
            for (int i = 0; i < count; i++)
            {
                byte v = (byte)(header.Depth == 1 ? 255 - i * 255 : i * scale);
                palette[i] = new Rgba32(v, v, v);
            }

            return count;
        }

        // The map is planar: all reds, then all greens, then all blues.
        int colors = header.ColorCount;
        for (int i = 0; i < colors; i++)
        {
            palette[i] = new Rgba32(map[i], map[colors + i], map[2 * colors + i]);
        }

        return colors;
    }

    private static void Convert(ReadOnlySpan<byte> source, SunRasterHeader header, ReadOnlySpan<Rgba32> palette, Span<Rgba32> target)
    {
        int width = target.Length;
        switch (header.Depth)
        {
            case 1 or 4 or 8:
                int depth = header.Depth, perByte = 8 / depth, mask = (1 << depth) - 1;
                for (int x = 0; x < width; x++)
                {
                    int shift = 8 - depth * (x % perByte + 1);
                    int index = (source[x / perByte] >> shift) & mask;
                    if (index >= palette.Length)
                    {
                        throw Error("A Sun Raster pixel refers to a colour outside the colour map.");
                    }

                    target[x] = palette[index];
                }

                break;
            default:
                int size = header.Depth / 8, first = size - 3;
                bool rgb = header.Type == SunRasterHeader.Rgb;
                for (int x = 0; x < width; x++)
                {
                    ReadOnlySpan<byte> p = source.Slice(x * size + first, 3);
                    target[x] = rgb ? new Rgba32(p[0], p[1], p[2]) : new Rgba32(p[2], p[1], p[0]);
                }

                break;
        }
    }

    /// <summary>
    /// The byte-encoded stream: 0x80 0x00 is a literal 0x80, 0x80 n v repeats v n + 1 times and any other byte is
    /// itself. A run may continue in the next row; runs past the last row are ignored.
    /// </summary>
    private ref struct RleReader(ReadOnlySpan<byte> source)
    {
        private readonly ReadOnlySpan<byte> _source = source;
        private int _pos;
        private int _pending;
        private byte _value;

        public void Read(Span<byte> row)
        {
            int d = 0;
            while (d < row.Length)
            {
                if (_pending > 0)
                {
                    int count = Math.Min(_pending, row.Length - d);
                    row.Slice(d, count).Fill(_value);
                    d += count;
                    _pending -= count;
                    continue;
                }

                if (_pos >= _source.Length)
                {
                    throw Error("The Sun Raster RLE data is truncated.");
                }

                byte b = _source[_pos++];
                if (b != 0x80)
                {
                    row[d++] = b;
                    continue;
                }

                if (_pos >= _source.Length)
                {
                    throw Error("The Sun Raster RLE data is truncated.");
                }

                int n = _source[_pos++];
                if (n == 0)
                {
                    row[d++] = 0x80;
                    continue;
                }

                if (_pos >= _source.Length)
                {
                    throw Error("The Sun Raster RLE data is truncated.");
                }

                _value = _source[_pos++];
                _pending = n + 1;
            }
        }
    }
}
