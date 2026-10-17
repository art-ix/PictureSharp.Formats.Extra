namespace PictureSharp.Formats.Xpm;

/// <summary>XPM shortcuts; require <see cref="XpmFormat"/> to be registered.</summary>
public static class XpmImageExtensions
{
    extension(ImageFormat)
    {
        /// <summary>XPM (<c>image/x-xpixmap</c>), available for decoding once <see cref="XpmFormat"/> is registered.</summary>
        public static ImageFormat Xpm => XpmFormat.Descriptor;
    }
}
