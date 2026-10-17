namespace PictureSharp.Formats.Pnm;

/// <summary>Options for the Netpbm encoder. Netpbm stores no metadata blocks; PBM, PGM and PPM drop alpha.</summary>
public sealed record PnmEncoderOptions : EncoderOptions
{
    /// <inheritdoc/>
    public override ImageFormat Format => PnmFormat.Descriptor;

    /// <summary>The file type. Default: <see cref="PnmKind.Auto"/>.</summary>
    public PnmKind Kind
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Kind), value, "Unknown Netpbm kind.");
    }

    /// <summary>
    /// Whether to write the plain (ASCII) variant P1–P3 instead of binary P4–P6. PAM has no plain variant, so the
    /// encoder rejects it together with <see cref="PnmKind.Pam"/> and writes binary PAM for <see cref="PnmKind.Auto"/>.
    /// Default: false.
    /// </summary>
    public bool Plain { get; init; }
}
