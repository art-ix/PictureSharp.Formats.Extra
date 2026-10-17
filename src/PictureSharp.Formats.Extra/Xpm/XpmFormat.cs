namespace PictureSharp.Formats.Xpm;

/// <summary>
/// The X PixMap format plugin (<c>.xpm</c>), read only: XPM3 files with up to four characters per pixel, hexadecimal
/// colours, X11 colour names and the transparent colour <c>None</c>.
/// </summary>
/// <example>
/// <code>
/// ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddFormat(new XpmFormat()).Build();
/// </code>
/// </example>
public sealed class XpmFormat : IImageFormatModule
{
    // One instance per process: ImageFormat compares by reference, so every module shares it.
    internal static readonly ImageFormat Descriptor = ImageFormat.Create("XPM", "image/x-xpixmap", ".xpm");

    /// <inheritdoc/>
    public ImageFormat Format => Descriptor;

    /// <inheritdoc/>
    public int HeaderSize => 64;

    /// <inheritdoc/>
    public bool IsSupported(ReadOnlySpan<byte> header) => XpmParser.HasSignature(header);

    /// <inheritdoc/>
    public IImageFormatDecoder Decoder => XpmDecoder.Instance;
}
