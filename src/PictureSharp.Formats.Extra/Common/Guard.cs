namespace PictureSharp.Formats.Extra.Common;

internal static class Guard
{
    /// <summary>Rejects images an encoder cannot borrow pixels from; the library never passes them, direct callers might.</summary>
    public static void Encodable(Image image, Stream output)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
        {
            throw new ArgumentException("The stream must be writable.", nameof(output));
        }
    }
}
