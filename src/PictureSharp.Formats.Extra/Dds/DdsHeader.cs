using System.Buffers.Binary;

namespace PictureSharp.Formats.Dds;

/// <summary>The DDS magic, the 124-byte DDS_HEADER and the optional 20-byte DDS_HEADER_DXT10.</summary>
internal readonly record struct DdsHeader(
    int Width,
    int Height,
    uint PixelFlags,
    uint FourCC,
    int BitCount,
    uint RedMask,
    uint GreenMask,
    uint BlueMask,
    uint AlphaMask,
    bool HasDx10,
    uint DxgiFormat,
    uint AlphaMode)
{
    public const int Size = 128;
    public const int Dx10Size = 20;

    public const uint AlphaPixels = 0x1;
    public const uint Alpha = 0x2;
    public const uint FourCCFlag = 0x4;
    public const uint PaletteIndexed8 = 0x20;
    public const uint Rgb = 0x40;
    public const uint Yuv = 0x200;
    public const uint Luminance = 0x20000;

    public static readonly uint Dx10 = Code("DX10");

    /// <summary>Where the pixel data of the first surface starts.</summary>
    public int DataOffset => Size + (HasDx10 ? Dx10Size : 0);

    public static uint Code(string fourCC) =>
        (uint)(fourCC[0] | (fourCC[1] << 8) | (fourCC[2] << 16) | (fourCC[3] << 24));

    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[..4].SequenceEqual("DDS "u8) && BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) == 124;

    public static DdsHeader Parse(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data))
        {
            throw DdsDecoder.Error("Not a DDS file or the header size is not 124.");
        }

        if (data.Length < Size)
        {
            throw DdsDecoder.Error("The DDS header is truncated.");
        }

        uint height = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        uint width = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
        {
            throw DdsDecoder.Error("The DDS image has invalid dimensions.");
        }

        ReadOnlySpan<byte> pf = data.Slice(76, 32);
        uint fourCC = BinaryPrimitives.ReadUInt32LittleEndian(pf[8..]);
        uint pixelFlags = BinaryPrimitives.ReadUInt32LittleEndian(pf[4..]);
        bool dx10 = (pixelFlags & FourCCFlag) != 0 && fourCC == Dx10;
        uint dxgi = 0, alphaMode = 0;
        if (dx10)
        {
            if (data.Length < Size + Dx10Size)
            {
                throw DdsDecoder.Error("The DDS DX10 header is truncated.");
            }

            dxgi = BinaryPrimitives.ReadUInt32LittleEndian(data[Size..]);
            alphaMode = BinaryPrimitives.ReadUInt32LittleEndian(data[(Size + 16)..]) & 7;
        }

        return new DdsHeader(
            (int)width,
            (int)height,
            pixelFlags,
            fourCC,
            (int)BinaryPrimitives.ReadUInt32LittleEndian(pf[12..]),
            BinaryPrimitives.ReadUInt32LittleEndian(pf[16..]),
            BinaryPrimitives.ReadUInt32LittleEndian(pf[20..]),
            BinaryPrimitives.ReadUInt32LittleEndian(pf[24..]),
            BinaryPrimitives.ReadUInt32LittleEndian(pf[28..]),
            dx10,
            dxgi,
            alphaMode);
    }
}
