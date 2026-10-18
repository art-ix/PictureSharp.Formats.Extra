using System.Buffers.Binary;
using System.Text;
using PictureSharp.Formats.Icns;

namespace PictureSharp.Formats.Extra.Tests;

public sealed class IcnsTests
{
    [Test]
    public async Task ArgbElement_DecodesItsFourPlanes()
    {
        // 16x16: each plane is one repeat run of 130 and one of 126 (0x80 + n - 3).
        byte[] planes = [.. Run(10), .. Run(20), .. Run(30), .. Run(40)];
        using Image image = TestSupport.Load(Icns(("ic04", [.. "ARGB"u8, .. planes])));
        await Assert.That(image.Width).IsEqualTo(16);
        await Assert.That(TestSupport.Pixels(image).All(px => px == new Rgba32(20, 30, 40, 10))).IsTrue();
    }

    [Test]
    public async Task MonochromeIcon_IsUsedOnlyWithoutColourIcons()
    {
        byte[] icon = new byte[64];
        icon[0] = 0x80;                     // first pixel black
        Array.Fill(icon, (byte)0xff, 32, 32); // mask: every pixel opaque
        icon[32] = 0x7f;                    // except the first
        using Image image = TestSupport.Load(Icns(("ics#", icon)));
        await Assert.That(image.GetPixelSpan()[0]).IsEqualTo(new Rgba32(0, 0, 0, 0));
        await Assert.That(image.GetPixelSpan()[1]).IsEqualTo(new Rgba32(255, 255, 255, 255));
    }

    [Test]
    public async Task Jpeg2000_IsSkippedForASmallerIcon()
    {
        byte[] jp2 = [0x00, 0x00, 0x00, 0x0c, .. "jP  "u8, 0x0d, 0x0a, 0x87, 0x0a];
        byte[] rgb = [.. Run(1), .. Run(2), .. Run(3)];
        using Image image = TestSupport.Load(Icns(("ic08", jp2), ("is32", rgb)));
        await Assert.That(image.Width).IsEqualTo(16);
        await Assert.That(image.GetPixelSpan()[0]).IsEqualTo(new Rgba32(1, 2, 3));
        await Assert.That(TestSupport.Identify(Icns(("is32", rgb))).HasAlpha).IsFalse();
        await Assert.That(() => TestSupport.Load(Icns(("ic08", jp2)))).Throws<NotSupportedException>();
    }

    [Test]
    public async Task InvalidFiles_ThrowImageFormatException()
    {
        byte[] valid = Icns(("is32", [.. Run(1), .. Run(2), .. Run(3)]));
        await Assert.That(() => TestSupport.Load(valid[..^1])).Throws<ImageFormatException>();
        await Assert.That(() => TestSupport.Load(Icns(("is32", [.. Run(1), .. Run(2)])))).Throws<ImageFormatException>();
        await Assert.That(() => TestSupport.Load(Icns(("is32", [0xff, 1, .. Run(1), .. Run(2), .. Run(3)])))).Throws<ImageFormatException>();
        await Assert.That(() => TestSupport.Load(Icns(("TOC ", [1, 2])))).Throws<ImageFormatException>();
        await Assert.That(() => TestSupport.Load(Icns(("it32", [1, 0, 0, 0, .. Run(1)])))).Throws<ImageFormatException>();

        byte[] badLength = [.. valid];
        BinaryPrimitives.WriteUInt32BigEndian(badLength.AsSpan(12), 4);
        await Assert.That(() => TestSupport.Load(badLength)).Throws<ImageFormatException>();
    }

    /// <summary>One 256-sample plane as two repeat runs.</summary>
    private static byte[] Run(byte value) => [0xff, value, 0x80 + 126 - 3, value];

    private static byte[] Icns(params (string Type, byte[] Data)[] elements)
    {
        var body = new List<byte>();
        foreach ((string type, byte[] data) in elements)
        {
            body.AddRange(Encoding.ASCII.GetBytes(type));
            byte[] length = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length + 8);
            body.AddRange(length);
            body.AddRange(data);
        }

        byte[] header = new byte[8];
        "icns"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)body.Count + 8);
        return [.. header, .. body];
    }
}
