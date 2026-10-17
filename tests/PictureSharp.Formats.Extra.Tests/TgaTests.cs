using System.Buffers.Binary;
using PictureSharp.Formats.Tga;

namespace PictureSharp.Formats.Extra.Tests;

public sealed class TgaTests
{
    [Test]
    [Arguments(false, false, TgaOrigin.BottomLeft)]
    [Arguments(true, false, TgaOrigin.BottomLeft)]
    [Arguments(true, true, TgaOrigin.TopLeft)]
    [Arguments(false, true, TgaOrigin.TopLeft)]
    public async Task RoundTrip_KeepsEveryPixel(bool rle, bool alpha, TgaOrigin origin)
    {
        using Image image = TestSupport.CreateImage(300, 23, alpha);
        byte[] encoded = TestSupport.Encode(image, new TgaEncoderOptions { Rle = rle, Origin = origin });
        using Image decoded = TestSupport.Load(encoded);
        await Assert.That(encoded[16]).IsEqualTo(alpha ? (byte)32 : (byte)24);
        await Assert.That(encoded[2]).IsEqualTo((byte)(rle ? 10 : 2));
        await Assert.That(TestSupport.Pixels(decoded)).IsEquivalentTo(TestSupport.Pixels(image), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Gray_IsWrittenAsEightBitGreyscale()
    {
        using Image image = TestSupport.CreateImage(31, 7, alpha: false, gray: true);
        byte[] encoded = TestSupport.Encode(image, new TgaEncoderOptions());
        using Image decoded = TestSupport.Load(encoded);
        await Assert.That(encoded[2]).IsEqualTo((byte)11);
        await Assert.That(encoded[16]).IsEqualTo((byte)8);
        await Assert.That(TestSupport.Pixels(decoded)).IsEquivalentTo(TestSupport.Pixels(image), CollectionOrdering.Matching);
    }

    [Test]
    public async Task SixteenBit_UsesAlphaBit_OnlyWhenDeclared()
    {
        byte[] withAlpha = Tga(2, 16, 0x21, [0xff, 0x7f, 0x00, 0x80]);   // white without the bit, black with it
        using Image a = TestSupport.Load(withAlpha);
        await Assert.That(a.GetPixelSpan()[0]).IsEqualTo(new Rgba32(255, 255, 255, 0));
        await Assert.That(a.GetPixelSpan()[1]).IsEqualTo(new Rgba32(0, 0, 0, 255));

        using Image opaque = TestSupport.Load(Tga(2, 16, 0x20, [0xff, 0x7f, 0x00, 0x80]));
        await Assert.That(opaque.GetPixelSpan()[0]).IsEqualTo(new Rgba32(255, 255, 255, 255));
    }

    [Test]
    [Arguments(4, 200, 100)]   // premultiplied: unpremultiplied to 200 * 255 / 100
    [Arguments(3, 200, 100)]   // straight alpha
    [Arguments(0, 200, 100)]   // no alpha: the fourth byte is ignored
    public async Task ExtensionArea_AttributesType_ControlsAlpha(int attributes, byte color, byte alpha)
    {
        byte[] image = Tga(2, 32, 0x28, [color, color, color, alpha]);
        byte[] extension = new byte[495];
        BinaryPrimitives.WriteUInt16LittleEndian(extension, 495);
        extension[494] = (byte)attributes;
        byte[] footer = new byte[26];
        BinaryPrimitives.WriteUInt32LittleEndian(footer, (uint)image.Length);
        "TRUEVISION-XFILE.\0"u8.CopyTo(footer.AsSpan(8));
        using Image decoded = TestSupport.Load([.. image, .. extension, .. footer]);

        Rgba32 expected = attributes switch
        {
            4 => new Rgba32(255, 255, 255, alpha),
            3 => new Rgba32(color, color, color, alpha),
            _ => new Rgba32(color, color, color, 255),
        };
        await Assert.That(decoded.GetPixelSpan()[0]).IsEqualTo(expected);
    }

    [Test]
    public async Task Decode_PaletteIndexOutsideMap_ThrowsImageFormatException()
    {
        byte[] header = new byte[18];
        header[1] = 1;
        header[2] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5), 2);
        header[7] = 24;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), 1);
        header[16] = 8;
        await Assert.That(() => TestSupport.Load([.. header, 1, 2, 3, 4, 5, 6, 7])).Throws<ImageFormatException>();
    }

    [Test]
    public async Task Decode_Truncated_ThrowsImageFormatException()
    {
        byte[] raw = TestSupport.ReadCorpus("tga/rgb.tga");
        byte[] rle = TestSupport.ReadCorpus("tga/rgba-rle.tga");
        await Assert.That(() => TestSupport.Load(raw[..100])).Throws<ImageFormatException>();
        await Assert.That(() => TestSupport.Load(rle[..100])).Throws<ImageFormatException>();
    }

    [Test]
    public async Task IsSupported_RejectsOtherFormatsAndBadHeaders()
    {
        var format = new TgaFormat();
        byte[] ico = [0, 0, 1, 0, 1, 0, 16, 16, 0, 0, 1, 0, 32, 0, 0, 0, 0, 0];
        byte[] pcx = [0x0a, 5, 1, 8, 0, 0, 0, 0, 15, 0, 15, 0, 72, 0, 72, 0, 0, 0];
        await Assert.That(format.IsSupported(ico)).IsFalse();
        await Assert.That(format.IsSupported(pcx)).IsFalse();
        await Assert.That(format.IsSupported("\x89PNG\r\n\x1a\n\0\0\0\rIHDR\0\0\0\x01"u8)).IsFalse();
        await Assert.That(format.IsSupported(Tga(2, 24, 0, [1, 2, 3]))).IsTrue();
        await Assert.That(format.IsSupported(Tga(2, 12, 0, [1, 2, 3]))).IsFalse();
        await Assert.That(format.IsSupported(Tga(2, 24, 0xc0, [1, 2, 3]))).IsFalse();
    }

    // A 2×1 or 1×1 uncompressed image with the given type, depth and descriptor.
    private static byte[] Tga(int type, int bits, int descriptor, byte[] pixels)
    {
        byte[] header = new byte[18];
        header[2] = (byte)type;
        int width = pixels.Length / ((bits + 7) / 8);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), 1);
        header[16] = (byte)bits;
        header[17] = (byte)descriptor;
        return [.. header, .. pixels];
    }
}
