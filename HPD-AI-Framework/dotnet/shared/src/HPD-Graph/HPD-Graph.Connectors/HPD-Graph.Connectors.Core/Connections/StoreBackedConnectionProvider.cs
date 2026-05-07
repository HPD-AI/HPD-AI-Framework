using HPDAgent.Graph.Connectors.Abstractions.Connections;

namespace HPDAgent.Graph.Connectors.Core.Connections;

public sealed class StoreBackedConnectionProvider : IConnectionProvider
{
    private readonly IConnectionStore _store;
    private readonly IConnectorSecretResolver? _secretResolver;

    public StoreBackedConnectionProvider(
        IConnectionStore store,
        IConnectorSecretResolver? secretResolver = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _secretResolver = secretResolver;
    }

    public async Task<ResolvedConnection?> ResolveAsync(
        string connectionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var definition = await _store.LoadAsync(connectionId, ct).ConfigureAwait(false);
        if (definition is null)
        {
            return null;
        }

        var secrets = definition.SecretRef is null || _secretResolver is null
            ? new Dictionary<string, string>()
            : await _secretResolver.ResolveAsync(definition.SecretRef, ct).ConfigureAwait(false);

        return new ResolvedConnection
        {
            ConnectionId = definition.ConnectionId,
            ConnectionType = definition.ConnectionType,
            AppId = definition.AppId,
            Config = definition.Config,
            Secrets = secrets
        };
    }
}

public interface IConnectorSecretResolver
{
    Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        string secretRef,
        CancellationToken ct = default);
}
