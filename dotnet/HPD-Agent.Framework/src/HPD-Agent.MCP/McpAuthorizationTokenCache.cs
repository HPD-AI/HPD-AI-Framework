using System.Text.Json;
using ModelContextProtocol.Authentication;

namespace HPD.Agent.MCP;

/// <summary>Bridges the SDK token cache to issuer-bound application persistence.</summary>
internal sealed class McpAuthorizationTokenCache : ITokenCache
{
    private const int CurrentVersion = 1;
    private readonly IMcpAuthorizationStore _store;
    private readonly McpResourceRegistrationId _resource;
    private readonly string? _configuredClientId;
    private readonly IReadOnlyList<string> _configuredScopes;

    internal McpAuthorizationTokenCache(
        IMcpAuthorizationStore store,
        McpServerConfig server,
        McpOAuthOptions oauth)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(oauth);
        _resource = McpResourceRegistrationId.Create(
            $"{server.Name}|{NormalizeResource(server.Endpoint!)}");
        _configuredClientId = oauth.ClientId ?? oauth.ClientIdMetadataDocument?.AbsoluteUri;
        _configuredScopes = NormalizeScopes(oauth.Scopes);
    }

    public async ValueTask StoreTokensAsync(
        TokenContainer tokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.AuthorizationServer is null)
        {
            await _store.DeleteAsync(_resource, cancellationToken).ConfigureAwait(false);
            return;
        }

        var issuer = NormalizeIssuer(tokens.AuthorizationServer);
        var clientId = tokens.ClientId ?? _configuredClientId ?? string.Empty;
        var scopes = NormalizeScopes(
            string.IsNullOrWhiteSpace(tokens.Scope)
                ? _configuredScopes
                : tokens.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            tokens, McpJsonSerializerContext.Default.TokenContainer);
        await _store.SaveAsync(_resource, new McpAuthorizationRecord
        {
            Version = CurrentVersion,
            ResourceRegistrationId = _resource.Value,
            Issuer = issuer,
            ClientId = clientId,
            Scopes = scopes,
            ProtectedTokenContainer = payload
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
    {
        var record = await _store.LoadAsync(_resource, cancellationToken).ConfigureAwait(false);
        if (record is null)
            return null;
        TokenContainer? tokens;
        try
        {
            tokens = JsonSerializer.Deserialize(
                record.ProtectedTokenContainer,
                McpJsonSerializerContext.Default.TokenContainer);
        }
        catch (JsonException)
        {
            await _store.DeleteAsync(_resource, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (record.Version != CurrentVersion ||
            !string.Equals(record.ResourceRegistrationId, _resource.Value, StringComparison.Ordinal) ||
            tokens?.AuthorizationServer is null ||
            !string.Equals(record.Issuer, NormalizeIssuer(tokens.AuthorizationServer), StringComparison.Ordinal) ||
            (!string.IsNullOrEmpty(_configuredClientId) &&
             !string.Equals(record.ClientId, _configuredClientId, StringComparison.Ordinal)) ||
            !_configuredScopes.SequenceEqual(NormalizeScopes(record.Scopes), StringComparer.Ordinal))
        {
            await _store.DeleteAsync(_resource, cancellationToken).ConfigureAwait(false);
            return null;
        }

        return tokens;
    }

    private static string NormalizeResource(Uri resource) =>
        resource.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped)
            .TrimEnd('/').ToLowerInvariant();

    private static string NormalizeIssuer(string issuerValue)
    {
        if (!Uri.TryCreate(issuerValue, UriKind.Absolute, out var issuer))
            throw new InvalidOperationException("MCP authorization-server issuer must be an absolute URI without a fragment.");
        if (!issuer.IsAbsoluteUri || issuer.Fragment.Length != 0)
            throw new InvalidOperationException("MCP authorization-server issuer must be an absolute URI without a fragment.");
        return issuer.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped)
            .TrimEnd('/').ToLowerInvariant();
    }

    private static IReadOnlyList<string> NormalizeScopes(IEnumerable<string> scopes) => scopes
        .Where(static scope => !string.IsNullOrWhiteSpace(scope))
        .Select(static scope => scope.Trim())
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
}
