using System.Diagnostics;
using PictureSharp.Formats.Dds;
using PictureSharp.Formats.Pnm;
using PictureSharp.Formats.Qoi;
using PictureSharp.Formats.Tga;

namespace PictureSharp.Formats.Extra.Tests;

/// <summary>Files written by the encoders must decode in Pillow 10.2.0 to the same pixels.</summary>
[Property("Category", "Oracle")]
public sealed class OracleTests
{
    private const string PillowRgba =
        "import hashlib,sys\nfrom PIL import Image\nim=Image.open(sys.argv[1])\nprint(hashlib.sha256(im.convert('RGBA').tobytes()).hexdigest())";

    private const string PillowVersion = "import PIL\nprint(PIL.__version__)";

    private static readonly bool HasPillow = FindPillow();

    public static IEnumerable<(string Name, EncoderOptions Options, bool Alpha, bool Gray)> Cases() =>
    [
        ("qoi-rgb", new QoiEncoderOptions(), false, false),
        ("qoi-rgba", new QoiEncoderOptions(), true, false),
        ("ppm", new PnmEncoderOptions { Kind = PnmKind.Pixmap }, false, false),
        ("ppm-plain", new PnmEncoderOptions { Kind = PnmKind.Pixmap, Plain = true }, false, false),
        ("pgm", new PnmEncoderOptions { Kind = PnmKind.Graymap }, false, true),
        ("pgm-plain", new PnmEncoderOptions { Kind = PnmKind.Graymap, Plain = true }, false, true),
        ("tga-rgb", new TgaEncoderOptions { Rle = false }, false, false),
        ("tga-rgb-rle", new TgaEncoderOptions(), false, false),
        ("tga-rgba-rle-top", new TgaEncoderOptions { Origin = TgaOrigin.TopLeft }, true, false),
        ("tga-gray-rle", new TgaEncoderOptions(), false, true),
        ("dds-bgra", new DdsEncoderOptions(), true, false),
        ("dds-bgrx", new DdsEncoderOptions { PixelFormat = DdsPixelFormat.Bgrx32 }, false, false),
    ];

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task EncodedFile_DecodesInPillowToTheSamePixels(string name, EncoderOptions options, bool alpha, bool gray)
    {
        Skip.Unless(HasPillow, "Needs python3 with Pillow 10.2.0.");

        using Image image = TestSupport.CreateImage(143, 37, alpha, gray);
        string file = Path.Combine(Path.GetTempPath(), $"picturesharp-extra-{name}-{Environment.ProcessId}");
        try
        {
            File.WriteAllBytes(file, TestSupport.Encode(image, options));
            await Assert.That(Run(PillowRgba, file)).IsEqualTo(TestSupport.Sha256(image));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static bool FindPillow()
    {
        try
        {
            return Run(PillowVersion) == "10.2.0";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string Run(string script, params string[] arguments)
    {
        var info = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-I");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(script);
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info) ?? throw new InvalidOperationException("python3 did not start.");
        string output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return output;
    }
}
