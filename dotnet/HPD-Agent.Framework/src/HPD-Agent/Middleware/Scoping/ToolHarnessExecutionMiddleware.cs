using System.Collections.Immutable;
using System.Text.Json;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HPD.Agent;

/// <summary>
/// Owns accepted-input ToolHarness execution independently of container visibility.
/// </summary>
public sealed class ToolHarnessExecutionMiddleware : IAgentMiddleware
{
    private readonly IReadOnlyDictionary<string, ToolHarnessFactory> _factories;
    private readonly IReadOnlyDictionary<string, int> _registrationOrder;
    private readonly IReadOnlyDictionary<string, Dictionary<Type, ToolHarnessMiddlewareFactory>>? _middlewareOverrides;
    private readonly IReadOnlyDictionary<string, Dictionary<string, JsonElement>>? _middlewareConfigs;
    private readonly ILogger<ToolHarnessExecutionMiddleware>? _logger;
    private Dictionary<string, string> _itemToToolHarnessMap;
    private Dictionary<string, IReadOnlyList<string>> _revealedFunctions;

    internal ToolHarnessExecutionMiddleware(
        IList<AITool> initialTools,
        IReadOnlyDictionary<string, ToolHarnessFactory> factories,
        IReadOnlyDictionary<string, Dictionary<Type, ToolHarnessMiddlewareFactory>>? middlewareOverrides = null,
        IReadOnlyDictionary<string, Dictionary<string, JsonElement>>? middlewareConfigs = null,
        ILogger<ToolHarnessExecutionMiddleware>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(initialTools);
        ArgumentNullException.ThrowIfNull(factories);
        _factories = factories;
        _middlewareOverrides = middlewareOverrides;
        _middlewareConfigs = middlewareConfigs;
        _logger = logger;
        _registrationOrder = factories.Values
            .SelectMany(static (factory, ordinal) => new[]
            {
                new KeyValuePair<string, int>(factory.Name, ordinal),
                new KeyValuePair<string, int>(factory.ActivationIdentity, ordinal)
            })
            .GroupBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().Value, StringComparer.Ordinal);
        _itemToToolHarnessMap = BuildItemToToolHarnessMap(initialTools);
        _revealedFunctions = BuildRevealedFunctions(initialTools);
    }

    internal void ReplaceCapabilityFunctions(IEnumerable<AIFunction> functions)
    {
        ArgumentNullException.ThrowIfNull(functions);
        var refreshed = functions.Cast<AITool>().ToList();
        _itemToToolHarnessMap = BuildItemToToolHarnessMap(refreshed);
        _revealedFunctions = BuildRevealedFunctions(refreshed);
    }

    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        await EnsureActiveAsync(context, context.RunConfig, cancellationToken).ConfigureAwait(false);
        foreach (var harnessName in ActiveHarnessesInRegistrationOrder(context.Base))
        {
            await using var lease = await AcquireActiveAsync(
                context,
                harnessName,
                context.RunConfig,
                cancellationToken).ConfigureAwait(false);
            if (lease is not null)
                await lease.Pipeline.DispatchBeforeMessageTurnAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task BeforeIterationAsync(
        BeforeIterationContext context,
        CancellationToken cancellationToken)
    {
        await EnsureActiveAsync(context, context.RunConfig, cancellationToken).ConfigureAwait(false);
        foreach (var harnessName in ActiveHarnessesInRegistrationOrder(context.Base))
        {
            await using var lease = await AcquireActiveAsync(
                context,
                harnessName,
                context.RunConfig,
                cancellationToken).ConfigureAwait(false);
            if (lease is not null)
                await lease.Pipeline.DispatchBeforeIterationAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task BeforeToolExecutionAsync(
        BeforeToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        await EnsureActiveAsync(context, context.RunConfig, cancellationToken).ConfigureAwait(false);
        foreach (var harnessName in ActiveHarnessesInRegistrationOrder(context.Base))
        {
            await using var lease = await AcquireActiveAsync(
                context,
                harnessName,
                context.RunConfig,
                cancellationToken).ConfigureAwait(false);
            if (lease is not null)
                await lease.Pipeline.DispatchBeforeToolExecutionAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task BeforeParallelBatchAsync(
        BeforeParallelBatchContext context,
        CancellationToken cancellationToken)
    {
        var owners = context.ParallelFunctions
            .Select(static function => function.FunctionName)
            .Select(name => _itemToToolHarnessMap.TryGetValue(name, out var owner) ? owner : null)
            .Where(static owner => owner is not null)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(owner => _registrationOrder.GetValueOrDefault(owner!, int.MaxValue))
            .ThenBy(static owner => owner, StringComparer.Ordinal);

        foreach (var harnessName in owners)
        {
            await using var lease = await AcquireActiveAsync(
                context,
                harnessName!,
                context.RunConfig,
                cancellationToken).ConfigureAwait(false);
            if (lease is not null)
                await lease.Pipeline.DispatchBeforeParallelBatchAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task BeforeFunctionAsync(
        BeforeFunctionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Function is null ||
            !_itemToToolHarnessMap.TryGetValue(context.Function.Name, out var harnessName))
            return;

        await using var lease = await AcquireActiveAsync(
            context,
            harnessName,
            context.RunConfig,
            cancellationToken).ConfigureAwait(false);
        if (lease is not null)
            await lease.Pipeline.DispatchBeforeFunctionAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<object?> WrapFunctionCallAsync(
        FunctionRequest request,
        Func<FunctionRequest, Task<object?>> handler,
        CancellationToken cancellationToken)
    {
        if (!_itemToToolHarnessMap.TryGetValue(request.FunctionName, out var harnessName))
            return await handler(request).ConfigureAwait(false);

        var execution = request.ExecutionContext ??
            throw new InvalidOperationException($"ToolHarness '{harnessName}' function dispatch has no accepted-input execution authority.");
        await using var lease = await AcquireActiveAsync(
            execution,
            harnessName,
            request.RunConfig,
            cancellationToken).ConfigureAwait(false);
        return lease is null
            ? await handler(request).ConfigureAwait(false)
            : await lease.Pipeline.ExecuteFunctionCallAsync(request, handler, cancellationToken).ConfigureAwait(false);
    }

    public async Task AfterFunctionAsync(
        AfterFunctionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Function is null ||
            !_itemToToolHarnessMap.TryGetValue(context.Function.Name, out var harnessName))
            return;

        await using var lease = await AcquireActiveAsync(
            context,
            harnessName,
            context.RunConfig,
            cancellationToken).ConfigureAwait(false);
        if (lease is not null)
            await lease.Pipeline.DispatchAfterFunctionAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public async Task AfterIterationAsync(
        AfterIterationContext context,
        CancellationToken cancellationToken)
    {
        foreach (var harnessName in ActiveHarnessesInRegistrationOrder(context.Base).Reverse())
        {
            await using var lease = await AcquireActiveAsync(
                context,
                harnessName,
                context.RunConfig,
                cancellationToken).ConfigureAwait(false);
            if (lease is not null)
                await lease.Pipeline.DispatchAfterIterationAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task AfterMessageTurnAsync(
        AfterMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        foreach (var harnessName in ActiveHarnessesInRegistrationOrder(context.Base).Reverse())
        {
            await using var lease = await AcquireActiveAsync(
                context,
                harnessName,
                context.RunConfig,
                cancellationToken).ConfigureAwait(false);
            if (lease is not null)
                await lease.Pipeline.DispatchAfterMessageTurnAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task OnErrorAsync(
        ErrorContext context,
        CancellationToken cancellationToken)
    {
        foreach (var harnessName in ActiveHarnessesInRegistrationOrder(context.Base).Reverse())
        {
            await using var lease = await AcquireActiveAsync(
                context,
                harnessName,
                context.RunConfig ?? new AgentRunConfig(),
                cancellationToken).ConfigureAwait(false);
            if (lease is not null)
                await lease.Pipeline.DispatchOnErrorAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task ActivateAndCommitExpansionAsync(
        BeforeToolExecutionContext context,
        IReadOnlyCollection<string> containerNames,
        IReadOnlyDictionary<string, RecoveryInfo> recoveredCalls,
        IReadOnlyDictionary<string, string> activationCalls,
        Action commitExpansion,
        CancellationToken cancellationToken)
    {
        var activated = new List<(string Name, ToolHarnessPipelineLease Lease)>();
        try
        {
            foreach (var containerName in containerNames.Order(StringComparer.Ordinal))
            {
                var lease = await AcquireForExpansionAsync(
                    context,
                    containerName,
                    context.RunConfig,
                    cancellationToken).ConfigureAwait(false);
                if (lease is not null) activated.Add((containerName, lease));
            }

            commitExpansion();

            foreach (var (containerName, lease) in activated)
            {
                var recovery = recoveredCalls.FirstOrDefault(pair =>
                    string.Equals(pair.Value.ContainerName, containerName, StringComparison.Ordinal));
                var activation = activationCalls.FirstOrDefault(pair =>
                    string.Equals(pair.Value, containerName, StringComparison.Ordinal));
                var reason = recovery.Value?.Type switch
                {
                    RecoveryType.HiddenItem => ToolHarnessExpansionReason.HiddenFunctionRecovery,
                    RecoveryType.QualifiedName => ToolHarnessExpansionReason.QualifiedFunctionRecovery,
                    _ => ToolHarnessExpansionReason.ContainerCall
                };
                await NotifyExpandedAsync(
                    lease,
                    context.Base,
                    containerName,
                    reason,
                    recovery.Key ?? activation.Key,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            for (var index = activated.Count - 1; index >= 0; index--)
                await activated[index].Lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask EnsureActiveAsync(
        HookContext context,
        AgentRunConfig runConfig,
        CancellationToken cancellationToken)
    {
        foreach (var factory in _factories.Values)
        {
            if (factory.EagerMiddleware is not { Count: > 0 }) continue;
            await using var lease = await AcquirePolicyAsync(
                context,
                factory.Name,
                eager: true,
                runConfig,
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Base.Config?.Collapsing is { Enabled: false }) return;
        var state = context.GetMiddlewareState<ContainerMiddlewareState>();
        if (state is null) return;

        foreach (var harnessName in state.ExpandedContainers)
        {
            await using var lease = await AcquireForExpansionAsync(
                context,
                harnessName,
                runConfig,
                cancellationToken).ConfigureAwait(false);
            if (lease is null) continue;
            await NotifyExpandedAsync(
                lease,
                context.Base,
                harnessName,
                ToolHarnessExpansionReason.RestoredExpansion,
                expansionCallId: null,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<ToolHarnessPipelineLease?> AcquireForExpansionAsync(
        HookContext context,
        string harnessName,
        AgentRunConfig runConfig,
        CancellationToken cancellationToken)
    {
        var lease = await AcquirePolicyAsync(
            context,
            harnessName,
            eager: false,
            runConfig,
            cancellationToken).ConfigureAwait(false);
        return lease ?? await AcquirePolicyAsync(
            context,
            harnessName,
            eager: true,
            runConfig,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ToolHarnessPipelineLease?> AcquireActiveAsync(
        HookContext context,
        string harnessName,
        AgentRunConfig runConfig,
        CancellationToken cancellationToken)
    {
        var factory = GetFactory(harnessName);
        if (factory is null) return null;
        var eager = factory.EagerMiddleware is { Count: > 0 };
        if (!eager)
        {
            var expandedState = context.GetMiddlewareState<ContainerMiddlewareState>();
            if (expandedState is null ||
                !expandedState.ExpandedContainers.Contains(factory.Name) &&
                !expandedState.ExpandedContainers.Contains(harnessName))
                return null;
        }
        return await AcquirePolicyAsync(
            context,
            harnessName,
            eager,
            runConfig,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ToolHarnessPipelineLease?> AcquireActiveAsync(
        AgentContext context,
        string harnessName,
        AgentRunConfig runConfig,
        CancellationToken cancellationToken)
    {
        var factory = GetFactory(harnessName);
        if (factory is null) return null;
        var eager = factory.EagerMiddleware is { Count: > 0 };
        if (!eager)
        {
            var expandedState = context.State.MiddlewareState.GetState<ContainerMiddlewareState>(
                "HPD.Agent.ContainerMiddlewareState");
            if (expandedState is null ||
                !expandedState.ExpandedContainers.Contains(factory.Name) &&
                !expandedState.ExpandedContainers.Contains(harnessName))
                return null;
        }
        return await AcquirePolicyAsync(
            context,
            harnessName,
            eager,
            runConfig,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ToolHarnessPipelineLease?> AcquirePolicyAsync(
        HookContext context,
        string harnessName,
        bool eager,
        AgentRunConfig runConfig,
        CancellationToken cancellationToken)
        => await AcquirePolicyAsync(context.Base, harnessName, eager, runConfig, cancellationToken).ConfigureAwait(false);

    private async ValueTask<ToolHarnessPipelineLease?> AcquirePolicyAsync(
        AgentContext context,
        string harnessName,
        bool eager,
        AgentRunConfig runConfig,
        CancellationToken cancellationToken)
    {
        var harness = GetEffectiveHarness(harnessName, eager);
        return harness is null
            ? null
            : await AcquireAsync(context, harness, eager, runConfig, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ToolHarnessPipelineLease> AcquireAsync(
        HookContext context,
        ToolHarnessFactory harness,
        bool eager,
        AgentRunConfig runConfig,
        CancellationToken cancellationToken)
        => await AcquireAsync(context.Base, harness, eager, runConfig, cancellationToken).ConfigureAwait(false);

    private async ValueTask<ToolHarnessPipelineLease> AcquireAsync(
        AgentContext context,
        ToolHarnessFactory harness,
        bool eager,
        AgentRunConfig runConfig,
        CancellationToken cancellationToken)
    {
        var registry = context.ToolHarnessPipelines ??
            throw new InvalidOperationException($"ToolHarness '{harness.ActivationIdentity}' dispatch has no accepted-input execution registry.");
        var activationContext = CreateActivationContext(context, harness, runConfig);
        return eager
            ? await registry.AcquireEagerAsync(harness, activationContext, cancellationToken).ConfigureAwait(false)
            : await registry.AcquireDeferredAsync(harness, activationContext, cancellationToken).ConfigureAwait(false);
    }

    private ToolHarnessFactory? GetEffectiveHarness(string harnessName, bool eager)
    {
        var factory = GetFactory(harnessName);
        if (factory is null) return null;
        var declared = eager ? factory.EagerMiddleware : factory.Middleware;
        if (declared is not { Count: > 0 }) return null;

        if (_middlewareOverrides is null ||
            !(_middlewareOverrides.TryGetValue(factory.ActivationIdentity, out var overrides) ||
              _middlewareOverrides.TryGetValue(factory.Name, out overrides)) ||
            overrides.Count == 0)
        {
            return factory with { Middleware = declared };
        }

        return factory with
        {
            Middleware = declared.Select(descriptor =>
                overrides.TryGetValue(descriptor.MiddlewareType, out var replacement)
                    ? descriptor with { Factory = replacement }
                    : descriptor).ToArray()
        };
    }

    private ToolHarnessFactory? GetFactory(string name)
    {
        if (_factories.TryGetValue(name, out var factory)) return factory;
        return _factories.Values.FirstOrDefault(candidate =>
            candidate.ActivationIdentity.Equals(name, StringComparison.Ordinal));
    }

    private ToolHarnessActivationContext CreateActivationContext(
        AgentContext context,
        ToolHarnessFactory harness,
        AgentRunConfig runConfig)
    {
        var configs = new Dictionary<Type, JsonElement>();
        if (_middlewareConfigs is not null &&
            (_middlewareConfigs.TryGetValue(harness.ActivationIdentity, out var configured) ||
             _middlewareConfigs.TryGetValue(harness.Name, out configured)))
        {
            foreach (var descriptor in harness.Middleware ?? [])
            {
                if (descriptor.ConfigurationType is null) continue;
                var match = configured.FirstOrDefault(pair =>
                    descriptor.MiddlewareType.Name.Equals(pair.Key, StringComparison.OrdinalIgnoreCase) ||
                    descriptor.MiddlewareType.FullName?.EndsWith(pair.Key, StringComparison.OrdinalIgnoreCase) == true);
                if (string.IsNullOrEmpty(match.Key)) continue;
                if (descriptor.UsesHarnessConfiguration)
                    throw new AgentRunConfigurationException(
                        "HarnessConfigurationConflict",
                        "middlewareConfigs." + match.Key,
                        "Configure this middleware through its owning harness settings section.");
                configs[descriptor.ConfigurationType] = match.Value;
            }
        }

        return new ToolHarnessActivationContext(
            harness.ActivationIdentity,
            context.ThreadExecutionId ?? context.State.RunId,
            context.Services,
            runConfig,
            agentResources: context.AgentResources,
            configuration: configs,
            sessionId: context.Session?.Id,
            threadId: context.Thread?.Id,
            canonicalWorkspaceIdentity: context.CanonicalWorkspaceIdentity);
    }

    private async ValueTask NotifyExpandedAsync(
        ToolHarnessPipelineLease lease,
        AgentContext context,
        string harnessName,
        ToolHarnessExpansionReason reason,
        string? expansionCallId,
        CancellationToken cancellationToken)
    {
        var harness = GetFactory(harnessName);
        if (harness is null) return;

        try
        {
            await lease.NotifyExpandedAsync(
                new ToolHarnessExpansionContext(
                    harness.ActivationIdentity,
                    context.ThreadExecutionId ?? context.State.RunId,
                    reason,
                    expansionCallId,
                    _revealedFunctions.GetValueOrDefault(harnessName, [])),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.LogError(
                exception,
                "ToolHarness '{Harness}' expansion hook failed after the visibility transition committed.",
                harness.ActivationIdentity);
        }
    }

    private IReadOnlyList<string> ActiveHarnessesInRegistrationOrder(AgentContext context) =>
        context.ToolHarnessPipelines?.GetDiagnosticsSnapshot().Entries
            .Where(static entry => entry.State == ToolHarnessPipelineLifecycleState.Active)
            .Select(static entry => entry.HarnessIdentity)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => _registrationOrder.GetValueOrDefault(name, int.MaxValue))
            .ThenBy(static name => name, StringComparer.Ordinal)
            .ToArray() ?? [];

    private static Dictionary<string, string> BuildItemToToolHarnessMap(IList<AITool> tools)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var function in tools.OfType<AIFunction>())
        {
            string? owner = null;
            if (function.AdditionalProperties?.TryGetValue("ToolHarnessName", out var harness) == true)
                owner = harness as string;
            if (string.IsNullOrWhiteSpace(owner) &&
                function.AdditionalProperties?.TryGetValue("ParentToolHarness", out var parent) == true)
                owner = parent as string;
            if (!string.IsNullOrWhiteSpace(owner))
                map[function.Name] = owner;
        }
        return map;
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildRevealedFunctions(IList<AITool> tools)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var function in tools.OfType<AIFunction>())
        {
            string? owner = null;
            if (function.AdditionalProperties?.TryGetValue("ToolHarnessName", out var harness) == true)
                owner = harness as string;
            if (string.IsNullOrWhiteSpace(owner) &&
                function.AdditionalProperties?.TryGetValue("ParentToolHarness", out var parent) == true)
                owner = parent as string;
            if (string.IsNullOrWhiteSpace(owner) ||
                string.Equals(owner, function.Name, StringComparison.Ordinal))
                continue;

            result[owner] = result.TryGetValue(owner, out var existing)
                ? existing.Append(function.Name).Distinct(StringComparer.Ordinal)
                    .OrderBy(static name => name, StringComparer.Ordinal).ToArray()
                : [function.Name];
        }
        return result;
    }
}
