using System.Collections.Frozen;

namespace PictureSharp.Formats.Xpm;

/// <summary>The X11 colour names of X.Org's <c>rgb.txt</c>, matched without case and spaces.</summary>
internal static partial class X11Colors
{
    private const int MaxName = 32;

    private static readonly FrozenDictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> Names = Build().GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>Finds an ASCII colour name such as <c>light blue</c> or <c>LightBlue</c>.</summary>
    public static bool TryGet(ReadOnlySpan<byte> name, out Rgba32 color)
    {
        color = default;
        Span<char> key = stackalloc char[MaxName];
        int length = 0;
        foreach (byte c in name)
        {
            if (c is (byte)' ' or (byte)'\t')
            {
                continue;
            }

            if (length == MaxName)
            {
                return false;
            }

            key[length++] = char.ToLowerInvariant((char)c);
        }

        if (!Names.TryGetValue(key[..length], out int rgb))
        {
            return false;
        }

        color = new Rgba32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static FrozenDictionary<string, int> Build()
    {
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Range entry in Table.AsSpan().Split(';'))
        {
            ReadOnlySpan<char> text = Table.AsSpan()[entry];
            if (text.IsEmpty)
            {
                continue;
            }

            int equals = text.IndexOf('=');
            names[text[..equals].ToString()] = int.Parse(text[(equals + 1)..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        }

        return names.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
