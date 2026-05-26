using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Primitives;
using Rhodium.Simulation.Data;
using Rhodium.Simulation.Frames;
using Rhodium.Simulation.Modules;

namespace Rhodium.Simulation;

/// <summary>
/// Entry-point helpers for fluent simulation construction.
/// </summary>
public static class Rhodium
{
    /// <summary>Create a simulation builder for a strategy type.</summary>
    public static SimulationBuilder<TStrategy> Simulate<TStrategy>()
        where TStrategy : Strategy, new()
        => new();
}

/// <summary>
/// Fluent builder for creating and running a simulation session.
/// </summary>
public sealed class SimulationBuilder<TStrategy>
    where TStrategy : Strategy, new()
{
    private SharedHistory? _history;
    private SimulationDataIterator? _data;
    private ParameterGrid? _grid;
    private Func<ParameterSet, TStrategy>? _gridFactory;
    private SimulationRunOptions _options = new();

    /// <summary>Use materialized shared history as the simulation input.</summary>
    public SimulationBuilder<TStrategy> WithHistory(SharedHistory history)
    {
        _history = history;
        _data = null;
        return this;
    }

    /// <summary>Use a streaming simulation data iterator as the simulation input.</summary>
    public SimulationBuilder<TStrategy> WithData(SimulationDataIterator data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _history = null;
        return this;
    }

    /// <summary>Use a simulation data plan as the simulation input.</summary>
    public SimulationBuilder<TStrategy> WithData(SimulationDataPlan plan)
        => WithData(new SimulationDataIterator(plan));

    internal SimulationBuilder<TStrategy> WithGeneratedGrid(
        ParameterGrid grid,
        Func<ParameterSet, TStrategy> factory)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(factory);

        _grid = grid;
        _gridFactory = factory;
        return this;
    }

    /// <summary>Set the default matching fidelity for venues without overrides.</summary>
    public SimulationBuilder<TStrategy> WithMatchingFidelity(MatchingFidelity fidelity)
    {
        _options = _options with { MatchingFidelity = fidelity };
        return this;
    }

    /// <summary>Set the default simulation config for venues without overrides.</summary>
    public SimulationBuilder<TStrategy> WithConfig(SimulationConfig config)
    {
        _options = _options with { Config = config };
        return this;
    }

    /// <summary>Set the default starting cash for venues without overrides.</summary>
    public SimulationBuilder<TStrategy> WithInitialCash(Money cash)
    {
        _options = _options with { InitialCash = cash };
        return this;
    }

    /// <summary>Add explicit opening account state for the registered strategy or grid variants.</summary>
    public SimulationBuilder<TStrategy> WithAccountSeed(AccountSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        var seeds = CopyAppending(_options.AccountSeeds, seed);
        _options = _options with { AccountSeeds = seeds };
        return this;
    }

    /// <summary>Replace explicit opening account state for the run.</summary>
    public SimulationBuilder<TStrategy> WithAccountSeeds(IReadOnlyList<AccountSeed> seeds)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        _options = _options with { AccountSeeds = CopyList(seeds) };
        return this;
    }

    /// <summary>Add or replace a venue configuration.</summary>
    public SimulationBuilder<TStrategy> WithVenue(SimulationVenueConfig venue)
    {
        var venues = CopyVenueConfigsReplacing(venue);
        _options = _options with { VenueConfigs = venues };
        return this;
    }

    /// <summary>Add or replace a venue configuration from individual values.</summary>
    public SimulationBuilder<TStrategy> WithVenue(
        Venue venue,
        Money? initialCash = null,
        Currency? baseCurrency = null,
        AccountType? accountType = null,
        MatchingFidelity? matchingFidelity = null,
        SimulationConfig? config = null,
        SimulationOrderPolicy? orderPolicy = null,
        SimulationVenuePolicy? simulationPolicy = null)
        => WithVenue(new SimulationVenueConfig
        {
            Venue = venue,
            InitialCash = initialCash,
            BaseCurrency = baseCurrency,
            AccountType = accountType,
            MatchingFidelity = matchingFidelity,
            Config = config,
            OrderPolicy = orderPolicy ?? SimulationOrderPolicy.Default,
            SimulationPolicy = simulationPolicy ?? SimulationVenuePolicy.Default
        });

    /// <summary>Add or replace an instrument configuration under its venue.</summary>
    public SimulationBuilder<TStrategy> WithInstrument(SimulationInstrumentConfig instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);

        var sourceVenues = _options.VenueConfigs;
        var venues = new List<SimulationVenueConfig>(sourceVenues.Count + 1);
        var index = -1;
        for (var i = 0; i < sourceVenues.Count; i++)
        {
            var existing = sourceVenues[i];
            if (existing.Venue == instrument.Instrument.Venue)
                index = venues.Count;
            venues.Add(existing);
        }

        var venue = index >= 0
            ? venues[index]
            : SimulationVenueConfig.For(instrument.Instrument.Venue);
        var sourceInstruments = venue.InstrumentConfigs;
        var instruments = new List<SimulationInstrumentConfig>(sourceInstruments.Count + 1);
        for (var i = 0; i < sourceInstruments.Count; i++)
        {
            var existing = sourceInstruments[i];
            if (existing.Instrument != instrument.Instrument)
                instruments.Add(existing);
        }

        instruments.Add(instrument);
        venue = venue with { InstrumentConfigs = instruments };

        if (index >= 0)
            venues[index] = venue;
        else
            venues.Add(venue);

        _options = _options with { VenueConfigs = venues };
        return this;
    }

    private List<SimulationVenueConfig> CopyVenueConfigsReplacing(SimulationVenueConfig venue)
    {
        var source = _options.VenueConfigs;
        var venues = new List<SimulationVenueConfig>(source.Count + 1);
        for (var i = 0; i < source.Count; i++)
        {
            var existing = source[i];
            if (existing.Venue != venue.Venue)
                venues.Add(existing);
        }

        venues.Add(venue);
        return venues;
    }

    /// <summary>Add a session-scoped simulation module.</summary>
    public SimulationBuilder<TStrategy> WithSessionModule(ISessionSimulationModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var modules = CopyAppending(_options.SessionModules, module);
        _options = _options with { SessionModules = modules };
        return this;
    }

    /// <summary>Add a venue-scoped simulation module.</summary>
    public SimulationBuilder<TStrategy> WithVenueModule(IVenueSimulationModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var modules = CopyAppending(_options.VenueModules, module);
        _options = _options with { VenueModules = modules };
        return this;
    }

    /// <summary>Add an instrument-scoped simulation module.</summary>
    public SimulationBuilder<TStrategy> WithInstrumentModule(IInstrumentSimulationModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var modules = CopyAppending(_options.InstrumentModules, module);
        _options = _options with { InstrumentModules = modules };
        return this;
    }

    private static List<TItem> CopyAppending<TItem>(IReadOnlyList<TItem> source, TItem item)
    {
        var items = new List<TItem>(source.Count + 1);
        for (var i = 0; i < source.Count; i++)
            items.Add(source[i]);
        items.Add(item);
        return items;
    }

    private static List<TItem> CopyList<TItem>(IReadOnlyList<TItem> source)
    {
        var items = new List<TItem>(source.Count);
        for (var i = 0; i < source.Count; i++)
            items.Add(source[i]);
        return items;
    }

    /// <summary>Set whether the simulation emits local struct frames.</summary>
    public SimulationBuilder<TStrategy> WithFrameMode(SimulationFrameMode mode)
    {
        _options = _options with { FrameMode = mode };
        return this;
    }

    /// <summary>Set maximum strategy dispatch parallelism.</summary>
    public SimulationBuilder<TStrategy> WithMaxDegreeOfParallelism(int degree)
    {
        if (degree < 1)
            throw new ArgumentOutOfRangeException(nameof(degree));

        _options = _options with { MaxDegreeOfParallelism = degree };
        return this;
    }

    /// <summary>Run the simulation synchronously.</summary>
    public SimulationResult Run()
        => RunAsync().GetAwaiter().GetResult();

    /// <summary>Run the simulation asynchronously.</summary>
    public async Task<SimulationResult> RunAsync(CancellationToken ct = default)
    {
        using var session = new SimulationSession(new RhodiumRuntime(), defaultConfig: _options.Config);
        if (_grid is null)
        {
            session.RegisterStrategy<TStrategy>();
        }
        else
        {
            var factory = _gridFactory
                ?? throw new InvalidOperationException("Parameter grids require a generated strategy parameter factory.");
            var descriptors = new List<VariantDescriptor>(_grid.Count);
            for (var variantIndex = 0; variantIndex < _grid.Count; variantIndex++)
            {
                var parameters = _grid.GetParametersForVariant(variantIndex);
                var id = session.Strategies.Register(factory(parameters), depth: 0);
                descriptors.Add(new VariantDescriptor(id, variantIndex, parameters));
            }

            session.SetVariantDescriptors(descriptors.ToArray());
        }

        if (_history is not null)
            return session.Run(_history, _options);

        if (_data is not null)
            return await session.RunAsync(_data, _options, ct).ConfigureAwait(false);

        throw new InvalidOperationException("Simulation history or data plan is required.");
    }
}
