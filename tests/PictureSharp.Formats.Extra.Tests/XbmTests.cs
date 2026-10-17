using System.Text;
using PictureSharp.Formats.Xbm;

namespace PictureSharp.Formats.Extra.Tests;

public sealed class XbmTests
{
    private static readonly Rgba32 Black = new(0, 0, 0), White = new(255, 255, 255);

    [Test]
    public async Task Encode_WritesAnX11Bitmap()
    {
        using var image = new Image(10, 2);
        Span<Rgba32> pixels = image.GetPixelSpan();
        pixels.Fill(White);
        pixels[0] = Black;
        pixels[9] = Black;
        pixels[10] = new Rgba32(127, 127, 127);           // luma below 128: black
        pixels[11] = new Rgba32(128, 128, 128);           // white
        pixels[12] = new Rgba32(0, 0, 0, 127);            // less than half opaque: white

        string text = Encoding.ASCII.GetString(TestSupport.Encode(image, new XbmEncoderOptions { Name = "icon", Hotspot = (3, 1) }));
        await Assert.That(text).IsEqualTo(
            "#define icon_width 10\n#define icon_height 2\n#define icon_x_hot 3\n#define icon_y_hot 1\n" +
            "static unsigned char icon_bits[] = {\n   0x01, 0x02, 0x01, 0x00};\n");
    }

    [Test]
    public async Task RoundTrip_KeepsBlackAndWhite()
    {
        using Image image = TestSupport.CreateImage(37, 13, alpha: false, gray: true);
        foreach (ref Rgba32 px in image.GetPixelSpan())
        {
            px = px.R < 128 ? Black : White;
        }

        byte[] encoded = TestSupport.Encode(image, new XbmEncoderOptions());
        using Image decoded = TestSupport.Load(encoded);
        await Assert.That(Encoding.ASCII.GetString(encoded).Split('\n').Max(line => line.Length)).IsLessThanOrEqualTo(80);
        await Assert.That(TestSupport.Pixels(decoded)).IsEquivalentTo(TestSupport.Pixels(image), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Options_RejectInvalidNamesAndHotspots()
    {
        await Assert.That(() => new XbmEncoderOptions { Name = "my-icon" }).Throws<ArgumentException>();
        await Assert.That(() => new XbmEncoderOptions { Name = "1st" }).Throws<ArgumentException>();
        await Assert.That(() => new XbmEncoderOptions { Hotspot = (-1, 0) }).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Syntax_CommentsUpperCaseHexAndDecimals_AreRead()
    {
        byte[] plain = "#define a_width 9\n#define a_height 2\nstatic char a_bits[] = { 0x01, 0x01, 0xfe, 0x00 };\n"u8.ToArray();
        byte[] exotic = ("/* made by hand */\r\n#define a_width 9 /* nine */\r\n#define a_height 2\r\n#ifndef X\r\n" +
            "static unsigned char a_bits[] = { /* row 0 */ 0X01, 1,\r\n // row 1\r\n 0XFE, 0 , };").Select(c => (byte)c).ToArray();
        using Image expected = TestSupport.Load(plain);
        using Image actual = TestSupport.Load(exotic);
        await Assert.That(TestSupport.Pixels(expected)[0]).IsEqualTo(Black);
        await Assert.That(TestSupport.Pixels(expected)[9]).IsEqualTo(White);
        await Assert.That(TestSupport.Pixels(actual)).IsEquivalentTo(TestSupport.Pixels(expected), CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("#define a_width 9\n#define a_height 2\nstatic char a_bits[] = { 0x01, 0x01, 0xfe };")]   // truncated
    [Arguments("#define a_width 2\n#define a_height 1\nstatic char a_bits[] = { 0x100 };")]               // above a byte
    [Arguments("#define a_width 2\n#define a_height 1\nstatic char a_bits[] = { zz };")]
    [Arguments("#define a_width 2\nstatic char a_bits[] = { 0x01 };")]                                   // no height
    [Arguments("#define a_width 0\n#define a_height 1\nstatic char a_bits[] = { 0x01 };")]
    [Arguments("#define a_width 2\n#define a_height 1\n")]
    public async Task InvalidFiles_ThrowImageFormatException(string text)
    {
        await Assert.That(() => TestSupport.Load(Encoding.ASCII.GetBytes(text))).Throws<ImageFormatException>();
    }

    [Test]
    public async Task IsSupported_SkipsLeadingSpaceAndComments()
    {
        var format = new XbmFormat();
        await Assert.That(format.IsSupported("  /* x */\n#define a_width 1"u8)).IsTrue();
        await Assert.That(format.IsSupported("#define"u8)).IsTrue();
        await Assert.That(format.IsSupported("/* XPM */"u8)).IsFalse();
        await Assert.That(format.IsSupported("/* unclosed #define"u8)).IsFalse();
    }
}
