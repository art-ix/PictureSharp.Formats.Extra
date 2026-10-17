using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace PictureSharp.Formats.Tga;

internal sealed class TgaDecoder : IImageFormatDecoder
{
    private const int ExtensionAreaSize = 495;
    private const int FooterSize = 26;

    public static TgaDecoder Instance { get; } = new();

    public ImageInfo Identify(ReadOnlyMemory<byte> data, DecoderOptions options)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        TgaHeader header = ReadHeader(bytes);
        return new ImageInfo
        {
            Format = TgaFormat.Descriptor,
            Width = header.Width,
            Height = header.Height,
            HasAlpha = GetAlphaMode(header, bytes) != AlphaMode.None,
        };
    }

    public Image Decode(ReadOnlyMemory<byte> data, ImageDecodeContext context)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        TgaHeader header = ReadHeader(bytes);
        if (header.BaseType == 1 && header.BitsPerPixel != 8)
        {
            throw new NotSupportedException("TGA colour maps with 16-bit indices are not supported.");
        }

        AlphaMode alpha = GetAlphaMode(header, bytes);
        int offset = TgaHeader.Size + header.IdLength;
        int colorMapBytes = header.ColorMapType == 1 ? header.ColorMapLength * header.ColorMapEntrySize : 0;
        if (bytes.Length < offset + colorMapBytes)
        {
            throw Error("The TGA colour map is truncated.");
        }

        context.ReservePixels(header.Width, header.Height);
        Rgba32[]? palette = null;
        try
        {
            if (header.BaseType == 1)
            {
                palette = ArrayPool<Rgba32>.Shared.Rent(256);
                ReadPalette(bytes.Slice(offset, colorMapBytes), header, palette);
            }

            offset += colorMapBytes;
            var image = new Image(header.Width, header.Height);
            Span<Rgba32> pixels = image.GetPixelSpan();
            var reader = new PixelReader(header, alpha, palette);
            if (header.IsRle)
            {
                DecodeRle(bytes[offset..], header, reader, pixels, context);
            }
            else
            {
                DecodeRaw(bytes[offset..], header, reader, pixels, context);
            }

            Orient(pixels, header);
            if (alpha == AlphaMode.Premultiplied)
            {
                Unpremultiply(pixels);
            }

            return image;
        }
        finally
        {
            if (palette is not null)
            {
                ArrayPool<Rgba32>.Shared.Return(palette);
            }
        }
    }

    public static ImageFormatException Error(string message) => new(TgaFormat.Descriptor, message);

    private static TgaHeader ReadHeader(ReadOnlySpan<byte> bytes) =>
        TgaHeader.TryParse(bytes, out TgaHeader header) ? header : throw Error("Not a TGA file or the header is invalid.");

    // Alpha follows Pillow: 32-bit pixels, 16-bit greyscale and 32-bit colour maps carry alpha; 16-bit colour only
    // when the descriptor declares an alpha bit. A TGA 2.0 extension area can declare no alpha or premultiplied alpha.
    private static AlphaMode GetAlphaMode(TgaHeader header, ReadOnlySpan<byte> bytes)
    {
        bool hasAlpha = header.BaseType switch
        {
            1 => header.ColorMapDepth == 32,
            2 => header.BitsPerPixel == 32 || (header.BitsPerPixel == 16 && header.AlphaBits == 1),
            _ => header.BitsPerPixel == 16,
        };
        if (!hasAlpha)
        {
            return AlphaMode.None;
        }

        return ReadAttributesType(bytes) switch
        {
            0 or 1 or 2 => AlphaMode.None,
            4 => AlphaMode.Premultiplied,
            _ => AlphaMode.Straight,
        };
    }

    // The attributes type of the TGA 2.0 extension area, or -1 when there is none. A damaged footer is ignored:
    // it follows the image data and does not affect the pixels.
    private static int ReadAttributesType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < TgaHeader.Size + FooterSize || !bytes[^18..].SequenceEqual("TRUEVISION-XFILE.\0"u8))
        {
            return -1;
        }

        uint extension = BinaryPrimitives.ReadUInt32LittleEndian(bytes[^FooterSize..]);
        if (extension < TgaHeader.Size || extension > (uint)(bytes.Length - FooterSize - ExtensionAreaSize)
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[(int)extension..]) != ExtensionAreaSize)
        {
            return -1;
        }

        return bytes[(int)extension + ExtensionAreaSize - 1];
    }

    private static void ReadPalette(ReadOnlySpan<byte> map, TgaHeader header, Span<Rgba32> palette)
    {
        // Indices below the first entry or past the map are outside the colour map; a pixel using one is corrupt.
        palette[..256].Clear();
        int entrySize = header.ColorMapEntrySize;
        for (int i = 0; i < header.ColorMapLength; i++)
        {
            int index = header.ColorMapFirst + i;
            if (index >= 256)
            {
                break;
            }

            ReadOnlySpan<byte> e = map.Slice(i * entrySize, entrySize);
            palette[index] = entrySize switch
            {
                2 => Bgr15(e),
                3 => new Rgba32(e[2], e[1], e[0]),
                _ => new Rgba32(e[2], e[1], e[0], e[3]),
            };
        }
    }

    // Pillow ImagingUnpackBGR15: value * 255 / 31.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rgba32 Bgr15(ReadOnlySpan<byte> e)
    {
        int pixel = e[0] | (e[1] << 8);
        return new Rgba32((byte)(((pixel >> 10) & 31) * 255 / 31), (byte)(((pixel >> 5) & 31) * 255 / 31), (byte)((pixel & 31) * 255 / 31));
    }

    private static void DecodeRaw(ReadOnlySpan<byte> data, TgaHeader header, in PixelReader reader, Span<Rgba32> pixels, ImageDecodeContext context)
    {
        int size = reader.BytesPerPixel;
        int width = header.Width;
        if (data.Length < (long)pixels.Length * size)
        {
            throw Error("The TGA image data is truncated.");
        }

        for (int y = 0; y < header.Height; y++)
        {
            context.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> row = data.Slice(y * width * size, width * size);
            Span<Rgba32> line = pixels.Slice(y * width, width);
            for (int x = 0; x < width; x++)
            {
                line[x] = reader.Read(row.Slice(x * size, size));
            }
        }
    }

    // Packets may cross rows: TGA 2.0 forbids it, but many writers do it and Pillow accepts it.
    private static void DecodeRle(ReadOnlySpan<byte> data, TgaHeader header, in PixelReader reader, Span<Rgba32> pixels, ImageDecodeContext context)
    {
        int size = reader.BytesPerPixel;
        int pos = 0;
        int i = 0;
        int nextCheck = 0;
        while (i < pixels.Length)
        {
            if (i >= nextCheck)
            {
                context.ThrowIfCancellationRequested();
                nextCheck = i + header.Width;
            }

            if (pos >= data.Length)
            {
                throw Error("The TGA RLE data is truncated.");
            }

            int packet = data[pos++];
            int count = Math.Min((packet & 0x7f) + 1, pixels.Length - i);
            if ((packet & 0x80) != 0)
            {
                if (pos + size > data.Length)
                {
                    throw Error("The TGA RLE data is truncated.");
                }

                pixels.Slice(i, count).Fill(reader.Read(data.Slice(pos, size)));
                pos += size;
            }
            else
            {
                if (pos + count * size > data.Length)
                {
                    throw Error("The TGA RLE data is truncated.");
                }

                for (int k = 0; k < count; k++)
                {
                    pixels[i + k] = reader.Read(data.Slice(pos + k * size, size));
                }

                pos += count * size;
            }

            i += count;
        }
    }

    private static void Orient(Span<Rgba32> pixels, TgaHeader header)
    {
        int width = header.Width;
        int height = header.Height;
        if (header.RightToLeft)
        {
            for (int y = 0; y < height; y++)
            {
                pixels.Slice(y * width, width).Reverse();
            }
        }

        if (!header.TopToBottom)
        {
            Rgba32[] temp = ArrayPool<Rgba32>.Shared.Rent(width);
            try
            {
                Span<Rgba32> row = temp.AsSpan(0, width);
                for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
                {
                    Span<Rgba32> a = pixels.Slice(top * width, width);
                    Span<Rgba32> b = pixels.Slice(bottom * width, width);
                    a.CopyTo(row);
                    b.CopyTo(a);
                    row.CopyTo(b);
                }
            }
            finally
            {
                ArrayPool<Rgba32>.Shared.Return(temp);
            }
        }
    }

    private static void Unpremultiply(Span<Rgba32> pixels)
    {
        foreach (ref Rgba32 px in pixels)
        {
            if (px.A is 0 or 255)
            {
                continue;
            }

            int a = px.A;
            px.R = (byte)Math.Min(255, (px.R * 255 + a / 2) / a);
            px.G = (byte)Math.Min(255, (px.G * 255 + a / 2) / a);
            px.B = (byte)Math.Min(255, (px.B * 255 + a / 2) / a);
        }
    }

    private enum AlphaMode
    {
        None,
        Straight,
        Premultiplied,
    }

    private readonly struct PixelReader(TgaHeader header, AlphaMode alpha, Rgba32[]? palette)
    {
        private readonly int _type = header.BaseType;
        private readonly int _bits = header.BitsPerPixel;
        private readonly byte _opaque = alpha == AlphaMode.None ? (byte)255 : (byte)0;
        private readonly int _first = header.ColorMapFirst;
        private readonly int _end = header.ColorMapFirst + header.ColorMapLength;

        public int BytesPerPixel => (_bits + 7) >> 3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rgba32 Read(ReadOnlySpan<byte> p)
        {
            Rgba32 px;
            switch (_type)
            {
                case 1:
                    int index = p[0];
                    if (index < _first || index >= _end)
                    {
                        throw Error("A TGA pixel uses an index outside the colour map.");
                    }

                    px = palette![index];
                    break;
                case 3:
                    px = new Rgba32(p[0], p[0], p[0], _bits == 16 ? p[1] : (byte)255);
                    break;
                default:
                    px = _bits switch
                    {
                        15 or 16 => Bgr15(p) with { A = (p[1] & 0x80) != 0 ? (byte)255 : (byte)0 },
                        24 => new Rgba32(p[2], p[1], p[0]),
                        _ => new Rgba32(p[2], p[1], p[0], p[3]),
                    };
                    break;
            }

            px.A |= _opaque;
            return px;
        }
    }
}
