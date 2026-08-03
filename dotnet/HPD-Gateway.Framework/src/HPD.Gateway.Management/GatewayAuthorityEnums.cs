namespace HPD.Gateway.Management;

public enum GatewayAuthorityDurability { ProcessLocal, RestartDurable }
public enum GatewayValidationOutcome { Valid, Invalid, DependencyUnavailable, Canceled, InternalFailure }
public enum GatewayDeliveryState { Immediate, Claimed, RetryScheduled, OutcomePersistencePending, Completed, TerminalFailure }
public enum GatewayNodeOutcomeKind { ActiveAcknowledged, RejectedBeforePublish, CanceledBeforePublish, PublicationIndeterminate, Superseded, Stale, Conflict }
public enum GatewayAdministrativeOperationKind { Purge, Backup }
public enum GatewayAdministrativeObservationKind { Succeeded, Failed, Indeterminate }
public enum GatewayAdministrativeCompletionState { Completed, ExecutionSucceededCompletionPending, IndeterminatePending }
