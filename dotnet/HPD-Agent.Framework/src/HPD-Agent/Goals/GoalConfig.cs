using System.Text.Json.Serialization;

namespace HPD.Agent.Goals;

/// <summary>Installs persistent Goals and defines agent-wide defaults.</summary>
public sealed class GoalConfig
{
    /// <summary>Enables the Goal capability. Does not start the continuous runtime.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Allows explicitly requested conversational Goal creation.</summary>
    public bool AllowModelCreatedGoals { get; set; } = true;
    /// <summary>Limits the complete objective's character count.</summary>
    public int MaximumObjectiveLength { get; set; } = 4_000;
    /// <summary>Requires matching blocker reports across this many distinct consecutive executions.</summary>
    public int RequiredConsecutiveBlockerExecutions { get; set; } = 3;
    /// <summary>Optionally suspends continuation after this many executions without progress.</summary>
    public int? MaximumConsecutiveNoProgressExecutions { get; set; }
    /// <summary>Selects registered runtime policies by stable key.</summary>
    public GoalPolicySelectionConfig Policies { get; set; } = new();

    internal GoalConfig Snapshot() => new()
    {
        Enabled = Enabled,
        AllowModelCreatedGoals = AllowModelCreatedGoals,
        MaximumObjectiveLength = MaximumObjectiveLength,
        RequiredConsecutiveBlockerExecutions = RequiredConsecutiveBlockerExecutions,
        MaximumConsecutiveNoProgressExecutions = MaximumConsecutiveNoProgressExecutions,
        Policies = Policies is null ? null! : new()
        {
            Completion = Policies.Completion, Blocker = Policies.Blocker,
            Continuation = Policies.Continuation, Accounting = Policies.Accounting, Fork = Policies.Fork
        }
    };

    internal IEnumerable<string> Validate()
    {
        if (MaximumObjectiveLength <= 0) yield return "Goals.MaximumObjectiveLength must be positive.";
        if (RequiredConsecutiveBlockerExecutions <= 0) yield return "Goals.RequiredConsecutiveBlockerExecutions must be positive.";
        if (MaximumConsecutiveNoProgressExecutions is <= 0) yield return "Goals.MaximumConsecutiveNoProgressExecutions must be positive when supplied.";
        if (Policies is null) { yield return "Goals.Policies is required."; yield break; }
        foreach (var (name, value) in Policies.Entries())
            if (string.IsNullOrWhiteSpace(value)) yield return $"Goals.Policies.{name} must be a non-empty registered key.";
    }
}

/// <summary>Serializable names of runtime-owned Goal policy implementations.</summary>
public sealed class GoalPolicySelectionConfig
{
    /// <summary>Selects completion verification.</summary>
    public string Completion { get; set; } = "default";
    /// <summary>Selects impasse verification.</summary>
    public string Blocker { get; set; } = "default";
    /// <summary>Selects automatic continuation decisions.</summary>
    public string Continuation { get; set; } = "default";
    /// <summary>Selects compatible provider-usage attribution.</summary>
    public string Accounting { get; set; } = "default";
    /// <summary>Selects thread-fork Goal inheritance.</summary>
    public string Fork { get; set; } = "default";

    internal IEnumerable<(string Name, string Value)> Entries()
    {
        yield return (nameof(Completion), Completion);
        yield return (nameof(Blocker), Blocker);
        yield return (nameof(Continuation), Continuation);
        yield return (nameof(Accounting), Accounting);
        yield return (nameof(Fork), Fork);
    }
}

/// <summary>Restricts Goal actions or overrides policy selection for one captured execution.</summary>
public sealed class GoalRunConfig
{
    /// <summary>Restricts the model's actions without changing the persistent Goal or its context.</summary>
    public GoalToolAccess? ToolAccess { get; set; }
    /// <summary>Overrides selected policy keys; omitted members independently inherit agent defaults.</summary>
    public GoalPolicyOverrideConfig? Policies { get; set; }

    internal GoalRunConfig Snapshot() => new()
    {
        ToolAccess = ToolAccess,
        Policies = Policies is null ? null : new()
        {
            Completion = Policies.Completion, Blocker = Policies.Blocker,
            Continuation = Policies.Continuation, Accounting = Policies.Accounting
        }
    };
}

/// <summary>Permitted model-facing access to the unified Goal function.</summary>
[JsonConverter(typeof(GoalToolAccessJsonConverter))]
public enum GoalToolAccess
{
    /// <summary>All otherwise authorized actions.</summary>
    All,
    /// <summary>Only the get action; mutation is rejected.</summary>
    ReadOnly,
    /// <summary>No Goal function is exposed or admitted.</summary>
    Hidden
}

/// <summary>Nullable per-execution policy keys. Fork policy belongs to agent configuration.</summary>
public sealed class GoalPolicyOverrideConfig
{
    /// <summary>Overrides completion verification.</summary>
    public string? Completion { get; set; }
    /// <summary>Overrides blocker verification.</summary>
    public string? Blocker { get; set; }
    /// <summary>Overrides continuation decisions.</summary>
    public string? Continuation { get; set; }
    /// <summary>Overrides compatible usage attribution.</summary>
    public string? Accounting { get; set; }
}

/// <summary>Configures persistent Goals through the builder's authoritative config graph.</summary>
public static class GoalBuilderExtensions
{
    /// <summary>Enables Goals without creating tools, middleware, policies, or a running runtime.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="configure">Optional updates to the single authoritative Goal configuration.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithGoals(this AgentBuilder builder, Action<GoalConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var config = builder.Config.Goals ?? new GoalConfig();
        config.Enabled = true;
        configure?.Invoke(config);
        builder.Config.Goals = config;
        return builder;
    }
}
