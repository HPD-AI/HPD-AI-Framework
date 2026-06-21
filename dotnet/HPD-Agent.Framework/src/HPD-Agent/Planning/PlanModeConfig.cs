using System;

namespace HPD.Agent.Planning;

/// <summary>
/// Configuration for Plan Mode - enables agents to create and manage execution plans.
/// </summary>
public class PlanModeConfig
{
    /// <summary>
    /// Whether plan mode is enabled for this agent.
    /// Default is true when configured via WithPlanMode().
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Custom instructions to add to system prompt explaining plan mode usage.
    /// If null, uses default instructions.
    /// </summary>
    public string? CustomInstructions { get; set; }
}
