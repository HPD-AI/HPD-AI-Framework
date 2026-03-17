using System.Buffers;

namespace HPD.ML.Abstractions;

internal static class BufferHelpers
{
    public static ReadOnlySequence<byte> ReadToSequence(Stream source)
    {
        using var ms = new MemoryStream();
        source.CopyTo(ms);
        return new ReadOnlySequence<byte>(ms.ToArray());
    }
}
