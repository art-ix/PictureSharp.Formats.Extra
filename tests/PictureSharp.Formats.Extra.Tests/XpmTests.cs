using System.Text;
using PictureSharp.Formats.Xpm;

namespace PictureSharp.Formats.Extra.Tests;

public sealed class XpmTests
{
    [Test]
    public async Task FourCharactersPerPixel_DecodeLikeOne()
    {
        using Image one = TestSupport.Load(Xpm("3 1 2 1", ["a c red", "b c #0000ff"], ["aba"]));
        using Image four = TestSupport.Load(Xpm("3 1 2 4", ["a  1 c red", "zz.. c #0000ff"], ["a  1zz..a  1"]));
        await Assert.That(TestSupport.Pixels(four)).IsEquivalentTo(TestSupport.Pixels(one), CollectionOrdering.Matching);
        await Assert.That(TestSupport.Pixels(one)[1]).IsEqualTo(new Rgba32(0, 0, 255));
    }

    [Test]
    [Arguments("#f80", 255, 136, 0)]
    [Arguments("#7f8081", 127, 128, 129)]
    [Arguments("#800000fff", 128, 0, 255)]           // 0x800 * 255 / 4095 = 127.53
    [Arguments("#80807f7f0000", 128, 127, 0)]        // 0x8080 / 257 = 128, 0x7f7f / 257 = 127
    [Arguments("light SLATE grey", 119, 136, 153)]
    [Arguments("gray", 190, 190, 190)]               // X11, not the CSS grey
    public async Task Colors_ScaleHexAndResolveX11Names(string spec, int r, int g, int b)
    {
        using Image image = TestSupport.Load(Xpm("1 1 1 1", [$"a c {spec}"], ["a"]));
        await Assert.That(image.GetPixelSpan()[0]).IsEqualTo(new Rgba32((byte)r, (byte)g, (byte)b));
    }

    [Test]
    [Arguments("s sym m black g #808080 c #ff0000", 255, 0, 0)]
    [Arguments("m black g4 #808080", 128, 128, 128)]
    [Arguments("m #0000ff s blue", 0, 0, 255)]
    public async Task Visuals_PreferColourThenGreyThenMono(string pairs, int r, int g, int b)
    {
        using Image image = TestSupport.Load(Xpm("1 1 1 1", [$"a {pairs}"], ["a"]));
        await Assert.That(image.GetPixelSpan()[0]).IsEqualTo(new Rgba32((byte)r, (byte)g, (byte)b));
    }

    [Test]
    public async Task None_IsTransparent()
    {
        byte[] file = Xpm("2 1 2 1", ["a c None", "b c white"], ["ab"]);
        using Image image = TestSupport.Load(file);
        await Assert.That(TestSupport.Identify(file).HasAlpha).IsTrue();
        await Assert.That(image.GetPixelSpan()[0]).IsEqualTo(new Rgba32(0, 0, 0, 0));
        await Assert.That(TestSupport.Identify(Xpm("1 1 1 1", ["a c white"], ["a"])).HasAlpha).IsFalse();
    }

    [Test]
    public async Task UnsupportedFeatures_ThrowNotSupported()
    {
        await Assert.That(() => TestSupport.Load(Xpm("1 1 1 1", ["a c NoSuchColour"], ["a"]))).Throws<NotSupportedException>();
        await Assert.That(() => TestSupport.Load(Xpm("1 1 1 1", ["a c %100"], ["a"]))).Throws<NotSupportedException>();
        await Assert.That(() => TestSupport.Load(Xpm("1 1 1 5", ["abcde c red"], ["abcde"]))).Throws<NotSupportedException>();
    }

    [Test]
    [Arguments("1 1 1 1", "a c red", "b")]           // undefined key
    [Arguments("2 1 1 1", "a c red", "a")]           // short row
    [Arguments("1 1 1 1", "a s name", "a")]          // no colour visual
    [Arguments("1 1 1 1", "a c #12345", "a")]        // digits not divisible by three
    [Arguments("1 x 1 1", "a c red", "a")]
    [Arguments("0 1 1 1", "a c red", "a")]
    [Arguments("1 1 1 1", "a red", "a")]             // no key
    public async Task InvalidFiles_ThrowImageFormatException(string values, string color, string row)
    {
        await Assert.That(() => TestSupport.Load(Xpm(values, [color], [row]))).Throws<ImageFormatException>();
    }

    [Test]
    public async Task TruncatedFiles_ThrowImageFormatException()
    {
        await Assert.That(() => TestSupport.Load("/* XPM */\nstatic char *a[] = {\n\"1 1 1 1\",\n\"a c red\",\n"u8.ToArray())).Throws<ImageFormatException>();
        await Assert.That(() => TestSupport.Load("/* XPM */\nstatic char *a[] = {\n\"1 1 1 1\",\n\"a c red\n"u8.ToArray())).Throws<ImageFormatException>();
    }

    private static byte[] Xpm(string values, string[] colors, string[] rows) =>
        Encoding.ASCII.GetBytes($"/* XPM */\nstatic char *test[] = {{\n/* values */\n\"{values}\",\n" +
            string.Concat(colors.Select(c => $"\"{c}\",\n")) + "/* pixels */\n" + string.Join(",\n", rows.Select(r => $"\"{r}\"")) + "\n};\n");
}
