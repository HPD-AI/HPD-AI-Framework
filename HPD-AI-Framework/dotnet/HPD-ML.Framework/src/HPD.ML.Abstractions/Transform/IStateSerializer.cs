using System.Buffers;

namespace HPD.ML.Abstractions;

/// <summary>Zero-copy state serialization for checkpointing.</summary>
public interface IStateSerializer<TState>
{
    void Serialize(TState state, IBufferWriter<byte> destination);
    TState Deserialize(ReadOnlySequence<byte> source);
}
