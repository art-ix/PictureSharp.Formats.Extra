using PictureSharp.Formats.Extra.Common;

namespace PictureSharp.Formats.Xbm;

/// <summary>
/// The XBM header: <c>#define</c> lines for the size and hotspot, then the bit array declaration. A declaration with
/// <c>short</c> is an X10 bitmap of 16-bit words, any other an X11 bitmap of bytes.
/// </summary>
internal readonly record struct XbmParser(int Width, int Height, bool IsX10, int DataOffset)
{
    private const int MaxDimension = 1 << 24;

    /// <summary>Rows are padded to a whole byte (X11) or word (X10).</summary>
    public int UnitsPerRow => IsX10 ? (Width + 15) / 16 : (Width + 7) / 8;

    public static bool HasSignature(ReadOnlySpan<byte> data)
    {
        var reader = new CSourceReader(data);
        return reader.SkipSpace() && data[reader.Position..].StartsWith("#define"u8);
    }

    public static XbmParser Parse(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data))
        {
            throw XbmDecoder.Error("Not an XBM file.");
        }

        var reader = new CSourceReader(data);
        int width = -1, height = -1;
        while (true)
        {
            if (!reader.SkipSpace() || reader.AtEnd)
            {
                throw XbmDecoder.Error("The XBM file has no bit array.");
            }

            if (reader.Current != '#')
            {
                break;
            }

            // "#define <name>_width <n>"; other directives and names are skipped.
            reader.Advance();
            reader.SkipBlanks();
            bool define = reader.ReadWord().SequenceEqual("define"u8);
            reader.SkipBlanks();
            // ImageMagick writes the file name as is, so a name may hold characters C does not allow.
            ReadOnlySpan<byte> name = reader.ReadToken();
            reader.SkipBlanks();
            ReadOnlySpan<byte> value = reader.ReadToken();
            reader.SkipLine();
            bool isWidth = name.EndsWith("_width"u8) || name.SequenceEqual("width"u8);
            bool isHeight = name.EndsWith("_height"u8) || name.SequenceEqual("height"u8);
            if (!define || !(isWidth || isHeight))
            {
                continue;
            }

            if (!CSourceReader.TryParseNumber(value, MaxDimension, out int number) || number == 0)
            {
                throw XbmDecoder.Error("The XBM size is invalid.");
            }

            if (isWidth)
            {
                width = number;
            }
            else
            {
                height = number;
            }
        }

        if (width < 0 || height < 0)
        {
            throw XbmDecoder.Error("The XBM file does not define its width and height.");
        }

        // The declaration, e.g. "static unsigned char name_bits[] = {".
        int start = reader.Position;
        int brace = data[start..].IndexOf((byte)'{');
        if (brace < 0)
        {
            throw XbmDecoder.Error("The XBM file has no bit array.");
        }

        ReadOnlySpan<byte> declaration = data.Slice(start, brace);
        return new XbmParser(width, height, declaration.IndexOf("short"u8) >= 0, start + brace + 1);
    }
}
