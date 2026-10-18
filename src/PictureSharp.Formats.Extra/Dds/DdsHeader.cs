using System.Buffers.Binary;
using System.Numerics;

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
    uint AlphaMode,
    int Depth,
    int MipMapCount,
    uint Caps2,
    uint Dimension,
    uint MiscFlag,
    uint ArraySize)
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

    private const uint CubeMap = 0x200;
    private const uint CubeMapFaces = 0xfc00;
    private const uint Volume = 0x200000;
    private const uint Texture3D = 4;
    private const uint TextureCube = 0x4;

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
        uint dxgi = 0, alphaMode = 0, dimension = 0, miscFlag = 0, arraySize = 1;
        if (dx10)
        {
            if (data.Length < Size + Dx10Size)
            {
                throw DdsDecoder.Error("The DDS DX10 header is truncated.");
            }

            dxgi = BinaryPrimitives.ReadUInt32LittleEndian(data[Size..]);
            dimension = BinaryPrimitives.ReadUInt32LittleEndian(data[(Size + 4)..]);
            miscFlag = BinaryPrimitives.ReadUInt32LittleEndian(data[(Size + 8)..]);
            arraySize = BinaryPrimitives.ReadUInt32LittleEndian(data[(Size + 12)..]);
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
            alphaMode,
            (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data[24..]), int.MaxValue),
            (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data[28..]), int.MaxValue),
            BinaryPrimitives.ReadUInt32LittleEndian(data[112..]),
            dimension,
            miscFlag,
            arraySize);
    }

    /// <summary>
    /// The size and data offset (after the palette) of the selected surface. Arrays and cube maps store every layer
    /// with its whole mip chain; volume textures store every slice of a mip level before the next level, with half the
    /// slices per level. The mip count is read as DirectXTex does (0 means 1, the flag is ignored) and capped at the
    /// full chain. The default selection needs none of these fields, so it decodes whatever they declare.
    /// </summary>
    public DdsSelection Select(DdsSurface surface, DdsDecoderOptions? options)
    {
        if (options is null || options is { MipLevel: 0, Layer: 0 })
        {
            return new DdsSelection(Width, Height, 0);
        }

        int mip = options.MipLevel, layer = options.Layer;
        int mips = Math.Min(Math.Max(1, MipMapCount), 32 - BitOperations.LeadingZeroCount((uint)Math.Max(Width, Height)));
        if (mip >= mips)
        {
            throw DdsDecoder.Error($"The DDS file has {mips} mip levels; level {mip} was requested.");
        }

        long offset = 0;
        bool volume = HasDx10 ? Dimension == Texture3D : (Caps2 & Volume) != 0;
        if (volume)
        {
            for (int m = 0; m < mip; m++)
            {
                offset = Add(offset, Multiply(LevelSize(surface, m), Math.Max(1, Depth >> m)));
            }

            int slices = Math.Max(1, Depth >> mip);
            if (layer >= slices)
            {
                throw DdsDecoder.Error($"Mip level {mip} of the DDS volume has {slices} slices; slice {layer} was requested.");
            }

            offset = Add(offset, Multiply(LevelSize(surface, mip), layer));
        }
        else
        {
            long layers = Layers;
            if (layer >= layers)
            {
                throw DdsDecoder.Error($"The DDS file has {layers} layers; layer {layer} was requested.");
            }

            long chain = 0;
            for (int m = 0; m < mips; m++)
            {
                if (m == mip)
                {
                    offset = chain;
                }

                chain = Add(chain, LevelSize(surface, m));
            }

            offset = Add(offset, Multiply(chain, layer));
        }

        return new DdsSelection(Math.Max(1, Width >> mip), Math.Max(1, Height >> mip), offset);
    }

    // DX10: array elements, six per cube; DX9: the stored cube faces, or one surface.
    private long Layers => HasDx10
        ? Math.Max(1, (long)ArraySize) * ((MiscFlag & TextureCube) != 0 ? 6 : 1)
        : (Caps2 & CubeMap) != 0 ? Math.Max(1, BitOperations.PopCount(Caps2 & CubeMapFaces)) : 1;

    private long LevelSize(DdsSurface surface, int mip) => surface.SurfaceSize(Math.Max(1, Width >> mip), Math.Max(1, Height >> mip));

    private static long Multiply(long a, long b) => b != 0 && a > long.MaxValue / b ? long.MaxValue : a * b;

    private static long Add(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;
}

/// <summary>The size of the selected surface and where its bytes start, relative to the data after the palette.</summary>
internal readonly record struct DdsSelection(int Width, int Height, long Offset);
