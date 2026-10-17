using System.Buffers;
using System.Runtime.CompilerServices;

namespace PictureSharp.Formats.Extra.Common;

/// <summary>A pooled write buffer that flushes to a stream in blocks, so encoders write bytes without a call per byte.</summary>
internal sealed class OutputBuffer(Stream output) : IDisposable
{
    private const int Capacity = 64 * 1024;
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(Capacity);
    private int _length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(byte value)
    {
        if (_length == _buffer.Length)
        {
            Flush();
        }

        _buffer[_length++] = value;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            if (_length == _buffer.Length)
            {
                Flush();
            }

            int count = Math.Min(data.Length, _buffer.Length - _length);
            data[..count].CopyTo(_buffer.AsSpan(_length));
            _length += count;
            data = data[count..];
        }
    }

    /// <summary>Returns room for <paramref name="count"/> bytes (at most 1024); call <see cref="Advance"/> with the bytes used.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int count)
    {
        if (_buffer.Length - _length < count)
        {
            Flush();
        }

        return _buffer.AsSpan(_length, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count) => _length += count;

    public void Flush()
    {
        output.Write(_buffer, 0, _length);
        _length = 0;
    }

    public void Dispose()
    {
        if (_buffer.Length != 0)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
        }
    }
}
