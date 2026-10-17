using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using PictureSharp.Formats.Extra;

namespace PictureSharp.Formats.Extra.Tests;

internal static class TestSupport
{
    public static ImageConfiguration Configuration { get; } = ImageConfiguration.Default.ToBuilder().AddExtraFormats().Build();

    public static DecoderOptions Options { get; } = new() { Configuration = Configuration };

    public static string CorpusDirectory => Path.Combine(AppContext.BaseDirectory, "corpus");

    public static IReadOnlyDictionary<string, ReferenceEntry> Reference { get; } =
        JsonSerializer.Deserialize<Dictionary<string, ReferenceEntry>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "reference.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static byte[] ReadCorpus(string name) => File.ReadAllBytes(Path.Combine(CorpusDirectory, name));

    public static Image Load(byte[] data, DecoderOptions? options = null) => Image.Load(data, options ?? Options);

    public static ImageInfo Identify(byte[] data) => Image.Identify(data, Options);

    public static byte[] Encode(Image image, EncoderOptions options) => image.Encode(options with { Configuration = Configuration });

    public static string Sha256(Image image) =>
        Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(image.GetPixelSpan())));

    public static Rgba32[] Pixels(Image image) => image.GetPixelSpan().ToArray();

    /// <summary>A deterministic test image with gradients, flat runs and, optionally, varying alpha.</summary>
    public static Image CreateImage(int width, int height, bool alpha, bool gray = false, int seed = 1)
    {
        var image = new Image(width, height);
        Span<Rgba32> pixels = image.GetPixelSpan();
        var random = new Random(seed);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte r = (byte)(x * 255 / Math.Max(1, width - 1));
                byte g = (byte)(y * 255 / Math.Max(1, height - 1));
                byte b = (byte)((x / 4 + y / 3) % 2 == 0 ? 40 : random.Next(256));
                if (x > width / 2 && y > height / 2)
                {
                    (r, g, b) = (90, 160, 20);                       // a flat block for runs
                }

                if (gray)
                {
                    (g, b) = (r, r);
                }

                byte a = alpha ? (byte)((x * 31 + y * 7) % 256) : (byte)255;
                pixels[y * width + x] = new Rgba32(r, g, b, a);
            }
        }

        return image;
    }
}

internal sealed record ReferenceEntry(int Width, int Height, string Oracle, string Sha256);
