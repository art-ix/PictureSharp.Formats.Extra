using System.Buffers.Binary;
using PictureSharp.Formats.Extra.Common;

namespace PictureSharp.Formats.Qoi;

internal sealed class QoiEncoder : IImageFormatEncoder<QoiEncoderOptions>
{
    public static QoiEncoder Instance { get; } = new();

    // port: qoi.h qoi_encode
    public void Encode(Image image, Stream output, QoiEncoderOptions options, CancellationToken cancellationToken)
    {
        Guard.Encodable(image, output);
        ArgumentNullException.ThrowIfNull(options);
        ReadOnlySpan<Rgba32> pixels = image.GetPixelSpan();
        bool alpha = options.IncludeAlpha ?? HasTransparency(pixels);
        QoiColorSpace colorSpace = options.ColorSpace ?? (image.Metadata.Gamma == 1f ? QoiColorSpace.Linear : QoiColorSpace.Srgb);

        using var buffer = new OutputBuffer(output);
        Span<byte> header = buffer.GetSpan(QoiDecoder.HeaderSize);
        "qoif"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header[8..], (uint)image.Height);
        header[12] = alpha ? (byte)4 : (byte)3;
        header[13] = (byte)colorSpace;
        buffer.Advance(QoiDecoder.HeaderSize);

        Span<Rgba32> index = stackalloc Rgba32[64];
        index.Clear();
        var prev = new Rgba32(0, 0, 0, 255);
        int run = 0;
        int width = image.Width;
        int last = pixels.Length - 1;

        for (int i = 0; i < pixels.Length; i++)
        {
            if (i % width == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            Rgba32 px = pixels[i];
            if (!alpha)
            {
                px.A = 255;
            }

            if (px == prev)
            {
                run++;
                if (run == 62 || i == last)
                {
                    buffer.Write((byte)(QoiDecoder.OpRun | (run - 1)));
                    run = 0;
                }

                continue;
            }

            if (run > 0)
            {
                buffer.Write((byte)(QoiDecoder.OpRun | (run - 1)));
                run = 0;
            }

            int indexPos = QoiDecoder.Hash(px);
            if (index[indexPos] == px)
            {
                buffer.Write((byte)(QoiDecoder.OpIndex | indexPos));
            }
            else
            {
                index[indexPos] = px;
                Span<byte> chunk = buffer.GetSpan(5);
                if (px.A == prev.A)
                {
                    // signed char differences, as in qoi.h; vg_r and vg_b are int and do not wrap.
                    int vr = (sbyte)(px.R - prev.R);
                    int vg = (sbyte)(px.G - prev.G);
                    int vb = (sbyte)(px.B - prev.B);
                    int vgR = vr - vg;
                    int vgB = vb - vg;

                    if (vr is > -3 and < 2 && vg is > -3 and < 2 && vb is > -3 and < 2)
                    {
                        chunk[0] = (byte)(QoiDecoder.OpDiff | ((vr + 2) << 4) | ((vg + 2) << 2) | (vb + 2));
                        buffer.Advance(1);
                    }
                    else if (vgR is > -9 and < 8 && vg is > -33 and < 32 && vgB is > -9 and < 8)
                    {
                        chunk[0] = (byte)(QoiDecoder.OpLuma | (vg + 32));
                        chunk[1] = (byte)(((vgR + 8) << 4) | (vgB + 8));
                        buffer.Advance(2);
                    }
                    else
                    {
                        chunk[0] = QoiDecoder.OpRgb;
                        chunk[1] = px.R;
                        chunk[2] = px.G;
                        chunk[3] = px.B;
                        buffer.Advance(4);
                    }
                }
                else
                {
                    chunk[0] = QoiDecoder.OpRgba;
                    chunk[1] = px.R;
                    chunk[2] = px.G;
                    chunk[3] = px.B;
                    chunk[4] = px.A;
                    buffer.Advance(5);
                }
            }

            prev = px;
        }

        buffer.Write([0, 0, 0, 0, 0, 0, 0, 1]);
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
