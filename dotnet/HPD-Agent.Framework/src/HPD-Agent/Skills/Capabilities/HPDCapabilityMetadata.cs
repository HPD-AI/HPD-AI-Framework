using System.Collections.Immutable;

namespace HPD.Agent;

/// <summary>
/// Classifies a capability in the HPD agent capability graph.
/// </summary>
public enum HPDCapabilityKind
{
    /// <summary>An ordinary callable function.</summary>
    Function,
    /// <summary>A progressive skill activation function.</summary>
    SkillActivation,
    /// <summary>A read-only skill resource function.</summary>
    SkillResource,
    /// <summary>An externally executed skill script function.</summary>
    SkillScript,
    /// <summary>A tool-harness activation function.</summary>
    ToolHarnessActivation,
    /// <summary>A subagent capability.</summary>
    SubAgent,
    /// <summary>A multi-agent capability.</summary>
    MultiAgent,
    /// <summary>An MCP capability.</summary>
    Mcp,
    /// <summary>An OpenAPI capability.</summary>
    OpenApi
}

/// <summary>
/// Strongly typed metadata attached to every HPD-managed <c>AIFunction</c>.
/// </summary>
public sealed record HPDCapabilityMetadata
{
    /// <summary>The single additional-properties key used for HPD capability metadata.</summary>
    public const string AdditionalPropertiesKey = "HPD.Capability";

    /// <summary>Gets the stable capability identifier.</summary>
    public required CapabilityId Id { get; init; }

    /// <summary>Gets the capability classification.</summary>
    public required HPDCapabilityKind Kind { get; init; }

    /// <summary>
    /// Gets the source declaration member name when the capability was generated from C#.
    /// This remains stable when the model-visible function name is customized.
    /// </summary>
    public string? DeclarationMemberName { get; init; }

    /// <summary>
    /// Gets the alternative parent containers that may reveal this capability.
    /// The capability is visible when any authorized parent path is active.
    /// </summary>
    public ImmutableArray<CapabilityId> ParentContainerIds { get; init; } = [];

    /// <summary>Gets the capabilities directly revealed by activating this container.</summary>
    public ImmutableArray<CapabilityId> Reveals { get; init; } = [];
}
