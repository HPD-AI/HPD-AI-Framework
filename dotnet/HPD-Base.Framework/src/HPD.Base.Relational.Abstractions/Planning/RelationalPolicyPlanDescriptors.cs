using System.Text.Json;
using HPD.Base;

namespace HPD.Base.Relational.Planning;

public enum RelationalPolicyPlanKind { RuntimeOnly, NativePolicy, TranslatedFilter, Hybrid, Unsupported, Unknown }

public sealed record RelationalPolicyPlanDescriptor
{
    public required string Id { get; init; }
    public RelationalPolicyPlanKind Kind { get; init; } = RelationalPolicyPlanKind.Unknown;
    public bool PolicyAppliedBeforeCandidateObservation { get; init; }
    public bool NativeProjectionUsed { get; init; }
    public bool RuntimeResidualRequired { get; init; }
    public bool SafeForRequestedContext { get; init; }
    public string[]? PolicyRefs { get; init; }
    public string[]? UnsupportedParts { get; init; }
    public string[]? UnsafeReasons { get; init; }
    public RelationalPlanDiagnostic[]? Diagnostics { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
