namespace PictureSharp.Formats.Dds;

/// <summary>DDS shortcuts for <see cref="Image"/>; require <see cref="DdsFormat"/> to be registered.</summary>
public static class DdsImageExtensions
{
    extension(Image image)
    {
        /// <summary>Saves the image as DDS; null options mean the defaults. Animations keep the first frame only.</summary>
        public void SaveAsDds(string path, DdsEncoderOptions? options = null) => image.Save(path, options ?? new());

        /// <summary>Writes the image to a stream as DDS; null options mean the defaults. The stream is left open.</summary>
        public void SaveAsDds(Stream stream, DdsEncoderOptions? options = null) => image.Save(stream, options ?? new());

        /// <summary>Saves the image as DDS asynchronously; null options mean the defaults.</summary>
        public Task SaveAsDdsAsync(string path, DdsEncoderOptions? options = null, CancellationToken cancellationToken = default) =>
            image.SaveAsync(path, options ?? new(), cancellationToken);

        /// <summary>Writes the image to a stream as DDS asynchronously; null options mean the defaults.</summary>
        public Task SaveAsDdsAsync(Stream stream, DdsEncoderOptions? options = null, CancellationToken cancellationToken = default) =>
            image.SaveAsync(stream, options ?? new(), cancellationToken);

        /// <summary>Encodes the image as DDS; null options mean the defaults.</summary>
        public byte[] ToDds(DdsEncoderOptions? options = null) => image.Encode(options ?? new());
    }

    extension(ImageFormat)
    {
        /// <summary>DDS (<c>image/vnd-ms.dds</c>), available for decoding and encoding once <see cref="DdsFormat"/> is registered.</summary>
        public static ImageFormat Dds => DdsFormat.Descriptor;
    }
}
