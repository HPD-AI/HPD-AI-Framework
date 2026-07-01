using HPD.Base.Policy;
using HPD.Base.Query;
using HPD.Base.Records;

namespace HPD.Base.Runtime.Policy.Admin;

/// <summary>
/// Describes a policy-protected BASE operation to explain without executing a store mutation.
/// </summary>
public sealed record BasePolicyExplainRequest
{
    /// <summary>Gets the simulated operation kind.</summary>
    public required BasePolicyExplainOperation Operation { get; init; }

    /// <summary>Gets the target collection id.</summary>
    public required string CollectionId { get; init; }

    /// <summary>Gets the target record id for record-scoped operations.</summary>
    public string? RecordId { get; init; }

    /// <summary>Gets the query to validate and compose for query explanations.</summary>
    public RecordQuery? Query { get; init; }

    /// <summary>Gets the create request to validate and explain.</summary>
    public RecordCreateRequest? Create { get; init; }

    /// <summary>Gets the patch request to validate and explain.</summary>
    public RecordPatchRequest? Patch { get; init; }

    /// <summary>Gets the replace request to validate and explain.</summary>
    public RecordReplaceRequest? Replace { get; init; }

    /// <summary>Gets the delete request to explain.</summary>
    public RecordDeleteRequest? Delete { get; init; }

    /// <summary>Gets optional redaction and diagnostics controls.</summary>
    public BasePolicyExplainOptions? Options { get; init; }
}

/// <summary>
/// Identifies the BASE operation being explained.
/// </summary>
public enum BasePolicyExplainOperation
{
    /// <summary>Explains collection-level policy.</summary>
    Collection,

    /// <summary>Explains list/query policy.</summary>
    Query,

    /// <summary>Explains get-record policy.</summary>
    Record,

    /// <summary>Explains create policy.</summary>
    Create,

    /// <summary>Explains patch policy.</summary>
    Patch,

    /// <summary>Explains replace policy.</summary>
    Replace,

    /// <summary>Explains delete policy.</summary>
    Delete
}

/// <summary>
/// Controls optional explain response details.
/// </summary>
public sealed record BasePolicyExplainOptions
{
    /// <summary>Gets whether a redacted constraint AST may be returned.</summary>
    public bool IncludeConstraintAst { get; init; }

    /// <summary>Gets whether the response may include a redacted payload shape summary.</summary>
    public bool IncludeRedactedPayloadShape { get; init; }

    /// <summary>Gets whether diagnostic reference ids should be included.</summary>
    public bool IncludeDiagnosticRefs { get; init; } = true;
}

/// <summary>
/// Contains an admin-safe explanation of runtime policy behavior.
/// </summary>
public sealed record BasePolicyExplainResponse
{
    /// <summary>Gets the generated explain id.</summary>
    public required string ExplainId { get; init; }

    /// <summary>Gets the operation that was explained.</summary>
    public required BasePolicyExplainOperation Operation { get; init; }

    /// <summary>Gets the target collection id.</summary>
    public required string CollectionId { get; init; }

    /// <summary>Gets the target record id when one was supplied.</summary>
    public string? RecordId { get; init; }

    /// <summary>Gets the simulated operation outcome.</summary>
    public required BasePolicyExplainOutcome Outcome { get; init; }

    /// <summary>Gets the admin-safe policy decision projection.</summary>
    public BasePolicyExplainDecision? Decision { get; init; }

    /// <summary>Gets runtime work and composition details.</summary>
    public BasePolicyExplainRuntimeSummary? Runtime { get; init; }

    /// <summary>Gets effective policy constraint summaries.</summary>
    public BasePolicyExplainConstraintSummary? Constraints { get; init; }

    /// <summary>Gets response redaction guarantees and shape summaries.</summary>
    public BasePolicyExplainRedactionSummary? Redaction { get; init; }

    /// <summary>Gets admin-safe diagnostic reference ids.</summary>
    public string[]? DiagnosticRefs { get; init; }

    /// <summary>Gets an admin-safe advisory about the explain snapshot.</summary>
    public string? Advisory { get; init; }

    /// <summary>Gets the correlation id associated with the explain request.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Represents the outcome of the simulated target operation.
/// </summary>
public enum BasePolicyExplainOutcome
{
    /// <summary>The simulated operation would be allowed.</summary>
    Allowed,

    /// <summary>The simulated operation would be allowed with constraints.</summary>
    AllowedWithConstraints,

    /// <summary>The simulated operation would be denied by policy.</summary>
    Denied,

    /// <summary>The caller or target operation would be unauthenticated.</summary>
    Unauthenticated,

    /// <summary>The target record or collection was not found.</summary>
    NotFound,

    /// <summary>The target would be presented as not found to a public caller because policy denied it.</summary>
    CloakedNotFound,

    /// <summary>The explain request or simulated payload failed validation.</summary>
    ValidationFailed,

    /// <summary>The simulated operation is not supported by runtime or policy constraints.</summary>
    Unsupported,

    /// <summary>A required capability is unavailable.</summary>
    CapabilityUnavailable,

    /// <summary>A store lookup or runtime dependency failed.</summary>
    StoreError
}

/// <summary>
/// Contains an admin-safe projection of a policy decision.
/// </summary>
public sealed record BasePolicyExplainDecision
{
    /// <summary>Gets the policy effect.</summary>
    public required PolicyEffect Effect { get; init; }

    /// <summary>Gets the policy outcome.</summary>
    public required PolicyOutcome Outcome { get; init; }

    /// <summary>Gets the safe reason code.</summary>
    public string? ReasonCode { get; init; }

    /// <summary>Gets a safe policy message.</summary>
    public string? SafeMessage { get; init; }

    /// <summary>Gets the evaluator id when it is safe to expose.</summary>
    public string? EvaluatorId { get; init; }

    /// <summary>Gets the policy id when it is safe to expose.</summary>
    public string? PolicyId { get; init; }

    /// <summary>Gets the policy version when it is safe to expose.</summary>
    public string? PolicyVersion { get; init; }

    /// <summary>Gets whether the policy reported an admin bypass.</summary>
    public bool AdminBypass { get; init; }

    /// <summary>Gets whether the policy reported a service bypass.</summary>
    public bool ServiceBypass { get; init; }

    /// <summary>Gets opaque matched grant references.</summary>
    public string[]? MatchedGrantRefs { get; init; }

    /// <summary>Gets matched subject kinds without subject identifiers.</summary>
    public string[]? MatchedSubjectKinds { get; init; }
}

/// <summary>
/// Summarizes effective policy constraints without exposing raw values.
/// </summary>
public sealed record BasePolicyExplainConstraintSummary
{
    /// <summary>Gets the record filter constraint summary.</summary>
    public BasePolicyExplainFilterSummary? RecordFilter { get; init; }

    /// <summary>Gets the write check constraint summary.</summary>
    public BasePolicyExplainFilterSummary? WriteCheck { get; init; }

    /// <summary>Gets the read mask summary.</summary>
    public BasePolicyExplainFieldMaskSummary? ReadMask { get; init; }

    /// <summary>Gets the write mask summary.</summary>
    public BasePolicyExplainFieldMaskSummary? WriteMask { get; init; }

    /// <summary>Gets obligation summaries.</summary>
    public BasePolicyExplainObligationSummary[]? Obligations { get; init; }

    /// <summary>Gets constraint tag names and non-sensitive values.</summary>
    public string[]? Tags { get; init; }
}

/// <summary>
/// Summarizes a filter expression with literal values redacted.
/// </summary>
public sealed record BasePolicyExplainFilterSummary
{
    /// <summary>Gets whether the filter is present.</summary>
    public required bool Present { get; init; }

    /// <summary>Gets a redacted text summary.</summary>
    public required string Summary { get; init; }

    /// <summary>Gets a redacted AST when explicitly requested.</summary>
    public FilterExpression? Ast { get; init; }

    /// <summary>Gets whether literal values were redacted.</summary>
    public bool ValuesRedacted { get; init; } = true;

    /// <summary>Gets whether the runtime can evaluate this filter.</summary>
    public bool RuntimeEvaluable { get; init; } = true;
}

/// <summary>
/// Summarizes a field mask without exposing field values.
/// </summary>
public sealed record BasePolicyExplainFieldMaskSummary
{
    /// <summary>Gets the mask mode.</summary>
    public required FieldMaskMode Mode { get; init; }

    /// <summary>Gets included field names.</summary>
    public string[]? Include { get; init; }

    /// <summary>Gets excluded field names.</summary>
    public string[]? Exclude { get; init; }

    /// <summary>Gets whether the mask applies to system fields.</summary>
    public bool AppliesToSystemFields { get; init; }
}

/// <summary>
/// Summarizes a policy obligation without exposing parameters.
/// </summary>
public sealed record BasePolicyExplainObligationSummary
{
    /// <summary>Gets the obligation kind.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the optional safe obligation code.</summary>
    public string? Code { get; init; }

    /// <summary>Gets the enforcement level.</summary>
    public required ObligationEnforcement Enforcement { get; init; }
}

/// <summary>
/// Summarizes runtime work that would happen for the simulated operation.
/// </summary>
public sealed record BasePolicyExplainRuntimeSummary
{
    /// <summary>Gets whether a store mutation was executed.</summary>
    public bool StoreMutationExecuted { get; init; }

    /// <summary>Gets whether an existing record lookup was performed.</summary>
    public bool ExistingRecordLookupPerformed { get; init; }

    /// <summary>Gets whether the existing record was found.</summary>
    public bool ExistingRecordFound { get; init; }

    /// <summary>Gets whether a proposed record was computed.</summary>
    public bool ProposedRecordComputed { get; init; }

    /// <summary>Gets whether a user filter was supplied.</summary>
    public bool UserFilterPresent { get; init; }

    /// <summary>Gets whether a policy filter was present.</summary>
    public bool PolicyFilterPresent { get; init; }

    /// <summary>Gets whether an effective filter was composed.</summary>
    public bool EffectiveFilterComposed { get; init; }

    /// <summary>Gets the redacted user filter summary.</summary>
    public BasePolicyExplainFilterSummary? UserFilter { get; init; }

    /// <summary>Gets the redacted policy filter summary.</summary>
    public BasePolicyExplainFilterSummary? PolicyFilter { get; init; }

    /// <summary>Gets the redacted effective filter summary.</summary>
    public BasePolicyExplainFilterSummary? EffectiveFilter { get; init; }

    /// <summary>Gets whether a public record get would be cloaked as not found.</summary>
    public bool CloakedNotFoundWouldBeReturnedToPublic { get; init; }

    /// <summary>Gets whether hidden fields would be omitted from a normal result.</summary>
    public bool HiddenFieldsWouldBeOmitted { get; init; }

    /// <summary>Gets whether a write check would fail closed because its shape is not runtime-evaluable.</summary>
    public bool WriteCheckUnsupportedByRuntime { get; init; }
}

/// <summary>
/// Summarizes redaction guarantees applied to an explain response.
/// </summary>
public sealed record BasePolicyExplainRedactionSummary
{
    /// <summary>Gets whether payload values were redacted.</summary>
    public bool PayloadValuesRedacted { get; init; } = true;

    /// <summary>Gets whether claims were redacted.</summary>
    public bool ClaimsRedacted { get; init; } = true;

    /// <summary>Gets whether hidden field values were redacted.</summary>
    public bool HiddenFieldValuesRedacted { get; init; } = true;

    /// <summary>Gets whether store internals were redacted.</summary>
    public bool StoreInternalsRedacted { get; init; } = true;

    /// <summary>Gets payload field names omitted from the response.</summary>
    public string[]? OmittedPayloadFields { get; init; }

    /// <summary>Gets safe redaction reason codes.</summary>
    public string[]? RedactionReasons { get; init; }
}
