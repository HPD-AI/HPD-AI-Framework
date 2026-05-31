namespace HPD.Events.Struct;

/// <summary>
/// Optional copy contract for struct events that want coordinator-assigned sequence numbers.
/// </summary>
public interface ISequencedStructEvent<TSelf>
    where TSelf : struct, IStructEvent
{
    /// <summary>Return a copy of this event with the supplied sequence number.</summary>
    TSelf WithSequenceNumber(long sequenceNumber);
}
