namespace PictureSharp.Formats.Tga;

/// <summary>
/// The Truevision TGA format plugin: colour-mapped, true-colour and greyscale images, uncompressed or RLE. TGA has no
/// signature, so detection checks the header fields; register it after formats with a signature.
/// </summary>
/// <example>
/// <code>
/// ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddFormat(new TgaFormat()).Build();
/// </code>
/// </example>
public sealed class TgaFormat : IImageFormatModule
{
    // One instance per process: ImageFormat compares by reference, so every module shares it.
    internal static readonly ImageFormat Descriptor = ImageFormat.Create("TGA", "image/x-tga", ".tga", ".icb", ".vda", ".vst");

    /// <inheritdoc/>
    public ImageFormat Format => Descriptor;

    /// <inheritdoc/>
    public int HeaderSize => TgaHeader.Size;

    /// <inheritdoc/>
    public bool IsSupported(ReadOnlySpan<byte> header) => TgaHeader.TryParse(header, out _);

    /// <inheritdoc/>
    public IImageFormatDecoder Decoder => TgaDecoder.Instance;

    /// <inheritdoc/>
    public IImageFormatEncoder Encoder => TgaEncoder.Instance;
}
