using PictureSharp.Formats.Dds;
using PictureSharp.Formats.Pnm;
using PictureSharp.Formats.Psd;
using PictureSharp.Formats.Qoi;
using PictureSharp.Formats.Tga;

namespace PictureSharp.Formats.Extra;

/// <summary>All formats of the PictureSharp.Formats.Extra package and their registration.</summary>
/// <example>
/// <code>
/// ImageConfiguration.Default = ImageConfiguration.Default.ToBuilder().AddExtraFormats().Build();
/// </code>
/// </example>
public static class ExtraFormats
{
    /// <summary>A new module for every format of the package: QOI, PNM, TGA, DDS and PSD.</summary>
    public static IReadOnlyList<IImageFormatModule> CreateModules() => [new QoiFormat(), new PnmFormat(), new TgaFormat(), new DdsFormat(), new PsdFormat()];

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
