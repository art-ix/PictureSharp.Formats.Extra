namespace PictureSharp.Formats.Tga;

/// <summary>TGA shortcuts for <see cref="Image"/>; require <see cref="TgaFormat"/> to be registered.</summary>
public static class TgaImageExtensions
{
    extension(Image image)
    {
        /// <summary>Saves the image as TGA; null options mean the defaults. Animations keep the first frame only.</summary>
        public void SaveAsTga(string path, TgaEncoderOptions? options = null) => image.Save(path, options ?? new());

        /// <summary>Writes the image to a stream as TGA; null options mean the defaults. The stream is left open.</summary>
        public void SaveAsTga(Stream stream, TgaEncoderOptions? options = null) => image.Save(stream, options ?? new());

        /// <summary>Saves the image as TGA asynchronously; null options mean the defaults.</summary>
        public Task SaveAsTgaAsync(string path, TgaEncoderOptions? options = null, CancellationToken cancellationToken = default) =>
            image.SaveAsync(path, options ?? new(), cancellationToken);

        /// <summary>Writes the image to a stream as TGA asynchronously; null options mean the defaults.</summary>
        public Task SaveAsTgaAsync(Stream stream, TgaEncoderOptions? options = null, CancellationToken cancellationToken = default) =>
            image.SaveAsync(stream, options ?? new(), cancellationToken);

        /// <summary>Encodes the image as TGA; null options mean the defaults.</summary>
        public byte[] ToTga(TgaEncoderOptions? options = null) => image.Encode(options ?? new());
    }

    extension(ImageFormat)
    {
        /// <summary>TGA (<c>image/x-tga</c>), available for decoding and encoding once <see cref="TgaFormat"/> is registered.</summary>
        public static ImageFormat Tga => TgaFormat.Descriptor;
    }
}
