namespace PictureSharp.Formats.Icns;

/// <summary>
/// The Apple Icon Image format plugin (<c>.icns</c>), read only: decodes the largest icon, from a PNG element, an
/// RLE-compressed RGB element with its 8-bit mask, an ARGB element or, without colour icons, a 1-bit icon with its mask.
/// JPEG 2000 elements are skipped.
/// </summary>
/// <example>
/// <code>
/// ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddFormat(new IcnsFormat()).Build();
/// </code>
/// </example>
public sealed class IcnsFormat : IImageFormatModule
{
    // One instance per process: ImageFormat compares by reference, so every module shares it.
    internal static readonly ImageFormat Descriptor = ImageFormat.Create("ICNS", "image/icns", ".icns");

    /// <inheritdoc/>
    public ImageFormat Format => Descriptor;

    /// <inheritdoc/>
    public int HeaderSize => 4;

    /// <inheritdoc/>
    public bool IsSupported(ReadOnlySpan<byte> header) => header.StartsWith("icns"u8);

    /// <inheritdoc/>
    public IImageFormatDecoder Decoder => IcnsDecoder.Instance;
}
