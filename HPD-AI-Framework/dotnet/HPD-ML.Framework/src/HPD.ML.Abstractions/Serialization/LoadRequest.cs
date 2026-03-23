using System.Buffers;

namespace HPD.ML.Abstractions;

public sealed record LoadRequest(
    ISerializationFormat Format,
    ReadOnlySequence<byte> Source);
