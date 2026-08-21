using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Defines the closed state selector for one bounded activation inspection.</summary>
public enum BaseActivationStateSelector
{
    /// <summary>Selects every state currently authorized by the request.</summary>
    All = 0,
    /// <summary>Selects work that may become executable.</summary>
    Runnable = 1,
    /// <summary>Selects currently claimed or effect-started work.</summary>
    Active = 2,
    /// <summary>Selects terminal retained work.</summary>
    Terminal = 3,
    /// <summary>Selects ambiguous external-effect outcomes.</summary>
    OutcomeUnknown = 4,
}

/// <summary>Contains the canonical total-order boundary for activation administration.</summary>
public sealed record BaseActivationAdministrationBoundary
{
    /// <summary>Gets the installed definition identity.</summary>
    public required string DefinitionId { get; init; }
    /// <summary>Gets the installed definition version.</summary>
    public required int DefinitionVersion { get; init; }
    /// <summary>Gets the effective due instant.</summary>
    public required long EffectiveDueAt { get; init; }
    /// <summary>Gets the stable activation identity.</summary>
    public required string ActivationId { get; init; }
}

/// <summary>Requests one bounded, protected activation-administration page.</summary>
public sealed record BaseActivationAdministrationQueryRequest
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets current protected scope seek authority.</summary>
    public required BaseOwnedScopeSeekAuthority Scope { get; init; }
    /// <summary>Gets an optional exact installed definition.</summary>
    public BaseActivationDefinitionKey? Definition { get; init; }
    /// <summary>Gets the closed state selector.</summary>
    public required BaseActivationStateSelector States { get; init; }
    /// <summary>Gets the exclusive continuation boundary.</summary>
    public BaseActivationAdministrationBoundary? After { get; init; }
    /// <summary>Gets the bounded page size.</summary>
    public required int Take { get; init; }
    /// <summary>Gets trusted accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets effective provider limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains one sanitized activation-administration row.</summary>
public sealed record BaseActivationAdministrationItem
{
    /// <summary>Gets the activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the exact installed definition.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets current durable state.</summary>
    public required BaseActivationState State { get; init; }
    /// <summary>Gets current activation generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets the effective due instant.</summary>
    public required long EffectiveDueAt { get; init; }
    /// <summary>Gets the optional occurrence identity.</summary>
    public string? OccurrenceId { get; init; }
    /// <summary>Gets the current attempt number.</summary>
    public required int AttemptNumber { get; init; }
    /// <summary>Gets whether a terminal result remains retained.</summary>
    public required bool ResultRetained { get; init; }
    /// <summary>Gets whether external-effect authority remains retained.</summary>
    public required bool EffectAuthorityRetained { get; init; }
    /// <summary>Gets the canonical control checksum.</summary>
    public required ImmutableArray<byte> ControlChecksum { get; init; }
}

/// <summary>Contains one bounded provider administration page and its read authority.</summary>
public sealed record BaseActivationAdministrationPage
{
    /// <summary>Gets canonical ordered sanitized items.</summary>
    public required ImmutableArray<BaseActivationAdministrationItem> Items { get; init; }
    /// <summary>Gets the exclusive next boundary when more rows exist.</summary>
    public BaseActivationAdministrationBoundary? Next { get; init; }
    /// <summary>Gets the finite activation-index generation read by the provider.</summary>
    public required long CapturedIndexGeneration { get; init; }
    /// <summary>Gets protected read-interval evidence.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> Intervals { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
}
