using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Enforces per-capability subagent depth availability on the final model-facing tool list.
/// </summary>
internal sealed class SubAgentAvailabilityMiddleware : IAgentMiddleware
{
    private readonly IReadOnlyList<AIFunction> _allFunctions;

    public SubAgentAvailabilityMiddleware(IEnumerable<AITool> allTools)
    {
        ArgumentNullException.ThrowIfNull(allTools);
        _allFunctions = allTools.OfType<AIFunction>().ToArray();
    }

    /// <inheritdoc />
    public Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
    {
        var tools = context.Options.Tools;
        if (tools is null || tools.Count == 0)
            return Task.CompletedTask;

        var currentDepth = context.GetParentAgentMetadata()?.Depth ?? 0;
        var maximumDepth = context.Base.Config?.MaxSubAgentDepth ?? 4;
        var projected = ContainerFunctionProjection.Project(
            _allFunctions,
            function => IsAvailable(function, currentDepth, maximumDepth));
        var projectedByName = projected.ToDictionary(function => function.Name, StringComparer.OrdinalIgnoreCase);
        context.Options.Tools = tools
            .Select(tool => tool is AIFunction function
                ? projectedByName.GetValueOrDefault(function.Name)
                : tool)
            .Where(tool => tool is not null)
            .Cast<AITool>()
            .ToList();

        return Task.CompletedTask;
    }

    private static bool IsAvailable(AIFunction function, int currentDepth, int maximumDepth)
    {
        if (function.AdditionalProperties?.TryGetValue("IsSubAgent", out var marker) != true || marker is not true)
            return true;

        if (currentDepth >= maximumDepth)
            return false;

        return function.AdditionalProperties.TryGetValue("SubAgentDefinition", out var value) &&
               value is SubAgent definition &&
               definition.Availability.AllowsInvocationFrom(currentDepth);
    }

}
