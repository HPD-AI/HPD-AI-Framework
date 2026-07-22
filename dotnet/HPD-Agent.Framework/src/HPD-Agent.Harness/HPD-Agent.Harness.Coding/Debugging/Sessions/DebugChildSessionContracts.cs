using System.Text.Json;
using System.Collections.Immutable;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugChildSessionPlan
{
    public required DebugAdapterLaunchPlan LaunchPlan { get; init; }
    public required bool IsAttach { get; init; }
    public required DebugDesiredBreakpointSnapshot Breakpoints { get; init; }
}

/// <summary>
/// Adapter/factory-owned boundary for validating untrusted startDebugging configuration, resolving
/// a fresh authorized plan of the same adapter type, and rediscovering portable breakpoint recipes.
/// </summary>
internal interface IDebugChildSessionPlanFactory
{
    ValueTask<DebugChildSessionPlan> CreateAsync(
        DebugRuntimeBinding runtime,
        DebugTreeAuthorization authorization,
        DebugAdapterLaunchPlan parentPlan,
        string request,
        JsonElement configuration,
        string? outputPresentation,
        DebugDesiredBreakpointSnapshot desiredBreakpoints,
        CancellationToken cancellationToken);
}

internal sealed record DebugValidatedChildConfiguration
{
    public required JsonElement Configuration { get; init; }
    public string? Target { get; init; }
    public string? ProcessId { get; init; }
    public string? EndpointId { get; init; }
}

internal interface IDebugChildConfigurationValidator
{
    ValueTask<DebugValidatedChildConfiguration> ValidateAsync(
        string adapterId,
        string request,
        JsonElement configuration,
        string? outputPresentation,
        CancellationToken cancellationToken);
}

internal interface IDebugChildBreakpointResolver
{
    ValueTask<DebugDesiredBreakpointSnapshot> ComposeAsync(
        DebugDesiredBreakpointSnapshot desired,
        DebugAdapterLaunchPlan parentPlan,
        DebugAdapterLaunchPlan childPlan,
        CancellationToken cancellationToken);
}

internal sealed class PortableDebugChildBreakpointResolver : IDebugChildBreakpointResolver
{
    private readonly bool _instructionReferencesArePortable;
    private readonly Func<DebugDataBreakpointRecipe, CancellationToken, ValueTask<DebugDataBreakpoint?>>? _rediscoverData;

    public PortableDebugChildBreakpointResolver(
        bool instructionReferencesArePortable = false,
        Func<DebugDataBreakpointRecipe, CancellationToken, ValueTask<DebugDataBreakpoint?>>? rediscoverData = null)
    {
        _instructionReferencesArePortable = instructionReferencesArePortable;
        _rediscoverData = rediscoverData;
    }

    public ValueTask<DebugDesiredBreakpointSnapshot> ComposeAsync(
        DebugDesiredBreakpointSnapshot desired,
        DebugAdapterLaunchPlan parentPlan,
        DebugAdapterLaunchPlan childPlan,
        CancellationToken cancellationToken)
    {
        if (_rediscoverData is null && desired.Data.Any(x => x.CanPersist && x.Recipe is not null))
            throw new InvalidOperationException(
                "The adapter has persistent data-breakpoint recipes but no child-session rediscovery provider.");
        var rediscover = _rediscoverData ??
            ((DebugDataBreakpointRecipe _, CancellationToken _) => ValueTask.FromResult<DebugDataBreakpoint?>(null));
        return DebugChildBreakpointComposer.ComposeAsync(
            desired,
            _instructionReferencesArePortable,
            rediscover,
            cancellationToken);
    }
}

/// <summary>
/// Production same-adapter factory path. Adapter-specific validation owns untrusted configuration;
/// breakpoint composition owns portability and rediscovery decisions.
/// </summary>
internal sealed class DebugAdapterChildSessionPlanFactory : IDebugChildSessionPlanFactory
{
    private readonly DebugAdapterDescriptor _descriptor;
    private readonly IDebugAdapterFactory _factory;
    private readonly DebugAdapterResolutionContext _resolution;
    private readonly IDebugChildConfigurationValidator _validator;
    private readonly IDebugChildBreakpointResolver _breakpoints;

    public DebugAdapterChildSessionPlanFactory(
        DebugAdapterDescriptor descriptor,
        IDebugAdapterFactory factory,
        DebugAdapterResolutionContext resolution,
        IDebugChildConfigurationValidator validator,
        IDebugChildBreakpointResolver? breakpoints = null)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _resolution = resolution ?? throw new ArgumentNullException(nameof(resolution));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _breakpoints = breakpoints ?? new PortableDebugChildBreakpointResolver();
    }

    public async ValueTask<DebugChildSessionPlan> CreateAsync(
        DebugRuntimeBinding runtime,
        DebugTreeAuthorization authorization,
        DebugAdapterLaunchPlan parentPlan,
        string request,
        JsonElement configuration,
        string? outputPresentation,
        DebugDesiredBreakpointSnapshot desiredBreakpoints,
        CancellationToken cancellationToken)
    {
        runtime.State.ThrowIfUnavailable();
        authorization.Demand(DebugTreeGrant.ChildSessions);
        authorization.ValidateCurrent(runtime, parentPlan);
        if (!string.Equals(_descriptor.Id, parentPlan.AdapterId, StringComparison.Ordinal))
            throw new InvalidOperationException("The retained child factory does not match the parent adapter.");
        if (request is not ("launch" or "attach"))
            throw new InvalidOperationException("A child request must be launch or attach.");

        var validated = await _validator.ValidateAsync(
            _descriptor.Id, request, configuration.Clone(), outputPresentation, cancellationToken).ConfigureAwait(false);
        if (validated.Configuration.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The validated child configuration must be a JSON object.");
        var resolution = _resolution with
        {
            EnvironmentId = parentPlan.EnvironmentId,
            EnvironmentRevision = parentPlan.EnvironmentRevision,
            PolicyRevision = parentPlan.PolicyRevision,
            EndpointCatalogRevision = parentPlan.EndpointCatalogRevision,
            AuthorizationScope = parentPlan.AuthorizationScope,
            ProcessExecution = runtime.ProcessExecution,
            TrustDecision = parentPlan.TrustDecision
        };
        var isAttach = request == "attach";
        var childPlan = isAttach
            ? await _factory.CreateAttachPlanAsync(_descriptor, new DebugAttachContext
            {
                Resolution = resolution,
                ProcessId = validated.ProcessId,
                EndpointId = validated.EndpointId,
                Configuration = validated.Configuration
            }, cancellationToken).ConfigureAwait(false)
            : await _factory.CreateLaunchPlanAsync(_descriptor, new DebugLaunchContext
            {
                Resolution = resolution,
                Target = validated.Target ?? parentPlan.CanonicalWorkingDirectory,
                Configuration = validated.Configuration
            }, cancellationToken).ConfigureAwait(false);
        authorization.ValidateCurrent(runtime, childPlan);
        var composed = await _breakpoints.ComposeAsync(
            desiredBreakpoints, parentPlan, childPlan, cancellationToken).ConfigureAwait(false);
        return new() { LaunchPlan = childPlan, IsAttach = isAttach, Breakpoints = composed };
    }
}

internal static class DebugChildBreakpointComposer
{
    public static async ValueTask<DebugDesiredBreakpointSnapshot> ComposeAsync(
        DebugDesiredBreakpointSnapshot desired,
        bool instructionReferencesArePortable,
        Func<DebugDataBreakpointRecipe, CancellationToken, ValueTask<DebugDataBreakpoint?>> rediscoverData,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(rediscoverData);
        var data = new List<DebugDataBreakpoint>();
        foreach (var item in desired.Data)
        {
            if (!item.CanPersist || item.Recipe is null) continue;
            var rediscovered = await rediscoverData(item.Recipe, cancellationToken).ConfigureAwait(false);
            if (rediscovered is not null) data.Add(rediscovered with
            {
                Condition = item.Condition,
                HitCondition = item.HitCondition,
                CanPersist = true,
                Recipe = item.Recipe,
                OriginSessionId = null,
                SuspensionEpoch = null
            });
        }
        return new()
        {
            Source = desired.Source,
            Function = desired.Function,
            Exception = desired.Exception,
            Instruction = instructionReferencesArePortable
                ? desired.Instruction.Where(x => x.Portable).ToImmutableArray()
                : [],
            Data = data.ToImmutableArray()
        };
    }
}
