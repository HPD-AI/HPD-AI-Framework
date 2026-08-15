using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal sealed class GlobalParticipantPageV1
{
    internal const int MaximumCanonicalBytes = 65_536;
    internal const int MaximumRecordsPerPage = 256;
    internal const int MaximumRecordBytes = 8_192;
    internal const int MaximumPages = 9_363;
    internal const ulong MaximumTotalRecords = 65_536;
    internal const ulong MaximumTotalCanonicalBytes = 536_870_912;

    private readonly byte[] _records;

    internal GlobalParticipantPageV1(
        GlobalParticipantAllocatorJournalId journalId,
        GlobalParticipantAuthorityHeadV1? pinnedHead,
        Hash256 indexRoot,
        ushort pageOrdinal,
        Hash256? previousPageHash,
        ReadOnlySpan<byte> records,
        ushort isFinal,
        ushort totalPages,
        ulong totalRecords,
        ulong totalCanonicalBytes)
    {
        if (!journalId.IsValid || !IsValidHash(indexRoot))
            throw new ArgumentException("A page requires a valid journal and index root.");
        if (pageOrdinal is 0 or > MaximumPages || totalPages is 0 or > MaximumPages || pageOrdinal > totalPages)
            throw new ArgumentOutOfRangeException(nameof(pageOrdinal), "Page ordinals and totals must be within the frozen lifetime.");
        if (isFinal > 1 || (isFinal == 1) != (pageOrdinal == totalPages))
            throw new ArgumentException("The final marker must be zero or one and agree with the page total.", nameof(isFinal));
        if ((pageOrdinal == 1) != (previousPageHash is null) || previousPageHash is { } prior && !IsValidHash(prior))
            throw new ArgumentException("Only the first page may omit the previous page hash.", nameof(previousPageHash));
        if (totalRecords > MaximumTotalRecords || totalCanonicalBytes > MaximumTotalCanonicalBytes)
            throw new ArgumentOutOfRangeException(nameof(totalRecords), "Pinned page totals exceed the frozen lifetime.");
        if (pinnedHead is { } head && (head.Position.JournalId != journalId || head.Position.Sequence != totalRecords))
            throw new ArgumentException("The pinned head must identify the page journal and exact pinned record total.", nameof(pinnedHead));

        var summary = GlobalParticipantPageCodecV1.InspectRecordsField(records);
        if (summary.Count == 0)
        {
            if (pinnedHead is not null || pageOrdinal != 1 || totalPages != 1 || isFinal != 1 ||
                totalRecords != 0 || totalCanonicalBytes != 0 || previousPageHash is not null ||
                indexRoot != GlobalParticipantPageCodecV1.DefaultIndexRoot)
                throw new ArgumentException("Only the exact canonical empty final page may contain zero records.", nameof(records));
        }
        else
        {
            if (pinnedHead is null || totalRecords < (ulong)summary.Count || totalCanonicalBytes < summary.CanonicalRecordBytes)
                throw new ArgumentException("A nonempty page requires compatible pinned head and totals.", nameof(records));
        }

        JournalId = journalId;
        PinnedHead = pinnedHead;
        IndexRoot = indexRoot;
        PageOrdinal = pageOrdinal;
        PreviousPageHash = previousPageHash;
        _records = records.ToArray();
        Records = Array.AsReadOnly(_records);
        RecordCount = summary.Count;
        PageCanonicalRecordBytes = summary.CanonicalRecordBytes;
        IsFinal = isFinal;
        TotalPages = totalPages;
        TotalRecords = totalRecords;
        TotalCanonicalBytes = totalCanonicalBytes;
    }

    internal GlobalParticipantAllocatorJournalId JournalId { get; }
    internal GlobalParticipantAuthorityHeadV1? PinnedHead { get; }
    internal Hash256 IndexRoot { get; }
    internal ushort PageOrdinal { get; }
    internal Hash256? PreviousPageHash { get; }
    internal IReadOnlyList<byte> Records { get; }
    internal ReadOnlySpan<byte> RecordsBytes => _records;
    internal ushort RecordCount { get; }
    internal ulong PageCanonicalRecordBytes { get; }
    internal ushort IsFinal { get; }
    internal ushort TotalPages { get; }
    internal ulong TotalRecords { get; }
    internal ulong TotalCanonicalBytes { get; }

    private static bool IsValidHash(Hash256 value)
    {
        Span<byte> bytes = stackalloc byte[32];
        return value.TryWriteBytes(bytes);
    }
}
