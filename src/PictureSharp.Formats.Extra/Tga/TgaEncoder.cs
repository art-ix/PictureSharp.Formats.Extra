using System.Buffers.Binary;
using PictureSharp.Formats.Extra.Common;

namespace PictureSharp.Formats.Tga;

internal sealed class TgaEncoder : IImageFormatEncoder<TgaEncoderOptions>
{
    private const int MaxPacket = 128;

    public static TgaEncoder Instance { get; } = new();

    public void Encode(Image image, Stream output, TgaEncoderOptions options, CancellationToken cancellationToken)
    {
        Guard.Encodable(image, output);
        ArgumentNullException.ThrowIfNull(options);
        if (image.Width > ushort.MaxValue || image.Height > ushort.MaxValue)
        {
            throw new NotSupportedException("TGA images are at most 65535 pixels wide and high.");
        }

        ReadOnlySpan<Rgba32> pixels = image.GetPixelSpan();
        (bool alpha, bool gray) = Analyze(pixels);
        int bytesPerPixel = alpha ? 4 : gray ? 1 : 3;
        int imageType = (gray && !alpha ? 3 : 2) | (options.Rle ? 8 : 0);
        bool topDown = options.Origin == TgaOrigin.TopLeft;

        using var buffer = new OutputBuffer(output);
        Span<byte> header = buffer.GetSpan(TgaHeader.Size);
        header.Clear();
        header[2] = (byte)imageType;
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], (ushort)image.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], (ushort)image.Height);
        header[16] = (byte)(bytesPerPixel * 8);
        header[17] = (byte)((alpha ? 8 : 0) | (topDown ? 0x20 : 0));
        buffer.Advance(TgaHeader.Size);

        int width = image.Width;
        for (int i = 0; i < image.Height; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int y = topDown ? i : image.Height - 1 - i;
            ReadOnlySpan<Rgba32> row = pixels.Slice(y * width, width);
            if (options.Rle)
            {
                WriteRleRow(buffer, row, bytesPerPixel);
            }
            else
            {
                foreach (Rgba32 px in row)
                {
                    WritePixel(buffer, px, bytesPerPixel);
                }
            }
        }

        // TGA 2.0 footer without extension or developer areas, as Pillow writes it.
        Span<byte> footer = buffer.GetSpan(26);
        footer[..8].Clear();
        "TRUEVISION-XFILE.\0"u8.CopyTo(footer[8..]);
        buffer.Advance(26);
        buffer.Flush();
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

    private static void WritePixel(OutputBuffer buffer, Rgba32 px, int bytesPerPixel)
    {
        Span<byte> p = buffer.GetSpan(4);
        switch (bytesPerPixel)
        {
            case 1:
                p[0] = px.R;
                break;
            case 3:
                (p[0], p[1], p[2]) = (px.B, px.G, px.R);
                break;
            default:
                (p[0], p[1], p[2], p[3]) = (px.B, px.G, px.R, px.A);
                break;
        }

        buffer.Advance(bytesPerPixel);
    }

    // A run packet for two or more equal pixels, otherwise a raw packet up to the next run; at most 128 pixels each.
    private static void WriteRleRow(OutputBuffer buffer, ReadOnlySpan<Rgba32> row, int bytesPerPixel)
    {
        int i = 0;
        while (i < row.Length)
        {
            int run = 1;
            while (i + run < row.Length && run < MaxPacket && row[i + run] == row[i])
            {
                run++;
            }

            if (run >= 2)
            {
                buffer.Write((byte)(0x80 | (run - 1)));
                WritePixel(buffer, row[i], bytesPerPixel);
                i += run;
                continue;
            }

            int raw = 1;
            while (i + raw < row.Length && raw < MaxPacket && !(i + raw + 1 < row.Length && row[i + raw] == row[i + raw + 1]))
            {
                raw++;
            }

            buffer.Write((byte)(raw - 1));
            for (int k = 0; k < raw; k++)
            {
                WritePixel(buffer, row[i + k], bytesPerPixel);
            }

            i += raw;
        }
    }
}
