namespace HPD.Base;

/// <summary>Requests transaction-bound authority for one graph-installed lifecycle consumer projection.</summary>
public sealed record BaseSubjectLifecycleConsumerProjectionCaptureRequest
{
    /// <summary>Gets the stable consumer ID.</summary>
    public required string ConsumerId { get; init; }
    /// <summary>Gets the consumer version.</summary>
    public required int ConsumerVersion { get; init; }
    /// <summary>Gets the finalized consumer checksum.</summary>
    public required string ConsumerChecksum { get; init; }
    /// <summary>Gets the exported contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the exported contract version.</summary>
    public required int ContractVersion { get; init; }
}

/// <summary>Contains authoritative current lifetime state for one requested retirement projection.</summary>
public sealed record BaseCapturedSubjectRetirementProjection
{
    /// <summary>Gets the source mutation ordinal.</summary>
    public required int SourceMutationOrdinal { get; init; }
    /// <summary>Gets the contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the contract checksum.</summary>
    public required string ContractChecksum { get; init; }
    /// <summary>Gets the retirement-policy checksum.</summary>
    public required string RetirementPolicyChecksum { get; init; }
    /// <summary>Gets the accepted-consumer-set checksum.</summary>
    public required string AcceptedConsumerSetChecksum { get; init; }
    /// <summary>Gets the subject ID bound to the captured lifetime.</summary>
    public required BaseSubjectId SubjectId { get; init; }
    /// <summary>Gets the protected provider scope bound to the captured lifetime.</summary>
    public required BaseProtectedSubjectScope ProtectedScope { get; init; }
    /// <summary>Gets the current authority epoch.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
    /// <summary>Gets the current incarnation.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
    /// <summary>Gets the current subject-local sequence.</summary>
    public required long CurrentSubjectSequence { get; init; }
    /// <summary>Gets the current lifecycle state.</summary>
    public required BaseSubjectLifecycleState CurrentState { get; init; }
    /// <summary>Gets the current barrier when present.</summary>
    public BaseSubjectRetirementBarrier? CurrentBarrier { get; init; }
}


/// <summary>Binds one lifecycle transition to one graph-installed consumer projection.</summary>
public sealed record BaseSubjectLifecycleMembershipPlanItem
{
    /// <summary>Gets the consumer ID.</summary>
    public required string ConsumerId { get; init; }
    /// <summary>Gets the consumer version.</summary>
    public required int ConsumerVersion { get; init; }
    /// <summary>Gets the consumer checksum.</summary>
    public required string ConsumerChecksum { get; init; }
    /// <summary>Gets the projection generation.</summary>
    public required long ProjectionGeneration { get; init; }
    /// <summary>Gets the matched observed state.</summary>
    public required BaseSubjectLifecycleState MatchedObservedState { get; init; }
}
