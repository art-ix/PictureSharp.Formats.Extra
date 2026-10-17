namespace PictureSharp.Formats.Qoi;

/// <summary>The colour space a QOI header declares; it does not change how pixels are stored.</summary>
public enum QoiColorSpace
{
    /// <summary>sRGB colour with linear alpha (header value 0).</summary>
    Srgb = 0,

    /// <summary>All channels linear (header value 1); decoded images get <see cref="ImageMetadata.Gamma"/> = 1.</summary>
    Linear = 1,
}
