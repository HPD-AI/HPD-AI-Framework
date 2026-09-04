using System.Buffers;
using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

internal sealed class AnsiFrameWriter : IDisposable
{
    private const int DefaultCapacity = 4096;
    private readonly ArrayPool<char> _pool;
    private char[]? _buffer;
    private int _written;

    public AnsiFrameWriter()
        : this(ArrayPool<char>.Shared, DefaultCapacity)
    {
    }

    internal AnsiFrameWriter(ArrayPool<char> pool, int initialCapacity = DefaultCapacity)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _buffer = pool.Rent(Math.Max(1, initialCapacity));
    }

    public int Length => _written;

    internal ReadOnlySpan<char> WrittenSpan
    {
        get
        {
            ThrowIfDisposed();
            return _buffer.AsSpan(0, _written);
        }
    }

    public TerminalFrameLease CreateLease()
    {
        ThrowIfDisposed();
        return new TerminalFrameLease(_buffer.AsSpan(0, _written));
    }

    public void Clear()
    {
        ThrowIfDisposed();
        _written = 0;
    }

    public void Write(char value)
    {
        EnsureCapacity(1);
        _buffer![_written++] = value;
    }

    public void Write(ReadOnlySpan<char> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(_buffer.AsSpan(_written));
        _written += value.Length;
    }

    public void WriteInt(int value)
    {
        Span<char> scratch = stackalloc char[16];
        if (!value.TryFormat(scratch, out var charsWritten))
        {
            throw new InvalidOperationException($"Could not format terminal integer value '{value}'.");
        }

        Write(scratch[..charsWritten]);
    }

    public void FlushTo(ITerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ThrowIfDisposed();
        if (_written == 0)
        {
            terminal.Flush();
            return;
        }

        terminal.Write(_buffer.AsSpan(0, _written));
        terminal.Flush();
        _written = 0;
    }

    public override string ToString()
    {
        ThrowIfDisposed();
        return new string(_buffer.AsSpan(0, _written));
    }

    public void Dispose()
    {
        var buffer = _buffer;
        if (buffer is null)
        {
            return;
        }

        _buffer = null;
        _written = 0;
        _pool.Return(buffer);
    }

    private void EnsureCapacity(int additional)
    {
        ThrowIfDisposed();
        if (additional <= _buffer!.Length - _written)
        {
            return;
        }

        var required = checked(_written + additional);
        var nextLength = _buffer.Length;
        do
        {
            nextLength = checked(nextLength * 2);
        }
        while (nextLength < required);

        var next = _pool.Rent(nextLength);
        _buffer.AsSpan(0, _written).CopyTo(next);
        _pool.Return(_buffer);
        _buffer = next;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_buffer is null, this);
    }
}
