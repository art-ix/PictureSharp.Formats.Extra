using System.Buffers.Binary;

namespace PictureSharp.Formats.Psd;

/// <summary>
/// The file header and the section layout of a PSD (version 1) or PSB (version 2) file: colour mode data, image
/// resources, the layer and mask section (read only for the layer count) and where the merged image starts.
/// </summary>
internal readonly record struct PsdHeader(
    bool IsLarge,
    int Channels,
    int Width,
    int Height,
    int Depth,
    PsdColorMode Mode,
    int ColorDataOffset,
    int ColorDataLength,
    int ResourcesOffset,
    int ResourcesLength,
    bool MergedAlpha,
    int ImageDataOffset)
{
    public const int Size = 26;
    public const int PaletteSize = 768;

    // Photoshop's limits are 30,000 (PSD) and 300,000 (PSB) pixels; both versions accept the larger one.
    private const int MaxDimension = 300_000;
    private const int MaxChannels = 56;

    /// <summary>Colour channels before the extra (alpha and spot) channels.</summary>
    public int BaseChannels => Mode switch
    {
        PsdColorMode.Rgb => 3,
        PsdColorMode.Cmyk => 4,
        _ => 1,
    };

    /// <summary>Channels the decoder reads: the colour channels and, with <see cref="MergedAlpha"/>, the first extra one.</summary>
    public int UsedChannels => BaseChannels + (MergedAlpha ? 1 : 0);

    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= 6 && data[..4].SequenceEqual("8BPS"u8) && BinaryPrimitives.ReadUInt16BigEndian(data[4..]) is 1 or 2;

    public static PsdHeader Parse(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data))
        {
            throw PsdDecoder.Error("Not a PSD or PSB file.");
        }

        if (data.Length < Size)
        {
            throw PsdDecoder.Error("The PSD header is truncated.");
        }

        bool large = BinaryPrimitives.ReadUInt16BigEndian(data[4..]) == 2;
        int channels = BinaryPrimitives.ReadUInt16BigEndian(data[12..]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(data[14..]);
        uint width = BinaryPrimitives.ReadUInt32BigEndian(data[18..]);
        int depth = BinaryPrimitives.ReadUInt16BigEndian(data[22..]);
        var mode = (PsdColorMode)BinaryPrimitives.ReadUInt16BigEndian(data[24..]);
        if (channels is < 1 or > MaxChannels)
        {
            throw PsdDecoder.Error($"The PSD file declares {channels} channels.");
        }

        if (width is 0 or > MaxDimension || height is 0 or > MaxDimension)
        {
            throw PsdDecoder.Error("The PSD image has invalid dimensions.");
        }

        CheckModeAndDepth(mode, depth);

        int pos = Size;
        (int colorOffset, int colorLength) = Section(data, ref pos, wide: false, "colour mode data");
        (int resourcesOffset, int resourcesLength) = Section(data, ref pos, wide: false, "image resources");
        (int layersOffset, int layersLength) = Section(data, ref pos, wide: large, "layer and mask information");
        if (mode == PsdColorMode.Indexed && colorLength != PaletteSize)
        {
            throw PsdDecoder.Error("An indexed PSD image needs a 768-byte palette.");
        }

        var header = new PsdHeader(large, channels, (int)width, (int)height, depth, mode, colorOffset, colorLength,
            resourcesOffset, resourcesLength, MergedAlpha: false, ImageDataOffset: pos);
        if (channels < header.BaseChannels)
        {
            throw PsdDecoder.Error($"The PSD colour mode needs {header.BaseChannels} channels, the file has {channels}.");
        }

        // A negative layer count means the first extra channel holds the transparency of the merged image; a file
        // without layers has no other place for it. Indexed and bitmap images cannot be transparent this way.
        bool alpha = channels > header.BaseChannels && mode is not (PsdColorMode.Indexed or PsdColorMode.Bitmap)
            && LayerCount(data.Slice(layersOffset, layersLength), large) is null or < 0;
        return header with { MergedAlpha = alpha };
    }

    /// <summary>Reads the next image resource of <paramref name="resources"/>; false at the end of the section.</summary>
    public static bool NextResource(ReadOnlySpan<byte> resources, ref int pos, out int id, out int dataOffset, out int dataLength)
    {
        id = dataOffset = dataLength = 0;
        if (pos >= resources.Length)
        {
            return false;
        }

        // Signature "8BIM" (other signatures occur and are skipped alike), id, Pascal name padded to an even size.
        if (resources.Length - pos < 7)
        {
            throw PsdDecoder.Error("An image resource is truncated.");
        }

        id = BinaryPrimitives.ReadUInt16BigEndian(resources[(pos + 4)..]);
        int nameLength = resources[pos + 6];
        long at = pos + 6L + ((nameLength + 2) & ~1);
        if (resources.Length - at < 4)
        {
            throw PsdDecoder.Error("An image resource is truncated.");
        }

        uint length = BinaryPrimitives.ReadUInt32BigEndian(resources[(int)at..]);
        at += 4;
        if (length > resources.Length - at)
        {
            throw PsdDecoder.Error("An image resource is truncated.");
        }

        dataOffset = (int)at;
        dataLength = (int)length;
        pos = (int)Math.Min(resources.Length, at + length + (length & 1));
        return true;
    }

    /// <summary>Horizontal and vertical resolution in pixels per inch from resource 1005, or zeros.</summary>
    public (float Horizontal, float Vertical) Resolution(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> resources = data.Slice(ResourcesOffset, ResourcesLength);
        int pos = 0;
        while (NextResource(resources, ref pos, out int id, out int offset, out int length))
        {
            // ResolutionInfo: 16.16 fixed-point pixels per inch; the unit fields only choose how Photoshop displays it.
            if (id == PsdResource.Resolution && length >= 16)
            {
                ReadOnlySpan<byte> info = resources.Slice(offset, length);
                return (BinaryPrimitives.ReadUInt32BigEndian(info) / 65536f, BinaryPrimitives.ReadUInt32BigEndian(info[8..]) / 65536f);
            }
        }

        return (0, 0);
    }

    private static void CheckModeAndDepth(PsdColorMode mode, int depth)
    {
        switch (mode)
        {
            case PsdColorMode.Lab or PsdColorMode.Multichannel:
                throw new NotSupportedException($"The PSD colour mode {mode} is not supported.");
            case PsdColorMode.Bitmap:
                if (depth != 1)
                {
                    throw new NotSupportedException($"PSD bitmap images with {depth} bits are not supported.");
                }

                return;
            case PsdColorMode.Grayscale or PsdColorMode.Indexed or PsdColorMode.Rgb or PsdColorMode.Cmyk or PsdColorMode.Duotone:
                if (depth == 32 && mode != PsdColorMode.Indexed)
                {
                    throw new NotSupportedException("32-bit PSD images are not supported.");
                }

                if (depth != 8 && (depth != 16 || mode == PsdColorMode.Indexed))
                {
                    throw PsdDecoder.Error($"The PSD colour mode {mode} does not allow {depth} bits per channel.");
                }

                return;
            default:
                throw PsdDecoder.Error($"The PSD colour mode {(int)mode} is unknown.");
        }
    }

    private static (int Offset, int Length) Section(ReadOnlySpan<byte> data, ref int pos, bool wide, string name)
    {
        int lengthSize = wide ? 8 : 4;
        if (data.Length - pos < lengthSize)
        {
            throw PsdDecoder.Error($"The PSD {name} section is truncated.");
        }

        ulong length = wide ? BinaryPrimitives.ReadUInt64BigEndian(data[pos..]) : BinaryPrimitives.ReadUInt32BigEndian(data[pos..]);
        pos += lengthSize;
        if (length > (ulong)(data.Length - pos))
        {
            throw PsdDecoder.Error($"The PSD {name} section is truncated.");
        }

        int offset = pos;
        pos += (int)length;
        return (offset, (int)length);
    }

    /// <summary>
    /// The signed layer count from the layer info, or from the Lr16/Lr32/Layr tagged block where Photoshop stores the
    /// layers of 16- and 32-bit files; null when the file has no layers. Damaged layer data is not an error because
    /// the merged image does not depend on it.
    /// </summary>
    private static int? LayerCount(ReadOnlySpan<byte> section, bool large)
    {
        int lengthSize = large ? 8 : 4;
        if (section.Length < lengthSize)
        {
            return null;
        }

        ulong layerInfo = large ? BinaryPrimitives.ReadUInt64BigEndian(section) : BinaryPrimitives.ReadUInt32BigEndian(section);
        if (layerInfo >= 2 && layerInfo <= (ulong)(section.Length - lengthSize))
        {
            return BinaryPrimitives.ReadInt16BigEndian(section[lengthSize..]);
        }

        if (layerInfo > (ulong)(section.Length - lengthSize))
        {
            return null;
        }

        // Skip the layer info and the global layer mask info, then walk the tagged blocks.
        long pos = lengthSize + (long)layerInfo;
        if (section.Length - pos < 4)
        {
            return null;
        }

        pos += 4 + BinaryPrimitives.ReadUInt32BigEndian(section[(int)pos..]);
        while (pos + 12 <= section.Length)
        {
            ReadOnlySpan<byte> key = section.Slice((int)pos + 4, 4);
            bool wideLength = large && IsWideKey(key);
            if (pos + (wideLength ? 16 : 12) > section.Length)
            {
                return null;
            }

            ulong length = wideLength ? BinaryPrimitives.ReadUInt64BigEndian(section[((int)pos + 8)..]) : BinaryPrimitives.ReadUInt32BigEndian(section[((int)pos + 8)..]);
            pos += wideLength ? 16 : 12;
            if (length > (ulong)(section.Length - pos))
            {
                return null;
            }

            if ((key.SequenceEqual("Lr16"u8) || key.SequenceEqual("Lr32"u8) || key.SequenceEqual("Layr"u8)) && length >= 2)
            {
                // The block is the layer info without its own length field.
                return BinaryPrimitives.ReadInt16BigEndian(section[(int)pos..]);
            }

            pos += (long)length;
        }

        return null;
    }

    // Tagged blocks whose length is 8 bytes in PSB files.
    private static bool IsWideKey(ReadOnlySpan<byte> key) =>
        key.SequenceEqual("LMsk"u8) || key.SequenceEqual("Lr16"u8) || key.SequenceEqual("Lr32"u8) || key.SequenceEqual("Layr"u8)
        || key.SequenceEqual("Mt16"u8) || key.SequenceEqual("Mt32"u8) || key.SequenceEqual("Mtrn"u8) || key.SequenceEqual("Alph"u8)
        || key.SequenceEqual("FMsk"u8) || key.SequenceEqual("lnk2"u8) || key.SequenceEqual("FEid"u8) || key.SequenceEqual("FXid"u8)
        || key.SequenceEqual("PxSD"u8);
}
