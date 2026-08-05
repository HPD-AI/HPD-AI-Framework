namespace HPD.Base;

/// <summary>Defines the operation status contract.</summary>
public enum OperationStatus
{
    /// <summary>Identifies ok.</summary>
Ok,
    /// <summary>Identifies created.</summary>
Created,
    /// <summary>Identifies updated.</summary>
Updated,
    /// <summary>Identifies deleted.</summary>
Deleted,
    /// <summary>Identifies no content.</summary>
NoContent,
    /// <summary>Identifies not found.</summary>
NotFound,
    /// <summary>Identifies conflict.</summary>
Conflict,
    /// <summary>Identifies validation failed.</summary>
ValidationFailed,
    /// <summary>Identifies policy denied.</summary>
PolicyDenied,
    /// <summary>Identifies unauthorized.</summary>
Unauthorized,
    /// <summary>Identifies unsupported.</summary>
Unsupported,
    /// <summary>Identifies capability unavailable.</summary>
CapabilityUnavailable,
    /// <summary>Identifies store error.</summary>
StoreError
}

/// <summary>Defines the error category contract.</summary>
public enum ErrorCategory { /// <summary>Identifies none.</summary>
None, /// <summary>Identifies validation.</summary>
Validation, /// <summary>Identifies authentication.</summary>
Authentication, /// <summary>Identifies authorization.</summary>
Authorization, /// <summary>Identifies not found.</summary>
NotFound, /// <summary>Identifies conflict.</summary>
Conflict, /// <summary>Identifies unsupported.</summary>
Unsupported, /// <summary>Identifies capability.</summary>
Capability, /// <summary>Identifies store.</summary>
Store, /// <summary>Identifies unexpected.</summary>
Unexpected }
/// <summary>Defines the conflict kind contract.</summary>
public enum ConflictKind { /// <summary>Identifies revision.</summary>
Revision, /// <summary>Identifies unique.</summary>
Unique, /// <summary>Identifies foreign key.</summary>
ForeignKey, /// <summary>Identifies state.</summary>
State, /// <summary>Identifies transaction.</summary>
Transaction, /// <summary>Identifies unknown.</summary>
Unknown }
/// <summary>Defines the capability failure reason contract.</summary>
public enum CapabilityFailureReason { /// <summary>Identifies unsupported.</summary>
Unsupported, /// <summary>Identifies not installed.</summary>
NotInstalled, /// <summary>Identifies disabled.</summary>
Disabled, /// <summary>Identifies unhealthy.</summary>
Unhealthy, /// <summary>Identifies misconfigured.</summary>
Misconfigured, /// <summary>Identifies not allowed.</summary>
NotAllowed }
/// <summary>Defines the revision guarantee contract.</summary>
public enum RevisionGuarantee { /// <summary>Identifies none.</summary>
None, /// <summary>Identifies advisory.</summary>
Advisory, /// <summary>Identifies runtime.</summary>
Runtime, /// <summary>Identifies store.</summary>
Store, /// <summary>Identifies native.</summary>
Native }
