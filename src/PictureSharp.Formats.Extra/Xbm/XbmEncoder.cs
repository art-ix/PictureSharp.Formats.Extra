using System.Text;
using PictureSharp.Formats.Extra.Common;

namespace PictureSharp.Formats.Xbm;

internal sealed class XbmEncoder : IImageFormatEncoder<XbmEncoderOptions>
{
    // X11's bitmap program writes 12 values per line.
    private const int ValuesPerLine = 12;

    public static XbmEncoder Instance { get; } = new();

    public void Encode(Image image, Stream output, XbmEncoderOptions options, CancellationToken cancellationToken)
    {
        Guard.Encodable(image, output);
        ArgumentNullException.ThrowIfNull(options);

        using var buffer = new OutputBuffer(output);
        var header = new StringBuilder();
        header.Append($"#define {options.Name}_width {image.Width}\n#define {options.Name}_height {image.Height}\n");
        if (options.Hotspot is (int x, int y))
        {
            header.Append($"#define {options.Name}_x_hot {x}\n#define {options.Name}_y_hot {y}\n");
        }

        header.Append($"static unsigned char {options.Name}_bits[] = {{\n");
        buffer.Write(Encoding.ASCII.GetBytes(header.ToString()));

        ReadOnlySpan<Rgba32> pixels = image.GetPixelSpan();
        int bytesPerRow = (image.Width + 7) / 8;
        long count = 0, total = (long)bytesPerRow * image.Height;
        for (int row = 0; row < image.Height; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlySpan<Rgba32> source = pixels.Slice(row * image.Width, image.Width);
            for (int i = 0; i < bytesPerRow; i++)
            {
                int value = 0;
                for (int b = 0; b < 8 && i * 8 + b < source.Length; b++)
                {
                    value |= IsBlack(source[i * 8 + b]) ? 1 << b : 0;
                }

                Span<byte> text = buffer.GetSpan(16);
                int at = 0;
                if (count % ValuesPerLine == 0)
                {
                    text[at++] = (byte)' ';
                    text[at++] = (byte)' ';
                    text[at++] = (byte)' ';
                }

                text[at++] = (byte)'0';
                text[at++] = (byte)'x';
                text[at++] = Hex(value >> 4);
                text[at++] = Hex(value & 15);
                count++;
                if (count < total)
                {
                    text[at++] = (byte)',';
                    text[at++] = count % ValuesPerLine == 0 ? (byte)'\n' : (byte)' ';
                }

                buffer.Advance(at);
            }
        }

        buffer.Write("};\n"u8);
        buffer.Flush();
    }

    // Pillow's L conversion (ITU-R BT.601, 16-bit fixed point); a pixel less than half opaque counts as background.
    private static bool IsBlack(Rgba32 px) => px.A >= 128 && (px.R * 19595 + px.G * 38470 + px.B * 7471 + 0x8000) >> 16 < 128;

    private static byte Hex(int digit) => (byte)(digit < 10 ? '0' + digit : 'a' + digit - 10);
}
