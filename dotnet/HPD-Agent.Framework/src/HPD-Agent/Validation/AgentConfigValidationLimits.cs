namespace HPD.Agent.Validation;

/// <summary>
/// Optional upper limits supplied by an application or host that owns the agent configuration.
/// </summary>
/// <remarks>
/// Core agent configuration validation only enforces structural validity. These limits are
/// null by default so portable agent definitions retain authority over their own operating
/// budgets. Shared or multi-tenant hosts can supply a policy to protect their resources.
/// </remarks>
public sealed record AgentConfigValidationLimits
{
    /// <summary>Gets the maximum accepted agent display-name length.</summary>
    public int? MaximumAgentNameLength { get; init; }

    /// <summary>Gets the maximum accepted agentic function-call turns.</summary>
    public int? MaximumAgenticIterations { get; init; }

    /// <summary>Gets the maximum accepted retries for either model or function calls.</summary>
    public int? MaximumRetries { get; init; }

    /// <summary>Gets the maximum accepted consecutive identical calls.</summary>
    public int? MaximumConsecutiveCalls { get; init; }

    /// <summary>Gets the maximum accepted consecutive errors.</summary>
    public int? MaximumConsecutiveErrors { get; init; }

    /// <summary>Gets the maximum accepted total errors.</summary>
    public int? MaximumTotalErrors { get; init; }

    /// <summary>Gets the maximum accepted executable-policy extension-name length.</summary>
    public int? MaximumExtensionNameLength { get; init; }

    /// <summary>Gets the maximum accepted audio component-instance length.</summary>
    public int? MaximumAudioComponentInstanceLength { get; init; }

    /// <summary>Gets the maximum accepted audio schema-name length.</summary>
    public int? MaximumAudioSchemaLength { get; init; }
}
