namespace PictureSharp.Formats.Sgi;

/// <summary>
/// Options for the SGI encoder. Images with transparency are written as RGBA, grey opaque images as greyscale and other
/// images as RGB, with 8 bits per channel. SGI stores no ICC, EXIF or XMP blocks.
/// </summary>
public sealed record SgiEncoderOptions : EncoderOptions
{
    /// <inheritdoc/>
    public override ImageFormat Format => SgiFormat.Descriptor;

    /// <summary>Whether to compress rows with run-length encoding. Default: true.</summary>
    public bool Rle { get; init; } = true;
}
