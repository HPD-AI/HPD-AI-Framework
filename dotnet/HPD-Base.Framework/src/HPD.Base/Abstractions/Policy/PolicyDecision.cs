using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Represents a policy decision.</summary>
public sealed record PolicyDecision
{
    /// <summary>Gets or sets the effect.</summary>
    public required PolicyEffect Effect { get; init; }
    /// <summary>Gets or sets the outcome.</summary>
    public required PolicyOutcome Outcome { get; init; }
    /// <summary>Gets or sets the reason code.</summary>
    public string? ReasonCode { get; init; }
    /// <summary>Gets or sets the safe message.</summary>
    public string? SafeMessage { get; init; }
    /// <summary>Gets or sets the constraints.</summary>
    public PolicyConstraints? Constraints { get; init; }
    /// <summary>Gets or sets the obligations.</summary>
    public PolicyObligation[]? Obligations { get; init; }
    /// <summary>Gets or sets the pushdown.</summary>
    public PolicyPushdown? Pushdown { get; init; }
    /// <summary>Gets or sets the audit.</summary>
    public PolicyAuditInfo? Audit { get; init; }

    /// <summary>Executes the allow operation.</summary>
    public static PolicyDecision Allow() => new()
    {
        Effect = PolicyEffect.Allow,
        Outcome = PolicyOutcome.Allowed,
    };

    /// <summary>Executes the abstain operation.</summary>
    public static PolicyDecision Abstain() => new()
    {
        Effect = PolicyEffect.Abstain,
        Outcome = PolicyOutcome.Bypassed,
    };

    /// <summary>Executes the deny operation.</summary>
    public static PolicyDecision Deny(string code, string safeMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);
        return new PolicyDecision
        {
            Effect = PolicyEffect.Deny,
            Outcome = PolicyOutcome.Denied,
            ReasonCode = code,
            SafeMessage = safeMessage,
        };
    }

    /// <summary>Executes the with record filter operation.</summary>
    public PolicyDecision WithRecordFilter(FilterExpression filter)
    {
        EnsureAllow();
        ArgumentNullException.ThrowIfNull(filter);
        return this with
        {
            Outcome = PolicyOutcome.AllowedWithConstraints,
            Constraints = (Constraints ?? new PolicyConstraints()) with
            {
                RecordFilter = filter,
            },
        };
    }

    /// <summary>Executes the with read mask operation.</summary>
    public PolicyDecision WithReadMask(FieldMask mask)
    {
        EnsureAllow();
        ArgumentNullException.ThrowIfNull(mask);
        return this with
        {
            Outcome = PolicyOutcome.AllowedWithConstraints,
            Constraints = (Constraints ?? new PolicyConstraints()) with
            {
                ReadMask = mask,
            },
        };
    }

    /// <summary>Executes the with write check operation.</summary>
    public PolicyDecision WithWriteCheck(FilterExpression check)
    {
        EnsureAllow();
        ArgumentNullException.ThrowIfNull(check);
        return this with
        {
            Outcome = PolicyOutcome.AllowedWithConstraints,
            Constraints = (Constraints ?? new PolicyConstraints()) with
            {
                WriteCheck = check,
            },
        };
    }

    /// <summary>Adds one pre-matching lexical field-influence filter.</summary>
    public PolicyDecision WithTextSearchInfluence(string stableFieldId, FilterExpression filter)
    {
        EnsureAllow(); ArgumentException.ThrowIfNullOrWhiteSpace(stableFieldId); ArgumentNullException.ThrowIfNull(filter);
        ImmutableDictionary<string, FilterExpression> current = Constraints?.TextSearchInfluenceFilters ?? ImmutableDictionary<string, FilterExpression>.Empty.WithComparers(StringComparer.Ordinal);
        return this with { Outcome = PolicyOutcome.AllowedWithConstraints, Constraints = (Constraints ?? new PolicyConstraints()) with { TextSearchInfluenceFilters = current.SetItem(stableFieldId, filter) } };
    }

    private void EnsureAllow()
    {
        if (Effect != PolicyEffect.Allow)
        {
            throw new InvalidOperationException(
                "Policy constraints may only be attached to an allow decision.");
        }
    }
}

/// <summary>Represents a policy constraints.</summary>
public sealed record PolicyConstraints
{
    /// <summary>Gets or sets the record filter.</summary>
    public FilterExpression? RecordFilter { get; init; }
    /// <summary>Gets or sets the write check.</summary>
    public FilterExpression? WriteCheck { get; init; }
    /// <summary>Gets or sets the read mask.</summary>
    public FieldMask? ReadMask { get; init; }
    /// <summary>Gets or sets the write mask.</summary>
    public FieldMask? WriteMask { get; init; }
    /// <summary>Gets or sets the tags.</summary>
    public Dictionary<string, string>? Tags { get; init; }
    /// <summary>Gets pre-matching lexical influence filters keyed by stable field identity.</summary>
    public ImmutableDictionary<string, FilterExpression>? TextSearchInfluenceFilters { get; init; }
}

/// <summary>Represents a field mask.</summary>
public sealed record FieldMask
{
    /// <summary>Gets or sets the mode.</summary>
    public FieldMaskMode Mode { get; init; } = FieldMaskMode.Unspecified;
    /// <summary>Gets or sets the include.</summary>
    public string[]? Include { get; init; }
    /// <summary>Gets or sets the exclude.</summary>
    public string[]? Exclude { get; init; }
    /// <summary>Gets or sets the applies to system fields.</summary>
    public bool AppliesToSystemFields { get; init; }
}

/// <summary>Represents a policy obligation.</summary>
public sealed record PolicyObligation
{
    /// <summary>Gets or sets the kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets or sets the code.</summary>
    public string? Code { get; init; }
    /// <summary>Gets or sets the parameters.</summary>
    public Dictionary<string, string>? Parameters { get; init; }
    /// <summary>Gets or sets the enforcement.</summary>
    public ObligationEnforcement Enforcement { get; init; } = ObligationEnforcement.Required;
}

/// <summary>Represents a policy pushdown.</summary>
public sealed record PolicyPushdown
{
    /// <summary>Gets or sets the mode.</summary>
    public PushdownMode Mode { get; init; } = PushdownMode.None;
    /// <summary>Gets or sets the trust.</summary>
    public PushdownTrust Trust { get; init; } = PushdownTrust.RuntimeEnforced;
    /// <summary>Gets or sets the store policy ref.</summary>
    public string? StorePolicyRef { get; init; }
    /// <summary>Gets or sets the applied constraint IDs.</summary>
    public string[]? AppliedConstraintIds { get; init; }
    /// <summary>Gets or sets the residual constraint IDs.</summary>
    public string[]? ResidualConstraintIds { get; init; }
    /// <summary>Gets or sets the warnings.</summary>
    public string[]? Warnings { get; init; }
}

/// <summary>Represents a policy audit info.</summary>
public sealed record PolicyAuditInfo
{
    /// <summary>Gets or sets the evaluator ID.</summary>
    public string? EvaluatorId { get; init; }
    /// <summary>Gets or sets the policy ID.</summary>
    public string? PolicyId { get; init; }
    /// <summary>Gets or sets the policy version.</summary>
    public string? PolicyVersion { get; init; }
    /// <summary>Gets or sets the matched subjects.</summary>
    public AccessSubject[]? MatchedSubjects { get; init; }
    /// <summary>Gets or sets the matched grant IDs.</summary>
    public string[]? MatchedGrantIds { get; init; }
    /// <summary>Gets or sets the admin bypass.</summary>
    public bool AdminBypass { get; init; }
    /// <summary>Gets or sets the service bypass.</summary>
    public bool ServiceBypass { get; init; }
    /// <summary>Gets or sets the correlation ID.</summary>
    public string? CorrelationId { get; init; }
}
