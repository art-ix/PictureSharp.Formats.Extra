namespace PictureSharp.Formats.Icns;

/// <summary>ICNS shortcuts; require <see cref="IcnsFormat"/> to be registered.</summary>
public static class IcnsImageExtensions
{
    extension(ImageFormat)
    {
        /// <summary>ICNS (<c>image/icns</c>), available for decoding once <see cref="IcnsFormat"/> is registered.</summary>
        public static ImageFormat Icns => IcnsFormat.Descriptor;
    }
}
