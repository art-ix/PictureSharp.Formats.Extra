namespace PictureSharp.Formats.Vtf;

/// <summary>
/// The Valve Texture Format plugin (<c>.vtf</c>), read only: versions 7.0–7.6 (7.6 with Deflate compression), the
/// largest mip level of the first frame, face and slice, or the one <see cref="VtfDecoderOptions"/> selects. Uncompressed 8- and 16-bit layouts and DXT1, DXT3, DXT5, ATI1N,
/// ATI2N and the Strata Source BC4–BC7 formats.
/// </summary>
/// <example>
/// <code>
/// ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddFormat(new VtfFormat()).Build();
/// </code>
/// </example>
public sealed class VtfFormat : IImageFormatModule
{
    // One instance per process: ImageFormat compares by reference, so every module shares it.
    internal static readonly ImageFormat Descriptor = ImageFormat.Create("VTF", "image/x-vtf", ".vtf");

    /// <inheritdoc/>
    public ImageFormat Format => Descriptor;

    /// <inheritdoc/>
    public int HeaderSize => 8;

    /// <inheritdoc/>
    public bool IsSupported(ReadOnlySpan<byte> header) => VtfHeader.HasSignature(header);

    /// <inheritdoc/>
    public IImageFormatDecoder Decoder => VtfDecoder.Instance;
}
