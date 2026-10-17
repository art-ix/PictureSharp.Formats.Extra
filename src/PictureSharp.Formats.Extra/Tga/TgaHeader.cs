using System.Buffers.Binary;

namespace PictureSharp.Formats.Tga;

/// <summary>The 18-byte TGA header.</summary>
internal readonly record struct TgaHeader(
    int IdLength,
    int ColorMapType,
    int ImageType,
    int ColorMapFirst,
    int ColorMapLength,
    int ColorMapDepth,
    int Width,
    int Height,
    int BitsPerPixel,
    int Descriptor)
{
    public const int Size = 18;

    public bool IsRle => (ImageType & 8) != 0;

    public int BaseType => ImageType & 7;

    public int AlphaBits => Descriptor & 0x0f;

    public bool RightToLeft => (Descriptor & 0x10) != 0;

    public bool TopToBottom => (Descriptor & 0x20) != 0;

    public int ColorMapEntrySize => (ColorMapDepth + 7) >> 3;

    /// <summary>
    /// Parses and checks the header fields; TGA has no signature, so this is also the format detection. Accepts what the
    /// decoder can read or reject with a clear error: types 1, 2, 3 and their RLE forms with sensible depths.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out TgaHeader header)
    {
        header = default;
        if (data.Length < Size)
        {
            return false;
        }

        header = new TgaHeader(
            data[0], data[1], data[2],
            BinaryPrimitives.ReadUInt16LittleEndian(data[3..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[5..]),
            data[7],
            BinaryPrimitives.ReadUInt16LittleEndian(data[12..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[14..]),
            data[16],
            data[17]);

        if (header.Width == 0 || header.Height == 0 || header.ColorMapType > 1 || (header.Descriptor & 0xc0) != 0)
        {
            return false;
        }

        bool colorMapValid = header.ColorMapType == 0
            || (header.ColorMapLength > 0 && header.ColorMapDepth is 15 or 16 or 24 or 32);
        if (!colorMapValid)
        {
            return false;
        }

        return header.ImageType switch
        {
            1 or 9 => header.ColorMapType == 1 && header.BitsPerPixel is 8 or 16,
            2 or 10 => header.BitsPerPixel is 15 or 16 or 24 or 32,
            3 or 11 => header.BitsPerPixel is 8 or 16,
            _ => false,
        };
    }
}
