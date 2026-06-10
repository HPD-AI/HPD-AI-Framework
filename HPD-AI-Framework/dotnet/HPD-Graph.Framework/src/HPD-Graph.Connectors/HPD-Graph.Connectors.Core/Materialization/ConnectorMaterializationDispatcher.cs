using System.Runtime.CompilerServices;
using HPD.Events;
using HPD.Graph.Connectors.Abstractions.Materialization;

namespace HPD.Graph.Connectors.Core.Materialization;

public interface IConnectorMaterializationDispatcher
{
    IAsyncEnumerable<Event> MaterializeAsync(
        string materializationType,
        ConnectorMaterializationContext context,
        CancellationToken ct = default);
}

public sealed class ConnectorMaterializationDispatcher : IConnectorMaterializationDispatcher
{
    private readonly IReadOnlyDictionary<string, IConnectorMaterializationProvider> _providers;
    private readonly IConnectorArtifactEventRecorder _recorder;

    public ConnectorMaterializationDispatcher(
        IEnumerable<IConnectorMaterializationProvider> providers,
        IConnectorArtifactEventRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToDictionary(static provider => provider.MaterializationType, StringComparer.Ordinal);
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
    }

    public async IAsyncEnumerable<Event> MaterializeAsync(
        string materializationType,
        ConnectorMaterializationContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materializationType);
        ArgumentNullException.ThrowIfNull(context);

        if (!_providers.TryGetValue(materializationType, out var provider))
        {
            throw new InvalidOperationException($"No connector materialization provider is registered for '{materializationType}'.");
        }

        await foreach (var evt in provider.MaterializeAsync(context, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            await _recorder.RecordAsync(evt, context.Artifacts, ct).ConfigureAwait(false);
            await context.Events.EmitAsync(evt, ct).ConfigureAwait(false);
            yield return evt;
        }
    }
}
