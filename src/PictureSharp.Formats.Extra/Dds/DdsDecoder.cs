using System.Buffers.Binary;
using System.Numerics;
using PictureSharp.Formats.Extra.Common.BlockCompression;

namespace PictureSharp.Formats.Dds;

internal sealed class DdsDecoder : IImageFormatDecoder
{
    private const int PaletteSize = 1024;

    public static DdsDecoder Instance { get; } = new();

    public ImageInfo Identify(ReadOnlyMemory<byte> data, DecoderOptions options)
    {
        DdsHeader header = DdsHeader.Parse(data.Span);
        DdsSurface surface = DdsSurface.Resolve(header);
        DdsSelection selection = header.Select(surface, options.GetFormatOptions<DdsDecoderOptions>());
        return new ImageInfo
        {
            Format = DdsFormat.Descriptor,
            Width = selection.Width,
            Height = selection.Height,
            HasAlpha = surface.HasAlpha,
        };
    }

    public Image Decode(ReadOnlyMemory<byte> data, ImageDecodeContext context)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        DdsHeader header = DdsHeader.Parse(bytes);
        DdsSurface surface = DdsSurface.Resolve(header);
        DdsSelection selection = header.Select(surface, context.Options.GetFormatOptions<DdsDecoderOptions>());
        (int width, int height) = (selection.Width, selection.Height);
        ReadOnlySpan<byte> pixelData = bytes[header.DataOffset..];
        ReadOnlySpan<byte> palette = default;
        if (surface.Kind == DdsSurfaceKind.Palette8)
        {
            if (pixelData.Length < PaletteSize)
            {
                throw Error("The DDS palette is truncated.");
            }

            palette = pixelData[..PaletteSize];
            pixelData = pixelData[PaletteSize..];
        }

        if (selection.Offset > pixelData.Length)
        {
            throw Error("The DDS surface data is truncated.");
        }

        pixelData = pixelData[(int)selection.Offset..];
        context.ReservePixels(width, height);

        var image = new Image(width, height);
        try
        {
            Span<Rgba32> pixels = image.GetPixelSpan();
            if (surface.IsBlockCompressed)
            {
                DecodeBlocks(pixelData, surface, width, height, pixels, context);
            }
            else
            {
                DecodeUncompressed(pixelData, palette, surface, width, height, pixels, context);
            }

            if (surface.Opaque)
            {
                foreach (ref Rgba32 px in pixels)
                {
                    px.A = 255;
                }
            }
            else if (surface.Premultiplied)
            {
                Unpremultiply(pixels);
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    public static ImageFormatException Error(string message) => new(DdsFormat.Descriptor, message);

    private static void DecodeBlocks(ReadOnlySpan<byte> data, DdsSurface surface, int width, int height, Span<Rgba32> pixels, ImageDecodeContext context)
    {
        int blocksX = (int)(((long)width + 3) / 4);
        int blocksY = (int)(((long)height + 3) / 4);
        int blockSize = surface.BlockSize;
        if (data.Length / blockSize / blocksX < blocksY)
        {
            throw Error("The DDS block data is truncated.");
        }

        Span<Rgba32> block = stackalloc Rgba32[16];
        for (int by = 0; by < blocksY; by++)
        {
            context.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> row = data.Slice(by * blocksX * blockSize, blocksX * blockSize);
            int rows = Math.Min(4, height - by * 4);
            for (int bx = 0; bx < blocksX; bx++)
            {
                DecodeBlock(row.Slice(bx * blockSize, blockSize), surface.Kind, block);
                int columns = Math.Min(4, width - bx * 4);
                for (int y = 0; y < rows; y++)
                {
                    block.Slice(y * 4, columns).CopyTo(pixels.Slice((by * 4 + y) * width + bx * 4, columns));
                }
            }
        }
    }

    private static void DecodeBlock(ReadOnlySpan<byte> source, DdsSurfaceKind kind, Span<Rgba32> block)
    {
        switch (kind)
        {
            case DdsSurfaceKind.Bc1:
                BcColorBlocks.DecodeBc1(source, block);
                break;
            case DdsSurfaceKind.Bc2:
                BcColorBlocks.DecodeBc2(source, block);
                break;
            case DdsSurfaceKind.Bc3:
                BcColorBlocks.DecodeBc3(source, block);
                break;
            case DdsSurfaceKind.Bc4 or DdsSurfaceKind.Bc4Signed:
                BcColorBlocks.DecodeBc4(source, block, kind == DdsSurfaceKind.Bc4Signed);
                break;
            case DdsSurfaceKind.Bc5 or DdsSurfaceKind.Bc5Signed:
                BcColorBlocks.DecodeBc5(source, block, kind == DdsSurfaceKind.Bc5Signed);
                break;
            case DdsSurfaceKind.Bc6H or DdsSurfaceKind.Bc6HSigned:
                Bc6HBlock.Decode(source, block, kind == DdsSurfaceKind.Bc6HSigned);
                break;
            default:
                Bc7Block.Decode(source, block);
                break;
        }
    }

    private static void DecodeUncompressed(ReadOnlySpan<byte> data, ReadOnlySpan<byte> palette, DdsSurface surface, int width, int height,
        Span<Rgba32> pixels, ImageDecodeContext context)
    {
        int bytesPerPixel = surface.BytesPerPixel;

        // Rows are tightly packed, as Pillow reads them.
        if (data.Length / bytesPerPixel / width < height)
        {
            throw Error("The DDS pixel data is truncated.");
        }

        var masked = new MaskedReader(surface);
        int stride = width * bytesPerPixel;
        for (int y = 0; y < height; y++)
        {
            context.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> row = data.Slice(y * stride, stride);
            Span<Rgba32> line = pixels.Slice(y * width, width);
            switch (surface.Kind)
            {
                case DdsSurfaceKind.Luminance8:
                    for (int x = 0; x < width; x++)
                    {
                        line[x] = new Rgba32(row[x], row[x], row[x]);
                    }

                    break;
                case DdsSurfaceKind.LuminanceAlpha8:
                    for (int x = 0; x < width; x++)
                    {
                        byte l = row[2 * x];
                        line[x] = new Rgba32(l, l, l, row[2 * x + 1]);
                    }

                    break;
                case DdsSurfaceKind.Palette8:
                    for (int x = 0; x < width; x++)
                    {
                        ReadOnlySpan<byte> entry = palette.Slice(row[x] * 4, 4);
                        line[x] = new Rgba32(entry[0], entry[1], entry[2], entry[3]);
                    }

                    break;
                default:
                    for (int x = 0; x < width; x++)
                    {
                        line[x] = masked.Read(row.Slice(x * bytesPerPixel, bytesPerPixel));
                    }

                    break;
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

    /// <summary>
    /// Reads pixels described by bit masks. Each channel is scaled as Pillow's DdsRgbDecoder does:
    /// <c>int(value / max * 255)</c> in double precision, where max is the mask shifted to bit 0.
    /// </summary>
    private readonly struct MaskedReader(DdsSurface surface)
    {
        private readonly Channel _red = new(surface.RedMask);
        private readonly Channel _green = new(surface.GreenMask);
        private readonly Channel _blue = new(surface.BlueMask);
        private readonly Channel _alpha = new(surface.AlphaMask);
        private readonly bool _gray = surface.Gray;
        private readonly bool _hasAlpha = surface.AlphaMask != 0;

        public Rgba32 Read(ReadOnlySpan<byte> p)
        {
            uint value = p.Length switch
            {
                1 => p[0],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(p),
                3 => (uint)(p[0] | (p[1] << 8) | (p[2] << 16)),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(p),
            };

            byte r = _red.Scale(value);
            byte a = _hasAlpha ? _alpha.Scale(value) : (byte)255;
            return _gray ? new Rgba32(r, r, r, a) : new Rgba32(r, _green.Scale(value), _blue.Scale(value), a);
        }
    }

    private readonly struct Channel
    {
        private readonly uint _mask;
        private readonly int _shift;
        private readonly double _max;

        public Channel(uint mask)
        {
            _mask = mask;
            _shift = mask == 0 ? 0 : BitOperations.TrailingZeroCount(mask);
            _max = mask >> _shift;
        }

        public byte Scale(uint value)
        {
            if (_mask == 0)
            {
                return 0;
            }

            uint v = (value & _mask) >> _shift;
            return _max == 255 ? (byte)v : (byte)Math.Min(255, (int)(v / _max * 255));
        }
    }
}
