using System.Text.Json;

namespace HPD.Base;

/// <summary>Defines the relational query plan status contract.</summary>
public enum RelationalQueryPlanStatus { /// <summary>Identifies supported.</summary>
Supported, /// <summary>Identifies unsupported.</summary>
Unsupported, /// <summary>Identifies partially supported.</summary>
PartiallySupported, /// <summary>Identifies unsafe.</summary>
Unsafe, /// <summary>Identifies unavailable.</summary>
Unavailable, /// <summary>Identifies provider specific.</summary>
ProviderSpecific }
/// <summary>Defines the relational pushdown support contract.</summary>
public enum RelationalPushdownSupport { /// <summary>Identifies complete.</summary>
Complete, /// <summary>Identifies partial.</summary>
Partial, /// <summary>Identifies none.</summary>
None, /// <summary>Identifies unsupported.</summary>
Unsupported, /// <summary>Identifies unsafe.</summary>
Unsafe, /// <summary>Identifies unknown.</summary>
Unknown }
/// <summary>Defines the relational residual kind contract.</summary>
public enum RelationalResidualKind { /// <summary>Identifies none.</summary>
None, /// <summary>Identifies after native scan before page.</summary>
AfterNativeScanBeforePage, /// <summary>Identifies after native filter before page.</summary>
AfterNativeFilterBeforePage, /// <summary>Identifies after page unsafe.</summary>
AfterPageUnsafe, /// <summary>Identifies after count unsafe.</summary>
AfterCountUnsafe, /// <summary>Identifies client side disallowed.</summary>
ClientSideDisallowed, /// <summary>Identifies unknown.</summary>
Unknown }
/// <summary>Defines the relational plan diagnostic severity contract.</summary>
public enum RelationalPlanDiagnosticSeverity { /// <summary>Identifies info.</summary>
Info, /// <summary>Identifies warning.</summary>
Warning, /// <summary>Identifies error.</summary>
Error }

/// <summary>Represents a relational query plan request.</summary>
public sealed record RelationalQueryPlanRequest
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the collection ID.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets or sets the query.</summary>
    public required RecordQuery Query { get; init; }
    /// <summary>Gets or sets the requested visibility.</summary>
    public VisibilityLevel RequestedVisibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the require executable plan.</summary>
    public bool RequireExecutablePlan { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational query plan descriptor.</summary>
public sealed record RelationalQueryPlanDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the collection ID.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets or sets the status.</summary>
    public RelationalQueryPlanStatus Status { get; init; } = RelationalQueryPlanStatus.Unavailable;
    /// <summary>Gets or sets the executable for requested context.</summary>
    public bool ExecutableForRequestedContext { get; init; }
    /// <summary>Gets or sets the safe for requested context.</summary>
    public bool SafeForRequestedContext { get; init; }
    /// <summary>Gets or sets the pushdown.</summary>
    public RelationalQueryPushdownDescriptor? Pushdown { get; init; }
    /// <summary>Gets or sets the residual.</summary>
    public RelationalResidualDescriptor? Residual { get; init; }
    /// <summary>Gets or sets the count.</summary>
    public RelationalCountPlanDescriptor? Count { get; init; }
    /// <summary>Gets or sets the page.</summary>
    public RelationalPagePlanDescriptor? Page { get; init; }
    /// <summary>Gets or sets the sort.</summary>
    public RelationalSortPlanDescriptor? Sort { get; init; }
    /// <summary>Gets or sets the includes.</summary>
    public RelationalIncludePlanDescriptor[]? Includes { get; init; }
    /// <summary>Gets or sets the policy.</summary>
    public RelationalPolicyPlanDescriptor? Policy { get; init; }
    /// <summary>Gets or sets the stages.</summary>
    public RelationalQueryPlanStageDescriptor[]? Stages { get; init; }
    /// <summary>Gets or sets the unsupported parts.</summary>
    public string[]? UnsupportedParts { get; init; }
    /// <summary>Gets or sets the unsafe reasons.</summary>
    public string[]? UnsafeReasons { get; init; }
    /// <summary>Gets or sets the diagnostics.</summary>
    public RelationalPlanDiagnostic[]? Diagnostics { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational query plan stage descriptor.</summary>
public sealed record RelationalQueryPlanStageDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets or sets the summary.</summary>
    public string? Summary { get; init; }
    /// <summary>Gets or sets the observable.</summary>
    public bool Observable { get; init; }
    /// <summary>Gets or sets the safe for requested context.</summary>
    public bool SafeForRequestedContext { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational query pushdown descriptor.</summary>
public sealed record RelationalQueryPushdownDescriptor
{
    /// <summary>Gets or sets the filter.</summary>
    public RelationalPushdownSupport Filter { get; init; } = RelationalPushdownSupport.Unknown;
    /// <summary>Gets or sets the sort.</summary>
    public RelationalPushdownSupport Sort { get; init; } = RelationalPushdownSupport.Unknown;
    /// <summary>Gets or sets the page.</summary>
    public RelationalPushdownSupport Page { get; init; } = RelationalPushdownSupport.Unknown;
    /// <summary>Gets or sets the count.</summary>
    public RelationalPushdownSupport Count { get; init; } = RelationalPushdownSupport.Unknown;
    /// <summary>Gets or sets the select.</summary>
    public RelationalPushdownSupport Select { get; init; } = RelationalPushdownSupport.Unknown;
    /// <summary>Gets or sets the include.</summary>
    public RelationalPushdownSupport Include { get; init; } = RelationalPushdownSupport.Unknown;
    /// <summary>Gets or sets the policy.</summary>
    public RelationalPushdownSupport Policy { get; init; } = RelationalPushdownSupport.Unknown;
    /// <summary>Gets or sets the complete before observable artifacts.</summary>
    public bool CompleteBeforeObservableArtifacts { get; init; }
    /// <summary>Gets or sets the unsupported parts.</summary>
    public string[]? UnsupportedParts { get; init; }
    /// <summary>Gets or sets the unsafe reasons.</summary>
    public string[]? UnsafeReasons { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational residual descriptor.</summary>
public sealed record RelationalResidualDescriptor
{
    /// <summary>Gets or sets the kind.</summary>
    public RelationalResidualKind Kind { get; init; } = RelationalResidualKind.Unknown;
    /// <summary>Gets or sets the required.</summary>
    public bool Required { get; init; }
    /// <summary>Gets or sets the runs before page.</summary>
    public bool RunsBeforePage { get; init; }
    /// <summary>Gets or sets the runs before count.</summary>
    public bool RunsBeforeCount { get; init; }
    /// <summary>Gets or sets the safe for requested context.</summary>
    public bool SafeForRequestedContext { get; init; }
    /// <summary>Gets or sets the affected parts.</summary>
    public string[]? AffectedParts { get; init; }
    /// <summary>Gets or sets the unsafe reasons.</summary>
    public string[]? UnsafeReasons { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational count plan descriptor.</summary>
public sealed record RelationalCountPlanDescriptor
{
    /// <summary>Gets or sets the requested.</summary>
    public bool Requested { get; init; }
    /// <summary>Gets or sets the mode.</summary>
    public QueryCountMode Mode { get; init; } = QueryCountMode.IfAvailable;
    /// <summary>Gets or sets the exact candidate set.</summary>
    public bool ExactCandidateSet { get; init; }
    /// <summary>Gets or sets the estimated.</summary>
    public bool Estimated { get; init; }
    /// <summary>Gets or sets the limited.</summary>
    public bool Limited { get; init; }
    /// <summary>Gets or sets the limit.</summary>
    public int? Limit { get; init; }
    /// <summary>Gets or sets the safe for requested context.</summary>
    public bool SafeForRequestedContext { get; init; }
    /// <summary>Gets or sets the unsafe reasons.</summary>
    public string[]? UnsafeReasons { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational page plan descriptor.</summary>
public sealed record RelationalPagePlanDescriptor
{
    /// <summary>Gets or sets the requested.</summary>
    public bool Requested { get; init; }
    /// <summary>Gets or sets the mode.</summary>
    public QueryPaginationMode Mode { get; init; } = QueryPaginationMode.Page;
    /// <summary>Gets or sets the page applied after all required filters.</summary>
    public bool PageAppliedAfterAllRequiredFilters { get; init; }
    /// <summary>Gets or sets the cursor binds policy context.</summary>
    public bool CursorBindsPolicyContext { get; init; }
    /// <summary>Gets or sets the bounded accessible page proof.</summary>
    public bool BoundedAccessiblePageProof { get; init; }
    /// <summary>Gets or sets the safe for requested context.</summary>
    public bool SafeForRequestedContext { get; init; }
    /// <summary>Gets or sets the unsafe reasons.</summary>
    public string[]? UnsafeReasons { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational sort plan descriptor.</summary>
public sealed record RelationalSortPlanDescriptor
{
    /// <summary>Gets or sets the requested.</summary>
    public bool Requested { get; init; }
    /// <summary>Gets or sets the complete before page.</summary>
    public bool CompleteBeforePage { get; init; }
    /// <summary>Gets or sets the sort keys visible or authorized.</summary>
    public bool SortKeysVisibleOrAuthorized { get; init; }
    /// <summary>Gets or sets the safe for requested context.</summary>
    public bool SafeForRequestedContext { get; init; }
    /// <summary>Gets or sets the unsupported parts.</summary>
    public string[]? UnsupportedParts { get; init; }
    /// <summary>Gets or sets the unsafe reasons.</summary>
    public string[]? UnsafeReasons { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational include plan descriptor.</summary>
public sealed record RelationalIncludePlanDescriptor
{
    /// <summary>Gets or sets the path.</summary>
    public required string Path { get; init; }
    /// <summary>Gets or sets the status.</summary>
    public RelationalQueryPlanStatus Status { get; init; } = RelationalQueryPlanStatus.Unsupported;
    /// <summary>Gets or sets the executable for requested context.</summary>
    public bool ExecutableForRequestedContext { get; init; }
    /// <summary>Gets or sets the safe for requested context.</summary>
    public bool SafeForRequestedContext { get; init; }
    /// <summary>Gets or sets the unsupported parts.</summary>
    public string[]? UnsupportedParts { get; init; }
    /// <summary>Gets or sets the unsafe reasons.</summary>
    public string[]? UnsafeReasons { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational plan diagnostic.</summary>
public sealed record RelationalPlanDiagnostic
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the severity.</summary>
    public RelationalPlanDiagnosticSeverity Severity { get; init; } = RelationalPlanDiagnosticSeverity.Info;
    /// <summary>Gets or sets the code.</summary>
    public required string Code { get; init; }
    /// <summary>Gets or sets the message.</summary>
    public required string Message { get; init; }
    /// <summary>Gets or sets the query part.</summary>
    public string? QueryPart { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
