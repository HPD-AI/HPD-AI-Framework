using System.Text.Json;

namespace HPD.Base;

/// <summary>Defines the relational policy plan kind contract.</summary>
public enum RelationalPolicyPlanKind { /// <summary>Identifies runtime only.</summary>
RuntimeOnly, /// <summary>Identifies native policy.</summary>
NativePolicy, /// <summary>Identifies translated filter.</summary>
TranslatedFilter, /// <summary>Identifies hybrid.</summary>
Hybrid, /// <summary>Identifies unsupported.</summary>
Unsupported, /// <summary>Identifies unknown.</summary>
Unknown }

/// <summary>Represents a relational policy plan descriptor.</summary>
public sealed record RelationalPolicyPlanDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public RelationalPolicyPlanKind Kind { get; init; } = RelationalPolicyPlanKind.Unknown;
    /// <summary>Gets or sets the policy applied before candidate observation.</summary>
    public bool PolicyAppliedBeforeCandidateObservation { get; init; }
    /// <summary>Gets or sets the native projection used.</summary>
    public bool NativeProjectionUsed { get; init; }
    /// <summary>Gets or sets the runtime residual required.</summary>
    public bool RuntimeResidualRequired { get; init; }
    /// <summary>Gets or sets the safe for requested context.</summary>
    public bool SafeForRequestedContext { get; init; }
    /// <summary>Gets or sets the policy refs.</summary>
    public string[]? PolicyRefs { get; init; }
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
