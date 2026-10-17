namespace PictureSharp.Formats.Psd;

/// <summary>PSD shortcuts; require <see cref="PsdFormat"/> to be registered.</summary>
public static class PsdImageExtensions
{
    extension(ImageFormat)
    {
        /// <summary>PSD and PSB (<c>image/vnd.adobe.photoshop</c>), available for decoding once <see cref="PsdFormat"/> is registered.</summary>
        public static ImageFormat Psd => PsdFormat.Descriptor;
    }
}
