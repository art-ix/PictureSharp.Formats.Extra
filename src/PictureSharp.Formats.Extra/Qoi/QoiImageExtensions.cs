namespace PictureSharp.Formats.Qoi;

/// <summary>QOI shortcuts for <see cref="Image"/>; require <see cref="QoiFormat"/> to be registered.</summary>
public static class QoiImageExtensions
{
    extension(Image image)
    {
        /// <summary>Saves the image as QOI; null options mean the defaults. Animations keep the first frame only.</summary>
        public void SaveAsQoi(string path, QoiEncoderOptions? options = null) => image.Save(path, options ?? new());

        /// <summary>Writes the image to a stream as QOI; null options mean the defaults. The stream is left open.</summary>
        public void SaveAsQoi(Stream stream, QoiEncoderOptions? options = null) => image.Save(stream, options ?? new());

        /// <summary>Saves the image as QOI asynchronously; null options mean the defaults.</summary>
        public Task SaveAsQoiAsync(string path, QoiEncoderOptions? options = null, CancellationToken cancellationToken = default) =>
            image.SaveAsync(path, options ?? new(), cancellationToken);

        /// <summary>Writes the image to a stream as QOI asynchronously; null options mean the defaults.</summary>
        public Task SaveAsQoiAsync(Stream stream, QoiEncoderOptions? options = null, CancellationToken cancellationToken = default) =>
            image.SaveAsync(stream, options ?? new(), cancellationToken);

        /// <summary>Encodes the image as QOI; null options mean the defaults.</summary>
        public byte[] ToQoi(QoiEncoderOptions? options = null) => image.Encode(options ?? new());
    }

    extension(ImageFormat)
    {
        /// <summary>QOI (<c>image/qoi</c>), available for decoding and encoding once <see cref="QoiFormat"/> is registered.</summary>
        public static ImageFormat Qoi => QoiFormat.Descriptor;
    }
}
