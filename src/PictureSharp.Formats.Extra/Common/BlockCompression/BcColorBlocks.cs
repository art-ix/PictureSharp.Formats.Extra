using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace PictureSharp.Formats.Extra.Common.BlockCompression;

/// <summary>
/// BC1–BC5 (DXT1–DXT5, ATI1/ATI2) block decoders. Each writes the 16 pixels of a 4×4 block in row order. Rounding follows
/// Pillow 10.2.0 BcnDecode.c: 5/6-bit endpoints are widened by bit replication and interpolated in 8 bits with
/// truncating division.
/// </summary>
internal static class BcColorBlocks
{
    /// <summary>BC1: four colours, or three colours and transparent black when the first endpoint is not greater.</summary>
    public static void DecodeBc1(ReadOnlySpan<byte> block, Span<Rgba32> pixels) => DecodeColor(block, pixels, threeColorMode: true);

    /// <summary>BC2: explicit 4-bit alpha followed by a four-colour BC1 block.</summary>
    public static void DecodeBc2(ReadOnlySpan<byte> block, Span<Rgba32> pixels)
    {
        DecodeColor(block[8..], pixels, threeColorMode: false);
        ulong alpha = BinaryPrimitives.ReadUInt64LittleEndian(block);
        for (int i = 0; i < 16; i++)
        {
            pixels[i].A = (byte)(((alpha >> (4 * i)) & 15) * 17);
        }
    }

    /// <summary>BC3: an interpolated alpha block followed by a four-colour BC1 block.</summary>
    public static void DecodeBc3(ReadOnlySpan<byte> block, Span<Rgba32> pixels)
    {
        DecodeColor(block[8..], pixels, threeColorMode: false);
        Span<byte> alpha = stackalloc byte[16];
        DecodeChannel(block, alpha, signed: false);
        for (int i = 0; i < 16; i++)
        {
            pixels[i].A = alpha[i];
        }
    }

    /// <summary>BC4: one channel, written as grey.</summary>
    public static void DecodeBc4(ReadOnlySpan<byte> block, Span<Rgba32> pixels, bool signed)
    {
        Span<byte> value = stackalloc byte[16];
        DecodeChannel(block, value, signed);
        for (int i = 0; i < 16; i++)
        {
            pixels[i] = new Rgba32(value[i], value[i], value[i]);
        }
    }

    /// <summary>BC5: red and green channels; blue is 0, or 128 for signed data as in Pillow.</summary>
    public static void DecodeBc5(ReadOnlySpan<byte> block, Span<Rgba32> pixels, bool signed)
    {
        Span<byte> red = stackalloc byte[16];
        Span<byte> green = stackalloc byte[16];
        DecodeChannel(block, red, signed);
        DecodeChannel(block[8..], green, signed);
        byte blue = signed ? (byte)128 : (byte)0;
        for (int i = 0; i < 16; i++)
        {
            pixels[i] = new Rgba32(red[i], green[i], blue);
        }
    }

    private static void DecodeColor(ReadOnlySpan<byte> block, Span<Rgba32> pixels, bool threeColorMode)
    {
        int c0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
        int c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
        Span<Rgba32> palette = stackalloc Rgba32[4];
        Rgba32 a = Expand565(c0);
        Rgba32 b = Expand565(c1);
        palette[0] = a;
        palette[1] = b;
        if (!threeColorMode || c0 > c1)
        {
            palette[2] = new Rgba32((byte)((2 * a.R + b.R) / 3), (byte)((2 * a.G + b.G) / 3), (byte)((2 * a.B + b.B) / 3));
            palette[3] = new Rgba32((byte)((a.R + 2 * b.R) / 3), (byte)((a.G + 2 * b.G) / 3), (byte)((a.B + 2 * b.B) / 3));
        }
        else
        {
            palette[2] = new Rgba32((byte)((a.R + b.R) / 2), (byte)((a.G + b.G) / 2), (byte)((a.B + b.B) / 2));
            palette[3] = default;
        }

        for (int i = 0; i < 16; i++)
        {
            pixels[i] = palette[(int)((indices >> (2 * i)) & 3)];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rgba32 Expand565(int c)
    {
        int r = (c >> 11) & 31;
        int g = (c >> 5) & 63;
        int b = c & 31;
        return new Rgba32((byte)((r << 3) | (r >> 2)), (byte)((g << 2) | (g >> 4)), (byte)((b << 3) | (b >> 2)));
    }

    // Signed endpoints are moved to the unsigned range (v + 128) and then decoded like unsigned ones, as Pillow does.
    private static void DecodeChannel(ReadOnlySpan<byte> block, Span<byte> values, bool signed)
    {
        int a0 = signed ? block[0] ^ 0x80 : block[0];
        int a1 = signed ? block[1] ^ 0x80 : block[1];
        Span<byte> palette = stackalloc byte[8];
        palette[0] = (byte)a0;
        palette[1] = (byte)a1;
        if (a0 > a1)
        {
            for (int i = 1; i < 7; i++)
            {
                palette[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
            }
        }
        else
        {
            for (int i = 1; i < 5; i++)
            {
                palette[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);
            }

            palette[6] = 0;
            palette[7] = 255;
        }

        ulong indices = BinaryPrimitives.ReadUInt64LittleEndian(block) >> 16;
        for (int i = 0; i < 16; i++)
        {
            values[i] = palette[(int)((indices >> (3 * i)) & 7)];
        }
    }
}
