using System.Collections.Concurrent;
using System.Text.Json;

namespace HPD.Agent.Permissions;

/// <summary>Stores versioned session permission preferences with atomic audit settlement.</summary>
public interface IPermissionPreferenceStore
{
    /// <summary>Reads the current immutable session preference snapshot.</summary>
    ValueTask<PermissionPreferenceSnapshot> ReadAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>Commits a compare/exchange replacement and its durable audit settlement.</summary>
    ValueTask<PermissionPreferenceCommitResult> CommitAsync(
        PermissionPreferenceCommit commit,
        CancellationToken cancellationToken);

    /// <summary>Claims committed audit records awaiting publication.</summary>
    ValueTask<IReadOnlyList<PermissionPreferenceOutboxRecord>> ClaimPendingPublicationAsync(
        string sessionId,
        string claimantId,
        int maxCount,
        CancellationToken cancellationToken);

    /// <summary>Acknowledges publication by exact settlement claim token.</summary>
    ValueTask<bool> AcknowledgePublicationAsync(
        string settlementId,
        string claimToken,
        CancellationToken cancellationToken);
}

/// <summary>Contains one immutable persisted permission preference.</summary>
public sealed record PermissionPreferenceRecord
{
    /// <summary>Gets the stable preference ID.</summary>
    public required string PreferenceId { get; init; }
    /// <summary>Gets the structured permission key.</summary>
    public required PermissionKey Key { get; init; }
    /// <summary>Gets the stored allow or deny decision.</summary>
    public required PermissionDecisionKind Decision { get; init; }
    /// <summary>Gets the persistence kind.</summary>
    public required PermissionPersistenceKind Kind { get; init; }
    /// <summary>Gets the exact request fingerprint.</summary>
    public string? RequestFingerprint { get; init; }
    /// <summary>Gets the optional expiration.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
    /// <summary>Gets the generated validated-rule type ID.</summary>
    public string? RuleTypeId { get; init; }
    /// <summary>Gets the canonical validated-rule payload.</summary>
    public JsonElement? CanonicalRule { get; init; }
    /// <summary>Gets the commit timestamp.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Contains a versioned immutable preference snapshot.</summary>
public sealed record PermissionPreferenceSnapshot(
    long Version,
    IReadOnlyList<PermissionPreferenceRecord> Records);

/// <summary>Requests one optimistic preference and audit commit.</summary>
public sealed record PermissionPreferenceCommit
{
    /// <summary>Gets the owning session ID.</summary>
    public required string SessionId { get; init; }
    /// <summary>Gets the originating audit thread.</summary>
    public required ThreadKey AuditThread { get; init; }
    /// <summary>Gets the expected snapshot version.</summary>
    public required long ExpectedVersion { get; init; }
    /// <summary>Gets the complete replacement snapshot.</summary>
    public required PermissionPreferenceSnapshot Replacement { get; init; }
    /// <summary>Gets the exact durable event committed with the replacement.</summary>
    public required PermissionPreferenceChangedEvent Event { get; init; }
    /// <summary>Gets the stable idempotency key.</summary>
    public required string IdempotencyKey { get; init; }
    /// <summary>Gets the initial publication claimant ID.</summary>
    public required string PublisherClaimantId { get; init; }
}

/// <summary>Classifies the outcome of an optimistic preference commit.</summary>
public enum PermissionPreferenceCommitStatus
{
    /// <summary>The replacement, audit event, and outbox settlement were committed.</summary>
    Committed,
    /// <summary>The same idempotency identity was committed previously.</summary>
    AlreadyCommitted,
    /// <summary>The expected snapshot version was stale.</summary>
    VersionConflict
}

/// <summary>Classifies publication settlement state.</summary>
public enum PermissionPreferenceOutboxState
{
    /// <summary>The settlement is available for a publisher claim.</summary>
    Pending,
    /// <summary>A publisher holds a time-bounded settlement lease.</summary>
    Claimed,
    /// <summary>The committed event was published and acknowledged.</summary>
    Acknowledged
}

/// <summary>Contains one atomically committed audit publication settlement.</summary>
public sealed record PermissionPreferenceOutboxRecord
{
    /// <summary>Gets the stable settlement ID.</summary>
    public required string SettlementId { get; init; }
    /// <summary>Gets the current claim token.</summary>
    public string? ClaimToken { get; init; }
    /// <summary>Gets the publisher identity holding the current claim.</summary>
    public string? ClaimantId { get; init; }
    /// <summary>Gets when the current publication claim expires.</summary>
    public DateTimeOffset? ClaimExpiresAt { get; init; }
    /// <summary>Gets the settlement state.</summary>
    public required PermissionPreferenceOutboxState State { get; init; }
    /// <summary>Gets the owning session.</summary>
    public required string SessionId { get; init; }
    /// <summary>Gets the durable audit thread.</summary>
    public required ThreadKey AuditThread { get; init; }
    /// <summary>Gets the exact store-committed event.</summary>
    public required PermissionPreferenceChangedEvent CommittedEvent { get; init; }
    /// <summary>Gets the durable thread sequence number.</summary>
    public required long ThreadSequenceNumber { get; init; }
    /// <summary>Gets the stable idempotency key.</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>Contains an optimistic preference commit result.</summary>
public sealed record PermissionPreferenceCommitResult
{
    /// <summary>Gets the commit status.</summary>
    public required PermissionPreferenceCommitStatus Status { get; init; }
    /// <summary>Gets the store's current version.</summary>
    public required long CurrentVersion { get; init; }
    /// <summary>Gets the committed or replayed outbox settlement.</summary>
    public PermissionPreferenceOutboxRecord? Outbox { get; init; }
}
