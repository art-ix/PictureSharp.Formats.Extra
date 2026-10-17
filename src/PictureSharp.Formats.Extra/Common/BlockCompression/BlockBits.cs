using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace PictureSharp.Formats.Extra.Common.BlockCompression;

/// <summary>Reads a 128-bit BC6H or BC7 block from its least significant bit upwards.</summary>
internal ref struct BlockBits(ReadOnlySpan<byte> block)
{
    private ulong _low = BinaryPrimitives.ReadUInt64LittleEndian(block);
    private ulong _high = BinaryPrimitives.ReadUInt64LittleEndian(block[8..]);

    /// <summary>Reads 1 to 32 bits; the first bit read becomes bit 0 of the result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(int count)
    {
        if (count == 0)
        {
            return 0;
        }

        int value = (int)(_low & ((1UL << count) - 1));
        _low = (_low >> count) | (_high << (64 - count));
        _high >>= count;
        return value;
    }

    /// <summary>Reads bits in reverse order: the first bit read becomes the most significant one.</summary>
    public int ReadReversed(int count)
    {
        int value = 0;
        for (int i = 0; i < count; i++)
        {
            value = (value << 1) | Read(1);
        }

        return value;
    }
}
