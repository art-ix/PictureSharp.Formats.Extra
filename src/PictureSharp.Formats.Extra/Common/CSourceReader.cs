namespace PictureSharp.Formats.Extra.Common;

/// <summary>
/// Reads the C source text of XBM and XPM files: skips white space and comments, and reads words, numbers and string
/// literals. Methods report failure instead of throwing, so each format raises its own exception.
/// </summary>
internal ref struct CSourceReader(ReadOnlySpan<byte> text)
{
    private readonly ReadOnlySpan<byte> _text = text;

    public int Position { get; private set; }

    public readonly bool AtEnd => Position >= _text.Length;

    public readonly byte Current => _text[Position];

    /// <summary>Skips white space, <c>/* */</c> and <c>//</c> comments; false when a block comment is not closed.</summary>
    public bool SkipSpace()
    {
        while (Position < _text.Length)
        {
            byte c = _text[Position];
            if (c is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f' or (byte)'\v')
            {
                Position++;
            }
            else if (c == '/' && Position + 1 < _text.Length && _text[Position + 1] == '*')
            {
                int end = _text[(Position + 2)..].IndexOf("*/"u8);
                if (end < 0)
                {
                    Position = _text.Length;
                    return false;
                }

                Position += end + 4;
            }
            else if (c == '/' && Position + 1 < _text.Length && _text[Position + 1] == '/')
            {
                SkipLine();
            }
            else
            {
                break;
            }
        }

        return true;
    }

    public void Advance() => Position++;

    /// <summary>Moves past the end of the current line.</summary>
    public void SkipLine()
    {
        int end = _text[Position..].IndexOf((byte)'\n');
        Position = end < 0 ? _text.Length : Position + end + 1;
    }

    /// <summary>Reads letters, digits and underscores; empty when none follow.</summary>
    public ReadOnlySpan<byte> ReadWord()
    {
        int start = Position;
        while (Position < _text.Length && (char.IsAsciiLetterOrDigit((char)_text[Position]) || _text[Position] == '_'))
        {
            Position++;
        }

        return _text[start..Position];
    }

    /// <summary>Reads up to the next white space; empty at white space or the end.</summary>
    public ReadOnlySpan<byte> ReadToken()
    {
        int start = Position;
        while (Position < _text.Length && _text[Position] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
        {
            Position++;
        }

        return _text[start..Position];
    }

    /// <summary>Skips spaces and tabs only, staying on the current line.</summary>
    public void SkipBlanks()
    {
        while (Position < _text.Length && _text[Position] is (byte)' ' or (byte)'\t')
        {
            Position++;
        }
    }

    /// <summary>Reads a string literal at the current position without its quotes; false when it is not closed on its line.</summary>
    public bool TryReadString(out ReadOnlySpan<byte> value)
    {
        value = default;
        if (Position >= _text.Length || _text[Position] != '"')
        {
            return false;
        }

        // XPM strings hold no escapes in practice; a backslash keeps the next character, as a C compiler would.
        int start = ++Position;
        while (Position < _text.Length)
        {
            byte c = _text[Position];
            if (c == '"')
            {
                value = _text[start..Position++];
                return true;
            }

            if (c is (byte)'\n' or (byte)'\r')
            {
                return false;
            }

            Position += c == '\\' ? 2 : 1;
        }

        return false;
    }

    /// <summary>Parses a decimal or <c>0x</c> hexadecimal number up to <paramref name="max"/>.</summary>
    public static bool TryParseNumber(ReadOnlySpan<byte> word, int max, out int value)
    {
        value = 0;
        int radix = 10;
        if (word.Length > 2 && word[0] == '0' && word[1] is (byte)'x' or (byte)'X')
        {
            radix = 16;
            word = word[2..];
        }

        if (word.IsEmpty)
        {
            return false;
        }

        long result = 0;
        foreach (byte c in word)
        {
            int digit = char.IsAsciiDigit((char)c) ? c - '0' : radix == 16 && char.IsAsciiHexDigit((char)c) ? (c | 0x20) - 'a' + 10 : -1;
            if (digit < 0)
            {
                return false;
            }

            result = result * radix + digit;
            if (result > max)
            {
                return false;
            }
        }

        value = (int)result;
        return true;
    }
}
