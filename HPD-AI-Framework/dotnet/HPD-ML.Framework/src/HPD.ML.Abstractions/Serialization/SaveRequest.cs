using System.Buffers;

namespace HPD.ML.Abstractions;

public sealed record SaveRequest(
    IModel Model,
    SaveContent Content,
    ISerializationFormat Format,
    IBufferWriter<byte> Destination,
    object? InferenceState = null);
