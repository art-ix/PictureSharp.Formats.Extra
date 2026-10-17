namespace PictureSharp.Formats.Psd;

/// <summary>
/// The Adobe Photoshop format plugin, PSD and PSB (read only). Only the merged image is decoded; layers, masks and
/// effects are skipped. Bitmap, greyscale, duotone (as greyscale), indexed, RGB and CMYK images with 1, 8 or 16 bits
/// per channel, raw or PackBits. ICC, EXIF, XMP and the resolution are read from the image resources.
/// </summary>
/// <example>
/// <code>
/// ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddFormat(new PsdFormat()).Build();
/// </code>
/// </example>
public sealed class PsdFormat : IImageFormatModule
{
    // One instance per process: ImageFormat compares by reference, so every module shares it.
    internal static readonly ImageFormat Descriptor = ImageFormat.Create("PSD", "image/vnd.adobe.photoshop", ".psd", ".psb");

    /// <inheritdoc/>
    public ImageFormat Format => Descriptor;

    /// <inheritdoc/>
    public int HeaderSize => 6;

    /// <inheritdoc/>
    public bool IsSupported(ReadOnlySpan<byte> header) => PsdHeader.HasSignature(header);

    /// <inheritdoc/>
    public IImageFormatDecoder Decoder => PsdDecoder.Instance;
}
