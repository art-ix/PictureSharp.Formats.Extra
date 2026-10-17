namespace PictureSharp.Formats.Tga;

/// <summary>
/// Options for the TGA encoder. Images with transparency are written as 32-bit BGRA, grey opaque images as 8-bit
/// greyscale and other images as 24-bit BGR. TGA stores no ICC, EXIF or XMP blocks.
/// </summary>
public sealed record TgaEncoderOptions : EncoderOptions
{
    /// <inheritdoc/>
    public override ImageFormat Format => TgaFormat.Descriptor;

    /// <summary>Whether to compress rows with run-length encoding; packets never cross rows. Default: true.</summary>
    public bool Rle { get; init; } = true;

    /// <summary>The order of stored rows. Default: <see cref="TgaOrigin.BottomLeft"/>.</summary>
    public TgaOrigin Origin
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Origin), value, "Unknown TGA origin.");
    }
}
