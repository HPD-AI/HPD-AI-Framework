using System.Text.Json.Serialization;

namespace HPD.Gateway.ControlPlane;

public enum GatewayAuthorityDurability { ProcessLocal, RestartDurable }
public enum GatewayValidationOutcome
{
    [JsonStringEnumMemberName("valid")] Valid,
    [JsonStringEnumMemberName("invalid")] Invalid,
    [JsonStringEnumMemberName("dependencyUnavailable")] DependencyUnavailable,
    [JsonStringEnumMemberName("canceled")] Canceled,
    [JsonStringEnumMemberName("internalFailure")] InternalFailure,
}

internal enum GatewayDeliveryState
{
    [JsonStringEnumMemberName("immediate")] Immediate,
    [JsonStringEnumMemberName("claimed")] Claimed,
    [JsonStringEnumMemberName("retryScheduled")] RetryScheduled,
    [JsonStringEnumMemberName("outcomePersistencePending")] OutcomePersistencePending,
    [JsonStringEnumMemberName("completed")] Completed,
    [JsonStringEnumMemberName("terminalFailure")] TerminalFailure,
}

public enum GatewayNodeOutcomeKind
{
    [JsonStringEnumMemberName("activeAcknowledged")] ActiveAcknowledged,
    [JsonStringEnumMemberName("rejectedBeforePublish")] RejectedBeforePublish,
    [JsonStringEnumMemberName("canceledBeforePublish")] CanceledBeforePublish,
    [JsonStringEnumMemberName("publicationIndeterminate")] PublicationIndeterminate,
    [JsonStringEnumMemberName("superseded")] Superseded,
    [JsonStringEnumMemberName("stale")] Stale,
    [JsonStringEnumMemberName("conflict")] Conflict,
}

public enum GatewayAdministrativeOperationKind
{
    [JsonStringEnumMemberName("purge")] Purge,
    [JsonStringEnumMemberName("backup")] Backup,
}

public enum GatewayAdministrativeObservationKind
{
    [JsonStringEnumMemberName("succeeded")] Succeeded,
    [JsonStringEnumMemberName("failed")] Failed,
    [JsonStringEnumMemberName("indeterminate")] Indeterminate,
}

public enum GatewayAdministrativeCompletionState
{
    [JsonStringEnumMemberName("completed")] Completed,
    [JsonStringEnumMemberName("failed")] Failed,
    [JsonStringEnumMemberName("executionSucceededCompletionPending")] ExecutionSucceededCompletionPending,
    [JsonStringEnumMemberName("indeterminatePending")] IndeterminatePending,
}

internal enum GatewayAdministrativeExecutionPhase
{
    [JsonStringEnumMemberName("unclaimed")] Unclaimed,
    [JsonStringEnumMemberName("claimedPreBoundary")] ClaimedPreBoundary,
    [JsonStringEnumMemberName("boundaryCrossed")] BoundaryCrossed,
    [JsonStringEnumMemberName("observed")] Observed,
}
