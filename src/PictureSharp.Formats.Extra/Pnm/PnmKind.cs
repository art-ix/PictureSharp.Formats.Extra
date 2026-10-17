namespace PictureSharp.Formats.Pnm;

/// <summary>The Netpbm file type the encoder writes.</summary>
public enum PnmKind
{
    /// <summary>PAM when some pixel is not opaque, PGM when all pixels are grey, otherwise PPM.</summary>
    Auto = 0,

    /// <summary>PBM (P4, or P1 when plain): 1 bit per pixel, white where the luma is at least 128.</summary>
    Bitmap,

    /// <summary>PGM (P5, or P2 when plain): 8-bit grey from the ITU-R BT.601 luma.</summary>
    Graymap,

    /// <summary>PPM (P6, or P3 when plain): 8-bit RGB.</summary>
    Pixmap,

    /// <summary>PAM (P7): <c>RGB_ALPHA</c>, <c>RGB</c>, <c>GRAYSCALE_ALPHA</c> or <c>GRAYSCALE</c>, whichever keeps every pixel.</summary>
    Pam,
}
