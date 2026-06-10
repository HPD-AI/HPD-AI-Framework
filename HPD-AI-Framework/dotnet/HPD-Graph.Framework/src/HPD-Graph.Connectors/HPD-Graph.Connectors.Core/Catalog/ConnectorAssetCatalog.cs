using HPD.Graph.Connectors.Abstractions.Assets;

namespace HPD.Graph.Connectors.Core.Catalog;

public interface IConnectorAssetCatalog
{
    Task<IReadOnlyList<ConnectorAssetDescriptor>> LoadAssetsAsync(
        ConnectorAssetCatalogRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<ConnectorAssetDescriptor>> LoadAssetsAsync(
        string catalogProviderName,
        ConnectorAssetCatalogRequest request,
        CancellationToken ct = default);
}

public sealed class ConnectorAssetCatalog : IConnectorAssetCatalog
{
    private readonly IReadOnlyDictionary<string, IConnectorAssetCatalogProvider> _providers;

    public ConnectorAssetCatalog(IEnumerable<IConnectorAssetCatalogProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToDictionary(
            static provider => provider.CatalogProviderName,
            StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<ConnectorAssetDescriptor>> LoadAssetsAsync(
        ConnectorAssetCatalogRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var assets = new List<ConnectorAssetDescriptor>();
        foreach (var provider in _providers.Values)
        {
            var providerAssets = await provider.LoadAssetsAsync(request, ct).ConfigureAwait(false);
            assets.AddRange(providerAssets);
        }

        return assets;
    }

    public Task<IReadOnlyList<ConnectorAssetDescriptor>> LoadAssetsAsync(
        string catalogProviderName,
        ConnectorAssetCatalogRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogProviderName);
        ArgumentNullException.ThrowIfNull(request);

        if (!_providers.TryGetValue(catalogProviderName, out var provider))
        {
            throw new KeyNotFoundException(
                $"Connector asset catalog provider '{catalogProviderName}' is not registered.");
        }

        return provider.LoadAssetsAsync(request, ct);
    }
}
