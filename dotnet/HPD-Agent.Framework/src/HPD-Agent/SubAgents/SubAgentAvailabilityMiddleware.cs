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
        var hiddenNames = _allFunctions
            .Where(function => !IsAvailable(function, currentDepth, maximumDepth))
            .Select(function => function.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        context.Options.Tools = tools
            .Where(tool => tool is not AIFunction function || IsAvailable(function, currentDepth, maximumDepth))
            .Select(tool => tool is AIFunction function
                ? SanitizeContainer(function, hiddenNames)
                : tool)
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

    private static AIFunction SanitizeContainer(AIFunction function, HashSet<string> hiddenNames)
    {
        if (hiddenNames.Count == 0 ||
            function.AdditionalProperties?.TryGetValue("IsContainer", out var container) != true ||
            container is not true)
        {
            return function;
        }

        var properties = new Dictionary<string, object?>(function.AdditionalProperties);
        var changed = FilterNames(properties, "ChildFunctions", hiddenNames) |
                      FilterNames(properties, "ReferencedFunctions", hiddenNames);
        if (!changed)
            return function;

        var activation = function.AdditionalProperties.TryGetValue("IsSkill", out var skill) && skill is true
            ? $"{function.Name} activated."
            : $"{function.Name} expanded.";
        if (properties.TryGetValue("FunctionResult", out var result) && result is string text && !string.IsNullOrWhiteSpace(text))
            activation = $"{activation}\n\n{text}";

        return HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>(activation),
            new HPDAIFunctionFactoryOptions
            {
                Name = function.Name,
                Description = $"{function.Name} provides tools available at the current agent depth.",
                SerializerOptions = function.JsonSerializerOptions,
                ResultType = typeof(string),
                SchemaProvider = () => function.JsonSchema,
                AdditionalProperties = properties
            });
    }

    private static bool FilterNames(
        IDictionary<string, object?> properties,
        string key,
        HashSet<string> hiddenNames)
    {
        if (!properties.TryGetValue(key, out var value) || value is not IEnumerable<string> names)
            return false;

        var original = names.ToArray();
        var filtered = original.Where(name => !hiddenNames.Contains(Unqualify(name))).ToArray();
        if (filtered.Length == original.Length)
            return false;

        properties[key] = filtered;
        return true;
    }

    private static string Unqualify(string name)
    {
        var separator = name.LastIndexOf('.');
        return separator < 0 ? name : name[(separator + 1)..];
    }
}
