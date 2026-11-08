using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Agent;

/// <summary>The immutable capability composition shared by every HPD agent host in an application.</summary>
public sealed class HPDApplicationComposition
{
    /// <summary>Creates a complete application composition.</summary>
    public HPDApplicationComposition(
        ProviderComposition providers,
        AgentEventComposition events,
        AgentInputComposition inputs)
    {
        Providers = providers ?? throw new ArgumentNullException(nameof(providers));
        Events = events ?? throw new ArgumentNullException(nameof(events));
        Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
    }

    /// <summary>Gets provider metadata, factories, serializers, and secret aliases.</summary>
    public ProviderComposition Providers { get; }

    /// <summary>Gets the output-event contract composition.</summary>
    public AgentEventComposition Events { get; }

    /// <summary>Gets the semantic-input contract composition.</summary>
    public AgentInputComposition Inputs { get; }
}

/// <summary>Receives the application composition emitted by the HPD source generator.</summary>
/// <remarks>
/// Generated code registers this composition from the consuming application assembly. The host
/// consumes the composition through DI; this type is not an extension point for consumers.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class HPDApplicationCompositionHost
{
    private static readonly object Gate = new();
    private static HPDApplicationComposition? _current;
    private static HPDApplicationComposition? _resolved;
    private static IReadOnlySet<string> _suppressedProviderAssemblies =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Registers the generated application composition once per process.</summary>
    /// <remarks>
    /// The first registered composition wins. Compile-time generation ensures every application
    /// assembly closes provider, event, and input capabilities before its entry point runs.
    /// </remarks>
    public static void Register(HPDApplicationComposition composition)
        => Register(composition, Array.Empty<string>());

    /// <summary>Registers the generated composition and its provider exclusion set.</summary>
    /// <param name="composition">The finalized application composition.</param>
    /// <param name="suppressedProviderAssemblies">
    /// Provider assemblies excluded from automatic contribution merging.
    /// </param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Register(
        HPDApplicationComposition composition,
        IEnumerable<string> suppressedProviderAssemblies)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(suppressedProviderAssemblies);
        lock (Gate)
        {
            if (_current is null)
            {
                _current = composition;
                _suppressedProviderAssemblies = suppressedProviderAssemblies
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .ToHashSet(StringComparer.Ordinal);
            }
            _resolved = null;
        }
    }

    /// <summary>Attempts to resolve the generated application composition.</summary>
    public static bool TryGetApplication(out HPDApplicationComposition composition)
    {
        lock (Gate)
        {
            if (_current is null)
            {
                composition = null!;
                return false;
            }

            _resolved ??= Resolve(_current);
            composition = _resolved;
            return true;
        }
    }

    private static HPDApplicationComposition Resolve(HPDApplicationComposition generated)
    {
        var providers = generated.Providers;
        if (ProviderCompositionHost.Current is { } registeredProviders)
        {
            var generatedOwners = generated.Providers.Fragments
                .Select(static fragment => fragment.OwnerAssembly)
                .Where(static owner => !string.IsNullOrWhiteSpace(owner))
                .ToHashSet(StringComparer.Ordinal);
            var additionalProviders = registeredProviders.Fragments
                .Where(fragment =>
                    (string.IsNullOrWhiteSpace(fragment.OwnerAssembly) ||
                     !generatedOwners.Contains(fragment.OwnerAssembly)) &&
                    (string.IsNullOrWhiteSpace(fragment.OwnerAssembly) ||
                     !_suppressedProviderAssemblies.Contains(fragment.OwnerAssembly)))
                .ToArray();
            if (additionalProviders.Length > 0)
            {
                providers = ProviderComposition.Create(
                    [.. generated.Providers.Fragments, .. additionalProviders]);
            }
        }

        var events = generated.Events;
        if (AgentEventCompositionHost.TryComposeRegisteredModules(out var registeredEvents))
        {
            var knownModules = generated.Events.Fragments
                .Select(static fragment => fragment.ModuleId)
                .ToHashSet(StringComparer.Ordinal);
            var additionalEvents = registeredEvents.Fragments
                .Where(fragment => !knownModules.Contains(fragment.ModuleId))
                .ToArray();
            if (additionalEvents.Length > 0)
                events = AgentEventComposition.Create([.. generated.Events.Fragments, .. additionalEvents]);
        }

        var inputs = generated.Inputs;
        if (AgentInputCompositionHost.TryComposeRegisteredModules(out var registeredInputs))
        {
            var knownModules = generated.Inputs.Fragments
                .Select(static fragment => fragment.ModuleId)
                .ToHashSet(StringComparer.Ordinal);
            var additionalInputs = registeredInputs.Fragments
                .Where(fragment => !knownModules.Contains(fragment.ModuleId))
                .ToArray();
            if (additionalInputs.Length > 0)
                inputs = AgentInputComposition.Create([.. generated.Inputs.Fragments, .. additionalInputs]);
        }

        return ReferenceEquals(providers, generated.Providers) &&
               ReferenceEquals(events, generated.Events) &&
               ReferenceEquals(inputs, generated.Inputs)
            ? generated
            : new HPDApplicationComposition(providers, events, inputs);
    }
}

/// <summary>Collects explicit application capability contributions before DI finalization.</summary>
public sealed class HPDApplicationBuilder
{
    private readonly List<ProviderManifestFragment> _providerFragments = [];
    private readonly List<AgentEventModuleFragment> _eventFragments = [];
    private readonly List<AgentInputModuleFragment> _inputFragments = [];
    private ProviderComposition? _providers;
    private AgentEventComposition? _events;
    private AgentInputComposition? _inputs;
    private bool _built;

    /// <summary>Adds one provider manifest as an application capability.</summary>
    public HPDApplicationBuilder AddProviderModule(ProviderManifestFragment fragment)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(fragment);
        _providerFragments.Add(fragment);
        return this;
    }

    /// <summary>Adds one output-event module as an application capability.</summary>
    public HPDApplicationBuilder AddEventModule(AgentEventModuleFragment fragment)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(fragment);
        _eventFragments.Add(fragment);
        return this;
    }

    /// <summary>Adds one semantic-input module as an application capability.</summary>
    public HPDApplicationBuilder AddInputModule(AgentInputModuleFragment fragment)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(fragment);
        _inputFragments.Add(fragment);
        return this;
    }

    /// <summary>Uses an already-generated provider composition.</summary>
    public HPDApplicationBuilder UseProviderComposition(ProviderComposition composition)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(composition);
        if (_providerFragments.Count > 0)
            throw new InvalidOperationException("Provider fragments and a complete provider composition cannot both be supplied.");
        _providers = composition;
        return this;
    }

    /// <summary>Uses an already-generated output-event composition.</summary>
    public HPDApplicationBuilder UseEventComposition(AgentEventComposition composition)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(composition);
        if (_eventFragments.Count > 0)
            throw new InvalidOperationException("Event fragments and a complete event composition cannot both be supplied.");
        _events = composition;
        return this;
    }

    /// <summary>Uses an already-generated semantic-input composition.</summary>
    public HPDApplicationBuilder UseInputComposition(AgentInputComposition composition)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(composition);
        if (_inputFragments.Count > 0)
            throw new InvalidOperationException("Input fragments and a complete input composition cannot both be supplied.");
        _inputs = composition;
        return this;
    }

    internal HPDApplicationComposition Build()
    {
        if (_built)
            throw new InvalidOperationException("The HPD application composition has already been built.");

        _built = true;
        var generated = HPDApplicationCompositionHost.TryGetApplication(out var application)
            ? application
            : null;
        return new HPDApplicationComposition(
            _providers
                ?? (_providerFragments.Count > 0
                    ? ProviderComposition.Create(_providerFragments)
                    : generated?.Providers ?? ProviderCompositionHost.Current ?? ProviderComposition.Create([])),
            _events
                ?? (_eventFragments.Count > 0
                    ? AgentEventComposition.Create(_eventFragments)
                    : generated?.Events ?? ResolveEventComposition()),
            _inputs
                ?? (_inputFragments.Count > 0
                    ? AgentInputComposition.Create(_inputFragments)
                    : generated?.Inputs ?? AgentInputCompositionHost.ResolveDefault()));
    }

    private static AgentEventComposition ResolveEventComposition()
    {
        if (AgentEventCompositionHost.TryGetDefaultApplication(out var application))
            return application;
        if (AgentEventCompositionHost.TryComposeRegisteredModules(out var modules))
            return modules;

        throw new InvalidOperationException(
            "No agent event composition is available. Add generated event modules to AddHPD(...) or reference the HPD-Agent source generator.");
    }

    private void ThrowIfBuilt()
    {
        if (_built)
            throw new InvalidOperationException("The HPD application composition has already been built.");
    }
}

/// <summary>Registers the HPD application capability composition.</summary>
public static class HPDApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers or extends the application-wide provider, output-event, and input-event composition.
    /// </summary>
    /// <remarks>
    /// The resulting immutable composition is registered in DI and is the only composition
    /// authority used by hosted agents. Use this method to
    /// add or override explicit capabilities. Host registration also invokes the zero-argument
    /// form automatically. When no explicit contributions are supplied, source-generated
    /// provider, event, and input manifests referenced by the application are composed
    /// automatically. The legacy global registries remain a fallback for project-reference and
    /// file-based consumers without the application generator.
    /// </remarks>
    public static IServiceCollection AddHPD(
        this IServiceCollection services,
        Action<HPDApplicationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var descriptor = services.LastOrDefault(static service =>
            service.ServiceType == typeof(HPDApplicationBuilder) &&
            service.ImplementationInstance is HPDApplicationBuilder);
        var builder = descriptor?.ImplementationInstance as HPDApplicationBuilder;
        if (builder is null)
        {
            builder = new HPDApplicationBuilder();
            services.AddSingleton(builder);
        }

        configure?.Invoke(builder);

        services.TryAddSingleton(sp => sp.GetRequiredService<HPDApplicationBuilder>().Build());
        services.TryAddSingleton(sp => sp.GetRequiredService<HPDApplicationComposition>().Providers);
        services.TryAddSingleton(sp => sp.GetRequiredService<HPDApplicationComposition>().Events);
        services.TryAddSingleton(sp => sp.GetRequiredService<HPDApplicationComposition>().Inputs);
        return services;
    }
}
