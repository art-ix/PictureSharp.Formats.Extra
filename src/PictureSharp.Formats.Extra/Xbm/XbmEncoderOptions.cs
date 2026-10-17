namespace PictureSharp.Formats.Xbm;

/// <summary>
/// Options for the XBM encoder, which writes X11 bitmaps. A pixel becomes a set (black) bit when it is at least half
/// opaque and its luma is below 128; other pixels become clear (white) bits.
/// </summary>
public sealed record XbmEncoderOptions : EncoderOptions
{
    /// <inheritdoc/>
    public override ImageFormat Format => XbmFormat.Descriptor;

    /// <summary>The C identifier that prefixes <c>_width</c>, <c>_height</c> and <c>_bits</c>. Default: <c>image</c>.</summary>
    public string Name
    {
        get;
        init => field = IsIdentifier(value) ? value : throw new ArgumentException("The XBM name must be a C identifier.", nameof(Name));
    } = "image";

    /// <summary>The hotspot written as <c>_x_hot</c> and <c>_y_hot</c>, or null for none. Default: null.</summary>
    public (int X, int Y)? Hotspot
    {
        get;
        init => field = value is null or (>= 0, >= 0)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Hotspot), value, "The XBM hotspot cannot be negative.");
    }

    private static bool IsIdentifier(string? value) =>
        !string.IsNullOrEmpty(value) && (char.IsAsciiLetter(value[0]) || value[0] == '_')
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
