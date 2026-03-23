using System.Buffers;

namespace HPD.ML.Abstractions;

/// <summary>Adapts a Stream to IBufferWriter&lt;byte&gt;.</summary>
internal sealed class StreamBufferWriter(Stream stream) : IBufferWriter<byte>
{
    private byte[] _buffer = new byte[4096];

    public void Advance(int count) => stream.Write(_buffer, 0, count);

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer;
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer;
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint > _buffer.Length)
            _buffer = new byte[Math.Max(sizeHint, _buffer.Length * 2)];
    }
}
