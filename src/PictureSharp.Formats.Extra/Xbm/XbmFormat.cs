namespace PictureSharp.Formats.Xbm;

/// <summary>
/// The X BitMap format plugin (<c>.xbm</c>): reads X10 and X11 bitmaps, writes X11 bitmaps. Set bits are black and clear
/// bits white.
/// </summary>
/// <example>
/// <code>
/// ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddFormat(new XbmFormat()).Build();
/// </code>
/// </example>
public sealed class XbmFormat : IImageFormatModule
{
    // One instance per process: ImageFormat compares by reference, so every module shares it.
    internal static readonly ImageFormat Descriptor = ImageFormat.Create("XBM", "image/x-xbitmap", ".xbm");

    /// <inheritdoc/>
    public ImageFormat Format => Descriptor;

    /// <inheritdoc/>
    public int HeaderSize => 256;

    /// <inheritdoc/>
    public bool IsSupported(ReadOnlySpan<byte> header) => XbmParser.HasSignature(header);

    /// <inheritdoc/>
    public IImageFormatDecoder Decoder => XbmDecoder.Instance;

    /// <inheritdoc/>
    public IImageFormatEncoder Encoder => XbmEncoder.Instance;
}
