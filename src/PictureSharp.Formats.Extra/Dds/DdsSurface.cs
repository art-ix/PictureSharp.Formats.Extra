namespace PictureSharp.Formats.Dds;

internal enum DdsSurfaceKind
{
    Masked,
    Luminance8,
    LuminanceAlpha8,
    Palette8,
    Bc1,
    Bc2,
    Bc3,
    Bc4,
    Bc4Signed,
    Bc5,
    Bc5Signed,
    Bc6H,
    Bc6HSigned,
    Bc7,
}

/// <summary>How the pixels of the first surface are stored, resolved from the DX9 pixel format or the DXGI format.</summary>
internal readonly record struct DdsSurface(
    DdsSurfaceKind Kind,
    int BitCount = 0,
    uint RedMask = 0,
    uint GreenMask = 0,
    uint BlueMask = 0,
    uint AlphaMask = 0,
    bool Gray = false,
    bool Premultiplied = false,
    bool Opaque = false)
{
    public bool IsBlockCompressed => Kind >= DdsSurfaceKind.Bc1;

    public int BlockSize => Kind is DdsSurfaceKind.Bc1 or DdsSurfaceKind.Bc4 or DdsSurfaceKind.Bc4Signed ? 8 : 16;

    public int BytesPerPixel => Kind switch
    {
        DdsSurfaceKind.Masked => BitCount / 8,
        DdsSurfaceKind.LuminanceAlpha8 => 2,
        _ => 1,
    };

    /// <summary>Bytes of one surface: whole 4×4 blocks, or tightly packed rows.</summary>
    public long SurfaceSize(int width, int height) => IsBlockCompressed
        ? ((width + 3L) / 4) * ((height + 3L) / 4) * BlockSize
        : (long)width * height * BytesPerPixel;

    public bool HasAlpha => !Opaque && Kind switch
    {
        DdsSurfaceKind.Masked => AlphaMask != 0,
        DdsSurfaceKind.LuminanceAlpha8 or DdsSurfaceKind.Palette8 => true,
        DdsSurfaceKind.Bc1 or DdsSurfaceKind.Bc2 or DdsSurfaceKind.Bc3 or DdsSurfaceKind.Bc7 => true,
        _ => false,
    };

    public static DdsSurface Resolve(DdsHeader header) =>
        header.HasDx10 ? FromDxgi(header) : FromPixelFormat(header);

    // The flags are checked in Pillow's order: RGB, luminance, palette, FourCC.
    private static DdsSurface FromPixelFormat(DdsHeader h)
    {
        uint flags = h.PixelFlags;
        bool alpha = (flags & DdsHeader.AlphaPixels) != 0;
        if ((flags & DdsHeader.Rgb) != 0)
        {
            CheckBitCount(h.BitCount);
            return new DdsSurface(DdsSurfaceKind.Masked, h.BitCount, h.RedMask, h.GreenMask, h.BlueMask, alpha ? h.AlphaMask : 0);
        }

        if ((flags & DdsHeader.Luminance) != 0)
        {
            if (h.BitCount == 8)
            {
                return new DdsSurface(DdsSurfaceKind.Luminance8);
            }

            if (h.BitCount == 16 && alpha)
            {
                return new DdsSurface(DdsSurfaceKind.LuminanceAlpha8);
            }

            CheckBitCount(h.BitCount);
            return new DdsSurface(DdsSurfaceKind.Masked, h.BitCount, h.RedMask, AlphaMask: alpha ? h.AlphaMask : 0, Gray: true);
        }

        if ((flags & DdsHeader.PaletteIndexed8) != 0)
        {
            return new DdsSurface(DdsSurfaceKind.Palette8);
        }

        if ((flags & DdsHeader.FourCCFlag) != 0)
        {
            return FromFourCC(h.FourCC);
        }

        if ((flags & DdsHeader.Alpha) != 0)
        {
            CheckBitCount(h.BitCount);
            return new DdsSurface(DdsSurfaceKind.Masked, h.BitCount, AlphaMask: h.AlphaMask == 0 ? 0xff : h.AlphaMask);
        }

        if ((flags & DdsHeader.Yuv) != 0)
        {
            throw new NotSupportedException("DDS YUV pixel formats are not supported.");
        }

        throw DdsDecoder.Error("The DDS pixel format declares no RGB, luminance, alpha, palette or FourCC data.");
    }

    private static DdsSurface FromFourCC(uint fourCC)
    {
        string code = Name(fourCC);
        return code switch
        {
            "DXT1" => new DdsSurface(DdsSurfaceKind.Bc1),
            "DXT2" => new DdsSurface(DdsSurfaceKind.Bc2, Premultiplied: true),
            "DXT3" => new DdsSurface(DdsSurfaceKind.Bc2),
            "DXT4" => new DdsSurface(DdsSurfaceKind.Bc3, Premultiplied: true),
            "DXT5" => new DdsSurface(DdsSurfaceKind.Bc3),
            "ATI1" or "BC4U" => new DdsSurface(DdsSurfaceKind.Bc4),
            "BC4S" => new DdsSurface(DdsSurfaceKind.Bc4Signed),
            "ATI2" or "BC5U" => new DdsSurface(DdsSurfaceKind.Bc5),
            "BC5S" => new DdsSurface(DdsSurfaceKind.Bc5Signed),
            _ => throw new NotSupportedException($"The DDS pixel format {code} is not supported."),
        };
    }

    private static DdsSurface FromDxgi(DdsHeader h)
    {
        DdsSurface surface = h.DxgiFormat switch
        {
            27 or 28 or 29 => Masked(32, 0xff, 0xff00, 0xff0000, 0xff000000),
            87 or 90 or 91 => Masked(32, 0xff0000, 0xff00, 0xff, 0xff000000),
            88 or 92 or 93 => Masked(32, 0xff0000, 0xff00, 0xff, 0),
            23 or 24 => Masked(32, 0x3ff, 0xffc00, 0x3ff00000, 0xc0000000),
            85 => Masked(16, 0xf800, 0x7e0, 0x1f, 0),
            86 => Masked(16, 0x7c00, 0x3e0, 0x1f, 0x8000),
            115 => Masked(16, 0xf00, 0xf0, 0xf, 0xf000),
            48 or 49 => Masked(16, 0xff, 0xff00, 0, 0),
            60 or 61 => Masked(8, 0xff, 0, 0, 0),
            65 => Masked(8, 0, 0, 0, 0xff),
            70 or 71 or 72 => new DdsSurface(DdsSurfaceKind.Bc1),
            73 or 74 or 75 => new DdsSurface(DdsSurfaceKind.Bc2),
            76 or 77 or 78 => new DdsSurface(DdsSurfaceKind.Bc3),
            79 or 80 => new DdsSurface(DdsSurfaceKind.Bc4),
            81 => new DdsSurface(DdsSurfaceKind.Bc4Signed),
            82 or 83 => new DdsSurface(DdsSurfaceKind.Bc5),
            84 => new DdsSurface(DdsSurfaceKind.Bc5Signed),
            94 or 95 => new DdsSurface(DdsSurfaceKind.Bc6H),
            96 => new DdsSurface(DdsSurfaceKind.Bc6HSigned),
            97 or 98 or 99 => new DdsSurface(DdsSurfaceKind.Bc7),
            _ => throw new NotSupportedException($"The DXGI format {h.DxgiFormat} of this DDS file is not supported."),
        };

        // DDS_ALPHA_MODE: 2 = premultiplied, 3 = opaque (the alpha channel is undefined).
        return h.AlphaMode switch
        {
            2 => surface with { Premultiplied = true },
            3 => surface with { Opaque = true },
            _ => surface,
        };
    }

    private static DdsSurface Masked(int bits, uint r, uint g, uint b, uint a) => new(DdsSurfaceKind.Masked, bits, r, g, b, a);

    private static void CheckBitCount(int bitCount)
    {
        if (bitCount is not (8 or 16 or 24 or 32))
        {
            throw new NotSupportedException($"DDS pixels with {bitCount} bits are not supported.");
        }
    }

    private static string Name(uint fourCC)
    {
        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            char c = (char)((fourCC >> (8 * i)) & 0xff);
            if (c is < ' ' or > '~')
            {
                return $"0x{fourCC:x8}";
            }

            chars[i] = c;
        }

        return new string(chars);
    }
}
