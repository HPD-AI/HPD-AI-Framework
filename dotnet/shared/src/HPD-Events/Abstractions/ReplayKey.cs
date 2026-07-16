namespace HPD.Events;

/// <summary>
/// Lexicographic replay ordering key.
/// </summary>
public readonly record struct ReplayKey(
    long TimestampNs,
    int SourcePriority,
    int EventPriority,
    int SourceOrdinal,
    long SourceSequence) : IComparable<ReplayKey>
{
    /// <inheritdoc />
    public int CompareTo(ReplayKey other)
    {
        var timestamp = TimestampNs.CompareTo(other.TimestampNs);
        if (timestamp != 0)
            return timestamp;

        var sourcePriority = SourcePriority.CompareTo(other.SourcePriority);
        if (sourcePriority != 0)
            return sourcePriority;

        var eventPriority = EventPriority.CompareTo(other.EventPriority);
        if (eventPriority != 0)
            return eventPriority;

        var sourceOrdinal = SourceOrdinal.CompareTo(other.SourceOrdinal);
        if (sourceOrdinal != 0)
            return sourceOrdinal;

        return SourceSequence.CompareTo(other.SourceSequence);
    }
}
