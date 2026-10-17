namespace PictureSharp.Formats.Xbm;

/// <summary>XBM shortcuts for <see cref="Image"/>; require <see cref="XbmFormat"/> to be registered.</summary>
public static class XbmImageExtensions
{
    extension(Image image)
    {
        /// <summary>Saves the image as XBM; null options mean the defaults. Animations keep the first frame only.</summary>
        public void SaveAsXbm(string path, XbmEncoderOptions? options = null) => image.Save(path, options ?? new());

        /// <summary>Writes the image to a stream as XBM; null options mean the defaults. The stream is left open.</summary>
        public void SaveAsXbm(Stream stream, XbmEncoderOptions? options = null) => image.Save(stream, options ?? new());

        /// <summary>Saves the image as XBM asynchronously; null options mean the defaults.</summary>
        public Task SaveAsXbmAsync(string path, XbmEncoderOptions? options = null, CancellationToken cancellationToken = default) =>
            image.SaveAsync(path, options ?? new(), cancellationToken);

        /// <summary>Writes the image to a stream as XBM asynchronously; null options mean the defaults.</summary>
        public Task SaveAsXbmAsync(Stream stream, XbmEncoderOptions? options = null, CancellationToken cancellationToken = default) =>
            image.SaveAsync(stream, options ?? new(), cancellationToken);

        /// <summary>Encodes the image as XBM; null options mean the defaults.</summary>
        public byte[] ToXbm(XbmEncoderOptions? options = null) => image.Encode(options ?? new());
    }

    extension(ImageFormat)
    {
        /// <summary>XBM (<c>image/x-xbitmap</c>), available for decoding and encoding once <see cref="XbmFormat"/> is registered.</summary>
        public static ImageFormat Xbm => XbmFormat.Descriptor;
    }
}
