namespace HPD.Base;
/// <summary>Defines policy Resource Kind.</summary>
public enum PolicyResourceKind
{
    /// <summary>Identifies collection.</summary>
Collection,
    /// <summary>Identifies query.</summary>
Query,
    /// <summary>Identifies record.</summary>
Record,
    /// <summary>Identifies create Payload.</summary>
CreatePayload,
    /// <summary>Identifies update Payload.</summary>
UpdatePayload,
    /// <summary>Identifies delete Candidate.</summary>
DeleteCandidate,
    /// <summary>Identifies relation Target.</summary>
RelationTarget,
    /// <summary>Identifies field.</summary>
Field,
    /// <summary>Identifies file.</summary>
File,
    /// <summary>Identifies schema.</summary>
Schema,
    /// <summary>Identifies admin Metadata.</summary>
AdminMetadata
}

/// <summary>Defines policy Effect.</summary>
public enum PolicyEffect
{
    /// <summary>Identifies allow.</summary>
Allow,
    /// <summary>Identifies deny.</summary>
Deny,
    /// <summary>Identifies abstain.</summary>
Abstain
}

/// <summary>Defines policy Outcome.</summary>
public enum PolicyOutcome
{
    /// <summary>Identifies allowed.</summary>
Allowed,
    /// <summary>Identifies allowed With Constraints.</summary>
AllowedWithConstraints,
    /// <summary>Identifies denied.</summary>
Denied,
    /// <summary>Identifies unauthenticated.</summary>
Unauthenticated,
    /// <summary>Identifies not Found.</summary>
NotFound,
    /// <summary>Identifies filtered Out.</summary>
FilteredOut,
    /// <summary>Identifies hidden Field.</summary>
HiddenField,
    /// <summary>Identifies validation Denied.</summary>
ValidationDenied,
    /// <summary>Identifies unsupported.</summary>
Unsupported,
    /// <summary>Identifies bypassed.</summary>
Bypassed
}

/// <summary>Defines field Mask Mode.</summary>
public enum FieldMaskMode
{
    /// <summary>Identifies unspecified.</summary>
Unspecified,
    /// <summary>Identifies allow All.</summary>
AllowAll,
    /// <summary>Identifies include Only.</summary>
IncludeOnly,
    /// <summary>Identifies exclude.</summary>
Exclude,
    /// <summary>Identifies deny All.</summary>
DenyAll
}

/// <summary>Defines obligation Enforcement.</summary>
public enum ObligationEnforcement
{
    /// <summary>Identifies required.</summary>
Required,
    /// <summary>Identifies best Effort.</summary>
BestEffort,
    /// <summary>Identifies advisory.</summary>
Advisory
}

/// <summary>Defines pushdown Mode.</summary>
public enum PushdownMode
{
    /// <summary>Identifies none.</summary>
None,
    /// <summary>Identifies filter Append.</summary>
FilterAppend,
    /// <summary>Identifies field Projection.</summary>
FieldProjection,
    /// <summary>Identifies native Policy.</summary>
NativePolicy,
    /// <summary>Identifies partial.</summary>
Partial
}

/// <summary>Defines pushdown Trust.</summary>
public enum PushdownTrust
{
    /// <summary>Identifies store Enforced.</summary>
StoreEnforced,
    /// <summary>Identifies runtime Enforced.</summary>
RuntimeEnforced,
    /// <summary>Identifies mixed.</summary>
Mixed,
    /// <summary>Identifies advisory.</summary>
Advisory
}

/// <summary>Defines access Subject Kind.</summary>
public enum AccessSubjectKind
{
    /// <summary>Identifies anonymous.</summary>
Anonymous,
    /// <summary>Identifies authenticated.</summary>
Authenticated,
    /// <summary>Identifies user.</summary>
User,
    /// <summary>Identifies role.</summary>
Role,
    /// <summary>Identifies team.</summary>
Team,
    /// <summary>Identifies team Role.</summary>
TeamRole,
    /// <summary>Identifies tenant.</summary>
Tenant,
    /// <summary>Identifies service Principal.</summary>
ServicePrincipal,
    /// <summary>Identifies admin.</summary>
Admin,
    /// <summary>Identifies system.</summary>
System,
    /// <summary>Identifies host Defined.</summary>
HostDefined
}

/// <summary>Defines grant Effect.</summary>
public enum GrantEffect
{
    /// <summary>Identifies allow.</summary>
Allow,
    /// <summary>Identifies deny.</summary>
Deny
}

/// <summary>Defines resource Scope Kind.</summary>
public enum ResourceScopeKind
{
    /// <summary>Identifies runtime.</summary>
Runtime,
    /// <summary>Identifies collection.</summary>
Collection,
    /// <summary>Identifies record.</summary>
Record,
    /// <summary>Identifies field.</summary>
Field,
    /// <summary>Identifies file.</summary>
File,
    /// <summary>Identifies schema.</summary>
Schema,
    /// <summary>Identifies admin.</summary>
Admin
}
