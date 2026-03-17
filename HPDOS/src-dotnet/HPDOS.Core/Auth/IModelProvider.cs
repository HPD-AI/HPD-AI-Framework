namespace HPDOS.Core.Auth;

/// <summary>
/// Opt-in interface for auth providers that have a known model list.
/// Separate from IAuthProvider — model listing is a distinct concern from authentication.
/// </summary>
public interface IModelProvider
{
    IReadOnlyList<ModelInfo> GetModels();

    /// <summary>
    /// Whether this provider supports filtering models by free tier (e.g. OpenRouter's :free suffix).
    /// Defaults to false; override to true on providers that expose this capability.
    /// </summary>
    bool SupportsFreeSearch => false;
}

/// <summary>
/// Opt-in interface for providers that can fetch a live model list from their API.
/// <paramref name="entry"/> is the active auth entry (may be null if provider doesn't need auth).
/// Falls back to <see cref="IModelProvider.GetModels"/> on error.
/// </summary>
public interface ILiveModelProvider : IModelProvider
{
    Task<IReadOnlyList<ModelInfo>> FetchModelsAsync(AuthEntry? entry, CancellationToken ct = default);
}
