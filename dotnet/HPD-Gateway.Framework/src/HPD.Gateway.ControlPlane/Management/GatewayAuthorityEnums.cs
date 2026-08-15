namespace HPD.Gateway.ControlPlane;

public enum GatewayAuthorityDurability { ProcessLocal, RestartDurable }
public enum GatewayValidationOutcome { Valid, Invalid, DependencyUnavailable, Canceled, InternalFailure }
internal enum GatewayDeliveryState { Immediate, Claimed, RetryScheduled, OutcomePersistencePending, Completed, TerminalFailure }
public enum GatewayNodeOutcomeKind { ActiveAcknowledged, RejectedBeforePublish, CanceledBeforePublish, PublicationIndeterminate, Superseded, Stale, Conflict }
public enum GatewayAdministrativeOperationKind { Purge, Backup }
public enum GatewayAdministrativeObservationKind { Succeeded, Failed, Indeterminate }
public enum GatewayAdministrativeCompletionState { Completed, Failed, ExecutionSucceededCompletionPending, IndeterminatePending }
internal enum GatewayAdministrativeExecutionPhase { Unclaimed, ClaimedPreBoundary, BoundaryCrossed, Observed }
