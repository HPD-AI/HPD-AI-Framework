namespace HPD.Agent;

/// <summary>Controls terminal operation and provider-deduplication retention.</summary>
public sealed record AgentOperationRetentionPolicy
{
    /// <summary>Gets how long complete operation snapshots remain materialized.</summary>
    public TimeSpan TerminalRetention { get; init; } = TimeSpan.FromDays(30);
    /// <summary>Gets how long terminal provider keys remain protected from replay.</summary>
    public TimeSpan ProviderDeduplicationRetention { get; init; } = TimeSpan.FromDays(90);
    /// <summary>Gets the maximum terminal snapshots retained per thread.</summary>
    public int MaximumTerminalOperationsPerThread { get; init; } = 10_000;

    internal void Validate()
    {
        if (TerminalRetention < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(TerminalRetention));
        if (ProviderDeduplicationRetention < TerminalRetention)
            throw new ArgumentOutOfRangeException(nameof(ProviderDeduplicationRetention),
                "Provider deduplication retention cannot be shorter than terminal retention.");
        if (MaximumTerminalOperationsPerThread < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumTerminalOperationsPerThread));
    }
}

/// <summary>Retains terminal identity and replay protection after snapshot compaction.</summary>
public sealed record AgentOperationTombstone
{
    /// <summary>Gets the authoritative operation identifier.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the owning address needed to journal eventual eviction.</summary>
    public required AgentExecutionAddress Address { get; init; }
    /// <summary>Gets provider observation keys retained against duplicate late delivery.</summary>
    public required IReadOnlyList<string> ProviderDeduplicationKeys { get; init; }
    /// <summary>Gets the terminal provider state.</summary>
    public required AgentOperationProviderStatus ProviderStatus { get; init; }
    /// <summary>Gets the provider terminal time.</summary>
    public required DateTimeOffset FinishedAt { get; init; }
    /// <summary>Gets the final aggregate version.</summary>
    public required long FinalVersion { get; init; }
}

/// <summary>Records replacement of a terminal operation snapshot with a tombstone.</summary>
public sealed record AgentOperationTombstonedEvent : AgentEvent
{
    /// <inheritdoc />
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    /// <summary>Gets the retained terminal tombstone.</summary>
    public required AgentOperationTombstone Tombstone { get; init; }
}

/// <summary>Records expiry of terminal replay protection.</summary>
public sealed record AgentOperationTombstoneEvictedEvent : AgentEvent
{
    /// <inheritdoc />
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    /// <summary>Gets the operation whose tombstone expired.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the eviction time.</summary>
    public required DateTimeOffset EvictedAt { get; init; }
}
