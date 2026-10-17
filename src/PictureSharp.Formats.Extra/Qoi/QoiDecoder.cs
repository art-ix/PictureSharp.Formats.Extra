using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace PictureSharp.Formats.Qoi;

internal sealed class QoiDecoder : IImageFormatDecoder
{
    public const int HeaderSize = 14;
    public const int PaddingSize = 8;

    public const byte OpIndex = 0x00;
    public const byte OpDiff = 0x40;
    public const byte OpLuma = 0x80;
    public const byte OpRun = 0xc0;
    public const byte OpRgb = 0xfe;
    public const byte OpRgba = 0xff;
    public const byte Mask2 = 0xc0;

    public static QoiDecoder Instance { get; } = new();

    public ImageInfo Identify(ReadOnlyMemory<byte> data, DecoderOptions options)
    {
        Header header = ReadHeader(data.Span);
        return new ImageInfo { Format = QoiFormat.Descriptor, Width = header.Width, Height = header.Height, HasAlpha = header.Channels == 4 };
    }

    // port: qoi.h qoi_decode
    public Image Decode(ReadOnlyMemory<byte> data, ImageDecodeContext context)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        Header header = ReadHeader(bytes);
        context.ReservePixels(header.Width, header.Height);

        var image = new Image(header.Width, header.Height);
        if (header.ColorSpace == QoiColorSpace.Linear && !context.Options.IgnoreMetadata)
        {
            image.Metadata.Gamma = 1f;
        }

        Span<Rgba32> pixels = image.GetPixelSpan();
        Span<Rgba32> index = stackalloc Rgba32[64];
        index.Clear();
        var px = new Rgba32(0, 0, 0, 255);
        int run = 0;
        int p = HeaderSize;
        int chunksEnd = bytes.Length - PaddingSize;
        int width = header.Width;
        // A 3-channel file can still carry alpha in QOI_OP_RGBA chunks; like qoi.h, the output drops it.
        byte opaque = header.Channels == 3 ? (byte)255 : (byte)0;

        for (int y = 0, i = 0; y < header.Height; y++)
        {
            context.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++, i++)
            {
                if (run > 0)
                {
                    run--;
                }
                else
                {
                    if (p >= chunksEnd)
                    {
                        throw Error("The QOI data ends before the last pixel.");
                    }

                    int b1 = bytes[p++];
                    if (b1 == OpRgb)
                    {
                        if (p + 3 > chunksEnd)
                        {
                            throw Error("The QOI data ends before the last pixel.");
                        }

                        px.R = bytes[p];
                        px.G = bytes[p + 1];
                        px.B = bytes[p + 2];
                        p += 3;
                    }
                    else if (b1 == OpRgba)
                    {
                        if (p + 4 > chunksEnd)
                        {
                            throw Error("The QOI data ends before the last pixel.");
                        }

                        px = new Rgba32(bytes[p], bytes[p + 1], bytes[p + 2], bytes[p + 3]);
                        p += 4;
                    }
                    else if ((b1 & Mask2) == OpIndex)
                    {
                        px = index[b1];
                    }
                    else if ((b1 & Mask2) == OpDiff)
                    {
                        px.R = (byte)(px.R + ((b1 >> 4) & 0x03) - 2);
                        px.G = (byte)(px.G + ((b1 >> 2) & 0x03) - 2);
                        px.B = (byte)(px.B + (b1 & 0x03) - 2);
                    }
                    else if ((b1 & Mask2) == OpLuma)
                    {
                        if (p >= chunksEnd)
                        {
                            throw Error("The QOI data ends before the last pixel.");
                        }

                        int b2 = bytes[p++];
                        int vg = (b1 & 0x3f) - 32;
                        px.R = (byte)(px.R + vg - 8 + ((b2 >> 4) & 0x0f));
                        px.G = (byte)(px.G + vg);
                        px.B = (byte)(px.B + vg - 8 + (b2 & 0x0f));
                    }
                    else
                    {
                        run = b1 & 0x3f;
                    }

                    index[Hash(px)] = px;
                }

                pixels[i] = px;
                pixels[i].A |= opaque;
            }
        }

        return image;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Hash(Rgba32 px) => (px.R * 3 + px.G * 5 + px.B * 7 + px.A * 11) % 64;

    private static Header ReadHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize + PaddingSize || !bytes.StartsWith("qoif"u8))
        {
            throw Error("Not a QOI file or the file is truncated.");
        }

        uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]);
        byte channels = bytes[12];
        byte colorSpace = bytes[13];
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
        {
            throw Error("The QOI image has invalid dimensions.");
        }

        if (channels is not (3 or 4) || colorSpace > 1)
        {
            throw Error("The QOI header has an invalid channel count or colour space.");
        }

        return new Header((int)width, (int)height, channels, (QoiColorSpace)colorSpace);
    }

    private static ImageFormatException Error(string message) => new(QoiFormat.Descriptor, message);

    private readonly record struct Header(int Width, int Height, int Channels, QoiColorSpace ColorSpace);
}
