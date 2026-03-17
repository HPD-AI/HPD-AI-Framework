using HPD.Agent.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Adapters.Slack.OAuth;

/// <summary>
/// Extension methods for registering the Slack OAuth installation flow.
/// </summary>
public static class SlackOAuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Slack OAuth services needed for multi-workspace installation:
    /// <list type="bullet">
    ///   <item><see cref="SlackOAuthConfig"/> options (via <paramref name="configure"/>).</item>
    ///   <item><see cref="ISlackTokenStore"/> — defaults to <see cref="InMemorySlackTokenStore"/>;
    ///         register your own before calling this to override.</item>
    ///   <item>
    ///     A <see cref="TokenStoreSecretResolver"/> prepended to the existing
    ///     <see cref="ISecretResolver"/> chain so that <see cref="SlackApiClient"/> automatically
    ///     resolves per-team tokens from the store under the key <c>slack:BotToken:{teamId}</c>.
    ///   </item>
    /// </list>
    /// Call <c>app.MapSlackOAuth()</c> after building to mount the install + callback endpoints.
    /// </summary>
    public static IServiceCollection AddSlackOAuth(
        this IServiceCollection services,
        Action<SlackOAuthConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        // Default in-memory store — replace before calling AddSlackOAuth to use a durable store.
        services.TryAddSingleton<ISlackTokenStore, InMemorySlackTokenStore>();

        // Prepend a TokenStoreSecretResolver in front of whatever ISecretResolver is already
        // registered. We snapshot the existing descriptor NOW (before adding ours) so the
        // factory can instantiate it directly — never via GetServices<ISecretResolver>(),
        // which would deadlock because our own singleton is already in the container.
        var existingDescriptor = services.LastOrDefault(
            d => d.ServiceType == typeof(ISecretResolver) && !d.IsKeyedService);
        services.AddSingleton<ISecretResolver>(sp =>
        {
            var store = sp.GetRequiredService<ISlackTokenStore>();
            ISecretResolver? baseResolver = existingDescriptor switch
            {
                { ImplementationInstance: ISecretResolver r }   => r,
                { ImplementationFactory: { } f }                => f(sp) as ISecretResolver,
                { ImplementationType: { } t }                   => (ISecretResolver)sp.GetRequiredService(t),
                _                                               => null,
            };
            return new TokenStoreSecretResolver(store, baseResolver);
        });

        return services;
    }
}

/// <summary>
/// <see cref="ISecretResolver"/> that resolves <c>slack:BotToken:{teamId}</c> keys
/// from an <see cref="ISlackTokenStore"/>. All other keys return <c>null</c> (fall-through).
/// </summary>
/// <summary>
/// <see cref="ISecretResolver"/> that resolves <c>slack:BotToken:{teamId}</c> keys
/// from an <see cref="ISlackTokenStore"/>, then falls through to an optional base resolver
/// for all other keys.
/// </summary>
public sealed class TokenStoreSecretResolver(
    ISlackTokenStore tokenStore,
    ISecretResolver? baseResolver = null) : ISecretResolver
{
    private const string Prefix = "slack:BotToken:";

    public async ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken ct = default)
    {
        if (key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var teamId = key[Prefix.Length..];
            var token  = await tokenStore.GetAsync(teamId, ct);
            if (token is not null)
                return new ResolvedSecret { Value = token, Source = $"slack-token-store:{key}" };
        }

        return baseResolver is not null
            ? await baseResolver.ResolveAsync(key, ct)
            : null;
    }

}
