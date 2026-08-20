using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Classifies the current lifecycle state of one exported subject lifetime.</summary>
public enum BaseSubjectLifecycleState
{
    /// <summary>The lifetime exists and may satisfy active references.</summary>
    Active = 0,
    /// <summary>The lifetime exists but cannot satisfy active references.</summary>
    Inactive = 1,
    /// <summary>Logical retirement has begun and no new reference is valid.</summary>
    Tombstoned = 2,
    /// <summary>The lifetime and private record are physically absent.</summary>
    Retired = 3,
}

/// <summary>Classifies one sanitized lifecycle fact payload.</summary>
public enum BaseSubjectLifecycleFactKind
{
    /// <summary>A fresh lifetime was created active.</summary>
    Created = 0,
    /// <summary>An existing lifetime changed nonterminal state.</summary>
    Transitioned = 1,
    /// <summary>A tombstoned lifetime was physically retired.</summary>
    Retired = 2,
}

/// <summary>Describes creation of an active subject lifetime.</summary>
public sealed record BaseSubjectLifecycleCreatedFact
{
    /// <summary>Gets the created state, which must be active.</summary>
    public required BaseSubjectLifecycleState CurrentState { get; init; }
}

/// <summary>Describes one allowed nonterminal lifecycle transition.</summary>
public sealed record BaseSubjectLifecycleTransitionedFact
{
    /// <summary>Gets the prior state.</summary>
    public required BaseSubjectLifecycleState PreviousState { get; init; }
    /// <summary>Gets the resulting state.</summary>
    public required BaseSubjectLifecycleState CurrentState { get; init; }
}

/// <summary>Describes terminal retirement from a tombstone.</summary>
public sealed record BaseSubjectLifecycleRetiredFact
{
    /// <summary>Gets the prior state, which must be tombstoned.</summary>
    public required BaseSubjectLifecycleState PreviousState { get; init; }
}

/// <summary>Contains one sanitized durable exported-subject lifecycle fact.</summary>
public sealed record BaseSubjectLifecycleFact
{
    /// <summary>Gets the source transaction journal position.</summary>
    public required BaseMutationJournalPosition CommitPosition { get; init; }
    /// <summary>Gets the exported contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the exported contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the canonical logical subject ID.</summary>
    public required BaseSubjectId SubjectId { get; init; }
    /// <summary>Gets the authority epoch.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
    /// <summary>Gets the epoch-bound lifetime incarnation.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
    /// <summary>Gets the positive subject-local sequence.</summary>
    public required long SubjectSequence { get; init; }
    /// <summary>Gets the contract state generation.</summary>
    public required long ContractStateGeneration { get; init; }
    /// <summary>Gets the lifecycle delivery epoch.</summary>
    public required long DeliveryEpoch { get; init; }
    /// <summary>Gets the payload discriminator.</summary>
    public required BaseSubjectLifecycleFactKind Kind { get; init; }
    /// <summary>Gets the creation payload only for <see cref="BaseSubjectLifecycleFactKind.Created"/>.</summary>
    public BaseSubjectLifecycleCreatedFact? Created { get; init; }
    /// <summary>Gets the transition payload only for <see cref="BaseSubjectLifecycleFactKind.Transitioned"/>.</summary>
    public BaseSubjectLifecycleTransitionedFact? Transitioned { get; init; }
    /// <summary>Gets the retirement payload only for <see cref="BaseSubjectLifecycleFactKind.Retired"/>.</summary>
    public BaseSubjectLifecycleRetiredFact? Retired { get; init; }
}

/// <summary>Contains one typed lifecycle fact and opaque subject reference.</summary>
public sealed record BaseSubjectLifecycleFact<TSubject>
{
    /// <summary>Gets the referenced subject lifetime.</summary>
    public required BaseSubjectReference<TSubject> Subject { get; init; }
    /// <summary>Gets the sanitized lifecycle fact.</summary>
    public required BaseSubjectLifecycleFact Fact { get; init; }
}

/// <summary>Requests the constrained identified tombstone transition for one exported subject.</summary>
public sealed record BaseSubjectTombstoneRequest<TSubject>
{
    /// <summary>Gets the exact current logical lifetime.</summary>
    public required BaseSubjectReference<TSubject> Subject { get; init; }
    /// <summary>Gets the expected opaque private-record revision.</summary>
    public required RevisionToken ExpectedPrivateRevision { get; init; }
    /// <summary>Gets the identified mutation authority.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
}

/// <summary>Requests constrained uncoordinated final retirement.</summary>
public sealed record BaseSubjectFinalRetirementRequest<TSubject>
{
    /// <summary>Gets the exact current tombstoned lifetime.</summary>
    public required BaseSubjectReference<TSubject> Subject { get; init; }
    /// <summary>Gets the expected tombstone sequence.</summary>
    public required long ExpectedTombstoneSequence { get; init; }
    /// <summary>Gets the expected opaque private-record revision.</summary>
    public required RevisionToken ExpectedPrivateRevision { get; init; }
    /// <summary>Gets the identified mutation authority.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
}

/// <summary>Returns exact terminal retirement evidence.</summary>
public sealed record BaseSubjectFinalRetirementResult<TSubject>
{
    /// <summary>Gets the retired logical lifetime.</summary>
    public required BaseSubjectReference<TSubject> Subject { get; init; }
    /// <summary>Gets the terminal subject sequence.</summary>
    public required long RetiredSubjectSequence { get; init; }
    /// <summary>Gets the terminal journal position.</summary>
    public required BaseMutationJournalPosition RetiredPosition { get; init; }
    /// <summary>Gets whether the original receipt was replayed.</summary>
    public required bool Duplicate { get; init; }
}

/// <summary>Defines the complete canonical lifecycle seek boundary.</summary>
public sealed record BaseSubjectLifecycleOrderingBoundary
{
    /// <summary>Gets the source transaction position.</summary>
    public required BaseMutationJournalPosition CommitPosition { get; init; }
    /// <summary>Gets the canonical subject ID.</summary>
    public required BaseSubjectId SubjectId { get; init; }
    /// <summary>Gets the authority epoch.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
    /// <summary>Gets the incarnation.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
    /// <summary>Gets the subject-local sequence.</summary>
    public required long SubjectSequence { get; init; }
}

/// <summary>Opaque protected continuation for lifecycle page reads.</summary>
public sealed class BaseSubjectLifecycleCursor
{
    private readonly byte[] _value;
    internal BaseSubjectLifecycleCursor(ReadOnlySpan<byte> value) => _value = value.ToArray();
    internal byte[] ToArray() => (byte[])_value.Clone();
}

/// <summary>Opaque protected evidence authorizing durable checkpoint advancement.</summary>
public sealed class BaseSubjectLifecycleCheckpoint
{
    private readonly byte[] _value;
    internal BaseSubjectLifecycleCheckpoint(ReadOnlySpan<byte> value) => _value = value.ToArray();
    internal byte[] ToArray() => (byte[])_value.Clone();
}

/// <summary>Contains one immutable observe-only delivery.</summary>
public sealed record BaseSubjectLifecycleDelivery<TSubject>
{
    /// <summary>Gets the delivered typed fact.</summary>
    public required BaseSubjectLifecycleFact<TSubject> Fact { get; init; }
    /// <summary>Gets the checkpoint through the delivered fact.</summary>
    public required BaseSubjectLifecycleCheckpoint Checkpoint { get; init; }
    /// <summary>Gets the deterministic consumer processing identity.</summary>
    public required BaseMutationRequestIdentity ProcessingIdentity { get; init; }
    /// <summary>Gets the deterministic checkpoint-advance identity.</summary>
    public required BaseMutationRequestIdentity AdvanceIdentity { get; init; }
}

/// <summary>Contains one bounded lifecycle page.</summary>
public sealed record BaseSubjectLifecyclePage<TSubject>
{
    /// <summary>Gets the ordered facts.</summary>
    public required ImmutableArray<BaseSubjectLifecycleFact<TSubject>> Facts { get; init; }
    /// <summary>Gets the protected next-page cursor.</summary>
    public required BaseSubjectLifecycleCursor? Next { get; init; }
    /// <summary>Gets checkpoint evidence through this page.</summary>
    public required BaseSubjectLifecycleCheckpoint Through { get; init; }
}

/// <summary>Contains one bounded authorized current-state reconciliation page.</summary>
public sealed record BaseSubjectLifecycleReconciliationPage<TSubject>
{
    /// <summary>Gets current subject lifetimes in canonical subject-ID order.</summary>
    public required ImmutableArray<BaseCurrentSubjectLifecycle<TSubject>> Subjects { get; init; }
    /// <summary>Gets the exclusive subject boundary for the next page.</summary>
    public BaseSubjectId? NextSubjectId { get; init; }
    /// <summary>Gets the captured durable-feed high-water boundary.</summary>
    public BaseSubjectLifecycleOrderingBoundary? CapturedHighWater { get; init; }
}

/// <summary>Defines immutable bounds for one lifecycle consumer.</summary>
public sealed record BaseSubjectLifecycleConsumerLimits
{
    /// <summary>Gets the maximum facts in one page.</summary>
    public required int MaximumFactsPerPage { get; init; }
    /// <summary>Gets the maximum encoded result bytes.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets the maximum durable checkpoint lag.</summary>
    public required TimeSpan MaximumCheckpointLag { get; init; }
    /// <summary>Gets the provider read timeout.</summary>
    public required TimeSpan ReadTimeout { get; init; }
}

/// <summary>Configures host-wide lifecycle continuation authority.</summary>
public sealed class HPDBaseSubjectLifecycleOptions
{
    /// <summary>Gets or sets the protected cursor and checkpoint lifetime.</summary>
    public TimeSpan CursorLifetime { get; set; } = TimeSpan.FromHours(24);
    internal void Validate()
    {
        if (CursorLifetime < TimeSpan.FromMinutes(1) || CursorLifetime > TimeSpan.FromDays(30))
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
    }
}

/// <summary>Defines the only principal audiences eligible for lifecycle-worker authority.</summary>
public enum BaseSubjectLifecycleConsumerAudience
{
    /// <summary>Allows Service and System principals.</summary>
    Service = 0,
    /// <summary>Allows only System principals.</summary>
    System = 1,
}

/// <summary>Defines one graph-owned lifecycle consumer.</summary>
public sealed record BaseSubjectLifecycleConsumerDefinition
{
    /// <summary>Gets the stable consumer ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive consumer version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the owning module ID.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the closed non-browser worker audience.</summary>
    public required BaseSubjectLifecycleConsumerAudience Audience { get; init; }
    /// <summary>Gets the exported contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the exported contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the normalized observed states.</summary>
    public required ImmutableArray<BaseSubjectLifecycleState> ObservedStates { get; init; }
    /// <summary>Gets the exact feed-delivery grant ID.</summary>
    public required string DeliveryGrantId { get; init; }
    /// <summary>Gets the optional reconciliation grant ID.</summary>
    public string? ReconciliationGrantId { get; init; }
    /// <summary>Gets immutable execution limits.</summary>
    public required BaseSubjectLifecycleConsumerLimits Limits { get; init; }
}

/// <summary>Defines the provider's certified lifecycle capabilities.</summary>
public sealed record BaseSubjectLifecycleCapability
{
    /// <summary>Gets whether lifecycle publication is transactional with source mutation.</summary>
    public required bool TransactionalPublicationSupported { get; init; }
    /// <summary>Gets whether each consumer owns an independent cursor.</summary>
    public required bool IndependentCursorSupported { get; init; }
    /// <summary>Gets whether bounded reconciliation is supported.</summary>
    public required bool ReconciliationSupported { get; init; }
    /// <summary>Gets the maximum installed consumers per contract.</summary>
    public required int MaximumConsumersPerContract { get; init; }
    /// <summary>Gets the maximum facts per page.</summary>
    public required int MaximumFactsPerPage { get; init; }
    /// <summary>Gets the maximum encoded page bytes.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets the maximum retained canonical facts.</summary>
    public required long MaximumRetainedFacts { get; init; }
    /// <summary>Gets the maximum read timeout.</summary>
    public required TimeSpan MaximumReadTimeout { get; init; }
}

/// <summary>Provides the built-in certified lifecycle envelope.</summary>
public static class BaseSubjectLifecycleProviderCapabilities
{
    /// <summary>Gets the built-in InMemory and SQLite capability.</summary>
    public static BaseSubjectLifecycleCapability BuiltIn { get; } = new()
    {
        TransactionalPublicationSupported = true,
        IndependentCursorSupported = true,
        ReconciliationSupported = false,
        MaximumConsumersPerContract = 32,
        MaximumFactsPerPage = 256,
        MaximumResultBytes = 1_048_576,
        MaximumRetainedFacts = 1_000_000,
        MaximumReadTimeout = TimeSpan.FromMinutes(2),
    };
}

/// <summary>Reads and durably advances one installed lifecycle consumer.</summary>
public interface IBaseSubjectLifecycleFeed<TSubject>
{
    /// <summary>Reads one authorized bounded page.</summary>
    ValueTask<BaseResult<BaseSubjectLifecyclePage<TSubject>>> ReadAsync(BaseSubjectLifecycleCursor? after, int take, CancellationToken cancellationToken = default);
    /// <summary>Advances the provider-owned durable checkpoint.</summary>
    ValueTask<BaseResult<BaseSubjectLifecycleCheckpointResult>> AdvanceCheckpointAsync(BaseSubjectLifecycleCheckpointAdvanceRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Requests an identified durable checkpoint advancement.</summary>
public sealed record BaseSubjectLifecycleCheckpointAdvanceRequest
{
    /// <summary>Gets the consumer ID.</summary>
    public required string ConsumerId { get; init; }
    /// <summary>Gets the consumer version.</summary>
    public required int ConsumerVersion { get; init; }
    /// <summary>Gets the consumer checksum.</summary>
    public required string ConsumerChecksum { get; init; }
    /// <summary>Gets the contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the exact authorized subject scope.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets issued checkpoint evidence.</summary>
    public required BaseSubjectLifecycleCheckpoint Through { get; init; }
    /// <summary>Gets the expected checkpoint generation.</summary>
    public required long ExpectedCheckpointGeneration { get; init; }
    /// <summary>Gets the identified mutation identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
}

/// <summary>Returns durable checkpoint state.</summary>
public sealed record BaseSubjectLifecycleCheckpointResult
{
    /// <summary>Gets the stored complete ordering boundary.</summary>
    public required BaseSubjectLifecycleOrderingBoundary? Through { get; init; }
    /// <summary>Gets the checkpoint generation.</summary>
    public required long CheckpointGeneration { get; init; }
    /// <summary>Gets the consumer projection generation.</summary>
    public required long ProjectionGeneration { get; init; }
    /// <summary>Gets the advancement time.</summary>
    public required DateTimeOffset AdvancedAtUtc { get; init; }
    /// <summary>Gets whether the identified operation was replayed.</summary>
    public required bool Duplicate { get; init; }
}

internal static class BaseSubjectLifecycleReceiptOwnership
{
    internal static BaseSubjectLifecycleCheckpointResult Clone(BaseSubjectLifecycleCheckpointResult value) => new()
    {
        Through = value.Through is null ? null : new BaseSubjectLifecycleOrderingBoundary
        {
            CommitPosition = value.Through.CommitPosition,
            SubjectId = value.Through.SubjectId,
            AuthorityEpoch = new BaseSubjectAuthorityEpoch(value.Through.AuthorityEpoch.ToArray()),
            Incarnation = new BaseSubjectIncarnation(value.Through.Incarnation.ToArray()),
            SubjectSequence = value.Through.SubjectSequence,
        },
        CheckpointGeneration = value.CheckpointGeneration,
        ProjectionGeneration = value.ProjectionGeneration,
        AdvancedAtUtc = value.AdvancedAtUtc,
        Duplicate = value.Duplicate,
    };
}

/// <summary>Contains protected provider-only subject scope authority.</summary>
public sealed record BaseProtectedSubjectScope
{
    /// <summary>Gets the scope kind.</summary>
    public required BaseSubjectScopeKind Kind { get; init; }
    /// <summary>Gets the protected seek digest.</summary>
    public required byte[] IndexDigest { get; init; }
    /// <summary>Gets the protected canonical scope bytes.</summary>
    public required byte[] ProtectedCanonicalValue { get; init; }
}

/// <summary>Classifies authorized provider scope queries.</summary>
public enum BaseSubjectScopeQueryMode
{
    /// <summary>Queries one exact tenant, project, or global scope.</summary>
    ExactScope = 0,
    /// <summary>Queries every scope admitted by an installed ControlPlane authority.</summary>
    AllAuthorizedScopes = 1,
}

/// <summary>Contains graph-owned scope-query authority.</summary>
public sealed record BaseSubjectScopeQueryAuthority
{
    /// <summary>Gets the query mode.</summary>
    public required BaseSubjectScopeQueryMode Mode { get; init; }
    /// <summary>Gets exact scope authority only for <see cref="BaseSubjectScopeQueryMode.ExactScope"/>.</summary>
    public BaseOwnedSubjectScopeEvidence? ExactScope { get; init; }
    /// <summary>Gets the installed authority digest.</summary>
    public required string InstalledAuthorityDigest { get; init; }
}

/// <summary>Contains one immutable provider-installed all-scope inspection authority receipt.</summary>
public sealed record BaseSubjectLifecycleInspectionAuthority
{
    /// <summary>Gets the exported contract identity.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the exported contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the owning module identity.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the exact ControlPlane grant identity.</summary>
    public required string GrantId { get; init; }
    /// <summary>Gets the lowercase SHA-256 installation digest.</summary>
    public required string Digest { get; init; }
}

/// <summary>Contains the latest non-prunable retirement evidence for one scoped subject key.</summary>
public sealed record BaseSubjectTerminalLifetimeReceipt
{
    /// <summary>Gets the contract identity.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the canonical subject identity.</summary>
    public required BaseSubjectId SubjectId { get; init; }
    /// <summary>Gets protected scope authority.</summary>
    public required BaseProtectedSubjectScope Scope { get; init; }
    /// <summary>Gets the retired authority epoch.</summary>
    public required BaseSubjectAuthorityEpoch RetiredAuthorityEpoch { get; init; }
    /// <summary>Gets the retired incarnation.</summary>
    public required BaseSubjectIncarnation RetiredIncarnation { get; init; }
    /// <summary>Gets the retired lifetime generation.</summary>
    public required long RetiredLifetimeGeneration { get; init; }
    /// <summary>Gets the retired subject sequence.</summary>
    public required long RetiredSubjectSequence { get; init; }
    /// <summary>Gets the retirement journal position.</summary>
    public required BaseMutationJournalPosition RetiredPosition { get; init; }
    /// <summary>Gets the contract state generation.</summary>
    public required long ContractStateGeneration { get; init; }
    /// <summary>Gets the restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the lowercase SHA-256 corruption checksum.</summary>
    public required string ReceiptChecksum { get; init; }
}

/// <summary>Contains one current sanitized lifecycle row.</summary>
public sealed record BaseCurrentSubjectLifecycle
{
    /// <summary>Gets the subject identity.</summary>
    public required BaseSubjectId SubjectId { get; init; }
    /// <summary>Gets the current authority epoch.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
    /// <summary>Gets the current incarnation.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
    /// <summary>Gets the current lifecycle state.</summary>
    public required BaseSubjectLifecycleState State { get; init; }
    /// <summary>Gets the current positive subject sequence.</summary>
    public required long SubjectSequence { get; init; }
}

/// <summary>Contains one typed current lifecycle row.</summary>
public sealed record BaseCurrentSubjectLifecycle<TSubject>
{
    /// <summary>Gets the subject identity.</summary>
    public required BaseSubjectId SubjectId { get; init; }
    /// <summary>Gets the current authority epoch.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
    /// <summary>Gets the current incarnation.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
    /// <summary>Gets the current lifecycle state.</summary>
    public required BaseSubjectLifecycleState State { get; init; }
    /// <summary>Gets the current subject sequence.</summary>
    public required long SubjectSequence { get; init; }
}

/// <summary>Contains one canonical provider read interval.</summary>
public sealed record BaseReadIntervalEvidence
{
    /// <summary>Gets the logical access-path identity.</summary>
    public required string LogicalAccessPathId { get; init; }
    /// <summary>Gets the inclusive lower boundary bytes.</summary>
    public required byte[] LowerInclusive { get; init; }
    /// <summary>Gets the inclusive upper boundary bytes.</summary>
    public required byte[] UpperInclusive { get; init; }
}

internal static class BaseSubjectLifecycleReadIntervals
{
    internal static ImmutableArray<BaseReadIntervalEvidence> Create(
        BaseSubjectLifecycleProviderReadRequest request,
        BaseProtectedSubjectScope scope,
        BaseSubjectLifecycleOrderingBoundary? through)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scope);
        byte[] lower = Encode(scope.IndexDigest, request.After);
        byte[] upper = Encode(scope.IndexDigest, through ?? request.After);
        return [new BaseReadIntervalEvidence
        {
            LogicalAccessPathId = $"subject-lifecycle-membership:{request.ConsumerId}:{request.ConsumerVersion}:{request.ProjectionGeneration}",
            LowerInclusive = lower,
            UpperInclusive = upper,
        }];
    }

    internal static bool Matches(
        ImmutableArray<BaseReadIntervalEvidence> intervals,
        BaseSubjectLifecycleProviderReadRequest request,
        BaseProtectedSubjectScope scope,
        BaseSubjectLifecycleOrderingBoundary? through)
    {
        if (intervals.IsDefault || intervals.Length != 1) return false;
        BaseReadIntervalEvidence expected = Create(request, scope, through)[0];
        BaseReadIntervalEvidence actual = intervals[0];
        return actual.LogicalAccessPathId == expected.LogicalAccessPathId
            && actual.LowerInclusive.AsSpan().SequenceEqual(expected.LowerInclusive)
            && actual.UpperInclusive.AsSpan().SequenceEqual(expected.UpperInclusive);
    }

    private static byte[] Encode(byte[] scopeDigest, BaseSubjectLifecycleOrderingBoundary? boundary)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(scopeDigest.Length);
        writer.Write(scopeDigest);
        writer.Write(boundary is null ? (byte)0 : (byte)1);
        if (boundary is not null)
        {
            writer.Write(boundary.CommitPosition.Value);
            Write(writer, boundary.SubjectId.Value);
            Write(writer, boundary.AuthorityEpoch.ToArray());
            Write(writer, boundary.Incarnation.ToArray());
            writer.Write(boundary.SubjectSequence);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static void Write(BinaryWriter writer, string value) => Write(writer, System.Text.Encoding.UTF8.GetBytes(value));
    private static void Write(BinaryWriter writer, byte[] value) { writer.Write(value.Length); writer.Write(value); }
}

/// <summary>Requests one bounded provider lifecycle page.</summary>
public sealed record BaseSubjectLifecycleProviderReadRequest
{
    /// <summary>Gets the application ID.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the contract checksum.</summary>
    public required string ContractChecksum { get; init; }
    /// <summary>Gets the consumer ID.</summary>
    public required string ConsumerId { get; init; }
    /// <summary>Gets the consumer version.</summary>
    public required int ConsumerVersion { get; init; }
    /// <summary>Gets the consumer checksum.</summary>
    public required string ConsumerChecksum { get; init; }
    /// <summary>Gets the consumer projection generation.</summary>
    public required long ProjectionGeneration { get; init; }
    /// <summary>Gets the exact authorized scope.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets the exclusive seek boundary.</summary>
    public BaseSubjectLifecycleOrderingBoundary? After { get; init; }
    /// <summary>Gets the page size.</summary>
    public required int Take { get; init; }
    /// <summary>Gets the maximum encoded result bytes.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets the absolute deadline.</summary>
    public required DateTimeOffset DeadlineUtc { get; init; }
}

/// <summary>Contains exact provider lifecycle-read accounting.</summary>
public sealed record BaseSubjectLifecycleReadAccounting
{
    /// <summary>Gets rows sought.</summary>
    public required int RowsSought { get; init; }
    /// <summary>Gets rows hydrated.</summary>
    public required int RowsHydrated { get; init; }
    /// <summary>Gets canonical result bytes.</summary>
    public required long ResultBytes { get; init; }
    /// <summary>Gets retained transient bytes.</summary>
    public required long TransientBytes { get; init; }
}

/// <summary>Contains one consumer-bound provider lifecycle fact.</summary>
public sealed record BaseSubjectLifecycleProviderFact
{
    /// <summary>Gets the complete ordering boundary.</summary>
    public required BaseSubjectLifecycleOrderingBoundary Boundary { get; init; }
    /// <summary>Gets protected scope authority.</summary>
    public required BaseProtectedSubjectScope Scope { get; init; }
    /// <summary>Gets the canonical lifecycle fact.</summary>
    public required BaseSubjectLifecycleFact Fact { get; init; }
    /// <summary>Gets the consumer ID.</summary>
    public required string ConsumerId { get; init; }
    /// <summary>Gets the consumer version.</summary>
    public required int ConsumerVersion { get; init; }
    /// <summary>Gets the consumer checksum.</summary>
    public required string ConsumerChecksum { get; init; }
    /// <summary>Gets the projection generation.</summary>
    public required long ProjectionGeneration { get; init; }
    /// <summary>Gets the observed state selecting this delivery.</summary>
    public required BaseSubjectLifecycleState MatchedObservedState { get; init; }
}

/// <summary>Returns one provider-owned lifecycle page.</summary>
public sealed record BaseSubjectLifecycleProviderPage
{
    /// <summary>Gets the exact provider store identity used only for protected continuation binding.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the restore epoch used only for protected continuation binding.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the lifecycle delivery epoch used only for protected continuation binding.</summary>
    public required long DeliveryEpoch { get; init; }
    /// <summary>Gets the current durable checkpoint generation for the requested scope.</summary>
    public required long CheckpointGeneration { get; init; }
    /// <summary>Gets protected scope authority.</summary>
    public required BaseProtectedSubjectScope Scope { get; init; }
    /// <summary>Gets the exact ordered facts.</summary>
    public required ImmutableArray<BaseSubjectLifecycleProviderFact> Facts { get; init; }
    /// <summary>Gets the earliest retained boundary.</summary>
    public BaseSubjectLifecycleOrderingBoundary? EarliestRetained { get; init; }
    /// <summary>Gets the captured high-water boundary.</summary>
    public BaseSubjectLifecycleOrderingBoundary? HighWater { get; init; }
    /// <summary>Gets the last returned boundary.</summary>
    public BaseSubjectLifecycleOrderingBoundary? Through { get; init; }
    /// <summary>Gets the projection generation.</summary>
    public required long ProjectionGeneration { get; init; }
    /// <summary>Gets exact provider read intervals.</summary>
    public required ImmutableArray<BaseReadIntervalEvidence> Intervals { get; init; }
    /// <summary>Gets exact provider accounting.</summary>
    public required BaseSubjectLifecycleReadAccounting Accounting { get; init; }
}

/// <summary>Requests one exact identified provider-owned checkpoint advancement.</summary>
public sealed record BaseSubjectLifecycleProviderCheckpointRequest
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the contract identity.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the consumer identity.</summary>
    public required string ConsumerId { get; init; }
    /// <summary>Gets the consumer version.</summary>
    public required int ConsumerVersion { get; init; }
    /// <summary>Gets the installed consumer checksum.</summary>
    public required string ConsumerChecksum { get; init; }
    /// <summary>Gets the projection generation.</summary>
    public required long ProjectionGeneration { get; init; }
    /// <summary>Gets the exact authorized scope.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets the issued complete ordering boundary.</summary>
    public required BaseSubjectLifecycleOrderingBoundary? Through { get; init; }
    /// <summary>Gets the expected checkpoint generation.</summary>
    public required long ExpectedCheckpointGeneration { get; init; }
    /// <summary>Gets the identified request authority.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets the absolute operation deadline.</summary>
    public required DateTimeOffset DeadlineUtc { get; init; }
}

/// <summary>Requests one bounded current-state reconciliation page.</summary>
public sealed record BaseSubjectLifecycleProviderReconciliationRequest
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the contract identity.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the installed contract checksum.</summary>
    public required string ContractChecksum { get; init; }
    /// <summary>Gets the consumer identity.</summary>
    public required string ConsumerId { get; init; }
    /// <summary>Gets the consumer version.</summary>
    public required int ConsumerVersion { get; init; }
    /// <summary>Gets the consumer checksum.</summary>
    public required string ConsumerChecksum { get; init; }
    /// <summary>Gets the consumer projection generation.</summary>
    public required long ProjectionGeneration { get; init; }
    /// <summary>Gets the exact authorized scope.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets the exclusive subject seek boundary.</summary>
    public BaseSubjectId? AfterSubjectId { get; init; }
    /// <summary>Gets the maximum rows.</summary>
    public required int Take { get; init; }
    /// <summary>Gets the maximum canonical result bytes.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets the absolute deadline.</summary>
    public required DateTimeOffset DeadlineUtc { get; init; }
}

/// <summary>Contains one provider-owned reconciliation page.</summary>
public sealed record BaseSubjectLifecycleProviderReconciliationPage
{
    /// <summary>Gets protected scope authority.</summary>
    public required BaseProtectedSubjectScope Scope { get; init; }
    /// <summary>Gets current sanitized subject lifetimes.</summary>
    public required ImmutableArray<BaseCurrentSubjectLifecycle> Subjects { get; init; }
    /// <summary>Gets the next exclusive subject boundary.</summary>
    public BaseSubjectId? NextSubjectId { get; init; }
    /// <summary>Gets the captured feed high water.</summary>
    public BaseSubjectLifecycleOrderingBoundary? CapturedHighWater { get; init; }
    /// <summary>Gets the consumer projection generation.</summary>
    public required long ProjectionGeneration { get; init; }
    /// <summary>Gets exact read intervals.</summary>
    public required ImmutableArray<BaseReadIntervalEvidence> Intervals { get; init; }
    /// <summary>Gets exact accounting.</summary>
    public required BaseSubjectLifecycleReadAccounting Accounting { get; init; }
}

/// <summary>Requests protected lifecycle provider inspection.</summary>
public sealed record BaseSubjectLifecycleProviderInspectionRequest
{
    /// <summary>Gets the contract identity.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets an optional exact consumer identity.</summary>
    public string? ConsumerId { get; init; }
    /// <summary>Gets installed scope-query authority.</summary>
    public required BaseSubjectScopeQueryAuthority ScopeAuthority { get; init; }
    /// <summary>Gets an optional canonical subject identity.</summary>
    public BaseSubjectId? SubjectId { get; init; }
    /// <summary>Gets whether terminal evidence is requested.</summary>
    public required bool IncludeTerminalReceipt { get; init; }
    /// <summary>Gets the maximum canonical result bytes.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets the absolute deadline.</summary>
    public required DateTimeOffset DeadlineUtc { get; init; }
}

/// <summary>Contains sanitized installed-consumer state.</summary>
public sealed record BaseSubjectLifecycleConsumerInspection
{
    /// <summary>Gets the consumer identity.</summary>
    public required string ConsumerId { get; init; }
    /// <summary>Gets the consumer version.</summary>
    public required int ConsumerVersion { get; init; }
    /// <summary>Gets the projection generation.</summary>
    public required long ProjectionGeneration { get; init; }
    /// <summary>Gets the exact future-only installation cutoff when lifecycle history existed.</summary>
    public BaseSubjectLifecycleOrderingBoundary? InstallationCutoff { get; init; }
    /// <summary>Gets the graph generation that published this consumer projection.</summary>
    public required long PublishedGraphGeneration { get; init; }
    /// <summary>Gets the durable checkpoint boundary.</summary>
    public BaseSubjectLifecycleOrderingBoundary? Through { get; init; }
    /// <summary>Gets the checkpoint generation.</summary>
    public required long CheckpointGeneration { get; init; }
    /// <summary>Gets whether the checkpoint was overtaken.</summary>
    public required bool Overtaken { get; init; }
}

/// <summary>Contains protected lifecycle provider inspection.</summary>
public sealed record BaseSubjectLifecycleProviderInspection
{
    /// <summary>Gets the exact provider store-instance identity.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the current nonnegative restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the delivery epoch.</summary>
    public required long DeliveryEpoch { get; init; }
    /// <summary>Gets the earliest retained boundary.</summary>
    public BaseSubjectLifecycleOrderingBoundary? EarliestRetained { get; init; }
    /// <summary>Gets the current high water.</summary>
    public BaseSubjectLifecycleOrderingBoundary? HighWater { get; init; }
    /// <summary>Gets installed consumer state.</summary>
    public required ImmutableArray<BaseSubjectLifecycleConsumerInspection> Consumers { get; init; }
    /// <summary>Gets authorized terminal evidence.</summary>
    public BaseSubjectTerminalLifetimeReceipt? TerminalReceipt { get; init; }
    /// <summary>Gets exact provider accounting.</summary>
    public required BaseSubjectLifecycleReadAccounting Accounting { get; init; }
}

/// <summary>Classifies one closed lifecycle maintenance operation.</summary>
public enum BaseSubjectLifecycleMaintenanceKind
{
    /// <summary>Prunes eligible delivery authority.</summary>
    Prune = 0,
    /// <summary>Marks a lagged durable checkpoint overtaken.</summary>
    MarkCheckpointOvertaken = 1,
    /// <summary>Removes one installed consumer after bounded cleanup.</summary>
    RemoveConsumer = 2,
    /// <summary>Rebuilds one delivery membership projection.</summary>
    RebuildDeliveryProjection = 3,
    /// <summary>Transforms lifecycle authority during restore.</summary>
    RestoreTransform = 4,
    /// <summary>Recovers an interrupted publication.</summary>
    RecoverPublication = 5,
    /// <summary>Rotates protected scope-index authority.</summary>
    RotateScopeProtection = 6,
}

/// <summary>Requests one identified bounded lifecycle maintenance operation.</summary>
public sealed record BaseSubjectLifecycleMaintenanceExecutionRequest
{
    private byte[] _planChecksum = [];
    private byte[]? _lastCanonicalKey;
    /// <summary>Gets the closed maintenance request format version.</summary>
    public required int FormatVersion { get; init; }
    /// <summary>Gets the maintenance kind.</summary>
    public required BaseSubjectLifecycleMaintenanceKind Kind { get; init; }
    /// <summary>Gets the exact contract for contract- or consumer-scoped work.</summary>
    public string? ContractId { get; init; }
    /// <summary>Gets the exact positive contract version when a contract is present.</summary>
    public int? ContractVersion { get; init; }
    /// <summary>Gets the exact consumer for consumer-scoped work.</summary>
    public string? ConsumerId { get; init; }
    /// <summary>Gets the exact positive consumer version when a consumer is present.</summary>
    public int? ConsumerVersion { get; init; }
    /// <summary>Gets the exact protected logical scope for scope-scoped work.</summary>
    public BaseOwnedSubjectScopeEvidence? Scope { get; init; }
    /// <summary>Gets the inclusive retained boundary used by prune and overtake operations.</summary>
    public BaseSubjectLifecycleOrderingBoundary? RetainedFrom { get; init; }
    /// <summary>Gets identified request authority.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets the canonical plan checksum.</summary>
    public required byte[] PlanChecksum
    {
        get => [.. _planChecksum];
        init => _planChecksum = value is null ? throw new ArgumentNullException(nameof(value)) : [.. value];
    }
    /// <summary>Gets the expected store generation.</summary>
    public required long ExpectedStoreGeneration { get; init; }
    /// <summary>Gets the expected installed schema generation.</summary>
    public required long ExpectedSchemaGeneration { get; init; }
    /// <summary>Gets the expected restore epoch.</summary>
    public required long ExpectedRestoreEpoch { get; init; }
    /// <summary>Gets the expected delivery epoch.</summary>
    public required long ExpectedDeliveryEpoch { get; init; }
    /// <summary>Gets the expected consumer projection generation for consumer-scoped work.</summary>
    public long? ExpectedProjectionGeneration { get; init; }
    /// <summary>Gets the expected scope-protection generation.</summary>
    public required long ExpectedScopeProtectionGeneration { get; init; }
    /// <summary>Gets the expected active scope-protection key.</summary>
    public required string ExpectedScopeProtectionKeyId { get; init; }
    /// <summary>Gets the replacement key only for rotation.</summary>
    public string? ReplacementScopeProtectionKeyId { get; init; }
    /// <summary>Gets the exclusive canonical resume key; null starts at the first page.</summary>
    public byte[]? LastCanonicalKey
    {
        get => _lastCanonicalKey is null ? null : [.. _lastCanonicalKey];
        init => _lastCanonicalKey = value is null ? null : [.. value];
    }
    /// <summary>Gets the fixed bounded page size.</summary>
    public required int PageSize { get; init; }
    /// <summary>Gets the operation timeout.</summary>
    public required TimeSpan OperationTimeout { get; init; }
    /// <summary>Gets the commit-completion timeout.</summary>
    public required TimeSpan CommitCompletionTimeout { get; init; }
}

/// <summary>Returns canonical evidence for one completed lifecycle maintenance publication.</summary>
public sealed record BaseSubjectLifecycleMaintenanceResult
{
    /// <summary>Gets the completed maintenance kind.</summary>
    public required BaseSubjectLifecycleMaintenanceKind Kind { get; init; }
    /// <summary>Gets rows examined across every bounded page.</summary>
    public required long ExaminedCount { get; init; }
    /// <summary>Gets rows changed across every bounded page.</summary>
    public required long ChangedCount { get; init; }
    /// <summary>Gets exact canonical retained-work bytes.</summary>
    public required long CanonicalBytes { get; init; }
    /// <summary>Gets the rolling SHA-256 checksum of canonical changed keys.</summary>
    public required string RollingChecksum { get; init; }
    /// <summary>Gets the resulting lifecycle delivery epoch.</summary>
    public required long DeliveryEpoch { get; init; }
    /// <summary>Gets the resulting consumer projection generation when applicable.</summary>
    public long? ProjectionGeneration { get; init; }
    /// <summary>Gets whether this invocation resolved previously completed authority.</summary>
    public required bool Duplicate { get; init; }
}

/// <summary>Provides one provider-owned, maintenance-closed lifecycle operation.</summary>
public interface IBaseSubjectLifecycleMaintenanceSession
{
    /// <summary>Executes or resumes the exact bounded provider plan.</summary>
    ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteAsync(
        BaseSubjectLifecycleMaintenanceExecutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Processes only BASE-owned lifecycle maintenance plans.</summary>
public interface IBaseSubjectLifecycleMaintenanceProcessor
{
    /// <summary>Executes one provider-bound maintenance request.</summary>
    ValueTask<RecordMutationExecutionResult> ExecuteAsync(
        IBaseSubjectLifecycleMaintenanceSession session,
        BaseSubjectLifecycleMaintenanceExecutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Provides dedicated durable exported-subject lifecycle delivery.</summary>
public interface IBaseSubjectLifecycleStore
{
    /// <summary>Reads one bounded consumer-indexed page.</summary>
    ValueTask<OperationResult<BaseSubjectLifecycleProviderPage>> ReadAsync(BaseSubjectLifecycleProviderReadRequest request, CancellationToken cancellationToken = default);
    /// <summary>Atomically advances one independent consumer checkpoint.</summary>
    ValueTask<RecordMutationExecutionResult> AdvanceCheckpointAsync(IAtomicMutationProcessor processor, RecordMutationExecutionRequest execution, CancellationToken cancellationToken = default);
    /// <summary>Reads one bounded authorized current-state reconciliation page.</summary>
    ValueTask<OperationResult<BaseSubjectLifecycleProviderReconciliationPage>> ReconcileAsync(BaseSubjectLifecycleProviderReconciliationRequest request, CancellationToken cancellationToken = default);
    /// <summary>Inspects protected lifecycle authority.</summary>
    ValueTask<OperationResult<BaseSubjectLifecycleProviderInspection>> InspectAsync(BaseSubjectLifecycleProviderInspectionRequest request, CancellationToken cancellationToken = default);
    /// <summary>Executes one BASE-owned lifecycle maintenance operation.</summary>
    ValueTask<RecordMutationExecutionResult> ExecuteMaintenanceAsync(IBaseSubjectLifecycleMaintenanceProcessor processor, BaseSubjectLifecycleMaintenanceExecutionRequest request, CancellationToken cancellationToken = default);
}
