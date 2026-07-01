using HPD.Base.Policy;
using HPD.Base.Query;
using HPD.Base.Records;

namespace HPD.Base.Runtime.Policy.Admin;

/// <summary>
/// Projects runtime policy objects into admin-safe explain summaries.
/// </summary>
public sealed class BasePolicyExplainRedactor
{
    /// <summary>
    /// Projects a policy decision without exposing raw subjects, claims, or provider internals.
    /// </summary>
    public BasePolicyExplainDecision Decision(PolicyDecision decision) => new()
    {
        Effect = decision.Effect,
        Outcome = decision.Outcome,
        ReasonCode = decision.ReasonCode,
        SafeMessage = decision.SafeMessage,
        EvaluatorId = EmptyToNull(decision.Audit?.EvaluatorId),
        PolicyId = SafePolicyMetadata(decision.Audit?.EvaluatorId, decision.Audit?.PolicyId),
        PolicyVersion = SafePolicyMetadata(decision.Audit?.EvaluatorId, decision.Audit?.PolicyVersion),
        AdminBypass = decision.Audit?.AdminBypass == true,
        ServiceBypass = decision.Audit?.ServiceBypass == true,
        MatchedGrantRefs = NonEmpty(decision.Audit?.MatchedGrantIds),
        MatchedSubjectKinds = NonEmpty(decision.Audit?.MatchedSubjects?
            .Select(static subject => subject.Kind.ToString())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray())
    };

    /// <summary>
    /// Projects constraints without exposing literal filter values.
    /// </summary>
    public BasePolicyExplainConstraintSummary? Constraints(
        PolicyDecision decision,
        bool includeConstraintAst)
    {
        var constraints = decision.Constraints;
        var obligations = decision.Obligations;
        if (constraints is null && obligations is null or { Length: 0 })
        {
            return null;
        }

        return new BasePolicyExplainConstraintSummary
        {
            RecordFilter = Filter(constraints?.RecordFilter, includeConstraintAst),
            WriteCheck = Filter(constraints?.WriteCheck, includeConstraintAst),
            ReadMask = Mask(constraints?.ReadMask),
            WriteMask = Mask(constraints?.WriteMask),
            Obligations = obligations is { Length: > 0 }
                ? obligations.Select(static obligation => new BasePolicyExplainObligationSummary
                {
                    Kind = obligation.Kind,
                    Code = obligation.Code,
                    Enforcement = obligation.Enforcement
                }).ToArray()
                : null,
            Tags = NonEmpty(constraints?.Tags?
                .Select(static tag => tag.Key)
                .Order(StringComparer.Ordinal)
                .ToArray())
        };
    }

    /// <summary>
    /// Projects a filter expression into a redacted summary.
    /// </summary>
    public BasePolicyExplainFilterSummary? Filter(
        FilterExpression? filter,
        bool includeConstraintAst,
        bool runtimeEvaluable = true) =>
        filter is null
            ? null
            : new BasePolicyExplainFilterSummary
            {
                Present = true,
                Summary = Summarize(filter),
                Ast = includeConstraintAst ? Redact(filter) : null,
                ValuesRedacted = true,
                RuntimeEvaluable = runtimeEvaluable
            };

    /// <summary>
    /// Projects a field mask into an admin-safe summary.
    /// </summary>
    public BasePolicyExplainFieldMaskSummary? Mask(FieldMask? mask) =>
        mask is null
            ? null
            : new BasePolicyExplainFieldMaskSummary
            {
                Mode = mask.Mode,
                Include = NonEmpty(mask.Include?.Order(StringComparer.Ordinal).ToArray()),
                Exclude = NonEmpty(mask.Exclude?.Order(StringComparer.Ordinal).ToArray()),
                AppliesToSystemFields = mask.AppliesToSystemFields
            };

    /// <summary>
    /// Projects response redaction guarantees and optional payload field names.
    /// </summary>
    public BasePolicyExplainRedactionSummary Redaction(RecordPayload? payload, bool includePayloadShape) => new()
    {
        PayloadValuesRedacted = true,
        ClaimsRedacted = true,
        HiddenFieldValuesRedacted = true,
        StoreInternalsRedacted = true,
        OmittedPayloadFields = includePayloadShape && payload is not null
            ? NonEmpty(BasePolicyRuntimeSimulation.PayloadFields(payload).Order(StringComparer.Ordinal).ToArray())
            : null,
        RedactionReasons =
        [
            "payloadValues",
            "claims",
            "hiddenFieldValues",
            "storeInternals"
        ]
    };

    private static string Summarize(FilterExpression filter)
    {
        return filter.Kind switch
        {
            FilterNodeKind.True => "true",
            FilterNodeKind.False => "false",
            FilterNodeKind.Not => filter.Children is { Length: > 0 }
                ? $"not ({Summarize(filter.Children[0])})"
                : "not <missing>",
            FilterNodeKind.And or FilterNodeKind.Or => filter.Children is { Length: > 0 }
                ? string.Join(filter.Kind == FilterNodeKind.And ? " and " : " or ", filter.Children.Select(Summarize))
                : filter.Kind.ToString().ToLowerInvariant(),
            FilterNodeKind.Compare => $"{filter.Field ?? "<field>"} {filter.Operator.ToString().ToLowerInvariant()} {Placeholder(filter.Value)}",
            FilterNodeKind.In => $"{filter.Field ?? "<field>"} in <redacted:{ValueKind(filter.Values)}>",
            FilterNodeKind.Between => $"{filter.Field ?? "<field>"} between <redacted:{ValueKind(filter.Values)}>",
            FilterNodeKind.IsNull => $"{filter.Field ?? "<field>"} is null",
            FilterNodeKind.IsDefined => $"{filter.Field ?? "<field>"} is defined",
            FilterNodeKind.Extension => $"{filter.ModuleId ?? "extension"}.{filter.Name ?? "filter"}(<redacted>)",
            _ => filter.Kind.ToString()
        };
    }

    private static FilterExpression Redact(FilterExpression filter) => filter with
    {
        Value = filter.Value is null ? null : Redact(filter.Value),
        Values = filter.Values?.Select(Redact).ToArray(),
        Arguments = filter.Arguments?.Select(Redact).ToArray(),
        Children = filter.Children?.Select(Redact).ToArray()
    };

    private static QueryValue Redact(QueryValue value) => new()
    {
        Kind = value.Kind,
        String = value.Kind == QueryValueKind.String ? "<redacted:string>" : null,
        Id = value.Kind == QueryValueKind.Id ? "<redacted:id>" : null,
        Decimal = value.Kind == QueryValueKind.Decimal ? "<redacted:decimal>" : null,
        Boolean = value.Kind == QueryValueKind.Boolean ? false : null,
        Integer = value.Kind == QueryValueKind.Integer ? 0 : null,
        Number = value.Kind == QueryValueKind.Number ? 0 : null,
        DateTime = value.Kind == QueryValueKind.DateTime ? DateTimeOffset.UnixEpoch : null,
        Array = value.Kind == QueryValueKind.Array && value.Array is not null
            ? value.Array.Select(Redact).ToArray()
            : null
    };

    private static string Placeholder(QueryValue? value) =>
        value is null ? "<redacted>" : $"<redacted:{value.Kind.ToString().ToLowerInvariant()}>";

    private static string ValueKind(QueryValue[]? values) =>
        values is { Length: > 0 }
            ? values[0].Kind.ToString().ToLowerInvariant()
            : "value";

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? SafePolicyMetadata(string? evaluatorId, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : evaluatorId?.Contains("hpd-auth", StringComparison.OrdinalIgnoreCase) == true
                || evaluatorId?.Contains("hpd.auth", StringComparison.OrdinalIgnoreCase) == true
                    ? value
                    : null;

    private static string[]? NonEmpty(string[]? values) =>
        values is { Length: > 0 } ? values : null;
}
