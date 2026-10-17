namespace PictureSharp.Formats.Tga;

/// <summary>Where the first stored row of a TGA image is.</summary>
public enum TgaOrigin
{
    /// <summary>Rows go from the bottom up, the TGA default that most readers expect.</summary>
    BottomLeft = 0,

    /// <summary>Rows go from the top down.</summary>
    TopLeft = 1,
}
