using System.Runtime.CompilerServices;
using System.Text;
using PictureSharp.Formats.Extra.Common;

namespace PictureSharp.Formats.Pnm;

internal sealed class PnmEncoder : IImageFormatEncoder<PnmEncoderOptions>
{
    // Netpbm recommends plain rasters with lines of at most 70 characters.
    private const int PlainLineLength = 70;

    public static PnmEncoder Instance { get; } = new();

    public void Encode(Image image, Stream output, PnmEncoderOptions options, CancellationToken cancellationToken)
    {
        Guard.Encodable(image, output);
        ArgumentNullException.ThrowIfNull(options);
        if (options is { Kind: PnmKind.Pam, Plain: true })
        {
            throw new ArgumentException("PAM has no plain variant.", nameof(options));
        }

        ReadOnlySpan<Rgba32> pixels = image.GetPixelSpan();
        (bool alpha, bool gray) = Analyze(pixels);
        PnmKind kind = options.Kind is not PnmKind.Auto ? options.Kind
            : alpha ? PnmKind.Pam
            : gray ? PnmKind.Graymap
            : PnmKind.Pixmap;
        bool plain = options.Plain && kind != PnmKind.Pam;

        using var buffer = new OutputBuffer(output);
        int depth = kind switch
        {
            PnmKind.Pam => (gray ? 1 : 3) + (alpha ? 1 : 0),
            PnmKind.Pixmap => 3,
            _ => 1,
        };
        WriteHeader(buffer, kind, plain, image.Width, image.Height, depth);

        if (kind == PnmKind.Bitmap)
        {
            WriteBitmap(buffer, pixels, image.Width, plain, cancellationToken);
        }
        else if (plain)
        {
            WritePlain(buffer, pixels, image.Width, depth, cancellationToken);
        }
        else
        {
            WriteBinary(buffer, pixels, image.Width, depth, cancellationToken);
        }

        buffer.Flush();
    }

    // Pillow's L conversion (ITU-R BT.601, 16-bit fixed point); grey pixels map to themselves.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Luma(Rgba32 px) => (byte)((px.R * 19595 + px.G * 38470 + px.B * 7471 + 0x8000) >> 16);

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

    private static void WriteHeader(OutputBuffer buffer, PnmKind kind, bool plain, int width, int height, int depth)
    {
        string header = kind switch
        {
            PnmKind.Pam => $"P7\nWIDTH {width}\nHEIGHT {height}\nDEPTH {depth}\nMAXVAL 255\nTUPLTYPE {depth switch
            {
                1 => "GRAYSCALE",
                2 => "GRAYSCALE_ALPHA",
                3 => "RGB",
                _ => "RGB_ALPHA",
            }}\nENDHDR\n",
            PnmKind.Bitmap => $"P{(plain ? 1 : 4)}\n{width} {height}\n",
            PnmKind.Graymap => $"P{(plain ? 2 : 5)}\n{width} {height}\n255\n",
            _ => $"P{(plain ? 3 : 6)}\n{width} {height}\n255\n",
        };
        buffer.Write(Encoding.ASCII.GetBytes(header));
    }

    private static void WriteBinary(OutputBuffer buffer, ReadOnlySpan<Rgba32> pixels, int width, int depth, CancellationToken cancellationToken)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            if (i % width == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            Rgba32 px = pixels[i];
            Span<byte> out4 = buffer.GetSpan(4);
            switch (depth)
            {
                case 1:
                    out4[0] = Luma(px);
                    break;
                case 2:
                    out4[0] = Luma(px);
                    out4[1] = px.A;
                    break;
                case 3:
                    out4[0] = px.R;
                    out4[1] = px.G;
                    out4[2] = px.B;
                    break;
                default:
                    out4[0] = px.R;
                    out4[1] = px.G;
                    out4[2] = px.B;
                    out4[3] = px.A;
                    break;
            }

            buffer.Advance(depth);
        }
    }

    private static void WritePlain(OutputBuffer buffer, ReadOnlySpan<Rgba32> pixels, int width, int depth, CancellationToken cancellationToken)
    {
        int column = 0;
        Span<byte> samples = stackalloc byte[3];
        for (int i = 0; i < pixels.Length; i++)
        {
            if (i % width == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            Rgba32 px = pixels[i];
            if (depth == 1)
            {
                samples[0] = Luma(px);
            }
            else
            {
                (samples[0], samples[1], samples[2]) = (px.R, px.G, px.B);
            }

            for (int c = 0; c < depth; c++)
            {
                WritePlainSample(buffer, samples[c], ref column);
            }
        }

        buffer.Write((byte)'\n');
    }

    private static void WritePlainSample(OutputBuffer buffer, byte value, ref int column)
    {
        int digits = value >= 100 ? 3 : value >= 10 ? 2 : 1;
        if (column > 0)
        {
            if (column + 1 + digits > PlainLineLength)
            {
                buffer.Write((byte)'\n');
                column = 0;
            }
            else
            {
                buffer.Write((byte)' ');
                column++;
            }
        }

        Span<byte> text = buffer.GetSpan(3);
        value.TryFormat(text, out int written);
        buffer.Advance(written);
        column += written;
    }

    private static void WriteBitmap(OutputBuffer buffer, ReadOnlySpan<Rgba32> pixels, int width, bool plain, CancellationToken cancellationToken)
    {
        int height = pixels.Length / width;
        int column = 0;
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlySpan<Rgba32> row = pixels.Slice(y * width, width);
            int packed = 0;
            for (int x = 0; x < width; x++)
            {
                // PBM stores 1 for black.
                int bit = Luma(row[x]) < 128 ? 1 : 0;
                if (plain)
                {
                    if (column == PlainLineLength)
                    {
                        buffer.Write((byte)'\n');
                        column = 0;
                    }

                    buffer.Write((byte)('0' + bit));
                    column++;
                    continue;
                }

                packed |= bit << (7 - (x & 7));
                if ((x & 7) == 7 || x == width - 1)
                {
                    buffer.Write((byte)packed);
                    packed = 0;
                }
            }
        }

        if (plain)
        {
            buffer.Write((byte)'\n');
        }
    }
}
