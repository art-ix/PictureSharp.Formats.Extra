using System.Buffers.Binary;
using PictureSharp.Formats.Psd;

namespace PictureSharp.Formats.Extra.Tests;

public sealed class PsdTests
{
    [Test]
    [Arguments("psd/im-rgba.psd", true)]
    [Arguments("psd/im-rgba.psb", true)]
    [Arguments("psd/im-graya.psd", true)]
    [Arguments("psd/im-cmyka.psd", true)]
    [Arguments("psd/rgba-nolayers.psd", true)]
    [Arguments("psd/rgb-selection.psd", false)]
    [Arguments("psd/im-rgb.psd", false)]
    [Arguments("psd/im-indexed.psd", false)]
    public async Task Identify_ReportsMergedTransparency(string name, bool alpha)
    {
        await Assert.That(TestSupport.Identify(TestSupport.ReadCorpus(name)).HasAlpha).IsEqualTo(alpha);
    }

    [Test]
    [Arguments("psd/im-bitmap.psd", 1)]
    [Arguments("psd/im-rgb.psd", 8)]
    [Arguments("psd/im-rgb16.psd", 16)]
    [Arguments("psd/gray16-raw.psb", 16)]
    public async Task Identify_ReportsBitsPerChannel(string name, int bits)
    {
        await Assert.That(TestSupport.Identify(TestSupport.ReadCorpus(name)).BitsPerChannel).IsEqualTo(bits);
    }

    [Test]
    public async Task NoLayerSection_TreatsTheFirstExtraChannelLikeANegativeLayerCount()
    {
        using Image noLayers = TestSupport.Load(TestSupport.ReadCorpus("psd/rgba-nolayers.psd"));
        using Image negative = TestSupport.Load(TestSupport.ReadCorpus("psd/rgba-negative-count.psd"));
        using Image twoExtra = TestSupport.Load(TestSupport.ReadCorpus("psd/rgba-two-extra.psd"));
        await Assert.That(TestSupport.Pixels(noLayers)).IsEquivalentTo(TestSupport.Pixels(negative), CollectionOrdering.Matching);
        await Assert.That(TestSupport.Pixels(twoExtra)).IsEquivalentTo(TestSupport.Pixels(negative), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Rgb_IsUnblendedFromWhite_GrayIsKept()
    {
        // Stored 245 at alpha 68: (245 - 187) / (68 / 255) = 217.5, which ImageMagick's double arithmetic rounds down.
        using Image rgb = TestSupport.Load(Psd(3, 1, 8, [[245], [255], [187], [68]]));
        await Assert.That(rgb.GetPixelSpan()[0]).IsEqualTo(new Rgba32(217, 255, 0, 68));

        using Image gray = TestSupport.Load(Psd(1, 1, 8, [[245], [68]]));
        await Assert.That(gray.GetPixelSpan()[0]).IsEqualTo(new Rgba32(245, 245, 245, 68));
    }

    [Test]
    public async Task Bitmap_OneBitsAreBlack()
    {
        using Image image = TestSupport.Load(Psd(0, 10, 1, [[0b1010_0000, 0b0100_0000]], height: 1));
        Rgba32 black = new(0, 0, 0), white = new(255, 255, 255);
        await Assert.That(TestSupport.Pixels(image)).IsEquivalentTo(new[] { black, white, black, white, white, white, white, white, white, black }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Cmyk_ConvertsAsPillow()
    {
        // Stored values are inverted: C = 255 - 155 = 100 ink, K = 255 - 205 = 50 ink, M and Y none.
        using Image image = TestSupport.Load(Psd(4, 1, 8, [[155], [255], [255], [205]]));
        await Assert.That(image.GetPixelSpan()[0]).IsEqualTo(new Rgba32(125, 205, 205));
    }

    [Test]
    public async Task SixteenBitSamples_RoundToNearest()
    {
        byte[] samples = [0x00, 0x80, 0x00, 0x81, 0xff, 0xff];   // 128 -> 0, 129 -> 1, 65535 -> 255
        using Image image = TestSupport.Load(Psd(1, 3, 16, [samples], height: 1));
        await Assert.That(TestSupport.Pixels(image).Select(px => (int)px.R)).IsEquivalentTo(new[] { 0, 1, 255 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task PackBits_CutsRunsAtTheRowEnd()
    {
        // One 3-pixel grey row: a 5-byte repeat run of 9 is cut to the row width.
        byte[] file = Psd(1, 3, 8, [[0xfc, 9]], height: 1, packBits: true);
        using Image image = TestSupport.Load(file);
        await Assert.That(TestSupport.Pixels(image).All(px => px == new Rgba32(9, 9, 9))).IsTrue();
    }

    [Test]
    public async Task Resources_FillMetadataAndResolution()
    {
        byte[] icc = [1, 2, 3, 4, 5];
        byte[] exif = [.. "MM\0*"u8, 0, 0, 0, 8, 0, 0];
        byte[] xmp = "<x:xmpmeta/>"u8.ToArray();
        byte[] resolution = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(resolution, 300u << 16);
        BinaryPrimitives.WriteUInt32BigEndian(resolution.AsSpan(8), 72u << 16);
        byte[] resources = [.. Resource(1005, resolution), .. Resource(1039, icc), .. Resource(1058, exif), .. Resource(1060, xmp), .. Resource(1028, [7])];
        byte[] file = Psd(3, 1, 8, [[1], [2], [3]], resources: resources);

        using Image image = TestSupport.Load(file);
        await Assert.That(image.Metadata.IccProfile).IsEquivalentTo(icc, CollectionOrdering.Matching);
        await Assert.That(image.Metadata.Exif).IsEquivalentTo(exif, CollectionOrdering.Matching);
        await Assert.That(image.Metadata.Xmp).IsEquivalentTo(xmp, CollectionOrdering.Matching);
        await Assert.That(image.Metadata.HorizontalResolution).IsEqualTo(300f);
        await Assert.That(image.Metadata.VerticalResolution).IsEqualTo(72f);
        await Assert.That(TestSupport.Identify(file).HorizontalResolution).IsEqualTo(300f);

        using Image ignored = TestSupport.Load(file, TestSupport.Options with { IgnoreMetadata = true });
        await Assert.That(ignored.Metadata.IsEmpty).IsTrue();
        await Assert.That(() => TestSupport.Load(file, TestSupport.Options with { MaxMetadataBytes = 10 })).Throws<ImageFormatException>();
    }

    [Test]
    public async Task UnsupportedFeatures_ThrowNotSupported()
    {
        await Assert.That(() => TestSupport.Load(Psd(9, 1, 8, [[1], [2], [3]]))).Throws<NotSupportedException>();      // Lab
        await Assert.That(() => TestSupport.Load(Psd(7, 1, 8, [[1]]))).Throws<NotSupportedException>();                 // multichannel
        await Assert.That(() => TestSupport.Load(Psd(3, 1, 32, [new byte[4], new byte[4], new byte[4]]))).Throws<NotSupportedException>();
        await Assert.That(() => TestSupport.Load(Psd(0, 1, 8, [[1]]))).Throws<NotSupportedException>();                 // 8-bit bitmap

        byte[] zip = Psd(3, 1, 8, [[1], [2], [3]]);
        zip[^4] = 2;   // the compression field precedes the three raw samples
        await Assert.That(() => TestSupport.Load(zip)).Throws<NotSupportedException>();
    }

    [Test]
    public async Task InvalidFiles_ThrowImageFormatException()
    {
        byte[] valid = Psd(3, 2, 8, [[1, 1], [2, 2], [3, 3]]);
        await Assert.That(() => TestSupport.Load(Psd(5, 1, 8, [[1]]))).Throws<ImageFormatException>();                  // unknown mode
        await Assert.That(() => TestSupport.Load(Psd(3, 1, 7, [[1], [2], [3]]))).Throws<ImageFormatException>();        // depth 7
        await Assert.That(() => TestSupport.Load(Psd(3, 1, 8, [[1], [2]]))).Throws<ImageFormatException>();             // too few channels
        await Assert.That(() => TestSupport.Load(Psd(2, 1, 8, [[1]]))).Throws<ImageFormatException>();                  // no palette
        await Assert.That(() => TestSupport.Load(valid[..^1])).Throws<ImageFormatException>();
        await Assert.That(() => TestSupport.Load(Psd(1, 4, 8, [[0x02, 1]], height: 1, packBits: true))).Throws<ImageFormatException>();

        byte[] badCompression = [.. valid];
        badCompression[^7] = 9;
        await Assert.That(() => TestSupport.Load(badCompression)).Throws<ImageFormatException>();

        byte[] zeroWidth = [.. valid];
        BinaryPrimitives.WriteUInt32BigEndian(zeroWidth.AsSpan(18), 0);
        await Assert.That(() => TestSupport.Load(zeroWidth)).Throws<ImageFormatException>();

        byte[] longSection = [.. valid];
        BinaryPrimitives.WriteUInt32BigEndian(longSection.AsSpan(30), 1000);   // image resources length
        await Assert.That(() => TestSupport.Load(longSection)).Throws<ImageFormatException>();
    }

    [Test]
    public async Task IsSupported_ChecksSignatureAndVersion()
    {
        var format = new PsdFormat();
        await Assert.That(format.IsSupported("8BPS\0\x01"u8)).IsTrue();
        await Assert.That(format.IsSupported("8BPS\0\x02"u8)).IsTrue();
        await Assert.That(format.IsSupported("8BPS\0\x03"u8)).IsFalse();
        await Assert.That(format.IsSupported("8BPS\0"u8)).IsFalse();
        await Assert.That(format.IsSupported("\x89PNG\r\n"u8)).IsFalse();
    }

    /// <summary>A PSD file without layers whose channels are given as raw rows, or as single PackBits rows.</summary>
    private static byte[] Psd(int mode, int width, int depth, byte[][] channels, int height = 1, byte[]? resources = null, bool packBits = false)
    {
        var file = new List<byte>();
        file.AddRange("8BPS\0\x01\0\0\0\0\0\0"u8.ToArray());
        file.AddRange(U16((ushort)channels.Length));
        file.AddRange(U32((uint)height));
        file.AddRange(U32((uint)width));
        file.AddRange(U16((ushort)depth));
        file.AddRange(U16((ushort)mode));
        file.AddRange(U32(0));                                   // colour mode data
        resources ??= [];
        file.AddRange(U32((uint)resources.Length));
        file.AddRange(resources);
        file.AddRange(U32(0));                                   // layer and mask information
        file.AddRange(U16(packBits ? (ushort)1 : (ushort)0));
        if (packBits)
        {
            foreach (byte[] channel in channels)
            {
                file.AddRange(U16((ushort)channel.Length));
            }
        }

        foreach (byte[] channel in channels)
        {
            file.AddRange(channel);
        }

        return [.. file];
    }

    private static byte[] Resource(int id, byte[] data) =>
        [.. "8BIM"u8, .. U16((ushort)id), 0, 0, .. U32((uint)data.Length), .. data, .. data.Length % 2 == 1 ? new byte[] { 0 } : []];

    private static byte[] U16(ushort value)
    {
        byte[] bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] U32(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }
}
