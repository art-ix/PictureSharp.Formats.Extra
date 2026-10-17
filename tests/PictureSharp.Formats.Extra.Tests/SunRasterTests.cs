using System.Buffers.Binary;
using PictureSharp.Formats.SunRaster;

namespace PictureSharp.Formats.Extra.Tests;

public sealed class SunRasterTests
{
    private static readonly Rgba32 Black = new(0, 0, 0), White = new(255, 255, 255);

    [Test]
    public async Task OneBit_WithoutMap_SetBitsAreBlack()
    {
        // Three pixels in one byte; the row is padded to 16 bits.
        using Image image = TestSupport.Load(Ras(3, 1, 1, 1, [0b1010_0000, 0]));
        await Assert.That(TestSupport.Pixels(image)).IsEquivalentTo(new[] { Black, White, Black }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task OneBit_WithMap_IndexesTheMap()
    {
        byte[] map = [200, 10, 30, 40, 100, 250];   // planar: reds, greens, blues
        using Image image = TestSupport.Load(Ras(2, 1, 1, 1, [0b0100_0000, 0], map: map));
        await Assert.That(TestSupport.Pixels(image)).IsEquivalentTo(new[] { new Rgba32(200, 30, 100), new Rgba32(10, 40, 250) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ThirtyTwoBit_FirstByteIsPadding()
    {
        using Image bgr = TestSupport.Load(Ras(1, 1, 32, 1, [0, 30, 20, 10]));
        using Image rgb = TestSupport.Load(Ras(1, 1, 32, 3, [0, 10, 20, 30]));
        await Assert.That(bgr.GetPixelSpan()[0]).IsEqualTo(new Rgba32(10, 20, 30));
        await Assert.That(rgb.GetPixelSpan()[0]).IsEqualTo(new Rgba32(10, 20, 30));
        await Assert.That(TestSupport.Identify(Ras(1, 1, 32, 1, [0, 30, 20, 10])).HasAlpha).IsFalse();
    }

    [Test]
    public async Task Rle_RunsCoverPaddingAndCrossRows()
    {
        // 3x2 greyscale, rows of 4 bytes with padding: one run of 5 then a literal 0x80, padding and 9.
        byte[] stream = [0x80, 4, 7, 0x80, 0x00, 0, 9];
        using Image image = TestSupport.Load(Ras(3, 2, 8, 2, stream));
        await Assert.That(TestSupport.Pixels(image).Select(px => (int)px.R)).IsEquivalentTo(new[] { 7, 7, 7, 7, 128, 0 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Rle_RunPastTheLastRow_IsIgnored()
    {
        using Image image = TestSupport.Load(Ras(2, 1, 8, 2, [0x80, 200, 5]));
        await Assert.That(TestSupport.Pixels(image).All(px => px == new Rgba32(5, 5, 5))).IsTrue();
    }

    [Test]
    public async Task UnsupportedFeatures_ThrowNotSupported()
    {
        await Assert.That(() => TestSupport.Load(Ras(1, 1, 16, 1, [0, 0]))).Throws<NotSupportedException>();
        await Assert.That(() => TestSupport.Load(Ras(1, 1, 8, 0xFFFF, [0, 0]))).Throws<NotSupportedException>();
        await Assert.That(() => TestSupport.Load(Ras(1, 1, 8, 1, [0, 0], map: [1, 2, 3], mapType: 2))).Throws<NotSupportedException>();
    }

    [Test]
    public async Task InvalidFiles_ThrowImageFormatException()
    {
        await Assert.That(() => TestSupport.Load(Ras(1, 1, 8, 6, [0, 0]))).Throws<ImageFormatException>();                    // unknown type
        await Assert.That(() => TestSupport.Load(Ras(1, 1, 8, 1, [0, 0], map: [1, 2]))).Throws<ImageFormatException>();      // map length
        await Assert.That(() => TestSupport.Load(Ras(1, 1, 8, 1, [3, 0], map: [1, 2, 3]))).Throws<ImageFormatException>();   // index 3 of 1
        await Assert.That(() => TestSupport.Load(Ras(3, 2, 8, 1, [1, 2, 3, 0]))).Throws<ImageFormatException>();             // one row
        await Assert.That(() => TestSupport.Load(Ras(3, 2, 8, 2, [0x80, 4, 7, 0x80]))).Throws<ImageFormatException>();      // RLE truncated
        await Assert.That(() => TestSupport.Load(Ras(0, 1, 8, 1, []))).Throws<ImageFormatException>();
    }

    private static byte[] Ras(int width, int height, int depth, int type, byte[] data, byte[]? map = null, int mapType = 1)
    {
        map ??= [];
        byte[] header = new byte[32];
        uint[] fields = [0x59A66A95, (uint)width, (uint)height, (uint)depth, (uint)data.Length, (uint)type, map.Length == 0 ? 0u : (uint)mapType, (uint)map.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4 * i), fields[i]);
        }

        return [.. header, .. map, .. data];
    }
}
