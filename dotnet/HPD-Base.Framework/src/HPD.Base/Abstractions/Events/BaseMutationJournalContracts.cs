
namespace HPD.Base;

/// <summary>Identifies a provider-local committed mutation-journal position.</summary>
public readonly record struct BaseMutationJournalPosition(long Value);

/// <summary>Describes the currently retained provider-journal position range.</summary>
public readonly record struct BaseMutationJournalBounds(
    BaseMutationJournalPosition Earliest,
    BaseMutationJournalPosition HighWatermark,
    long RestoreEpoch);

/// <summary>Contains immutable facts for one transactionally committed record mutation.</summary>
public sealed record BaseMutationJournalEntry
{
    /// <summary>Gets the provider-local journal position.</summary>
    public required BaseMutationJournalPosition Position { get; init; }

    /// <summary>Gets the stable event identity shared with live publication.</summary>
    public required string EventId { get; init; }

    /// <summary>Gets the stable BASE event type.</summary>
    public required string Type { get; init; }

    /// <summary>Gets the BASE event schema version.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>Gets the committed event time.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Gets the tenant partition when the mutation is tenant-scoped.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the mutation operation.</summary>
    public required BaseOperationKind Operation { get; init; }

    /// <summary>Gets the collection-level visibility captured at commit time.</summary>
    public VisibilityLevel Visibility { get; init; }

    /// <summary>Gets the affected collection id.</summary>
    public required string CollectionId { get; init; }

    /// <summary>Gets the affected record id.</summary>
    public required RecordId RecordId { get; init; }

    /// <summary>Gets the record state before the mutation when available.</summary>
    public RecordSnapshot? Before { get; init; }

    /// <summary>Gets the record state after the mutation when available.</summary>
    public RecordSnapshot? After { get; init; }
}

/// <summary>Defines a bounded provider-journal read.</summary>
public sealed record BaseMutationJournalReadRequest
{
    /// <summary>Gets the exclusive position after which entries are returned.</summary>
    public BaseMutationJournalPosition After { get; init; }

    /// <summary>Gets an optional inclusive high-water position.</summary>
    public BaseMutationJournalPosition? Through { get; init; }

    /// <summary>Gets the maximum number of entries to return.</summary>
    public int Limit { get; init; } = 100;
}

/// <summary>Contains one bounded provider-journal page.</summary>
public sealed record BaseMutationJournalPage
{
    /// <summary>Gets the entries in ascending provider position order.</summary>
    public required BaseMutationJournalEntry[] Entries { get; init; }

    /// <summary>Gets the high-water position observed for this read.</summary>
    public required BaseMutationJournalPosition HighWatermark { get; init; }

    /// <summary>Gets the earliest position retained when this page was read.</summary>
    public required BaseMutationJournalPosition Earliest { get; init; }

    /// <summary>Gets whether more entries remain within the requested boundary.</summary>
    public bool HasMore { get; init; }
}
