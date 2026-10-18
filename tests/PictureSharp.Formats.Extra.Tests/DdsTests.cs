using System.Buffers.Binary;
using PictureSharp.Formats.Dds;

namespace PictureSharp.Formats.Extra.Tests;

public sealed class DdsTests
{
    [Test]
    [Arguments(DdsPixelFormat.Auto, true, true)]
    [Arguments(DdsPixelFormat.Auto, false, false)]
    [Arguments(DdsPixelFormat.Bgra32, false, true)]
    [Arguments(DdsPixelFormat.Bgrx32, false, false)]
    public async Task RoundTrip_KeepsEveryPixel(DdsPixelFormat format, bool alpha, bool expectAlphaChannel)
    {
        using Image image = TestSupport.CreateImage(37, 19, alpha);
        byte[] encoded = TestSupport.Encode(image, new DdsEncoderOptions { PixelFormat = format });
        using Image decoded = TestSupport.Load(encoded);

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(80));
        await Assert.That(encoded.Length).IsEqualTo(128 + 37 * 19 * 4);
        await Assert.That(flags).IsEqualTo(expectAlphaChannel ? 0x41u : 0x40u);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(104))).IsEqualTo(expectAlphaChannel ? 0xff000000 : 0u);
        await Assert.That(TestSupport.Identify(encoded).HasAlpha).IsEqualTo(expectAlphaChannel);
        await Assert.That(TestSupport.Pixels(decoded)).IsEquivalentTo(TestSupport.Pixels(image), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Bgrx32_DropsAlpha()
    {
        using Image image = TestSupport.CreateImage(9, 4, alpha: true);
        using Image decoded = TestSupport.Load(TestSupport.Encode(image, new DdsEncoderOptions { PixelFormat = DdsPixelFormat.Bgrx32 }));
        await Assert.That(TestSupport.Pixels(decoded).All(px => px.A == 255)).IsTrue();
    }

    [Test]
    [Arguments("dds/bc1-random.dds", true)]
    [Arguments("dds/bc3-random.dds", true)]
    [Arguments("dds/bc4-random-ati1.dds", false)]
    [Arguments("dds/bc5-random-dx10.dds", false)]
    [Arguments("dds/bc6h-random.dds", false)]
    [Arguments("dds/bc7-random.dds", true)]
    [Arguments("dds/rgb.dds", false)]
    [Arguments("dds/graya.dds", true)]
    public async Task Identify_ReportsAlphaByPixelFormat(string name, bool alpha)
    {
        await Assert.That(TestSupport.Identify(TestSupport.ReadCorpus(name)).HasAlpha).IsEqualTo(alpha);
    }

    [Test]
    public async Task Dxt2_IsUnpremultiplied()
    {
        // BC2 block: alpha 8/15 (136) everywhere, colour endpoint 0 = grey 0x8410 (132, 130, 132).
        byte[] block = [.. Enumerable.Repeat((byte)0x88, 8), 0x10, 0x84, 0, 0, 0, 0, 0, 0];
        using Image image = TestSupport.Load(Dds(4, 4, block, fourCC: "DXT2"));
        await Assert.That(image.GetPixelSpan()[0]).IsEqualTo(new Rgba32(248, 244, 248, 136));
    }

    [Test]
    [Arguments(2u, 248, 136)]   // premultiplied
    [Arguments(3u, 132, 255)]   // opaque: alpha is undefined and ignored
    [Arguments(1u, 132, 136)]   // straight
    public async Task Dx10AlphaMode_ControlsAlpha(uint alphaMode, int red, int alpha)
    {
        byte[] block = [.. Enumerable.Repeat((byte)0x88, 8), 0x10, 0x84, 0, 0, 0, 0, 0, 0];
        using Image image = TestSupport.Load(Dds(4, 4, block, dxgi: 74, alphaMode: alphaMode));
        await Assert.That((int)image.GetPixelSpan()[0].R).IsEqualTo(red);
        await Assert.That((int)image.GetPixelSpan()[0].A).IsEqualTo(alpha);
    }

    [Test]
    public async Task Bc7ReservedMode_DecodesToOpaqueBlack()
    {
        using Image image = TestSupport.Load(Dds(4, 4, new byte[16], dxgi: 98));
        await Assert.That(TestSupport.Pixels(image).All(px => px == new Rgba32(0, 0, 0))).IsTrue();
    }

    [Test]
    public async Task Bc6HReservedMode_DecodesToBlack()
    {
        byte[] block = new byte[16];
        Array.Fill(block, (byte)0xff);
        block[0] = 0b10011;
        using Image image = TestSupport.Load(Dds(4, 4, block, dxgi: 95));
        await Assert.That(TestSupport.Pixels(image).All(px => px == new Rgba32(0, 0, 0))).IsTrue();
    }

    [Test]
    public async Task Luminance16_AndAlpha8_UseTheirMasks()
    {
        // L16: 0xffff and 0x8000 scale to 255 and int(32768 / 65535 * 255) = 127.
        using Image gray = TestSupport.Load(Dds(2, 1, [0xff, 0xff, 0x00, 0x80], flags: 0x20000, bitCount: 16, masks: [0xffff, 0, 0, 0]));
        await Assert.That(gray.GetPixelSpan()[0]).IsEqualTo(new Rgba32(255, 255, 255));
        await Assert.That(gray.GetPixelSpan()[1]).IsEqualTo(new Rgba32(127, 127, 127));

        using Image alpha = TestSupport.Load(Dds(2, 1, [0x40, 0xc0], flags: 0x2, bitCount: 8, masks: [0, 0, 0, 0xff]));
        await Assert.That(alpha.GetPixelSpan()[0]).IsEqualTo(new Rgba32(0, 0, 0, 0x40));
        await Assert.That(alpha.GetPixelSpan()[1]).IsEqualTo(new Rgba32(0, 0, 0, 0xc0));
    }

    [Test]
    public async Task Bc1_PartialBlocks_AreCropped()
    {
        // A 5×3 image needs 2×1 blocks; the first block is red, the second blue.
        byte[] blocks = [0x00, 0xf8, 0, 0, 0, 0, 0, 0, 0x1f, 0x00, 0, 0, 0, 0, 0, 0];
        using Image image = TestSupport.Load(Dds(5, 3, blocks, fourCC: "DXT1"));
        await Assert.That(image.Width).IsEqualTo(5);
        await Assert.That(image.GetPixelSpan()[3]).IsEqualTo(new Rgba32(255, 0, 0));
        await Assert.That(image.GetPixelSpan()[4]).IsEqualTo(new Rgba32(0, 0, 255));
        await Assert.That(image.GetPixelSpan()[14]).IsEqualTo(new Rgba32(0, 0, 255));
    }

    [Test]
    [Arguments(10u)]    // R16G16B16A16_FLOAT
    [Arguments(2u)]     // R32G32B32A32_FLOAT
    [Arguments(134u)]   // ASTC
    public async Task UnsupportedDxgiFormat_ThrowsNotSupported(uint dxgi)
    {
        await Assert.That(() => TestSupport.Load(Dds(4, 4, new byte[256], dxgi: dxgi))).Throws<NotSupportedException>();
    }

    [Test]
    public async Task UnsupportedLegacyFormats_ThrowNotSupported()
    {
        byte[] float16 = Dds(4, 4, new byte[128], fourCC: "q\0\0\0");   // D3DFMT_A16B16G16R16F = 113
        byte[] yuv = Dds(4, 4, new byte[64], flags: 0x200, bitCount: 16);
        await Assert.That(() => TestSupport.Load(float16)).Throws<NotSupportedException>();
        await Assert.That(() => TestSupport.Load(yuv)).Throws<NotSupportedException>();
    }

    [Test]
    public async Task Decode_TruncatedOrInvalid_ThrowsImageFormatException()
    {
        byte[] bc7 = TestSupport.ReadCorpus("dds/bc7-random.dds");
        byte[] rgb = TestSupport.ReadCorpus("dds/rgb.dds");
        await Assert.That(() => TestSupport.Load(bc7[..1000])).Throws<ImageFormatException>();
        await Assert.That(() => TestSupport.Load(rgb[..200])).Throws<ImageFormatException>();
        await Assert.That(() => TestSupport.Load(bc7[..140])).Throws<ImageFormatException>();

        byte[] zeroWidth = [.. bc7];
        BinaryPrimitives.WriteUInt32LittleEndian(zeroWidth.AsSpan(16), 0);
        await Assert.That(() => TestSupport.Load(zeroWidth)).Throws<ImageFormatException>();

        byte[] noFormat = Dds(4, 4, new byte[64], flags: 0, bitCount: 32);
        await Assert.That(() => TestSupport.Load(noFormat)).Throws<ImageFormatException>();
    }

    [Test]
    public async Task IsSupported_ChecksMagicAndHeaderSize()
    {
        var format = new DdsFormat();
        await Assert.That(format.IsSupported(TestSupport.ReadCorpus("dds/rgb.dds"))).IsTrue();
        await Assert.That(format.IsSupported("DDS \x7c\0\0"u8)).IsFalse();
        await Assert.That(format.IsSupported("DDS \x7d\0\0\0"u8)).IsFalse();
        await Assert.That(format.IsSupported("\x89PNG\r\n\x1a\n"u8)).IsFalse();
    }

    [Test]
    public async Task MipLevel_SelectsLevelOfTheChain()
    {
        // L8 4x4 with levels 4x4, 2x2 and 1x1 holding 10, 20 and 30.
        byte[] file = Dds(4, 4, [.. Fill(16, 10), .. Fill(4, 20), 30], flags: 0x20000, bitCount: 8, mips: 3);
        using Image level1 = TestSupport.Load(file, Select(new DdsDecoderOptions { MipLevel = 1 }));
        using Image level2 = TestSupport.Load(file, Select(new DdsDecoderOptions { MipLevel = 2 }));
        await Assert.That(TestSupport.Pixels(level1)).IsEquivalentTo(Enumerable.Repeat(new Rgba32(20, 20, 20), 4), CollectionOrdering.Matching);
        await Assert.That(level2.GetPixelSpan()[0]).IsEqualTo(new Rgba32(30, 30, 30));
        ImageInfo info = Image.Identify(file, Select(new DdsDecoderOptions { MipLevel = 1 }));
        await Assert.That((info.Width, info.Height)).IsEqualTo((2, 2));
        await Assert.That(() => TestSupport.Load(file, Select(new DdsDecoderOptions { MipLevel = 3 }))).Throws<ImageFormatException>();
        await Assert.That(() => Image.Identify(file, Select(new DdsDecoderOptions { Layer = 1 }))).Throws<ImageFormatException>();
    }

    [Test]
    public async Task Layer_SelectsArrayElementWithItsChain()
    {
        // DX10 R8_UNORM array of two 2x2 elements with two levels: element 0 = 1, 2; element 1 = 3, 4.
        byte[] file = Dds(2, 2, [.. Fill(4, 1), 2, .. Fill(4, 3), 4], dxgi: 61, mips: 2, arraySize: 2);
        using Image element1 = TestSupport.Load(file, Select(new DdsDecoderOptions { Layer = 1 }));
        using Image element1Mip = TestSupport.Load(file, Select(new DdsDecoderOptions { Layer = 1, MipLevel = 1 }));
        await Assert.That(element1.GetPixelSpan()[3]).IsEqualTo(new Rgba32(3, 0, 0));
        await Assert.That(element1Mip.GetPixelSpan()[0]).IsEqualTo(new Rgba32(4, 0, 0));
        await Assert.That(() => TestSupport.Load(file, Select(new DdsDecoderOptions { Layer = 2 }))).Throws<ImageFormatException>();
    }

    [Test]
    public async Task Layer_CountsStoredCubeFacesAndDx10Cubes()
    {
        // DX9 cube map storing only +X and +Y: layer 1 is +Y.
        byte[] partial = Dds(1, 1, [7, 8], flags: 0x20000, bitCount: 8, caps2: 0x200 | 0x400 | 0x1000);
        using Image face = TestSupport.Load(partial, Select(new DdsDecoderOptions { Layer = 1 }));
        await Assert.That(face.GetPixelSpan()[0]).IsEqualTo(new Rgba32(8, 8, 8));
        await Assert.That(() => TestSupport.Load(partial, Select(new DdsDecoderOptions { Layer = 2 }))).Throws<ImageFormatException>();

        // DX10 cube array of two elements: twelve faces.
        byte[] cubes = Dds(1, 1, [.. Enumerable.Range(0, 12).Select(i => (byte)i)], dxgi: 61, arraySize: 2, miscFlag: 0x4);
        using Image last = TestSupport.Load(cubes, Select(new DdsDecoderOptions { Layer = 11 }));
        await Assert.That(last.GetPixelSpan()[0]).IsEqualTo(new Rgba32(11, 0, 0));
    }

    [Test]
    public async Task Layer_SelectsVolumeSliceOfTheLevel()
    {
        // DX9 volume 2x2x4 with two levels: four 2x2 slices (1–4), then two 1x1 slices (5, 6).
        byte[] file = Dds(2, 2, [.. Fill(4, 1), .. Fill(4, 2), .. Fill(4, 3), .. Fill(4, 4), 5, 6],
            flags: 0x20000, bitCount: 8, mips: 2, depth: 4, caps2: 0x200000);
        using Image slice2 = TestSupport.Load(file, Select(new DdsDecoderOptions { Layer = 2 }));
        using Image smallSlice1 = TestSupport.Load(file, Select(new DdsDecoderOptions { MipLevel = 1, Layer = 1 }));
        await Assert.That(slice2.GetPixelSpan()[0]).IsEqualTo(new Rgba32(3, 3, 3));
        await Assert.That(smallSlice1.GetPixelSpan()[0]).IsEqualTo(new Rgba32(6, 6, 6));
        await Assert.That(() => TestSupport.Load(file, Select(new DdsDecoderOptions { MipLevel = 1, Layer = 2 }))).Throws<ImageFormatException>();
    }

    [Test]
    public async Task MipLevel_OfBlockCompressedPalettedAndTruncatedFiles()
    {
        // BC1 8x8: level 0 is four blocks, level 1 one red block.
        byte[] red = [0x00, 0xf8, 0, 0, 0, 0, 0, 0];
        byte[] bc1 = Dds(8, 8, [.. new byte[32], .. red], fourCC: "DXT1", mips: 2);
        using Image mip = TestSupport.Load(bc1, Select(new DdsDecoderOptions { MipLevel = 1 }));
        await Assert.That(mip.GetPixelSpan()[15]).IsEqualTo(new Rgba32(255, 0, 0));

        // The palette precedes every surface.
        byte[] palette = new byte[1024];
        palette[4 * 9 + 1] = 200;
        palette[4 * 9 + 3] = 255;
        byte[] paletted = Dds(2, 1, [.. palette, 0, 0, 9], flags: 0x20, bitCount: 8, mips: 2);
        using Image small = TestSupport.Load(paletted, Select(new DdsDecoderOptions { MipLevel = 1 }));
        await Assert.That(small.GetPixelSpan()[0]).IsEqualTo(new Rgba32(0, 200, 0));

        await Assert.That(() => TestSupport.Load(bc1[..^1], Select(new DdsDecoderOptions { MipLevel = 1 }))).Throws<ImageFormatException>();
        await Assert.That(() => TestSupport.Load(bc1[..150], Select(new DdsDecoderOptions { MipLevel = 1 }))).Throws<ImageFormatException>();
    }

    [Test]
    public async Task DecoderOptions_RejectNegativeIndices()
    {
        await Assert.That(() => new DdsDecoderOptions { MipLevel = -1 }).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new DdsDecoderOptions { Layer = -1 }).Throws<ArgumentOutOfRangeException>();
    }

    private static DecoderOptions Select(DdsDecoderOptions options) => TestSupport.Options with { FormatOptions = [options] };

    private static byte[] Fill(int count, byte value) => Enumerable.Repeat(value, count).ToArray();

    private static byte[] Dds(int width, int height, byte[] body, string? fourCC = null, uint? dxgi = null, uint alphaMode = 0,
        uint flags = 0, int bitCount = 0, uint[]? masks = null, int mips = 0, int depth = 0, uint caps2 = 0, uint arraySize = 1,
        uint miscFlag = 0)
    {
        byte[] header = new byte[dxgi is null ? 128 : 148];
        "DDS "u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 0x1007);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), (uint)depth);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), (uint)mips);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(76), 32);
        if (dxgi is not null)
        {
            fourCC = "DX10";
        }

        if (fourCC is not null)
        {
            flags |= 0x4;
            for (int i = 0; i < 4; i++)
            {
                header[84 + i] = (byte)fourCC[i];
            }
        }

        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(80), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(88), (uint)bitCount);
        for (int i = 0; i < 4 && masks is not null; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(92 + 4 * i), masks[i]);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(108), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(112), caps2);
        if (dxgi is not null)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(128), dxgi.Value);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(132), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(136), miscFlag);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(140), arraySize);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(144), alphaMode);
        }

        return [.. header, .. body];
    }
}
