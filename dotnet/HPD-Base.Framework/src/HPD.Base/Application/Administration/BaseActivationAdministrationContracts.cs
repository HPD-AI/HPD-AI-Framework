using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Contains common authority for one ControlPlane activation transition.</summary>
public abstract record BaseActivationAdministrationRequest
{
    /// <summary>Gets the configured store identity.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the current ControlPlane principal.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets the installed activation-definition identity.</summary>
    public required string DefinitionId { get; init; }
    /// <summary>Gets the installed activation-definition version.</summary>
    public required int DefinitionVersion { get; init; }
    /// <summary>Gets the durable activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the expected activation generation.</summary>
    public required long ExpectedGeneration { get; init; }
    /// <summary>Gets the identified semantic request.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
}

/// <summary>Requests ControlPlane cancellation of one activation.</summary>
public sealed record BaseActivationAdministrationCancelRequest : BaseActivationAdministrationRequest
{
    /// <summary>Gets bounded descendant propagation behavior.</summary>
    public required BaseCancellationPropagation Propagation { get; init; }
}

/// <summary>Requests ControlPlane retry of one exhausted activation.</summary>
public sealed record BaseActivationAdministrationRetryRequest : BaseActivationAdministrationRequest
{
    /// <summary>Gets the requested due instant; null means trusted accepted time.</summary>
    public DateTimeOffset? DueAt { get; init; }
}

/// <summary>Requests ControlPlane reconciliation of one outcome-unknown external effect.</summary>
public sealed record BaseActivationAdministrationReconcileRequest : BaseActivationAdministrationRequest
{
    /// <summary>Gets the expected effect-start generation.</summary>
    public required long ExpectedEffectStartGeneration { get; init; }
    /// <summary>Gets the protected retained-effect checksum.</summary>
    public required ImmutableArray<byte> ExpectedEffectChecksum { get; init; }
    /// <summary>Gets the selected verified terminal disposition.</summary>
    public required BaseEffectReconciliationDisposition Disposition { get; init; }
    /// <summary>Gets bounded canonical external verification evidence.</summary>
    public required ImmutableArray<byte> VerificationEvidence { get; init; }
    /// <summary>Gets the SHA-256 verification-evidence checksum.</summary>
    public required ImmutableArray<byte> VerificationChecksum { get; init; }
}

/// <summary>Requests ControlPlane disposal of retained terminal activation authority.</summary>
public sealed record BaseActivationAdministrationDisposeRequest : BaseActivationAdministrationRequest;

/// <summary>Requests one exact-scope bounded ControlPlane activation page.</summary>
public sealed record BaseActivationAdministrationReadRequest
{
    /// <summary>Gets the configured store identity.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the current ControlPlane principal.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets exact semantic scope authority.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets an optional installed definition identity.</summary>
    public required string DefinitionId { get; init; }
    /// <summary>Gets the optional installed definition version.</summary>
    public required int DefinitionVersion { get; init; }
    /// <summary>Gets the closed state selector.</summary>
    public required BaseActivationStateSelector States { get; init; }
    /// <summary>Gets the exclusive continuation boundary.</summary>
    public BaseActivationAdministrationBoundary? After { get; init; }
    /// <summary>Gets the bounded requested page size.</summary>
    public required int Take { get; init; }
}

/// <summary>Contains common authority for one paged activation-maintenance operation.</summary>
public abstract record BaseActivationAdministrationPageRequest
{
    /// <summary>Gets the configured store identity.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the current ControlPlane principal.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets exact semantic scope authority.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets the installed definition identity.</summary>
    public required string DefinitionId { get; init; }
    /// <summary>Gets the installed definition version.</summary>
    public required int DefinitionVersion { get; init; }
    /// <summary>Gets the optional exclusive activation-ID boundary.</summary>
    public string? AfterActivationId { get; init; }
    /// <summary>Gets the bounded page size.</summary>
    public required int Take { get; init; }
    /// <summary>Gets the identified request authority.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
}

/// <summary>Requests one ControlPlane crash-recovery maintenance page.</summary>
public sealed record BaseActivationAdministrationMaintenanceRequest : BaseActivationAdministrationPageRequest
{
    /// <summary>Gets the closed recovery kind.</summary>
    public required BaseActivationMaintenanceKind Kind { get; init; }
}

/// <summary>Requests one ControlPlane disposed-activation pruning page.</summary>
public sealed record BaseActivationAdministrationPruneRequest : BaseActivationAdministrationPageRequest;

/// <summary>Requests one installed callback-free activation migration.</summary>
public sealed record BaseActivationAdministrationMigrationRequest
{
    /// <summary>Gets the configured store identity.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the current ControlPlane principal.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets exact semantic scope authority.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets the installed migration identity.</summary>
    public required string MigrationId { get; init; }
    /// <summary>Gets the installed migration version.</summary>
    public required int MigrationVersion { get; init; }
    /// <summary>Gets the source activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the exact expected source generation.</summary>
    public required long ExpectedGeneration { get; init; }
    /// <summary>Gets the replacement due instant; null uses trusted accepted time.</summary>
    public DateTimeOffset? DueAt { get; init; }
    /// <summary>Gets the identified semantic request.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
}
