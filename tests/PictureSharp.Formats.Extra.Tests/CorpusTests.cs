namespace PictureSharp.Formats.Extra.Tests;

public sealed class CorpusTests
{
    public static IEnumerable<string> Files() => TestSupport.Reference.Keys.Order(StringComparer.Ordinal);

    [Test]
    [MethodDataSource(nameof(Files))]
    public async Task Decode_MatchesReferencePixels(string name)
    {
        ReferenceEntry expected = TestSupport.Reference[name];
        using Image image = TestSupport.Load(TestSupport.ReadCorpus(name));
        await Assert.That(image.Width).IsEqualTo(expected.Width);
        await Assert.That(image.Height).IsEqualTo(expected.Height);
        await Assert.That(TestSupport.Sha256(image)).IsEqualTo(expected.Sha256);
    }

    [Test]
    [MethodDataSource(nameof(Files))]
    public async Task Identify_MatchesDecode(string name)
    {
        byte[] data = TestSupport.ReadCorpus(name);
        ImageInfo info = TestSupport.Identify(data);
        using Image image = TestSupport.Load(data);
        await Assert.That(info.Width).IsEqualTo(image.Width);
        await Assert.That(info.Height).IsEqualTo(image.Height);
        await Assert.That(info.Format).IsSameReferenceAs(image.SourceFormat);
        string folder = name[..name.IndexOf('/')];
        await Assert.That(info.Format.Name).IsEqualTo(folder.ToUpperInvariant());
    }
}
