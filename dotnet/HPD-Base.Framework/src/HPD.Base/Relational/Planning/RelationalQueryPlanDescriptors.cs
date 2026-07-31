using System.Text.Json;

namespace HPD.Base;

public enum RelationalQueryPlanStatus { Supported, Unsupported, PartiallySupported, Unsafe, Unavailable, ProviderSpecific }
public enum RelationalPushdownSupport { Complete, Partial, None, Unsupported, Unsafe, Unknown }
public enum RelationalResidualKind { None, AfterNativeScanBeforePage, AfterNativeFilterBeforePage, AfterPageUnsafe, AfterCountUnsafe, ClientSideDisallowed, Unknown }
public enum RelationalPlanDiagnosticSeverity { Info, Warning, Error }

public sealed record RelationalQueryPlanRequest
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string CollectionId { get; init; }
    public required RecordQuery Query { get; init; }
    public VisibilityLevel RequestedVisibility { get; init; } = VisibilityLevel.Public;
    public bool RequireExecutablePlan { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalQueryPlanDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string CollectionId { get; init; }
    public RelationalQueryPlanStatus Status { get; init; } = RelationalQueryPlanStatus.Unavailable;
    public bool ExecutableForRequestedContext { get; init; }
    public bool SafeForRequestedContext { get; init; }
    public RelationalQueryPushdownDescriptor? Pushdown { get; init; }
    public RelationalResidualDescriptor? Residual { get; init; }
    public RelationalCountPlanDescriptor? Count { get; init; }
    public RelationalPagePlanDescriptor? Page { get; init; }
    public RelationalSortPlanDescriptor? Sort { get; init; }
    public RelationalIncludePlanDescriptor[]? Includes { get; init; }
    public RelationalPolicyPlanDescriptor? Policy { get; init; }
    public RelationalQueryPlanStageDescriptor[]? Stages { get; init; }
    public string[]? UnsupportedParts { get; init; }
    public string[]? UnsafeReasons { get; init; }
    public RelationalPlanDiagnostic[]? Diagnostics { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalQueryPlanStageDescriptor
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public string? Summary { get; init; }
    public bool Observable { get; init; }
    public bool SafeForRequestedContext { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalQueryPushdownDescriptor
{
    public RelationalPushdownSupport Filter { get; init; } = RelationalPushdownSupport.Unknown;
    public RelationalPushdownSupport Sort { get; init; } = RelationalPushdownSupport.Unknown;
    public RelationalPushdownSupport Page { get; init; } = RelationalPushdownSupport.Unknown;
    public RelationalPushdownSupport Count { get; init; } = RelationalPushdownSupport.Unknown;
    public RelationalPushdownSupport Select { get; init; } = RelationalPushdownSupport.Unknown;
    public RelationalPushdownSupport Include { get; init; } = RelationalPushdownSupport.Unknown;
    public RelationalPushdownSupport Policy { get; init; } = RelationalPushdownSupport.Unknown;
    public bool CompleteBeforeObservableArtifacts { get; init; }
    public string[]? UnsupportedParts { get; init; }
    public string[]? UnsafeReasons { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalResidualDescriptor
{
    public RelationalResidualKind Kind { get; init; } = RelationalResidualKind.Unknown;
    public bool Required { get; init; }
    public bool RunsBeforePage { get; init; }
    public bool RunsBeforeCount { get; init; }
    public bool SafeForRequestedContext { get; init; }
    public string[]? AffectedParts { get; init; }
    public string[]? UnsafeReasons { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalCountPlanDescriptor
{
    public bool Requested { get; init; }
    public QueryCountMode Mode { get; init; } = QueryCountMode.IfAvailable;
    public bool ExactCandidateSet { get; init; }
    public bool Estimated { get; init; }
    public bool Limited { get; init; }
    public int? Limit { get; init; }
    public bool SafeForRequestedContext { get; init; }
    public string[]? UnsafeReasons { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalPagePlanDescriptor
{
    public bool Requested { get; init; }
    public QueryPaginationMode Mode { get; init; } = QueryPaginationMode.Page;
    public bool PageAppliedAfterAllRequiredFilters { get; init; }
    public bool CursorBindsPolicyContext { get; init; }
    public bool BoundedAccessiblePageProof { get; init; }
    public bool SafeForRequestedContext { get; init; }
    public string[]? UnsafeReasons { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalSortPlanDescriptor
{
    public bool Requested { get; init; }
    public bool CompleteBeforePage { get; init; }
    public bool SortKeysVisibleOrAuthorized { get; init; }
    public bool SafeForRequestedContext { get; init; }
    public string[]? UnsupportedParts { get; init; }
    public string[]? UnsafeReasons { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalIncludePlanDescriptor
{
    public required string Path { get; init; }
    public RelationalQueryPlanStatus Status { get; init; } = RelationalQueryPlanStatus.Unsupported;
    public bool ExecutableForRequestedContext { get; init; }
    public bool SafeForRequestedContext { get; init; }
    public string[]? UnsupportedParts { get; init; }
    public string[]? UnsafeReasons { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalPlanDiagnostic
{
    public required string Id { get; init; }
    public RelationalPlanDiagnosticSeverity Severity { get; init; } = RelationalPlanDiagnosticSeverity.Info;
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? QueryPart { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
