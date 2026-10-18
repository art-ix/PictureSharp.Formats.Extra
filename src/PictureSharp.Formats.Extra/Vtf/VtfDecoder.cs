using System.Buffers.Binary;
using PictureSharp.Formats.Extra.Common.BlockCompression;

namespace PictureSharp.Formats.Vtf;

/// <summary>
/// Decodes the largest mip level of the first frame, face and slice, or the surface <see cref="VtfDecoderOptions"/>
/// selects. Bytes are little endian; 4-, 5- and 6-bit fields
/// widen as <c>v * 255 / max</c> (truncating), 16-bit channels as <c>round(v / 257)</c>. Bluescreen formats make pure
/// blue transparent; A8 is black with alpha, I8 grey; UV88 and UVLX8888 store their fields in red, green (and blue).
/// DXT and BC blocks use the DDS decoders.
/// </summary>
internal sealed class VtfDecoder : IImageFormatDecoder
{
    public static VtfDecoder Instance { get; } = new();

    public ImageInfo Identify(ReadOnlyMemory<byte> data, DecoderOptions options)
    {
        VtfHeader header = VtfHeader.Parse(data.Span);
        // The default selection is the header's size; a chosen face needs the face count, which depends on the data.
        (int width, int height) = (header.Width, header.Height);
        if (options.GetFormatOptions<VtfDecoderOptions>() is { } selected)
        {
            VtfSelection selection = header.Select(data.Span, selected);
            (width, height) = (selection.Width, selection.Height);
        }

        return new ImageInfo
        {
            Format = VtfFormat.Descriptor,
            Width = width,
            Height = height,
            HasAlpha = header.HasAlpha,
            BitsPerChannel = header.Format == VtfPixelFormat.Rgba16161616 ? 16 : 8,
        };
    }

    public Image Decode(ReadOnlyMemory<byte> data, ImageDecodeContext context)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        VtfHeader header = VtfHeader.Parse(bytes);
        VtfHeader.ImageSize(header.Format, 1, 1);     // rejects unsupported formats before any allocation
        VtfSelection selection = header.Select(bytes, context.Options.GetFormatOptions<VtfDecoderOptions>());
        (int width, int height) = (selection.Width, selection.Height);
        context.ReservePixels(width, height);
        ReadOnlySpan<byte> surface = header.ImageData(bytes, selection, out _);

        var image = new Image(width, height);
        try
        {
            Span<Rgba32> pixels = image.GetPixelSpan();
            if (IsBlockFormat(header.Format))
            {
                DecodeBlocks(surface, header.Format, width, height, pixels, context);
            }
            else
            {
                DecodePixels(surface, header.Format, width, pixels, context);
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    public static ImageFormatException Error(string message) => new(VtfFormat.Descriptor, message);

    private static bool IsBlockFormat(VtfPixelFormat format) => format is VtfPixelFormat.Dxt1 or VtfPixelFormat.Dxt1OneBitAlpha
        or VtfPixelFormat.Dxt3 or VtfPixelFormat.Dxt5 or VtfPixelFormat.Ati1N or VtfPixelFormat.Ati2N or VtfPixelFormat.StrataBc4
        or VtfPixelFormat.StrataBc5 or VtfPixelFormat.StrataBc6HSigned or VtfPixelFormat.StrataBc6HUnsigned or VtfPixelFormat.StrataBc7;

    private static void DecodeBlocks(ReadOnlySpan<byte> data, VtfPixelFormat format, int width, int height, Span<Rgba32> pixels, ImageDecodeContext context)
    {
        int blocksX = (width + 3) / 4, blocksY = (height + 3) / 4;
        int blockSize = (int)(VtfHeader.ImageSize(format, 4, 4));
        Span<Rgba32> block = stackalloc Rgba32[16];
        for (int by = 0; by < blocksY; by++)
        {
            context.ThrowIfCancellationRequested();
            int rows = Math.Min(4, height - by * 4);
            for (int bx = 0; bx < blocksX; bx++)
            {
                ReadOnlySpan<byte> source = data.Slice((by * blocksX + bx) * blockSize, blockSize);
                switch (format)
                {
                    case VtfPixelFormat.Dxt1 or VtfPixelFormat.Dxt1OneBitAlpha:
                        BcColorBlocks.DecodeBc1(source, block);
                        break;
                    case VtfPixelFormat.Dxt3:
                        BcColorBlocks.DecodeBc2(source, block);
                        break;
                    case VtfPixelFormat.Dxt5:
                        BcColorBlocks.DecodeBc3(source, block);
                        break;
                    case VtfPixelFormat.Ati1N or VtfPixelFormat.StrataBc4:
                        BcColorBlocks.DecodeBc4(source, block, signed: false);
                        break;
                    case VtfPixelFormat.Ati2N or VtfPixelFormat.StrataBc5:
                        BcColorBlocks.DecodeBc5(source, block, signed: false);
                        break;
                    case VtfPixelFormat.StrataBc6HSigned or VtfPixelFormat.StrataBc6HUnsigned:
                        Bc6HBlock.Decode(source, block, format == VtfPixelFormat.StrataBc6HSigned);
                        break;
                    default:
                        Bc7Block.Decode(source, block);
                        break;
                }

                int columns = Math.Min(4, width - bx * 4);
                for (int y = 0; y < rows; y++)
                {
                    block.Slice(y * 4, columns).CopyTo(pixels.Slice((by * 4 + y) * width + bx * 4, columns));
                }
            }
        }
    }

    private static void DecodePixels(ReadOnlySpan<byte> data, VtfPixelFormat format, int width, Span<Rgba32> pixels, ImageDecodeContext context)
    {
        int size = VtfHeader.BytesPerPixel(format);
        for (int i = 0; i < pixels.Length; i++)
        {
            if (i % width == 0)
            {
                context.ThrowIfCancellationRequested();
            }

            ReadOnlySpan<byte> p = data.Slice(i * size, size);
            pixels[i] = format switch
            {
                VtfPixelFormat.Rgba8888 or VtfPixelFormat.Uvwq8888 => new Rgba32(p[0], p[1], p[2], p[3]),
                VtfPixelFormat.Abgr8888 => new Rgba32(p[3], p[2], p[1], p[0]),
                VtfPixelFormat.Argb8888 => new Rgba32(p[3], p[0], p[1], p[2]),   // Source stores "ARGB" as G, B, A, R (as VTFLib reads it)
                VtfPixelFormat.Bgra8888 => new Rgba32(p[2], p[1], p[0], p[3]),
                VtfPixelFormat.Bgrx8888 => new Rgba32(p[2], p[1], p[0]),
                VtfPixelFormat.Rgbx8888 => new Rgba32(p[0], p[1], p[2]),
                VtfPixelFormat.Uvlx8888 => new Rgba32(p[0], p[1], p[2]),
                VtfPixelFormat.Rgb888 => new Rgba32(p[0], p[1], p[2]),
                VtfPixelFormat.Bgr888 => new Rgba32(p[2], p[1], p[0]),
                VtfPixelFormat.Rgb888Bluescreen => Bluescreen(p[0], p[1], p[2]),
                VtfPixelFormat.Bgr888Bluescreen => Bluescreen(p[2], p[1], p[0]),
                VtfPixelFormat.I8 => new Rgba32(p[0], p[0], p[0]),
                VtfPixelFormat.Ia88 => new Rgba32(p[0], p[0], p[0], p[1]),
                VtfPixelFormat.A8 => new Rgba32(0, 0, 0, p[0]),
                VtfPixelFormat.Uv88 => new Rgba32(p[0], p[1], 0),
                VtfPixelFormat.Rgba16161616 => new Rgba32(Reduce(p), Reduce(p[2..]), Reduce(p[4..]), Reduce(p[6..])),
                _ => Packed(format, BinaryPrimitives.ReadUInt16LittleEndian(p)),
            };
        }
    }

    // Source's bluescreen formats key out pure blue.
    private static Rgba32 Bluescreen(byte r, byte g, byte b) => new(r, g, b, r == 0 && g == 0 && b == 255 ? (byte)0 : (byte)255);

    private static byte Reduce(ReadOnlySpan<byte> sample) => (byte)((BinaryPrimitives.ReadUInt16LittleEndian(sample) + 128) / 257);

    /// <summary>16-bit layouts, fields listed from the least significant bit: RGB565 is red first, the BGR ones blue first.</summary>
    private static Rgba32 Packed(VtfPixelFormat format, int v) => format switch
    {
        VtfPixelFormat.Rgb565 => new Rgba32(Widen(v & 31, 31), Widen(v >> 5 & 63, 63), Widen(v >> 11, 31)),
        VtfPixelFormat.Bgr565 => new Rgba32(Widen(v >> 11, 31), Widen(v >> 5 & 63, 63), Widen(v & 31, 31)),
        VtfPixelFormat.Bgrx5551 => new Rgba32(Widen(v >> 10 & 31, 31), Widen(v >> 5 & 31, 31), Widen(v & 31, 31)),
        VtfPixelFormat.Bgra5551 => new Rgba32(Widen(v >> 10 & 31, 31), Widen(v >> 5 & 31, 31), Widen(v & 31, 31), (v & 0x8000) != 0 ? (byte)255 : (byte)0),
        _ => new Rgba32(Widen(v >> 8 & 15, 15), Widen(v >> 4 & 15, 15), Widen(v & 15, 15), Widen(v >> 12, 15)),   // BGRA4444
    };

    private static byte Widen(int value, int max) => (byte)(value * 255 / max);
}
