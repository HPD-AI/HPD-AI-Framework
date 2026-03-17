// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.Logging;

namespace HPD.Agent.Planning;

/// <summary>
/// Extension methods for configuring Plan Mode on AgentBuilder.
/// Plans are automatically persisted to the session via MiddlewareState.
/// </summary>
public static class PlanModeBuilderExtensions
{
    /// <summary>
    /// Enables Plan Mode for the agent.
    /// Plans are automatically persisted across agent runs within the same session.
    /// </summary>
    /// <example>
    /// <code>
    /// var agent = await new AgentBuilder()
    ///     .WithPlanMode()
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithPlanMode(this AgentBuilder builder, Action<PlanModeOptions>? configure = null)
    {
        var options = new PlanModeOptions();
        configure?.Invoke(options);

        if (!options.Enabled)
            return builder;

        var config = new PlanModeConfig
        {
            Enabled = options.Enabled,
            CustomInstructions = options.CustomInstructions
        };

        var toolkit = new AgentPlanToolkit(builder.Logger?.CreateLogger<AgentPlanToolkit>());
        var middleware = new AgentPlanAgentMiddleware(
            config,
            builder.Logger?.CreateLogger<AgentPlanAgentMiddleware>());

        // WithToolkit loads the MiddlewareStateRegistry from this assembly,
        // registering PlanModePersistentStateData for session persistence.
        builder.WithToolkit(toolkit);
        builder.Middlewares.Add(middleware);

        return builder;
    }
}
