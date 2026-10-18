namespace PictureSharp.Formats.Vtf;

/// <summary>
/// Selects the VTF surface to decode, passed in <see cref="DecoderOptions.FormatOptions"/>. Without it the decoder
/// reads the largest mip level of the first frame, face and slice. <c>Image.Identify</c> reports the size of the
/// selected surface. An index the file does not store throws <see cref="ImageFormatException"/>.
/// </summary>
/// <example>
/// <code>
/// using var frame = Image.Load("water.vtf", new DecoderOptions { FormatOptions = [new VtfDecoderOptions { Frame = 3 }] });
/// </code>
/// </example>
public sealed record VtfDecoderOptions : FormatDecoderOptions
{
    /// <summary>The mip level: 0 is the full size, each next level halves both sides (at least 1). Default: 0.</summary>
    public int MipLevel { get; init => field = NotNegative(value, nameof(MipLevel)); }

    /// <summary>The animation frame. Default: 0.</summary>
    public int Frame { get; init => field = NotNegative(value, nameof(Frame)); }

    /// <summary>
    /// The face of an environment map: 0–5 for the cube faces in the file's order (right, left, back, front, up,
    /// down), 6 for the sphere map that some 7.1–7.4 files add. Default: 0.
    /// </summary>
    public int Face { get; init => field = NotNegative(value, nameof(Face)); }

    /// <summary>The slice of a volume texture; each mip level has half the slices of the previous one (at least 1). Default: 0.</summary>
    public int Slice { get; init => field = NotNegative(value, nameof(Slice)); }

    private static int NotNegative(int value, string name) =>
        value >= 0 ? value : throw new ArgumentOutOfRangeException(name, value, "The index must not be negative.");
}
