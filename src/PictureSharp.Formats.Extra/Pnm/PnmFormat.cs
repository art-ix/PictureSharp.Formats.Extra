namespace PictureSharp.Formats.Pnm;

/// <summary>
/// The Netpbm format plugin: PBM, PGM and PPM (P1–P6, plain and binary) and PAM (P7). Samples above 8 bits are reduced
/// to 8 bits as round(value / maxval × 255), ties to even, as in Pillow.
/// </summary>
/// <example>
/// <code>
/// ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddFormat(new PnmFormat()).Build();
/// </code>
/// </example>
public sealed class PnmFormat : IImageFormatModule
{
    // One instance per process: ImageFormat compares by reference, so every module shares it.
    internal static readonly ImageFormat Descriptor =
        ImageFormat.Create("PNM", "image/x-portable-anymap", ".pnm", ".pbm", ".pgm", ".ppm", ".pam");

    /// <inheritdoc/>
    public ImageFormat Format => Descriptor;

    /// <inheritdoc/>
    public int HeaderSize => 3;

    /// <inheritdoc/>
    public bool IsSupported(ReadOnlySpan<byte> header) =>
        header.Length >= 3 && header[0] == (byte)'P' && header[1] is >= (byte)'1' and <= (byte)'7' && PnmHeader.IsWhitespace(header[2]);

    /// <inheritdoc/>
    public IImageFormatDecoder Decoder => PnmDecoder.Instance;

    /// <inheritdoc/>
    public IImageFormatEncoder Encoder => PnmEncoder.Instance;
}
