using System.Runtime.CompilerServices;

namespace PictureSharp.Formats.Pnm;

/// <summary>A parsed Netpbm header: the file type, size, sample range and where the raster starts.</summary>
internal readonly record struct PnmHeader(char Magic, int Width, int Height, int Depth, int MaxValue, PnmTuple Tuple, int DataOffset)
{
    // Pillow limits header tokens to 10 characters; anything longer cannot be a valid int dimension anyway.
    private const int MaxTokenLength = 10;

    public bool IsPlain => Magic is '1' or '2' or '3';

    public bool HasAlpha => Tuple is PnmTuple.GrayAlpha or PnmTuple.RgbAlpha or PnmTuple.BlackAndWhiteAlpha;

    public int BitsPerChannel => 32 - int.LeadingZeroCount(MaxValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWhitespace(byte c) => c is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r';

    public static PnmHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3 || data[0] != (byte)'P' || data[1] is < (byte)'1' or > (byte)'7' || !IsWhitespace(data[2]))
        {
            throw PnmDecoder.Error("Not a Netpbm file.");
        }

        char magic = (char)data[1];
        if (magic == '7')
        {
            return ParsePam(data);
        }

        int pos = 2;
        int width = ReadInt(data, ref pos);
        int height = ReadInt(data, ref pos);
        int maxValue = magic is '1' or '4' ? 1 : ReadInt(data, ref pos);
        if (width <= 0 || height <= 0)
        {
            throw PnmDecoder.Error("The Netpbm image has invalid dimensions.");
        }

        if (maxValue is <= 0 or > 65535)
        {
            throw PnmDecoder.Error("The Netpbm maxval must be from 1 to 65535.");
        }

        // Exactly one whitespace character separates the header from a binary raster; plain rasters are tokens.
        bool plain = magic is '1' or '2' or '3';
        if (!plain && (pos >= data.Length || !IsWhitespace(data[pos])))
        {
            throw PnmDecoder.Error("The Netpbm header must end with one whitespace character.");
        }

        (int depth, PnmTuple tuple) = magic switch
        {
            '1' or '4' => (1, PnmTuple.Bitmap),
            '2' or '5' => (1, PnmTuple.Gray),
            _ => (3, PnmTuple.Rgb),
        };
        return new PnmHeader(magic, width, height, depth, maxValue, tuple, plain ? pos : pos + 1);
    }

    // Reads a decimal token after whitespace and '#' comments; leaves pos on the whitespace that ends it.
    private static int ReadInt(ReadOnlySpan<byte> data, ref int pos)
    {
        SkipWhitespaceAndComments(data, ref pos);
        int start = pos;
        long value = 0;
        while (pos < data.Length && !IsWhitespace(data[pos]) && data[pos] != (byte)'#')
        {
            byte c = data[pos];
            if (c is < (byte)'0' or > (byte)'9' || pos - start >= MaxTokenLength)
            {
                throw PnmDecoder.Error("The Netpbm header has an invalid number.");
            }

            value = value * 10 + (c - '0');
            pos++;
        }

        if (pos == start)
        {
            throw PnmDecoder.Error("The Netpbm header is truncated.");
        }

        if (value > int.MaxValue)
        {
            throw PnmDecoder.Error("The Netpbm header has an invalid number.");
        }

        return (int)value;
    }

    public static void SkipWhitespaceAndComments(ReadOnlySpan<byte> data, ref int pos)
    {
        while (pos < data.Length)
        {
            byte c = data[pos];
            if (c == (byte)'#')
            {
                // A comment runs to the end of the line (CR or LF).
                while (pos < data.Length && data[pos] is not ((byte)'\n' or (byte)'\r'))
                {
                    pos++;
                }
            }
            else if (IsWhitespace(c))
            {
                pos++;
            }
            else
            {
                return;
            }
        }
    }

    private static PnmHeader ParsePam(ReadOnlySpan<byte> data)
    {
        int pos = 3;
        int width = -1, height = -1, depth = -1, maxValue = -1;
        Span<byte> tupleType = stackalloc byte[32];
        int tupleLength = 0;
        bool ended = false;

        while (pos < data.Length && !ended)
        {
            int end = data[pos..].IndexOf((byte)'\n');
            if (end < 0)
            {
                throw PnmDecoder.Error("The PAM header has no ENDHDR line.");
            }

            ReadOnlySpan<byte> line = Trim(data.Slice(pos, end));
            pos += end + 1;
            if (line.IsEmpty || line[0] == (byte)'#')
            {
                continue;
            }

            int split = line.IndexOfAny((byte)' ', (byte)'\t');
            ReadOnlySpan<byte> key = split < 0 ? line : line[..split];
            ReadOnlySpan<byte> value = split < 0 ? [] : Trim(line[(split + 1)..]);

            if (key.SequenceEqual("ENDHDR"u8))
            {
                ended = true;
            }
            else if (key.SequenceEqual("WIDTH"u8))
            {
                width = ParseValue(value);
            }
            else if (key.SequenceEqual("HEIGHT"u8))
            {
                height = ParseValue(value);
            }
            else if (key.SequenceEqual("DEPTH"u8))
            {
                depth = ParseValue(value);
            }
            else if (key.SequenceEqual("MAXVAL"u8))
            {
                maxValue = ParseValue(value);
            }
            else if (key.SequenceEqual("TUPLTYPE"u8))
            {
                // Repeated TUPLTYPE lines concatenate, separated by a space.
                int needed = tupleLength + (tupleLength > 0 ? 1 : 0) + value.Length;
                if (needed > tupleType.Length)
                {
                    throw new NotSupportedException("The PAM tuple type is not supported.");
                }

                if (tupleLength > 0)
                {
                    tupleType[tupleLength++] = (byte)' ';
                }

                value.CopyTo(tupleType[tupleLength..]);
                tupleLength += value.Length;
            }
            else
            {
                throw PnmDecoder.Error("The PAM header has an unknown line.");
            }
        }

        if (!ended)
        {
            throw PnmDecoder.Error("The PAM header has no ENDHDR line.");
        }

        if (width <= 0 || height <= 0 || depth <= 0)
        {
            throw PnmDecoder.Error("The PAM header lacks WIDTH, HEIGHT or DEPTH.");
        }

        if (maxValue is <= 0 or > 65535)
        {
            throw PnmDecoder.Error("The Netpbm maxval must be from 1 to 65535.");
        }

        PnmTuple tuple = ParseTuple(tupleType[..tupleLength], depth, maxValue);
        return new PnmHeader('7', width, height, depth, maxValue, tuple, pos);
    }

    private static PnmTuple ParseTuple(ReadOnlySpan<byte> name, int depth, int maxValue)
    {
        (PnmTuple tuple, int channels) = name switch
        {
            [] => depth switch
            {
                1 => (PnmTuple.Gray, 1),
                2 => (PnmTuple.GrayAlpha, 2),
                3 => (PnmTuple.Rgb, 3),
                4 => (PnmTuple.RgbAlpha, 4),
                _ => throw new NotSupportedException("PAM files without a tuple type need a depth from 1 to 4."),
            },
            _ when name.SequenceEqual("BLACKANDWHITE"u8) => (PnmTuple.BlackAndWhite, 1),
            _ when name.SequenceEqual("BLACKANDWHITE_ALPHA"u8) => (PnmTuple.BlackAndWhiteAlpha, 2),
            _ when name.SequenceEqual("GRAYSCALE"u8) => (PnmTuple.Gray, 1),
            _ when name.SequenceEqual("GRAYSCALE_ALPHA"u8) => (PnmTuple.GrayAlpha, 2),
            _ when name.SequenceEqual("RGB"u8) => (PnmTuple.Rgb, 3),
            _ when name.SequenceEqual("RGB_ALPHA"u8) => (PnmTuple.RgbAlpha, 4),
            _ => throw new NotSupportedException("The PAM tuple type is not supported."),
        };

        if (depth != channels)
        {
            throw PnmDecoder.Error("The PAM depth does not match the tuple type.");
        }

        if (tuple is PnmTuple.BlackAndWhite or PnmTuple.BlackAndWhiteAlpha && maxValue != 1)
        {
            throw PnmDecoder.Error("BLACKANDWHITE PAM files must have maxval 1.");
        }

        return tuple;
    }

    private static int ParseValue(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty || value.Length > MaxTokenLength)
        {
            throw PnmDecoder.Error("The PAM header has an invalid number.");
        }

        long result = 0;
        foreach (byte c in value)
        {
            if (c is < (byte)'0' or > (byte)'9')
            {
                throw PnmDecoder.Error("The PAM header has an invalid number.");
            }

            result = result * 10 + (c - '0');
        }

        return result > int.MaxValue ? throw PnmDecoder.Error("The PAM header has an invalid number.") : (int)result;
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> s)
    {
        int start = 0, end = s.Length;
        while (start < end && IsWhitespace(s[start]))
        {
            start++;
        }

        while (end > start && IsWhitespace(s[end - 1]))
        {
            end--;
        }

        return s[start..end];
    }
}

/// <summary>The sample layout of a Netpbm raster.</summary>
internal enum PnmTuple
{
    /// <summary>PBM: 1 is black.</summary>
    Bitmap,

    /// <summary>PAM BLACKANDWHITE: 1 is white.</summary>
    BlackAndWhite,
    BlackAndWhiteAlpha,
    Gray,
    GrayAlpha,
    Rgb,
    RgbAlpha,
}
