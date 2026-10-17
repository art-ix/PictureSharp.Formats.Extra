namespace PictureSharp.Formats.Pnm;

/// <summary>Netpbm shortcuts for <see cref="Image"/>; require <see cref="PnmFormat"/> to be registered.</summary>
public static class PnmImageExtensions
{
    extension(Image image)
    {
        /// <summary>Saves the image as Netpbm; null options mean the defaults. Animations keep the first frame only.</summary>
        public void SaveAsPnm(string path, PnmEncoderOptions? options = null) => image.Save(path, options ?? new());

        /// <summary>Writes the image to a stream as Netpbm; null options mean the defaults. The stream is left open.</summary>
        public void SaveAsPnm(Stream stream, PnmEncoderOptions? options = null) => image.Save(stream, options ?? new());

        /// <summary>Saves the image as Netpbm asynchronously; null options mean the defaults.</summary>
        public Task SaveAsPnmAsync(string path, PnmEncoderOptions? options = null, CancellationToken cancellationToken = default) =>
            image.SaveAsync(path, options ?? new(), cancellationToken);

        /// <summary>Writes the image to a stream as Netpbm asynchronously; null options mean the defaults.</summary>
        public Task SaveAsPnmAsync(Stream stream, PnmEncoderOptions? options = null, CancellationToken cancellationToken = default) =>
            image.SaveAsync(stream, options ?? new(), cancellationToken);

        /// <summary>Encodes the image as Netpbm; null options mean the defaults.</summary>
        public byte[] ToPnm(PnmEncoderOptions? options = null) => image.Encode(options ?? new());
    }

    extension(ImageFormat)
    {
        /// <summary>Netpbm (<c>image/x-portable-anymap</c>), available for decoding and encoding once <see cref="PnmFormat"/> is registered.</summary>
        public static ImageFormat Pnm => PnmFormat.Descriptor;
    }
}
