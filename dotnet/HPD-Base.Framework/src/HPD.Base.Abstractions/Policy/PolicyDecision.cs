using HPD.Base.Query;

namespace HPD.Base.Policy;

public sealed record PolicyDecision
{
    public required PolicyEffect Effect { get; init; }
    public required PolicyOutcome Outcome { get; init; }
    public string? ReasonCode { get; init; }
    public string? SafeMessage { get; init; }
    public PolicyConstraints? Constraints { get; init; }
    public PolicyObligation[]? Obligations { get; init; }
    public PolicyPushdown? Pushdown { get; init; }
    public PolicyAuditInfo? Audit { get; init; }
}

public sealed record PolicyConstraints
{
    public FilterExpression? RecordFilter { get; init; }
    public FilterExpression? WriteCheck { get; init; }
    public FieldMask? ReadMask { get; init; }
    public FieldMask? WriteMask { get; init; }
    public Dictionary<string, string>? Tags { get; init; }
}

public sealed record FieldMask
{
    public FieldMaskMode Mode { get; init; } = FieldMaskMode.Unspecified;
    public string[]? Include { get; init; }
    public string[]? Exclude { get; init; }
    public bool AppliesToSystemFields { get; init; }
}

public sealed record PolicyObligation
{
    public required string Kind { get; init; }
    public string? Code { get; init; }
    public Dictionary<string, string>? Parameters { get; init; }
    public ObligationEnforcement Enforcement { get; init; } = ObligationEnforcement.Required;
}

public sealed record PolicyPushdown
{
    public PushdownMode Mode { get; init; } = PushdownMode.None;
    public PushdownTrust Trust { get; init; } = PushdownTrust.RuntimeEnforced;
    public string? StorePolicyRef { get; init; }
    public string[]? AppliedConstraintIds { get; init; }
    public string[]? ResidualConstraintIds { get; init; }
    public string[]? Warnings { get; init; }
}

public sealed record PolicyAuditInfo
{
    public string? EvaluatorId { get; init; }
    public string? PolicyId { get; init; }
    public string? PolicyVersion { get; init; }
    public AccessSubject[]? MatchedSubjects { get; init; }
    public string[]? MatchedGrantIds { get; init; }
    public bool AdminBypass { get; init; }
    public bool ServiceBypass { get; init; }
    public string? CorrelationId { get; init; }
}
