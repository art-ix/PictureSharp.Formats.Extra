using PictureSharp.Formats.Extra.Common;

namespace PictureSharp.Formats.Xpm;

/// <summary>
/// Walks the string literals of an XPM3 file in order: the values line, one string per colour and one per row. The C
/// code around them (<c>static char *name[] = {</c>, commas, comments) is skipped.
/// </summary>
internal ref struct XpmParser
{
    private CSourceReader _reader;

    public XpmParser(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data))
        {
            throw XpmDecoder.Error("Not an XPM file.");
        }

        _reader = new CSourceReader(data);
    }

    public static bool HasSignature(ReadOnlySpan<byte> data)
    {
        int start = 0;
        while (start < data.Length && data[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            start++;
        }

        return data[start..].StartsWith("/* XPM */"u8);
    }

    public ReadOnlySpan<byte> NextString()
    {
        while (true)
        {
            if (!_reader.SkipSpace() || _reader.AtEnd)
            {
                throw XpmDecoder.Error("The XPM file is truncated.");
            }

            if (_reader.Current != '"')
            {
                _reader.Advance();
                continue;
            }

            if (!_reader.TryReadString(out ReadOnlySpan<byte> value))
            {
                throw XpmDecoder.Error("An XPM string is not closed.");
            }

            return value;
        }
    }
}
