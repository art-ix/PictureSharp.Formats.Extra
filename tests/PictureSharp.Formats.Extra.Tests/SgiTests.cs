using System.Buffers.Binary;
using PictureSharp.Formats.Sgi;

namespace PictureSharp.Formats.Extra.Tests;

public sealed class SgiTests
{
    [Test]
    [Arguments(true, false, false, 3)]
    [Arguments(false, false, false, 3)]
    [Arguments(true, true, false, 4)]
    [Arguments(false, true, false, 4)]
    [Arguments(true, false, true, 1)]
    [Arguments(false, false, true, 1)]
    public async Task RoundTrip_KeepsEveryPixel(bool rle, bool alpha, bool gray, int channels)
    {
        using Image image = TestSupport.CreateImage(300, 23, alpha, gray);
        byte[] encoded = TestSupport.Encode(image, new SgiEncoderOptions { Rle = rle });
        using Image decoded = TestSupport.Load(encoded);
        await Assert.That(encoded[2]).IsEqualTo(rle ? (byte)1 : (byte)0);
        await Assert.That((int)BinaryPrimitives.ReadUInt16BigEndian(encoded.AsSpan(10))).IsEqualTo(channels);
        await Assert.That(TestSupport.Pixels(decoded)).IsEquivalentTo(TestSupport.Pixels(image), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Rle_HandlesLongRunsAndNoise()
    {
        using var image = new Image(1000, 3);
        Span<Rgba32> pixels = image.GetPixelSpan();
        var random = new Random(5);
        for (int i = 0; i < pixels.Length; i++)
        {
            // Row 0: one 1000-pixel run; row 1: noise; row 2: runs of two, which stay literal.
            byte v = (i / 1000) switch
            {
                0 => 7,
                1 => (byte)random.Next(256),
                _ => (byte)(i / 2 % 2 * 200),
            };
            pixels[i] = new Rgba32(v, v, v);
        }

        byte[] encoded = TestSupport.Encode(image, new SgiEncoderOptions());
        using Image decoded = TestSupport.Load(encoded);
        await Assert.That(TestSupport.Pixels(decoded)).IsEquivalentTo(TestSupport.Pixels(image), CollectionOrdering.Matching);
    }

    [Test]
    public async Task OneRow_IsWrittenAsDimensionOne()
    {
        using Image image = TestSupport.CreateImage(9, 1, alpha: false, gray: true);
        byte[] encoded = TestSupport.Encode(image, new SgiEncoderOptions { Rle = false });
        await Assert.That((int)BinaryPrimitives.ReadUInt16BigEndian(encoded.AsSpan(4))).IsEqualTo(1);
        await Assert.That(encoded.Length).IsEqualTo(512 + 9);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TwoChannels_AreGreyAndAlpha(bool rle)
    {
        // Rows are stored bottom up: the bottom row comes first.
        byte[] file = Sgi(3, 2, 2, 1, rle, [[10, 20], [30, 40]], [[255, 0], [128, 64]]);
        using Image image = TestSupport.Load(file);
        await Assert.That(TestSupport.Identify(file).HasAlpha).IsTrue();
        await Assert.That(TestSupport.Pixels(image)).IsEquivalentTo(
            new[] { new Rgba32(30, 30, 30, 128), new Rgba32(40, 40, 40, 64), new Rgba32(10, 10, 10, 255), new Rgba32(20, 20, 20, 0) },
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task SixteenBitSamples_RoundToNearest()
    {
        byte[] file = Sgi(2, 3, 1, 2, false, [[0x00, 0x80, 0x00, 0x81, 0xff, 0xff]]);   // 128 -> 0, 129 -> 1, 65535 -> 255
        using Image image = TestSupport.Load(file);
        await Assert.That(TestSupport.Pixels(image).Select(px => (int)px.R)).IsEquivalentTo(new[] { 0, 1, 255 }, CollectionOrdering.Matching);
        await Assert.That(TestSupport.Identify(file).BitsPerChannel).IsEqualTo(16);
    }

    [Test]
    public async Task UnsupportedFeatures_ThrowNotSupported()
    {
        byte[] colormap = Sgi(2, 1, 1, 1, false, [[1]]);
        BinaryPrimitives.WriteInt32BigEndian(colormap.AsSpan(104), 3);
        await Assert.That(() => TestSupport.Load(colormap)).Throws<NotSupportedException>();
        await Assert.That(() => TestSupport.Load(Sgi(3, 1, 1, 1, false, [[1]], [[2]], [[3]], [[4]], [[5]]))).Throws<NotSupportedException>();

        using var wide = new Image(65536, 1);
        await Assert.That(() => TestSupport.Encode(wide, new SgiEncoderOptions())).Throws<NotSupportedException>();
    }

    [Test]
    public async Task InvalidFiles_ThrowImageFormatException()
    {
        await Assert.That(() => TestSupport.Load(Sgi(4, 1, 1, 1, false, [[1]]))).Throws<ImageFormatException>();     // dimension 4
        await Assert.That(() => TestSupport.Load(Sgi(3, 1, 1, 1, false, [[1]], [[2]], [[3]])[..514])).Throws<ImageFormatException>();

        byte[] overflow = Sgi(2, 2, 1, 1, true, [[5, 6]]);
        overflow[^4] = 0x83;                                 // the literal run of two claims three samples
        await Assert.That(() => TestSupport.Load(overflow)).Throws<ImageFormatException>();

        byte[] outside = Sgi(2, 2, 1, 1, true, [[5, 6]]);
        BinaryPrimitives.WriteUInt32BigEndian(outside.AsSpan(512), 100_000);
        await Assert.That(() => TestSupport.Load(outside)).Throws<ImageFormatException>();

        byte[] early = Sgi(2, 2, 1, 1, true, [[5, 6]]);
        early[^4] = 0;                                       // a zero count before the row is full
        await Assert.That(() => TestSupport.Load(early)).Throws<ImageFormatException>();
    }

    [Test]
    public async Task IsSupported_ChecksMagicStorageAndPrecision()
    {
        var format = new SgiFormat();
        await Assert.That(format.IsSupported([0x01, 0xda, 0x01, 0x01])).IsTrue();
        await Assert.That(format.IsSupported([0x01, 0xda, 0x00, 0x02])).IsTrue();
        await Assert.That(format.IsSupported([0x01, 0xda, 0x02, 0x01])).IsFalse();
        await Assert.That(format.IsSupported([0x01, 0xda, 0x00, 0x03])).IsFalse();
        await Assert.That(format.IsSupported([0x01, 0xda])).IsFalse();
    }

    /// <summary>An SGI file; each channel lists its rows bottom up, written verbatim or as one RLE literal run per row.</summary>
    private static byte[] Sgi(int dimension, int width, int height, int bytesPerChannel, bool rle, params byte[][][] channels)
    {
        byte[] header = new byte[512];
        BinaryPrimitives.WriteUInt16BigEndian(header, 474);
        header[2] = rle ? (byte)1 : (byte)0;
        header[3] = (byte)bytesPerChannel;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)dimension);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), (ushort)channels.Length);
        byte[][] rows = [.. channels.SelectMany(c => c)];
        if (!rle)
        {
            return [.. header, .. rows.SelectMany(r => r)];
        }

        // Each row as a literal run of all its samples (8-bit only), then the terminating zero.
        byte[][] encoded = [.. rows.Select(r => (byte[])[(byte)(0x80 | r.Length), .. r, 0])];
        var tables = new byte[8 * rows.Length];
        int offset = 512 + tables.Length;
        for (int i = 0; i < rows.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(tables.AsSpan(4 * i), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(tables.AsSpan(4 * (rows.Length + i)), (uint)encoded[i].Length);
            offset += encoded[i].Length;
        }

        return [.. header, .. tables, .. encoded.SelectMany(r => r)];
    }
}
