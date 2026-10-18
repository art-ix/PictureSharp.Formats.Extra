namespace PictureSharp.Formats.Dds;

/// <summary>
/// The DirectDraw Surface format plugin: uncompressed and block-compressed (BC1–BC7, BC6H) textures with DX9 or DX10
/// headers. One surface is decoded: mip level 0 of the first array element, cube face or volume slice, or the one
/// <see cref="DdsDecoderOptions"/> selects.
/// </summary>
/// <example>
/// <code>
/// ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddFormat(new DdsFormat()).Build();
/// </code>
/// </example>
public sealed class DdsFormat : IImageFormatModule
{
    // One instance per process: ImageFormat compares by reference, so every module shares it.
    internal static readonly ImageFormat Descriptor = ImageFormat.Create("DDS", "image/vnd-ms.dds", ".dds");

    /// <inheritdoc/>
    public ImageFormat Format => Descriptor;

    /// <inheritdoc/>
    public int HeaderSize => 8;

    /// <inheritdoc/>
    public bool IsSupported(ReadOnlySpan<byte> header) => DdsHeader.HasSignature(header);

    /// <inheritdoc/>
    public IImageFormatDecoder Decoder => DdsDecoder.Instance;

    /// <inheritdoc/>
    public IImageFormatEncoder Encoder => DdsEncoder.Instance;
}
