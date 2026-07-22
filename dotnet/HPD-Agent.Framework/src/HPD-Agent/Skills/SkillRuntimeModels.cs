/// <summary>Specifies how long a skill activation remains effective.</summary>
public enum SkillActivationLifetime
{
    /// <summary>Activation lasts for one model iteration.</summary>
    ModelIteration,
    /// <summary>Activation lasts for the current user-message turn.</summary>
    MessageTurn,
    /// <summary>Activation lasts for the current session.</summary>
    Session
}

/// <summary>Describes where a skill definition originated and how it can be audited.</summary>
/// <param name="Source">The source kind or source identifier.</param>
/// <param name="PackageId">The package identifier, when installed from a package.</param>
/// <param name="Version">The source or package version.</param>
/// <param name="Publisher">The publisher identity, when known.</param>
/// <param name="Scope">The tenant or installation scope, when applicable.</param>
/// <param name="ContentHash">The source content hash, when available.</param>
public sealed record SkillProvenance(
    string Source,
    string? PackageId = null,
    string? Version = null,
    string? Publisher = null,
    string? Scope = null,
    string? ContentHash = null);

/// <summary>Controls delivery of activated skill instructions.</summary>
public enum SkillInstructionDelivery
{
    /// <summary>Return authoritative instructions only as the activation tool result.</summary>
    ToolResult,
    /// <summary>Return instructions and add declared reinforcement to subsequent system context.</summary>
    ToolResultWithSystemReinforcement
}

/// <summary>Configures runtime skill activation behavior.</summary>
public sealed class SkillRuntimeOptions
{
    /// <summary>Gets or sets how instructions are delivered.</summary>
    public SkillInstructionDelivery InstructionDelivery { get; set; } = SkillInstructionDelivery.ToolResult;

    /// <summary>Gets or sets the default activation lifetime.</summary>
    public SkillActivationLifetime ActivationLifetime { get; set; } = SkillActivationLifetime.MessageTurn;
}
