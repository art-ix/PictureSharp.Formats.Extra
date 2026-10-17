using System.Buffers.Binary;

namespace PictureSharp.Formats.Sgi;

/// <summary>The 512-byte SGI header; only the fields that describe the pixel layout are kept.</summary>
internal readonly record struct SgiHeader(bool Rle, int BytesPerChannel, int Width, int Height, int Channels)
{
    public const int Size = 512;
    public const ushort Magic = 474;

    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && BinaryPrimitives.ReadUInt16BigEndian(data) == Magic && data[2] is 0 or 1 && data[3] is 1 or 2;

    public static SgiHeader Parse(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data))
        {
            throw SgiDecoder.Error("Not an SGI file.");
        }

        if (data.Length < Size)
        {
            throw SgiDecoder.Error("The SGI header is truncated.");
        }

        int dimension = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
        int width = BinaryPrimitives.ReadUInt16BigEndian(data[6..]);
        int height = BinaryPrimitives.ReadUInt16BigEndian(data[8..]);
        int channels = BinaryPrimitives.ReadUInt16BigEndian(data[10..]);
        int colormap = BinaryPrimitives.ReadInt32BigEndian(data[104..]);

        // A one-dimensional image is a single row and a two-dimensional one a single channel, whatever the sizes say.
        switch (dimension)
        {
            case 1:
                (height, channels) = (1, 1);
                break;
            case 2:
                channels = 1;
                break;
            case 3:
                break;
            default:
                throw SgiDecoder.Error($"The SGI dimension {dimension} is invalid.");
        }

        if (width == 0 || height == 0 || channels == 0)
        {
            throw SgiDecoder.Error("The SGI image has invalid dimensions.");
        }

        if (channels > 4)
        {
            throw new NotSupportedException($"SGI images with {channels} channels are not supported.");
        }

        if (colormap != 0)
        {
            throw new NotSupportedException($"The SGI colormap type {colormap} (dithered, screen or colour map) is not supported.");
        }

        return new SgiHeader(data[2] == 1, data[3], width, height, channels);
    }
}
