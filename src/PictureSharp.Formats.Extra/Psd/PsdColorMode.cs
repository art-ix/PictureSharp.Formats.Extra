namespace PictureSharp.Formats.Psd;

/// <summary>The colour mode field of the PSD file header.</summary>
internal enum PsdColorMode
{
    Bitmap = 0,
    Grayscale = 1,
    Indexed = 2,
    Rgb = 3,
    Cmyk = 4,
    Multichannel = 7,
    Duotone = 8,
    Lab = 9,
}
