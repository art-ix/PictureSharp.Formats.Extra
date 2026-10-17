using System.Buffers.Binary;

namespace PictureSharp.Formats.SunRaster;

/// <summary>The 32-byte Sun Raster header followed by the optional colour map.</summary>
internal readonly record struct SunRasterHeader(int Width, int Height, int Depth, int Type, int ColorMapLength)
{
    public const int Size = 32;
    public const uint Magic = 0x59A66A95;

    public const int Old = 0;
    public const int Standard = 1;
    public const int ByteEncoded = 2;
    public const int Rgb = 3;
    public const int Tiff = 4;
    public const int Iff = 5;
    public const int Experimental = 0xFFFF;

    private const int MaxDimension = 1 << 24;

    /// <summary>Bytes per row: rows are padded to 16 bits.</summary>
    public long Stride => ((long)Width * Depth + 15) / 16 * 2;

    public int ColorCount => ColorMapLength / 3;

    public int DataOffset => Size + ColorMapLength;

    public static bool HasSignature(ReadOnlySpan<byte> data) => data.Length >= 4 && BinaryPrimitives.ReadUInt32BigEndian(data) == Magic;

    public static SunRasterHeader Parse(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data))
        {
            throw SunRasterDecoder.Error("Not a Sun Raster file.");
        }

        if (data.Length < Size)
        {
            throw SunRasterDecoder.Error("The Sun Raster header is truncated.");
        }

        uint width = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        uint depth = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
        uint type = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
        uint mapType = BinaryPrimitives.ReadUInt32BigEndian(data[24..]);
        uint mapLength = BinaryPrimitives.ReadUInt32BigEndian(data[28..]);
        if (width is 0 or > MaxDimension || height is 0 or > MaxDimension)
        {
            throw SunRasterDecoder.Error("The Sun Raster image has invalid dimensions.");
        }

        if (depth is not (1 or 4 or 8 or 24 or 32))
        {
            throw new NotSupportedException($"Sun Raster images with {depth} bits per pixel are not supported.");
        }

        if (type == Experimental)
        {
            throw new NotSupportedException("Experimental Sun Raster images are not supported.");
        }

        if (type > Iff)
        {
            throw SunRasterDecoder.Error($"The Sun Raster type {type} is unknown.");
        }

        if (mapLength > data.Length - Size)
        {
            throw SunRasterDecoder.Error("The Sun Raster colour map is truncated.");
        }

        // Map type 0 means no map, 1 an RGB map; a raw map (2) has no defined meaning.
        if (mapLength != 0 && mapType != 1)
        {
            throw mapType == 2
                ? new NotSupportedException("Sun Raster raw colour maps are not supported.")
                : SunRasterDecoder.Error($"The Sun Raster colour map type {mapType} is unknown.");
        }

        if (mapLength % 3 != 0 || mapLength > 3 * 256)
        {
            throw SunRasterDecoder.Error("The Sun Raster colour map length is invalid.");
        }

        return new SunRasterHeader((int)width, (int)height, (int)depth, (int)type, (int)mapLength);
    }
}
