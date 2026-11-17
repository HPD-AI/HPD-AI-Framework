using System.Text.Json;
using HPD.Agent.Permissions;

namespace HPD.Agent;

/// <summary>Describes one exact callable capability published by the current Agent epoch.</summary>
public sealed record AgentCapabilityInspection
{
    /// <summary>Gets the stable generated or runtime-owned capability identifier.</summary>
    public required CapabilityId Id { get; init; }
    /// <summary>Gets the protocol-neutral capability classification.</summary>
    public required HPDCapabilityKind Kind { get; init; }
    /// <summary>Gets the model-facing function name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the function description.</summary>
    public required string Description { get; init; }
    /// <summary>Gets a detached copy of the exact closed input schema.</summary>
    public required JsonElement InputSchema { get; init; }
    /// <summary>Gets a detached copy of the declared result schema, when one exists.</summary>
    public JsonElement? ReturnSchema { get; init; }
    /// <summary>Gets the canonical composed-schema fingerprint, when one exists.</summary>
    public string? SchemaFingerprint { get; init; }
    /// <summary>Gets the exact function-level invocation policy.</summary>
    public required AgentInvocationModePolicy InvocationModePolicy { get; init; }
    /// <summary>Gets the exact function-level invocation handling policy.</summary>
    public required AgentInvocationModeHandling InvocationModeHandling { get; init; }
    /// <summary>Gets the exact function-level replay policy.</summary>
    public required ReplayPolicy ReplayPolicy { get; init; }
    /// <summary>Gets the function-level permission declaration, when declared.</summary>
    public AIFunctionPermissionDeclaration? Permission { get; init; }
    /// <summary>Gets the exact serialized action-union argument name.</summary>
    public string? ActionArgumentName { get; init; }
    /// <summary>Gets the exact action discriminator property name.</summary>
    public string? ActionDiscriminator { get; init; }
    /// <summary>Gets every statically declared action branch.</summary>
    public IReadOnlyList<AgentFunctionActionInspection> Actions { get; init; } = [];
    /// <summary>Gets the exact generated owner harness identity.</summary>
    public string? OwnerHarnessIdentity { get; init; }
    /// <summary>Gets the harness that originally declared this capability contract.</summary>
    public string? DeclaringHarnessIdentity { get; init; }
    /// <summary>Gets the exact generated declaration member.</summary>
    public string? DeclarationMemberName { get; init; }
    /// <summary>Gets the selected implementation member, including override dispatch.</summary>
    public string? ImplementationIdentity { get; init; }
    /// <summary>Gets every alternative parent capability container.</summary>
    public IReadOnlyList<CapabilityId> ParentContainerIds { get; init; } = [];
    /// <summary>Gets every capability directly revealed by this node.</summary>
    public IReadOnlyList<CapabilityId> RevealedCapabilityIds { get; init; } = [];
    /// <summary>Gets bounded parameter descriptions declared by the function.</summary>
    public IReadOnlyDictionary<string, string> ParameterDescriptions { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    /// <summary>Gets whether this function creates or controls durable subagents.</summary>
    public bool IsSubAgent { get; init; }
    /// <summary>Gets the declared child execution model.</summary>
    public string? ExecutionModel { get; init; }
}

/// <summary>Describes one immutable discriminator branch inside an action-contracted function.</summary>
public sealed record AgentFunctionActionInspection
{
    /// <summary>Gets the exact case-sensitive action discriminator.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the branch description when the closed schema supplies one.</summary>
    public string Description { get; init; } = "";
    /// <summary>Gets a detached copy of the exact closed action branch schema.</summary>
    public required JsonElement InputSchema { get; init; }
    /// <summary>Gets the resolved action invocation-mode policy.</summary>
    public required AgentInvocationModePolicy InvocationModePolicy { get; init; }
    /// <summary>Gets the resolved action invocation handling policy.</summary>
    public required AgentInvocationModeHandling InvocationModeHandling { get; init; }
    /// <summary>Gets the resolved action replay policy.</summary>
    public required ReplayPolicy ReplayPolicy { get; init; }
    /// <summary>Gets the complete resolved action permission declaration.</summary>
    public required AIFunctionPermissionDeclaration Permission { get; init; }
}

/// <summary>Contains the complete capability set published by one Agent runtime epoch.</summary>
public sealed record AgentCapabilityInspectionSnapshot
{
    /// <summary>Gets the immutable capability-catalog epoch.</summary>
    public required long Epoch { get; init; }
    /// <summary>Gets the exact published functions.</summary>
    public required IReadOnlyList<AgentCapabilityInspection> Functions { get; init; }
}
