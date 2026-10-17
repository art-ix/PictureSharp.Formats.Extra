namespace PictureSharp.Formats.Extra.Tests;

public sealed class RobustnessTests
{
    public static IEnumerable<string> Files() => TestSupport.Reference.Keys.Order(StringComparer.Ordinal);

    [Test]
    [Property("Category", "Slow")]
    [MethodDataSource(nameof(Files))]
    public async Task MutatedInput_FailsOnlyWithContractExceptions(string name)
    {
        byte[] original = TestSupport.ReadCorpus(name);
        var random = new Random(name.GetHashCode(StringComparison.Ordinal) & 0x7fff);
        var unexpected = new List<string>();
        for (int round = 0; round < 300; round++)
        {
            byte[] data = [.. original];
            int changes = 1 + random.Next(8);
            for (int k = 0; k < changes; k++)
            {
                // Bias towards the header, where lengths and dimensions live.
                int at = random.Next(2) == 0 ? random.Next(Math.Min(data.Length, 64)) : random.Next(data.Length);
                data[at] = (byte)random.Next(256);
            }

            if (random.Next(4) == 0)
            {
                data = data[..random.Next(data.Length)];
            }

            Check(data, unexpected);
        }

        await Assert.That(unexpected).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(Files))]
    public async Task EveryTruncation_FailsOnlyWithContractExceptions(string name)
    {
        byte[] original = TestSupport.ReadCorpus(name);
        var unexpected = new List<string>();
        int step = Math.Max(1, original.Length / 200);
        for (int length = 0; length < original.Length; length += length < 64 ? 1 : step)
        {
            Check(original[..length], unexpected);
        }

        await Assert.That(unexpected).IsEmpty();
    }

    public static IEnumerable<string> LargerThanOnePixel() =>
        Files().Where(name => TestSupport.Reference[name] is { Width: > 1 } or { Height: > 1 });

    [Test]
    [MethodDataSource(nameof(LargerThanOnePixel))]
    public async Task MaxPixels_IsEnforcedBeforeDecoding(string name)
    {
        byte[] data = TestSupport.ReadCorpus(name);
        ReferenceEntry entry = TestSupport.Reference[name];
        DecoderOptions options = TestSupport.Options with { MaxPixels = (long)entry.Width * entry.Height - 1 };
        await Assert.That(() => TestSupport.Load(data, options)).Throws<ImageFormatException>();
    }

    [Test]
    public async Task CanceledLoad_ThrowsOperationCanceled()
    {
        byte[] data = TestSupport.ReadCorpus("qoi/big-rgb.qoi");
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        await Assert.That(() => Image.Load(data, TestSupport.Options, source.Token)).Throws<OperationCanceledException>();
    }

    private static void Check(byte[] data, List<string> unexpected)
    {
        try
        {
            using Image image = Image.Load(data, TestSupport.Options);
        }
        catch (Exception e) when (e is ImageFormatException or NotSupportedException)
        {
        }
        catch (Exception e)
        {
            unexpected.Add($"{data.Length} bytes: {e.GetType().Name}: {e.Message}");
        }
    }
}
