namespace PictureSharp.Formats.SunRaster;

/// <summary>Sun Raster shortcuts; require <see cref="SunRasterFormat"/> to be registered.</summary>
public static class SunRasterImageExtensions
{
    extension(ImageFormat)
    {
        /// <summary>Sun Raster (<c>image/x-sun-raster</c>), available for decoding once <see cref="SunRasterFormat"/> is registered.</summary>
        public static ImageFormat SunRaster => SunRasterFormat.Descriptor;
    }
}
