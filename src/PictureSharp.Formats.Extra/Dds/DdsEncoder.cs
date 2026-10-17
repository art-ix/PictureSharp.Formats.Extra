using System.Buffers.Binary;
using PictureSharp.Formats.Extra.Common;

namespace PictureSharp.Formats.Dds;

internal sealed class DdsEncoder : IImageFormatEncoder<DdsEncoderOptions>
{
    // DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PITCH | DDSD_PIXELFORMAT
    private const uint HeaderFlags = 0x1 | 0x2 | 0x4 | 0x8 | 0x1000;
    private const uint CapsTexture = 0x1000;

    public static DdsEncoder Instance { get; } = new();

    public void Encode(Image image, Stream output, DdsEncoderOptions options, CancellationToken cancellationToken)
    {
        Guard.Encodable(image, output);
        ArgumentNullException.ThrowIfNull(options);
        if ((long)image.Width * 4 > uint.MaxValue)
        {
            throw new NotSupportedException("The DDS row pitch would not fit in 32 bits.");
        }

        ReadOnlySpan<Rgba32> pixels = image.GetPixelSpan();
        bool alpha = options.PixelFormat switch
        {
            DdsPixelFormat.Bgra32 => true,
            DdsPixelFormat.Bgrx32 => false,
            _ => HasTransparency(pixels),
        };

        using var buffer = new OutputBuffer(output);
        Span<byte> header = buffer.GetSpan(DdsHeader.Size);
        header.Clear();
        "DDS "u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 124);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], HeaderFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)image.Height);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], (uint)image.Width);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], (uint)image.Width * 4);
        Span<byte> pf = header.Slice(76, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(pf, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(pf[4..], DdsHeader.Rgb | (alpha ? DdsHeader.AlphaPixels : 0));
        BinaryPrimitives.WriteUInt32LittleEndian(pf[12..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(pf[16..], 0x00ff0000);
        BinaryPrimitives.WriteUInt32LittleEndian(pf[20..], 0x0000ff00);
        BinaryPrimitives.WriteUInt32LittleEndian(pf[24..], 0x000000ff);
        BinaryPrimitives.WriteUInt32LittleEndian(pf[28..], alpha ? 0xff000000 : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header[108..], CapsTexture);
        buffer.Advance(DdsHeader.Size);

        int width = image.Width;
        for (int y = 0; y < image.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (Rgba32 px in pixels.Slice(y * width, width))
            {
                Span<byte> p = buffer.GetSpan(4);
                (p[0], p[1], p[2], p[3]) = (px.B, px.G, px.R, alpha ? px.A : (byte)255);
                buffer.Advance(4);
            }
        }

        buffer.Flush();
    }

    private static bool HasTransparency(ReadOnlySpan<Rgba32> pixels)
    {
        foreach (Rgba32 px in pixels)
        {
            if (px.A != 255)
            {
                return true;
            }
        }

        return false;
    }
}
