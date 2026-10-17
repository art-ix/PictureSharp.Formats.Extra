namespace PictureSharp.Formats.SunRaster;

/// <summary>
/// The Sun Raster format plugin (<c>.ras</c>, <c>.sun</c>), read only: 1, 4 and 8 bits per pixel with or without an
/// RGB colour map, 24 and 32 bits per pixel in BGR or RGB order, uncompressed or RLE.
/// </summary>
/// <example>
/// <code>
/// ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddFormat(new SunRasterFormat()).Build();
/// </code>
/// </example>
public sealed class SunRasterFormat : IImageFormatModule
{
    // One instance per process: ImageFormat compares by reference, so every module shares it.
    internal static readonly ImageFormat Descriptor = ImageFormat.Create("SUN", "image/x-sun-raster", ".ras", ".sun");

    /// <inheritdoc/>
    public ImageFormat Format => Descriptor;

    /// <inheritdoc/>
    public int HeaderSize => 4;

    /// <inheritdoc/>
    public bool IsSupported(ReadOnlySpan<byte> header) => SunRasterHeader.HasSignature(header);

    /// <inheritdoc/>
    public IImageFormatDecoder Decoder => SunRasterDecoder.Instance;
}
