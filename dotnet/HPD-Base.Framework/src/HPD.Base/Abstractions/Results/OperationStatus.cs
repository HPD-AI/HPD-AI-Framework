namespace HPD.Base;

public enum OperationStatus
{
    Ok,
    Created,
    Updated,
    Deleted,
    NoContent,
    NotFound,
    Conflict,
    ValidationFailed,
    PolicyDenied,
    Unauthorized,
    Unsupported,
    CapabilityUnavailable,
    StoreError
}

public enum ErrorCategory { None, Validation, Authentication, Authorization, NotFound, Conflict, Unsupported, Capability, Store, Unexpected }
public enum ConflictKind { Revision, Unique, ForeignKey, State, Transaction, Unknown }
public enum CapabilityFailureReason { Unsupported, NotInstalled, Disabled, Unhealthy, Misconfigured, NotAllowed }
public enum RevisionGuarantee { None, Advisory, Runtime, Store, Native }
