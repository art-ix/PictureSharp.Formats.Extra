using PictureSharp.Formats.Dds;
using PictureSharp.Formats.Icns;
using PictureSharp.Formats.Pnm;
using PictureSharp.Formats.Psd;
using PictureSharp.Formats.Qoi;
using PictureSharp.Formats.Sgi;
using PictureSharp.Formats.SunRaster;
using PictureSharp.Formats.Tga;
using PictureSharp.Formats.Vtf;
using PictureSharp.Formats.Xbm;
using PictureSharp.Formats.Xpm;

namespace PictureSharp.Formats.Extra;

/// <summary>All formats of the PictureSharp.Formats.Extra package and their registration.</summary>
/// <example>
/// <code>
/// ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddExtraFormats().Build();
/// </code>
/// </example>
public static class ExtraFormats
{
    /// <summary>A new module for every format of the package: QOI, PNM, TGA, DDS, PSD, SGI, Sun Raster, XBM, XPM, VTF and ICNS.</summary>
    public static IReadOnlyList<IImageFormatModule> CreateModules() =>
    [
        new QoiFormat(), new PnmFormat(), new TgaFormat(), new DdsFormat(), new PsdFormat(),
        new SgiFormat(), new SunRasterFormat(), new XbmFormat(), new XpmFormat(), new VtfFormat(), new IcnsFormat(),
    ];

    extension(ImageConfigurationBuilder builder)
    {
        /// <summary>Registers every format of the package; see <see cref="CreateModules"/>.</summary>
        public ImageConfigurationBuilder AddExtraFormats()
        {
            ArgumentNullException.ThrowIfNull(builder);
            foreach (IImageFormatModule module in CreateModules())
            {
                builder.AddFormat(module);
            }

            return builder;
        }
    }
}
