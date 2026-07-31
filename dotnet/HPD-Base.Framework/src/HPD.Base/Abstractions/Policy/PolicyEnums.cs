namespace HPD.Base;

public enum PolicyResourceKind { Collection, Query, Record, CreatePayload, UpdatePayload, DeleteCandidate, Field, File, Schema, AdminMetadata }
public enum PolicyEffect { Allow, Deny, Abstain }
public enum PolicyOutcome { Allowed, AllowedWithConstraints, Denied, Unauthenticated, NotFound, FilteredOut, HiddenField, ValidationDenied, Unsupported, Bypassed }
public enum FieldMaskMode { Unspecified, AllowAll, IncludeOnly, Exclude, DenyAll }
public enum ObligationEnforcement { Required, BestEffort, Advisory }
public enum PushdownMode { None, FilterAppend, FieldProjection, NativePolicy, Partial }
public enum PushdownTrust { StoreEnforced, RuntimeEnforced, Mixed, Advisory }
public enum AccessSubjectKind { Anonymous, Authenticated, User, Role, Team, TeamRole, Tenant, ServicePrincipal, Admin, System, HostDefined }
public enum GrantEffect { Allow, Deny }
public enum ResourceScopeKind { Runtime, Collection, Record, Field, File, Schema, Admin }
