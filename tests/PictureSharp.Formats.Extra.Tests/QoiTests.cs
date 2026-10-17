using PictureSharp.Formats.Qoi;

namespace PictureSharp.Formats.Extra.Tests;

public sealed class QoiTests
{
    public static IEnumerable<string> CorpusFiles() =>
        TestSupport.Reference.Keys.Where(name => name.StartsWith("qoi/", StringComparison.Ordinal)).Order(StringComparer.Ordinal);

    [Test]
    [MethodDataSource(nameof(CorpusFiles))]
    public async Task Encode_ReproducesQoiHBytes(string name)
    {
        // The corpus files come from a port of qoi.h qoi_encode; re-encoding their pixels must give the same bytes.
        byte[] original = TestSupport.ReadCorpus(name);
        using Image image = TestSupport.Load(original);
        byte[] encoded = TestSupport.Encode(image, new QoiEncoderOptions
        {
            IncludeAlpha = original[12] == 4,
            ColorSpace = (QoiColorSpace)original[13],
        });
        await Assert.That(encoded).IsEquivalentTo(original, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RoundTrip_KeepsEveryPixel(bool alpha)
    {
        using Image image = TestSupport.CreateImage(67, 41, alpha);
        byte[] encoded = TestSupport.Encode(image, new QoiEncoderOptions());
        using Image decoded = TestSupport.Load(encoded);
        await Assert.That(encoded[12]).IsEqualTo(alpha ? (byte)4 : (byte)3);
        await Assert.That(TestSupport.Pixels(decoded)).IsEquivalentTo(TestSupport.Pixels(image), CollectionOrdering.Matching);
    }

    [Test]
    public async Task ColorSpace_FollowsGammaOne()
    {
        using Image image = TestSupport.CreateImage(5, 5, false);
        image.Metadata.Gamma = 1f;
        byte[] encoded = TestSupport.Encode(image, new QoiEncoderOptions());
        await Assert.That(encoded[13]).IsEqualTo((byte)1);
        using Image decoded = TestSupport.Load(encoded);
        await Assert.That(decoded.Metadata.Gamma).IsEqualTo(1f);
        using Image ignored = TestSupport.Load(encoded, TestSupport.Options with { IgnoreMetadata = true });
        await Assert.That(ignored.Metadata.Gamma).IsNull();
    }

    [Test]
    public async Task IncludeAlphaFalse_DropsAlpha()
    {
        using Image image = TestSupport.CreateImage(9, 4, alpha: true);
        using Image decoded = TestSupport.Load(TestSupport.Encode(image, new QoiEncoderOptions { IncludeAlpha = false }));
        await Assert.That(TestSupport.Pixels(decoded).All(p => p.A == 255)).IsTrue();
    }

    [Test]
    public async Task Decode_TruncatedOrInvalid_ThrowsImageFormatException()
    {
        byte[] valid = TestSupport.ReadCorpus("qoi/photo-rgba.qoi");
        await Assert.That(() => TestSupport.Load(valid[..(valid.Length / 2)])).Throws<ImageFormatException>();

        byte[] channels = [.. valid];
        channels[12] = 5;
        await Assert.That(() => TestSupport.Load(channels)).Throws<ImageFormatException>();

        byte[] empty = [.. valid];
        empty[4] = empty[5] = empty[6] = empty[7] = 0;
        await Assert.That(() => TestSupport.Load(empty)).Throws<ImageFormatException>();
    }

    [Test]
    public async Task Format_IsDetectedAndRegistered()
    {
        await Assert.That(Image.DetectFormat("qoif\0\0\0\x01\0\0\0\x01\x04\0"u8)).IsNull();   // not registered by default
        await Assert.That(TestSupport.Configuration.FromExtension("x.qoi")).IsSameReferenceAs(ImageFormat.Qoi);
        await Assert.That(TestSupport.Configuration.CanEncode(ImageFormat.Qoi)).IsTrue();
        await Assert.That(new QoiFormat().IsSupported("qoif"u8)).IsFalse();
    }
}
