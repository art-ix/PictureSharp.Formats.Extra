using System.Buffers;

namespace PictureSharp.Formats.Xpm;

/// <summary>
/// The colours of an XPM file by their pixel keys. Keys of one or two characters index a table; longer keys (up to four
/// characters) use a dictionary.
/// </summary>
internal sealed class XpmPalette : IDisposable
{
    private readonly int _charsPerPixel;
    private readonly Rgba32[]? _table;
    private readonly bool[]? _defined;
    private readonly Dictionary<int, Rgba32>? _map;

    public XpmPalette(int charsPerPixel)
    {
        _charsPerPixel = charsPerPixel;
        if (charsPerPixel <= 2)
        {
            int size = 1 << (8 * charsPerPixel);
            _table = ArrayPool<Rgba32>.Shared.Rent(size);
            _defined = ArrayPool<bool>.Shared.Rent(size);
            Array.Clear(_defined, 0, size);
        }
        else
        {
            _map = [];
        }
    }

    public bool HasAlpha { get; private set; }

    public void Add(ReadOnlySpan<byte> key, Rgba32 color)
    {
        int k = Key(key);
        HasAlpha |= color.A != 255;
        if (_map is not null)
        {
            _map[k] = color;
        }
        else
        {
            _table![k] = color;
            _defined![k] = true;
        }
    }

    public bool TryGet(ReadOnlySpan<byte> key, out Rgba32 color)
    {
        int k = Key(key);
        if (_map is not null)
        {
            return _map.TryGetValue(k, out color);
        }

        color = _table![k];
        return _defined![k];
    }

    public void Dispose()
    {
        if (_table is not null)
        {
            ArrayPool<Rgba32>.Shared.Return(_table);
            ArrayPool<bool>.Shared.Return(_defined!);
        }
    }

    private int Key(ReadOnlySpan<byte> key)
    {
        int k = 0;
        for (int i = 0; i < _charsPerPixel; i++)
        {
            k |= key[i] << (8 * i);
        }

        return k;
    }
}
