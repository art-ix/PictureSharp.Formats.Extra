using System.Text;
using PictureSharp.Formats.Pnm;

namespace PictureSharp.Formats.Extra.Tests;

public sealed class PnmTests
{
    [Test]
    [Arguments(PnmKind.Pixmap, false, "P6")]
    [Arguments(PnmKind.Pixmap, true, "P3")]
    [Arguments(PnmKind.Pam, false, "P7")]
    public async Task RoundTrip_ColorKinds_KeepRgb(PnmKind kind, bool plain, string magic)
    {
        using Image image = TestSupport.CreateImage(29, 17, alpha: kind == PnmKind.Pam);
        byte[] encoded = TestSupport.Encode(image, new PnmEncoderOptions { Kind = kind, Plain = plain });
        using Image decoded = TestSupport.Load(encoded);
        await Assert.That(Encoding.ASCII.GetString(encoded, 0, 2)).IsEqualTo(magic);
        await Assert.That(TestSupport.Pixels(decoded)).IsEquivalentTo(TestSupport.Pixels(image), CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(false, "P5")]
    [Arguments(true, "P2")]
    public async Task RoundTrip_Graymap_KeepsGrey(bool plain, string magic)
    {
        using Image image = TestSupport.CreateImage(29, 17, alpha: false, gray: true);
        byte[] encoded = TestSupport.Encode(image, new PnmEncoderOptions { Kind = PnmKind.Graymap, Plain = plain });
        using Image decoded = TestSupport.Load(encoded);
        await Assert.That(Encoding.ASCII.GetString(encoded, 0, 2)).IsEqualTo(magic);
        await Assert.That(TestSupport.Pixels(decoded)).IsEquivalentTo(TestSupport.Pixels(image), CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Bitmap_ThresholdsLumaAt128(bool plain)
    {
        using var image = new Image(10, 1);
        Span<Rgba32> pixels = image.GetPixelSpan();
        for (int x = 0; x < 10; x++)
        {
            byte v = (byte)(x < 5 ? 127 : 128);
            pixels[x] = new Rgba32(v, v, v);
        }

        using Image decoded = TestSupport.Load(TestSupport.Encode(image, new PnmEncoderOptions { Kind = PnmKind.Bitmap, Plain = plain }));
        byte[] values = [.. TestSupport.Pixels(decoded).Select(p => p.R)];
        await Assert.That(values).IsEquivalentTo(new byte[] { 0, 0, 0, 0, 0, 255, 255, 255, 255, 255 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Auto_ChoosesPamPgmOrPpm()
    {
        using Image alpha = TestSupport.CreateImage(4, 4, alpha: true);
        using Image gray = TestSupport.CreateImage(4, 4, alpha: false, gray: true);
        using Image color = TestSupport.CreateImage(4, 4, alpha: false);
        await Assert.That(Magic(TestSupport.Encode(alpha, new PnmEncoderOptions { Plain = true }))).IsEqualTo("P7");
        await Assert.That(Magic(TestSupport.Encode(gray, new PnmEncoderOptions()))).IsEqualTo("P5");
        await Assert.That(Magic(TestSupport.Encode(color, new PnmEncoderOptions()))).IsEqualTo("P6");
    }

    [Test]
    public async Task PlainPam_IsRejected()
    {
        using Image image = TestSupport.CreateImage(2, 2, false);
        await Assert.That(() => TestSupport.Encode(image, new PnmEncoderOptions { Kind = PnmKind.Pam, Plain = true })).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("P6\n2 2\n255\n")]                                  // raster missing
    [Arguments("P6\n0 2\n255\nxxxxxxxxxxxx")]                      // zero width
    [Arguments("P5\n2 1\n70000\nxxxx")]                            // maxval too large
    [Arguments("P2\n2 1\n7\n1 9\n")]                               // plain sample above maxval
    [Arguments("P1\n2 1\n0 2\n")]                                  // invalid plain bit
    [Arguments("P6\n99999999999 1\n255\n")]                        // number too long
    [Arguments("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 3\nMAXVAL 255\n")]    // no ENDHDR
    [Arguments("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 2\nMAXVAL 255\nTUPLTYPE RGB\nENDHDR\nxx")]  // depth mismatch
    public async Task Decode_Corrupt_ThrowsImageFormatException(string data)
    {
        await Assert.That(() => TestSupport.Load(Encoding.ASCII.GetBytes(data))).Throws<ImageFormatException>();
    }

    [Test]
    public async Task Decode_UnknownTupleType_ThrowsNotSupported()
    {
        byte[] data = Encoding.ASCII.GetBytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 4\nMAXVAL 255\nTUPLTYPE CMYK\nENDHDR\nxxxx");
        await Assert.That(() => TestSupport.Load(data)).Throws<NotSupportedException>();
    }

    [Test]
    public async Task Decode_CommentsAndSixteenBitSamples()
    {
        byte[] data = [.. Encoding.ASCII.GetBytes("P5 # comment\n# another\n2 1 # size\n1023\n"), 0x03, 0xff, 0x01, 0xff];
        using Image image = TestSupport.Load(data);
        // round(1023 / 1023 * 255) and round(511 / 1023 * 255 = 127.37...)
        await Assert.That(image.GetPixelSpan()[0].R).IsEqualTo((byte)255);
        await Assert.That(image.GetPixelSpan()[1].R).IsEqualTo((byte)127);
        ImageInfo info = TestSupport.Identify(data);
        await Assert.That(info.BitsPerChannel).IsEqualTo(10);
    }

    [Test]
    public async Task Decode_PamBlackAndWhite_OneIsWhite()
    {
        byte[] data = [.. Encoding.ASCII.GetBytes("P7\nWIDTH 2\nHEIGHT 1\nDEPTH 1\nMAXVAL 1\nTUPLTYPE BLACKANDWHITE\nENDHDR\n"), 0, 1];
        using Image image = TestSupport.Load(data);
        await Assert.That(image.GetPixelSpan()[0].R).IsEqualTo((byte)0);
        await Assert.That(image.GetPixelSpan()[1].R).IsEqualTo((byte)255);
    }

    private static string Magic(byte[] data) => Encoding.ASCII.GetString(data, 0, 2);
}
