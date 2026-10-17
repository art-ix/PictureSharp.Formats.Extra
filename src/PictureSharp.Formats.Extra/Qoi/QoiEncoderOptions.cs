namespace PictureSharp.Formats.Qoi;

/// <summary>Options for the QOI encoder. QOI is lossless and stores no metadata blocks.</summary>
public sealed record QoiEncoderOptions : EncoderOptions
{
    /// <inheritdoc/>
    public override ImageFormat Format => QoiFormat.Descriptor;

    /// <summary>
    /// Whether to store the alpha channel (4 channels) or only RGB (3 channels, alpha dropped). Null stores alpha only
    /// when some pixel is not opaque. Default: null.
    /// </summary>
    public bool? IncludeAlpha { get; init; }

    /// <summary>
    /// The colour space written to the header. Null writes <see cref="QoiColorSpace.Linear"/> when the image's
    /// <see cref="ImageMetadata.Gamma"/> is 1, otherwise <see cref="QoiColorSpace.Srgb"/>. Default: null.
    /// </summary>
    public QoiColorSpace? ColorSpace
    {
        get;
        init => field = value is null || Enum.IsDefined(value.Value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(ColorSpace), value, "Unknown QOI colour space.");
    }
}
