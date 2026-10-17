namespace PictureSharp.Formats.Qoi;

/// <summary>
/// The QOI ("Quite OK Image") format plugin: lossless 8-bit RGB and RGBA. Register it, or all formats of the package
/// with <c>AddExtraFormats</c>, to read and write <c>.qoi</c> files through the usual <see cref="Image"/> API.
/// </summary>
/// <example>
/// <code>
/// ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddFormat(new QoiFormat()).Build();
/// </code>
/// </example>
public sealed class QoiFormat : IImageFormatModule
{
    // One instance per process: ImageFormat compares by reference, so every module shares it.
    internal static readonly ImageFormat Descriptor = ImageFormat.Create("QOI", "image/qoi", ".qoi");

    /// <inheritdoc/>
    public ImageFormat Format => Descriptor;

    /// <inheritdoc/>
    public int HeaderSize => QoiDecoder.HeaderSize;

    /// <inheritdoc/>
    public bool IsSupported(ReadOnlySpan<byte> header) => header.Length >= QoiDecoder.HeaderSize && header.StartsWith("qoif"u8);

    /// <inheritdoc/>
    public IImageFormatDecoder Decoder => QoiDecoder.Instance;

    /// <inheritdoc/>
    public IImageFormatEncoder Encoder => QoiEncoder.Instance;
}
