using System.Collections.Immutable;

namespace HPD.AI.Platform.Studio;

/// <summary>Owns the exact executable Studio Runtime map for the finalized application graph.</summary>
public sealed class BaseStudioRuntimeCatalog
{
    private readonly ImmutableDictionary<string, BaseStudioProducerBinding> _producers;
    /// <summary>Creates and validates all installed module Runtime contributions.</summary>
    public BaseStudioRuntimeCatalog(BaseStudioApplicationGraphProvider graphProvider,
        IEnumerable<IBaseStudioModuleRuntimeContributionFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(graphProvider); ArgumentNullException.ThrowIfNull(factories);
        BaseStudioApplicationGraph graph = graphProvider.GetRequiredGraph();
        var factoryList = factories.ToArray();
        var contributions = ImmutableArray.CreateBuilder<BaseStudioModuleRuntimeContribution>(graph.Modules.Length);
        foreach (BaseStudioModuleRegistration module in graph.Modules)
        {
            IBaseStudioModuleRuntimeContributionFactory? factory = factoryList.SingleOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.ModuleId, module.Identity.ModuleId));
            if (factory is null && module.Views.IsEmpty && module.Resources.IsEmpty && module.Commands.IsEmpty)
                continue;
            if (factory is null)
                throw new InvalidOperationException($"Studio module '{module.Identity.ModuleId}' has no exact Runtime contribution.");
            BaseStudioModuleRuntimeContribution contribution = factory.Create(module);
            if (!StringComparer.Ordinal.Equals(contribution.ModuleId, module.Identity.ModuleId) || contribution.Version != module.Identity.Version ||
                !BaseStudioSha256.FixedTimeEquals(contribution.RegistrationChecksum, module.Identity.Checksum))
                throw new InvalidOperationException("A Studio Runtime contribution differs from its finalized module.");
            contributions.Add(contribution);
        }
        Contributions = contributions.OrderBy(static value => value.ModuleId, StringComparer.Ordinal).ThenBy(static value => value.Version).ToImmutableArray();
        _producers = Contributions.SelectMany(static value => value.Producers)
            .ToImmutableDictionary(static value => value.RegisteredMethodId, StringComparer.Ordinal);
    }
    /// <summary>Gets contributions in canonical module identity order.</summary>
    public ImmutableArray<BaseStudioModuleRuntimeContribution> Contributions { get; }
    /// <summary>Resolves one exact registered producer without accepting a caller-supplied producer kind.</summary>
    public bool TryGetProducer(string methodId, out BaseStudioProducerBinding binding)
        => _producers.TryGetValue(methodId, out binding!);
}
