using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Classifies one crash-recoverable activation maintenance operation.</summary>
public enum BaseActivationMaintenanceKind
{
    /// <summary>Returns expired claimed work to retry-pending authority.</summary>
    RecoverExpiredClaims,
    /// <summary>Moves effects whose exact executor authority is provably dead to outcome-unknown.</summary>
    RecoverExpiredEffects
}

/// <summary>Requests one identified bounded activation-maintenance page.</summary>
public sealed record BaseActivationMaintenanceRequest
{
    /// <summary>Gets the exact application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the exact protected scope seek.</summary>
    public required BaseOwnedScopeSeekAuthority Scope { get; init; }
    /// <summary>Gets the exact installed definition.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the closed maintenance kind.</summary>
    public required BaseActivationMaintenanceKind Kind { get; init; }
    /// <summary>Gets the optional exclusive activation-ID boundary.</summary>
    public string? AfterActivationId { get; init; }
    /// <summary>Gets the bounded page size.</summary>
    public required int Take { get; init; }
    /// <summary>Gets the provider-accepted time receipt.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the identified request authority.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets the exact effective safety envelope.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains one committed activation-maintenance item.</summary>
public sealed record BaseActivationMaintenanceItem
{
    /// <summary>Gets the activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the prior generation.</summary>
    public required long PreviousGeneration { get; init; }
    /// <summary>Gets the committed generation.</summary>
    public required long ResultingGeneration { get; init; }
    /// <summary>Gets the prior state.</summary>
    public required BaseActivationState PreviousState { get; init; }
    /// <summary>Gets the committed state.</summary>
    public required BaseActivationState ResultingState { get; init; }
    /// <summary>Gets the committed control checksum.</summary>
    public required ImmutableArray<byte> ControlChecksum { get; init; }
}

/// <summary>Returns one crash-recoverable activation-maintenance page.</summary>
public sealed record BaseActivationMaintenancePage
{
    /// <summary>Gets committed items in activation-ID order.</summary>
    public required ImmutableArray<BaseActivationMaintenanceItem> Items { get; init; }
    /// <summary>Gets the next exclusive activation-ID boundary.</summary>
    public string? NextActivationId { get; init; }
    /// <summary>Gets whether the captured page reached its high-water.</summary>
    public required bool Completed { get; init; }
    /// <summary>Gets exact provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets request disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

/// <summary>Requests pruning of dependency-free disposed activation authority.</summary>
public sealed record BaseActivationPruneRequest
{
    /// <summary>Gets the exact application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the exact protected scope seek.</summary>
    public required BaseOwnedScopeSeekAuthority Scope { get; init; }
    /// <summary>Gets the exact installed definition.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the optional exclusive activation-ID boundary.</summary>
    public string? AfterActivationId { get; init; }
    /// <summary>Gets the bounded page size.</summary>
    public required int Take { get; init; }
    /// <summary>Gets the provider-accepted time receipt.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the identified request authority.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets the exact effective safety envelope.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Returns one committed activation-pruning page.</summary>
public sealed record BaseActivationPrunePage
{
    /// <summary>Gets the exact removed activation identities.</summary>
    public required ImmutableArray<string> ActivationIds { get; init; }
    /// <summary>Gets the next exclusive activation-ID boundary.</summary>
    public string? NextActivationId { get; init; }
    /// <summary>Gets whether the captured page reached its high-water.</summary>
    public required bool Completed { get; init; }
    /// <summary>Gets exact provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets request disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

/// <summary>Requests exact outcome-unknown reconciliation under operator evidence.</summary>
public sealed record BaseActivationIndeterminateRequest
{
    /// <summary>Gets the closed reconciliation transition.</summary>
    public required BaseActivationReconcileEffectRequest Reconciliation { get; init; }
}

/// <summary>Returns exact indeterminate resolution evidence.</summary>
public sealed record BaseActivationIndeterminateResolution
{
    /// <summary>Gets the committed activation transition.</summary>
    public required BaseActivationTransitionResult Transition { get; init; }
}

/// <summary>Requests one bounded sanitized quarantine page.</summary>
public sealed record BaseActivationQuarantineRequest
{
    /// <summary>Gets an optional exclusive sequence boundary.</summary>
    public long? AfterSequence { get; init; }
    /// <summary>Gets the bounded page size.</summary>
    public required int Take { get; init; }
}

/// <summary>Contains one sanitized retained-work observation.</summary>
public sealed record BaseActivationQuarantineItem
{
    /// <summary>Gets the positive observation sequence.</summary>
    public required long Sequence { get; init; }
    /// <summary>Gets the stable operation kind.</summary>
    public required string Operation { get; init; }
    /// <summary>Gets the retention start instant.</summary>
    public required DateTimeOffset RetainedAt { get; init; }
}

/// <summary>Returns one sanitized quarantine page.</summary>
public sealed record BaseActivationQuarantinePage
{
    /// <summary>Gets retained work in sequence order.</summary>
    public required ImmutableArray<BaseActivationQuarantineItem> Items { get; init; }
    /// <summary>Gets the next exclusive sequence boundary.</summary>
    public long? NextSequence { get; init; }
}
