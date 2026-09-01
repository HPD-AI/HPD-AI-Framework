using System.Collections.Concurrent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Projects the unified subagent action surface for the current parent and iteration.</summary>
internal sealed class SubAgentAvailabilityMiddleware : IAgentMiddleware
{
    private readonly IReadOnlyList<SubAgentActionDescriptor> _actions;
    private readonly bool _toolHarnessActivationEnabled;
    private readonly HashSet<string> _neverCollapse;
    private readonly ConcurrentDictionary<ProjectionKey, AIFunction?> _cache = new();

    /// <summary>Captures immutable role descriptors; functions are composed per parent revision.</summary>
    public SubAgentAvailabilityMiddleware(
        IEnumerable<AITool> allTools,
        bool toolHarnessActivationEnabled,
        IEnumerable<string>? neverCollapse = null)
    {
        ArgumentNullException.ThrowIfNull(allTools);
        _actions = allTools.OfType<AIFunction>()
            .Where(static function => string.Equals(function.Name, SubAgentsFunctionFactory.FunctionName, StringComparison.Ordinal))
            .SelectMany(static function =>
                function.AdditionalProperties.TryGetValue("SubAgentActions", out var value) &&
                value is IReadOnlyList<SubAgentActionDescriptor> actions ? actions : [])
            .ToArray();
        _toolHarnessActivationEnabled = toolHarnessActivationEnabled;
        _neverCollapse = new HashSet<string>(neverCollapse ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
    {
        if (context.Options.Tools is null) return;
        var depth = context.GetParentAgentMetadata()?.Depth ?? 0;
        var maximumDepth = context.Base.Config?.MaxSubAgentDepth ?? 4;
        var expanded = context.GetMiddlewareState<ContainerMiddlewareState>()?.ExpandedContainers
            ?? System.Collections.Immutable.ImmutableHashSet<string>.Empty;
        var available = _actions.Where(action =>
                IsCreationVisible(action, expanded) &&
                depth < maximumDepth && action.Definition.Availability.AllowsInvocationFrom(depth))
            .ToArray();
        long generation = 0;
        long revision = 0;
        var hasRegistryEntries = false;
        if (context.Session?.Store is { } store && context.SessionId is { } sessionId && context.ThreadId is { } threadId)
        {
            var key = new ThreadKey(sessionId, threadId);
            var head = await store.GetThreadEventHeadAsync(key, cancellationToken).ConfigureAwait(false);
            if (head is not null)
            {
                generation = head.Generation;
                revision = head.ThreadSequenceNumber;
                hasRegistryEntries = (await new SubAgentChildRegistry(store)
                    .ProjectAsync(key, head.Cursor, cancellationToken).ConfigureAwait(false)).Children.Count > 0;
            }
        }
        var parent = new ThreadKey(context.SessionId ?? string.Empty, context.ThreadId ?? string.Empty);
        var digest = string.Join('|', available.Select(static action => string.Join(':',
            action.Action,
            action.ParentToolHarness,
            action.RequiresToolHarnessActivation,
            action.Description,
            action.CapabilityId.Value,
            action.Definition.AgentId,
            action.InvocationModePolicy,
            action.InvocationModeHandling,
            action.ContextPolicy,
            action.RequiresPermission,
            action.Definition.Availability.MaximumChildDepth)));
        var projectionKey = new ProjectionKey(parent, generation, revision, depth, digest, 1);
        if (_cache.Count >= 256)
            _cache.Clear();
        var function = _cache.GetOrAdd(projectionKey, _ =>
            available.Length == 0 && !hasRegistryEntries ? null : (AIFunction)SubAgentsFunctionFactory.Create(available));
        context.Options.Tools = context.Options.Tools
            .Where(static tool => tool is not AIFunction function ||
                !string.Equals(function.Name, SubAgentsFunctionFactory.FunctionName, StringComparison.Ordinal))
            .Concat(function is null ? [] : [function])
            .ToList();
    }

    private bool IsCreationVisible(
        SubAgentActionDescriptor action,
        System.Collections.Immutable.ImmutableHashSet<string> expanded) =>
        !_toolHarnessActivationEnabled ||
        !action.RequiresToolHarnessActivation ||
        _neverCollapse.Contains(action.ParentToolHarness) ||
        expanded.Contains(action.ParentToolHarness, StringComparer.OrdinalIgnoreCase);

    private readonly record struct ProjectionKey(
        ThreadKey Parent,
        long Generation,
        long Revision,
        int Depth,
        string AvailabilityDigest,
        int CompositionVersion);
}
