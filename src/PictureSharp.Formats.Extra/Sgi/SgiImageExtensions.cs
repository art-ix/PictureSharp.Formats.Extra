namespace PictureSharp.Formats.Sgi;

/// <summary>SGI shortcuts for <see cref="Image"/>; require <see cref="SgiFormat"/> to be registered.</summary>
public static class SgiImageExtensions
{
    extension(Image image)
    {
        /// <summary>Saves the image as SGI; null options mean the defaults. Animations keep the first frame only.</summary>
        public void SaveAsSgi(string path, SgiEncoderOptions? options = null) => image.Save(path, options ?? new());

        /// <summary>Writes the image to a stream as SGI; null options mean the defaults. The stream is left open.</summary>
        public void SaveAsSgi(Stream stream, SgiEncoderOptions? options = null) => image.Save(stream, options ?? new());

        /// <summary>Saves the image as SGI asynchronously; null options mean the defaults.</summary>
        public Task SaveAsSgiAsync(string path, SgiEncoderOptions? options = null, CancellationToken cancellationToken = default) =>
            image.SaveAsync(path, options ?? new(), cancellationToken);

        /// <summary>Writes the image to a stream as SGI asynchronously; null options mean the defaults.</summary>
        public Task SaveAsSgiAsync(Stream stream, SgiEncoderOptions? options = null, CancellationToken cancellationToken = default) =>
            image.SaveAsync(stream, options ?? new(), cancellationToken);

        /// <summary>Encodes the image as SGI; null options mean the defaults.</summary>
        public byte[] ToSgi(SgiEncoderOptions? options = null) => image.Encode(options ?? new());
    }

    extension(ImageFormat)
    {
        /// <summary>SGI (<c>image/sgi</c>), available for decoding and encoding once <see cref="SgiFormat"/> is registered.</summary>
        public static ImageFormat Sgi => SgiFormat.Descriptor;
    }
}
