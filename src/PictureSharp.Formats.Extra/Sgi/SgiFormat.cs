namespace PictureSharp.Formats.Sgi;

/// <summary>
/// The SGI image format plugin (<c>.sgi</c>, <c>.rgb</c>, <c>.rgba</c>, <c>.bw</c>): greyscale, greyscale with alpha,
/// RGB and RGBA with 8 or 16 bits per channel, verbatim or RLE. Images are written with 8 bits per channel.
/// </summary>
/// <example>
/// <code>
/// ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddFormat(new SgiFormat()).Build();
/// </code>
/// </example>
public sealed class SgiFormat : IImageFormatModule
{
    // One instance per process: ImageFormat compares by reference, so every module shares it.
    internal static readonly ImageFormat Descriptor = ImageFormat.Create("SGI", "image/sgi", ".sgi", ".rgb", ".rgba", ".bw");

    /// <inheritdoc/>
    public ImageFormat Format => Descriptor;

    /// <inheritdoc/>
    public int HeaderSize => 4;

    /// <inheritdoc/>
    public bool IsSupported(ReadOnlySpan<byte> header) => SgiHeader.HasSignature(header);

    /// <inheritdoc/>
    public IImageFormatDecoder Decoder => SgiDecoder.Instance;

    /// <inheritdoc/>
    public IImageFormatEncoder Encoder => SgiEncoder.Instance;
}
