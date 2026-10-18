using System.Buffers.Binary;
using System.IO.Compression;
using PictureSharp.Formats.Vtf;

namespace PictureSharp.Formats.Extra.Tests;

public sealed class VtfTests
{
    [Test]
    public async Task PackedFormats_WidenByTruncation()
    {
        // RGB565 stores red in the low bits; BGR565 blue. 0x8410 = red 16, green 32, blue 16 (BGR565: the reverse).
        await Assert.That(Pixel(4, [0x10, 0x84])).IsEqualTo(new Rgba32(16 * 255 / 31, 32 * 255 / 63, 16 * 255 / 31));
        await Assert.That(Pixel(17, [0x1f, 0x00])).IsEqualTo(new Rgba32(0, 0, 255));
        await Assert.That(Pixel(19, [0x21, 0x43])).IsEqualTo(new Rgba32(3 * 17, 2 * 17, 1 * 17, 4 * 17));     // BGRA4444
        await Assert.That(Pixel(21, [0x00, 0x7c])).IsEqualTo(new Rgba32(255, 0, 0, 0));                      // BGRA5551, alpha bit clear
        await Assert.That(Pixel(18, [0x00, 0x7c])).IsEqualTo(new Rgba32(255, 0, 0));                         // BGRX5551
    }

    [Test]
    public async Task Bluescreen_KeysOutPureBlue()
    {
        await Assert.That(Pixel(9, [0, 0, 255])).IsEqualTo(new Rgba32(0, 0, 255, 0));
        await Assert.That(Pixel(10, [255, 0, 0])).IsEqualTo(new Rgba32(0, 0, 255, 0));
        await Assert.That(Pixel(9, [0, 1, 255])).IsEqualTo(new Rgba32(0, 1, 255));
    }

    [Test]
    public async Task SixteenBitChannels_RoundToNearest()
    {
        byte[] p = [0x80, 0x00, 0x81, 0x00, 0xff, 0xff, 0x00, 0x00];   // 128 -> 0, 129 -> 1, 65535 -> 255, 0
        await Assert.That(Pixel(25, p)).IsEqualTo(new Rgba32(0, 1, 255, 0));
        await Assert.That(TestSupport.Identify(Vtf(25, p)).BitsPerChannel).IsEqualTo(16);
    }

    [Test]
    public async Task Version76_InflatesDeflateCompressedLevels()
    {
        // Two levels (2x1 and 1x1), stored smallest first, each compressed on its own.
        byte[] small = Zlib([9, 9, 9, 255]), large = Zlib([10, 20, 30, 255, 40, 50, 60, 255]);
        byte[] file = Vtf76(2, 1, 2, [.. small, .. large], [(uint)small.Length, (uint)large.Length], method: 8);
        using Image image = TestSupport.Load(file);
        await Assert.That(TestSupport.Pixels(image)).IsEquivalentTo(new[] { new Rgba32(10, 20, 30), new Rgba32(40, 50, 60) }, CollectionOrdering.Matching);

        byte[] zstd = Vtf76(2, 1, 2, [.. small, .. large], [(uint)small.Length, (uint)large.Length], method: 93);
        await Assert.That(() => TestSupport.Load(zstd)).Throws<NotSupportedException>();
        byte[] corrupt = Vtf76(2, 1, 2, [.. small, 1, 2, 3, 4, 5, 6], [(uint)small.Length, 6], method: 8);
        await Assert.That(() => TestSupport.Load(corrupt)).Throws<ImageFormatException>();
    }

    [Test]
    public async Task UnsupportedFeatures_ThrowNotSupported()
    {
        await Assert.That(() => TestSupport.Load(Vtf(29, new byte[16]))).Throws<NotSupportedException>();    // RGBA32323232F
        await Assert.That(() => TestSupport.Load(Vtf(7, new byte[1]))).Throws<NotSupportedException>();      // P8
        byte[] future = Vtf(0, new byte[4]);
        future[8] = 7;
        await Assert.That(() => TestSupport.Load(future)).Throws<NotSupportedException>();
    }

    [Test]
    public async Task InvalidFiles_ThrowImageFormatException()
    {
        await Assert.That(() => TestSupport.Load(Vtf(0, new byte[4])[..^1])).Throws<ImageFormatException>();
        await Assert.That(() => TestSupport.Load(Vtf(-1, new byte[4]))).Throws<ImageFormatException>();
        byte[] noMips = Vtf(0, new byte[4]);
        noMips[56] = 0;
        await Assert.That(() => TestSupport.Load(noMips)).Throws<ImageFormatException>();
        byte[] badHeader = Vtf(0, new byte[4]);
        BinaryPrimitives.WriteUInt32LittleEndian(badHeader.AsSpan(12), 10_000);
        await Assert.That(() => TestSupport.Load(badHeader)).Throws<ImageFormatException>();
    }

    [Test]
    public async Task IsSupported_ChecksSignatureAndMajorVersion()
    {
        var format = new VtfFormat();
        await Assert.That(format.IsSupported("VTF\0\x07\0\0\0"u8)).IsTrue();
        await Assert.That(format.IsSupported("VTF\0\x08\0\0\0"u8)).IsFalse();
        await Assert.That(format.IsSupported("VTF\0"u8)).IsFalse();
    }

    private static Rgba32 Pixel(int format, byte[] data)
    {
        using Image image = TestSupport.Load(Vtf(format, data));
        return image.GetPixelSpan()[0];
    }

    /// <summary>A 1x1 version 7.2 file without a thumbnail or mip chain.</summary>
    private static byte[] Vtf(int format, byte[] data)
    {
        byte[] header = Header(2, 80, 1, 1, format, 1);
        return [.. header, .. data];
    }

    private static byte[] Vtf76(int width, int height, int mips, byte[] data, uint[] lengths, int method)
    {
        // Resources: the image data and the AXC resource (length, level | method << 16, one length per level).
        byte[] axc = new byte[8 + 4 * lengths.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(axc, (uint)(axc.Length - 4));
        BinaryPrimitives.WriteUInt32LittleEndian(axc.AsSpan(4), (uint)(9 | method << 16));
        for (int i = 0; i < lengths.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(axc.AsSpan(8 + 4 * i), lengths[i]);
        }

        const int size = 96;
        byte[] header = Header(6, size, width, height, 0, mips);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(68), 2);
        header[80] = 0x30;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(84), size + (uint)axc.Length);
        "AXC"u8.CopyTo(header.AsSpan(88));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(92), size);
        return [.. header, .. axc, .. data];
    }

    private static byte[] Header(int minor, int size, int width, int height, int format, int mips)
    {
        byte[] header = new byte[size];
        "VTF\0"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)minor);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(18), (ushort)height);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(24), 1);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(52), format);
        header[56] = (byte)mips;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(57), -1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(63), 1);
        return header;
    }

    private static byte[] Zlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}
