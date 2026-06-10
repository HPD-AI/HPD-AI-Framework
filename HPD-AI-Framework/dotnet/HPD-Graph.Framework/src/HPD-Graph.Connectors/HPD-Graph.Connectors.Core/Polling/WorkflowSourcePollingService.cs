using HPD.Graph.Connectors.Abstractions.Sources;

namespace HPD.Graph.Connectors.Core.Polling;

public interface IWorkflowSourcePollingService
{
    Task<int> PollOnceAsync(CancellationToken ct = default);
}

public interface IWorkflowSourcePollingBackgroundService
{
    Task RunAsync(CancellationToken ct = default);
}

public sealed record WorkflowSourcePollingOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);
    public bool PollImmediately { get; init; } = true;
}

public sealed class WorkflowSourcePollingService : IWorkflowSourcePollingService
{
    private readonly IWorkflowSourceStore _sourceStore;
    private readonly IReadOnlyDictionary<string, IPollingWorkflowSourceProvider> _providers;

    public WorkflowSourcePollingService(
        IWorkflowSourceStore sourceStore,
        IEnumerable<IPollingWorkflowSourceProvider> providers)
    {
        _sourceStore = sourceStore ?? throw new ArgumentNullException(nameof(sourceStore));
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToDictionary(static provider => provider.SourceType, StringComparer.Ordinal);
    }

    public async Task<int> PollOnceAsync(CancellationToken ct = default)
    {
        var sources = await _sourceStore.ListAsync(ct).ConfigureAwait(false);
        var polled = 0;

        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();

            if (!source.Enabled || !_providers.TryGetValue(source.SourceType, out var provider))
            {
                continue;
            }

            var state = await _sourceStore.LoadStateAsync(source.SourceId, ct).ConfigureAwait(false);
            await provider.PollAsync(source, state, ct).ConfigureAwait(false);
            polled++;
        }

        return polled;
    }
}

public sealed class WorkflowSourcePollingBackgroundService : IWorkflowSourcePollingBackgroundService
{
    private readonly IWorkflowSourcePollingService _polling;
    private readonly WorkflowSourcePollingOptions _options;

    public WorkflowSourcePollingBackgroundService(
        IWorkflowSourcePollingService polling,
        WorkflowSourcePollingOptions? options = null)
    {
        _polling = polling ?? throw new ArgumentNullException(nameof(polling));
        _options = options ?? new WorkflowSourcePollingOptions();

        if (_options.Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Polling interval must be positive.");
        }
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (_options.PollImmediately)
        {
            await _polling.PollOnceAsync(ct).ConfigureAwait(false);
        }

        using var timer = new PeriodicTimer(_options.Interval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            await _polling.PollOnceAsync(ct).ConfigureAwait(false);
        }
    }
}
