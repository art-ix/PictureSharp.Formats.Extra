namespace PictureSharp.Formats.Dds;

/// <summary>
/// Options for the DDS encoder. It writes one uncompressed 2D surface without mipmaps, with a DX9 header that every
/// DDS reader understands. DDS stores no ICC, EXIF or XMP blocks.
/// </summary>
public sealed record DdsEncoderOptions : EncoderOptions
{
    /// <inheritdoc/>
    public override ImageFormat Format => DdsFormat.Descriptor;

    /// <summary>The pixel layout. Default: <see cref="DdsPixelFormat.Auto"/>.</summary>
    public DdsPixelFormat PixelFormat
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(PixelFormat), value, "Unknown DDS pixel format.");
    }
}
