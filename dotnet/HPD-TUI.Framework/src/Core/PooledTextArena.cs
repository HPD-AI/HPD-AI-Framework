using System.Buffers;

namespace HPD.TUI.Core;

/// <summary>Generation-owned storage for command text copied from ephemeral spans.</summary>
internal sealed class PooledTextArena : IDisposable
{
    private char[] _buffer = ArrayPool<char>.Shared.Rent(256);
    private int _length;

    public (int Offset, int Length) Append(ReadOnlySpan<char> text)
    {
        EnsureCapacity(_length + text.Length);
        var offset = _length;
        text.CopyTo(_buffer.AsSpan(offset));
        _length += text.Length;
        return (offset, text.Length);
    }

    public ReadOnlySpan<char> GetSpan(int offset, int length) => _buffer.AsSpan(offset, length);

    public void Reset() => _length = 0;

    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = [];
        _length = 0;
        if (buffer.Length != 0) ArrayPool<char>.Shared.Return(buffer);
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _buffer.Length) return;
        var replacement = ArrayPool<char>.Shared.Rent(Math.Max(required, _buffer.Length * 2));
        _buffer.AsSpan(0, _length).CopyTo(replacement);
        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = replacement;
    }
}
