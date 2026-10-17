namespace PictureSharp.Formats.Vtf;

/// <summary>VTF shortcuts; require <see cref="VtfFormat"/> to be registered.</summary>
public static class VtfImageExtensions
{
    extension(ImageFormat)
    {
        /// <summary>VTF (<c>image/x-vtf</c>), available for decoding once <see cref="VtfFormat"/> is registered.</summary>
        public static ImageFormat Vtf => VtfFormat.Descriptor;
    }
}
