using System.Buffers.Binary;

namespace PictureSharp.Formats.Vtf;

/// <summary>
/// The VTF header (version 7.0–7.6) and where the largest mip level of the first frame, face and slice lies. Image
/// data is ordered from the smallest mip level to the largest; within a level by frame, face and slice.
/// </summary>
internal readonly record struct VtfHeader(
    int Minor,
    int HeaderSize,
    int Width,
    int Height,
    uint Flags,
    int Frames,
    VtfPixelFormat Format,
    int MipCount,
    VtfPixelFormat ThumbnailFormat,
    int ThumbnailWidth,
    int ThumbnailHeight,
    int Depth)
{
    public const int MinimumSize = 64;

    private const uint EnvironmentMap = 0x4000;
    private const int MaxMipCount = 32;
    private const int ResourceTableOffset = 80;
    private const int ImageResource = 0x30;
    private const int ThumbnailResource = 0x01;
    private const int ResourceHasNoData = 0x02;
    private const int Deflate = 8;

    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[..4].SequenceEqual("VTF\0"u8) && BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) == 7;

    public bool HasAlpha => Format switch
    {
        VtfPixelFormat.Rgb888 or VtfPixelFormat.Bgr888 or VtfPixelFormat.Rgb565 or VtfPixelFormat.Bgr565 or VtfPixelFormat.I8
            or VtfPixelFormat.Bgrx8888 or VtfPixelFormat.Bgrx5551 or VtfPixelFormat.Rgbx8888 or VtfPixelFormat.Uv88
            or VtfPixelFormat.Uvlx8888 or VtfPixelFormat.Ati1N or VtfPixelFormat.Ati2N or VtfPixelFormat.StrataBc4
            or VtfPixelFormat.StrataBc5 or VtfPixelFormat.StrataBc6HSigned or VtfPixelFormat.StrataBc6HUnsigned => false,
        _ => true,
    };

    public static VtfHeader Parse(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data))
        {
            throw VtfDecoder.Error("Not a VTF file.");
        }

        if (data.Length < MinimumSize)
        {
            throw VtfDecoder.Error("The VTF header is truncated.");
        }

        int minor = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data[8..]), int.MaxValue);
        if (minor > 6)
        {
            throw new NotSupportedException($"VTF version 7.{minor} is not supported.");
        }

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data[16..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[18..]);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data[20..]);
        int frames = Math.Max(1, (int)BinaryPrimitives.ReadUInt16LittleEndian(data[24..]));
        var format = (VtfPixelFormat)BinaryPrimitives.ReadInt32LittleEndian(data[52..]);
        int mipCount = data[56];
        var thumbnailFormat = (VtfPixelFormat)BinaryPrimitives.ReadInt32LittleEndian(data[57..]);
        int depth = minor >= 2 ? Math.Max(1, (int)BinaryPrimitives.ReadUInt16LittleEndian(data[63..])) : 1;
        if (headerSize < MinimumSize || headerSize > data.Length || minor >= 3 && headerSize < ResourceTableOffset)
        {
            throw VtfDecoder.Error("The VTF header size is invalid.");
        }

        if (width == 0 || height == 0)
        {
            throw VtfDecoder.Error("The VTF image has invalid dimensions.");
        }

        if (mipCount is 0 or > MaxMipCount)
        {
            throw VtfDecoder.Error($"The VTF file declares {mipCount} mip levels.");
        }

        return new VtfHeader(minor, (int)headerSize, width, height, flags, frames, format, mipCount, thumbnailFormat, data[61], data[62], depth);
    }

    /// <summary>
    /// The bytes of the largest mip level of the first frame, face and slice, decompressed when a 7.6 file stores its
    /// image data with Deflate.
    /// </summary>
    public ReadOnlySpan<byte> LargestImage(ReadOnlySpan<byte> data, out byte[]? decompressed)
    {
        decompressed = null;
        long size = ImageSize(Format, Width, Height);
        (long start, long compression) = Minor >= 3 ? FindResources(data) : (DataStartBeforeResources(), -1L);
        int faces = FaceCount(data.Length - start);
        if (compression >= 0 && TryReadCompression(data, compression, faces, start, size, out decompressed))
        {
            return decompressed;
        }

        long offset = start;
        for (int mip = MipCount - 1; mip > 0; mip--)
        {
            long items = (long)Frames * faces * Math.Max(1, Depth >> mip);
            offset = Add(offset, Multiply(ImageSize(Format, Math.Max(1, Width >> mip), Math.Max(1, Height >> mip)), items));
        }

        if (offset > data.Length || size > data.Length - offset)
        {
            throw VtfDecoder.Error("The VTF image data is truncated.");
        }

        return data.Slice((int)offset, (int)size);
    }

    /// <summary>Bytes of one surface of <paramref name="format"/>; throws <see cref="NotSupportedException"/> for formats the decoder cannot read.</summary>
    public static long ImageSize(VtfPixelFormat format, int width, int height)
    {
        long blocks = ((width + 3L) / 4) * ((height + 3L) / 4);
        return format switch
        {
            VtfPixelFormat.Dxt1 or VtfPixelFormat.Dxt1OneBitAlpha or VtfPixelFormat.Ati1N or VtfPixelFormat.StrataBc4 => blocks * 8,
            VtfPixelFormat.Dxt3 or VtfPixelFormat.Dxt5 or VtfPixelFormat.Ati2N or VtfPixelFormat.StrataBc5 or VtfPixelFormat.StrataBc6HSigned
                or VtfPixelFormat.StrataBc6HUnsigned or VtfPixelFormat.StrataBc7 => blocks * 16,
            _ => (long)width * height * BytesPerPixel(format),
        };
    }

    public static int BytesPerPixel(VtfPixelFormat format) => format switch
    {
        VtfPixelFormat.I8 or VtfPixelFormat.A8 => 1,
        VtfPixelFormat.Rgb565 or VtfPixelFormat.Bgr565 or VtfPixelFormat.Ia88 or VtfPixelFormat.Bgrx5551 or VtfPixelFormat.Bgra4444
            or VtfPixelFormat.Bgra5551 or VtfPixelFormat.Uv88 => 2,
        VtfPixelFormat.Rgb888 or VtfPixelFormat.Bgr888 or VtfPixelFormat.Rgb888Bluescreen or VtfPixelFormat.Bgr888Bluescreen => 3,
        VtfPixelFormat.Rgba8888 or VtfPixelFormat.Abgr8888 or VtfPixelFormat.Argb8888 or VtfPixelFormat.Bgra8888 or VtfPixelFormat.Bgrx8888
            or VtfPixelFormat.Uvwq8888 or VtfPixelFormat.Uvlx8888 or VtfPixelFormat.Rgbx8888 => 4,
        VtfPixelFormat.Rgba16161616 => 8,
        VtfPixelFormat.None => throw VtfDecoder.Error("The VTF file has no image format."),
        _ => throw new NotSupportedException($"The VTF image format {(int)format} is not supported."),
    };

    // Before 7.3 the thumbnail (always DXT1) follows the header and the image data follows the thumbnail.
    private long DataStartBeforeResources() =>
        ThumbnailWidth == 0 || ThumbnailHeight == 0 || ThumbnailFormat == VtfPixelFormat.None
            ? HeaderSize
            : HeaderSize + ImageSize(VtfPixelFormat.Dxt1, ThumbnailWidth, ThumbnailHeight);

    /// <summary>The image data offset and the offset of the AXC (auxiliary compression) resource, or -1.</summary>
    private (long Image, long Compression) FindResources(ReadOnlySpan<byte> data)
    {
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data[68..]);
        if (count > (HeaderSize - ResourceTableOffset) / 8)
        {
            throw VtfDecoder.Error("The VTF resource directory is larger than the header.");
        }

        long image = -1, compression = -1;
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = data.Slice(ResourceTableOffset + 8 * i, 8);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            bool inline = (entry[3] & ResourceHasNoData) != 0;
            if (entry[0] == ImageResource && entry[1] == 0 && entry[2] == 0 && !inline)
            {
                image = offset;
            }
            else if (entry[..3].SequenceEqual("AXC"u8) && !inline)
            {
                compression = offset;
            }
        }

        if (image < 0 || image > data.Length)
        {
            throw VtfDecoder.Error("The VTF file has no image data.");
        }

        return (image, compression);
    }

    /// <summary>
    /// Environment maps have six faces; 7.1–7.4 files may add a seventh (sphere map) face, which shows as data beyond
    /// six faces.
    /// </summary>
    private int FaceCount(long available)
    {
        if ((Flags & EnvironmentMap) == 0)
        {
            return 1;
        }

        if (Minor is < 1 or > 4)
        {
            return 6;
        }

        long six = 0;
        for (int mip = 0; mip < MipCount; mip++)
        {
            long items = (long)Frames * 6 * Math.Max(1, Depth >> mip);
            six = Add(six, Multiply(ImageSize(Format, Math.Max(1, Width >> mip), Math.Max(1, Height >> mip)), items));
        }

        return available > six ? 7 : 6;
    }

    /// <summary>
    /// The AXC resource of 7.6: a length, the compression level (low 16 bits) and method (high 16 bits), then the
    /// compressed length of every mip level, frame and face in storage order; the slices of a face are compressed
    /// together. Level 0 means uncompressed data.
    /// </summary>
    private bool TryReadCompression(ReadOnlySpan<byte> data, long resource, int faces, long start, long size, out byte[]? decompressed)
    {
        decompressed = null;
        if (resource > data.Length - 8)
        {
            throw VtfDecoder.Error("The VTF compression resource is truncated.");
        }

        uint settings = BinaryPrimitives.ReadUInt32LittleEndian(data[(int)(resource + 4)..]);
        int level = (short)settings, method = (short)(settings >> 16);
        if (level == 0)
        {
            return false;
        }

        if (method > 0 && method != Deflate)
        {
            throw new NotSupportedException($"VTF compression method {method} is not supported.");
        }

        long index = (long)(MipCount - 1) * Frames * faces;
        if (index > (data.Length - resource - 12) / 4)
        {
            throw VtfDecoder.Error("The VTF compression resource is truncated.");
        }

        long offset = start;
        ReadOnlySpan<byte> lengths = data.Slice((int)(resource + 8), (int)(index + 1) * 4);
        for (int i = 0; i < index; i++)
        {
            offset += BinaryPrimitives.ReadUInt32LittleEndian(lengths[(4 * i)..]);
        }

        uint length = BinaryPrimitives.ReadUInt32LittleEndian(lengths[(int)(4 * index)..]);
        if (offset > data.Length || length > data.Length - offset)
        {
            throw VtfDecoder.Error("The VTF compressed image data is truncated.");
        }

        decompressed = Inflate(data.Slice((int)offset, (int)length).ToArray(), size);
        return true;
    }

    private static byte[] Inflate(byte[] compressed, long size)
    {
        if (size > Array.MaxLength)
        {
            throw new NotSupportedException("The VTF mip level is too large.");
        }

        byte[] result = new byte[size];
        try
        {
            using var stream = new System.IO.Compression.ZLibStream(new MemoryStream(compressed), System.IO.Compression.CompressionMode.Decompress);
            if (stream.ReadAtLeast(result, result.Length, throwOnEndOfStream: false) < result.Length)
            {
                throw VtfDecoder.Error("The VTF compressed image data is shorter than the mip level.");
            }
        }
        catch (InvalidDataException e)
        {
            throw new ImageFormatException(VtfFormat.Descriptor, "The VTF compressed image data is corrupt.", e);
        }

        return result;
    }

    private static long Multiply(long a, long b) => b != 0 && a > long.MaxValue / b ? long.MaxValue : a * b;

    private static long Add(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;
}
