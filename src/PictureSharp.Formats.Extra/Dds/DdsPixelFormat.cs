namespace PictureSharp.Formats.Dds;

/// <summary>The uncompressed pixel layout the DDS encoder writes.</summary>
public enum DdsPixelFormat
{
    /// <summary><see cref="Bgra32"/> when any pixel is transparent, otherwise <see cref="Bgrx32"/>.</summary>
    Auto,

    /// <summary>32-bit BGRA with alpha (D3DFMT_A8R8G8B8, DXGI_FORMAT_B8G8R8A8_UNORM).</summary>
    Bgra32,

    /// <summary>32-bit BGR with an unused fourth byte (D3DFMT_X8R8G8B8, DXGI_FORMAT_B8G8R8X8_UNORM).</summary>
    Bgrx32,
}
