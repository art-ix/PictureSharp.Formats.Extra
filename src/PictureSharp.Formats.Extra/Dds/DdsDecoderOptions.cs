namespace PictureSharp.Formats.Dds;

/// <summary>
/// Selects the DDS surface to decode, passed in <see cref="DecoderOptions.FormatOptions"/>. Without it the decoder
/// reads mip level 0 of the first layer. <c>Image.Identify</c> reports the size of the selected surface.
/// </summary>
/// <example>
/// <code>
/// using var mip = Image.Load("texture.dds", new DecoderOptions { FormatOptions = [new DdsDecoderOptions { MipLevel = 2 }] });
/// </code>
/// </example>
public sealed record DdsDecoderOptions : FormatDecoderOptions
{
    /// <summary>
    /// The mip level: 0 is the full size, each next level halves both sides (rounding down, at least 1). A level the
    /// file does not store throws <see cref="ImageFormatException"/>. Default: 0.
    /// </summary>
    public int MipLevel
    {
        get;
        init => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MipLevel), value, "The mip level must not be negative.");
    }

    /// <summary>
    /// The layer: the texture array element, the cube face (in the order +X, −X, +Y, −Y, +Z, −Z; a cube map array
    /// numbers the faces of element <c>n</c> from <c>6 × n</c>; a DX9 cube map without some faces counts only the
    /// stored ones) or the volume slice of the selected mip level. A layer the file does not store throws
    /// <see cref="ImageFormatException"/>. Default: 0.
    /// </summary>
    public int Layer
    {
        get;
        init => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(Layer), value, "The layer must not be negative.");
    }
}
