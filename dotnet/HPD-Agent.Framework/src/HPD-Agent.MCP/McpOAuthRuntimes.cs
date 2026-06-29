using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Authentication;

namespace HPD.Agent.MCP;

/// <summary>
/// Persisted OAuth client registration credentials for an MCP server.
/// </summary>
public sealed class McpOAuthClientRegistration
{
    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("clientSecret")]
    public string? ClientSecret { get; set; }
}

/// <summary>
/// In-memory MCP OAuth runtime for tests and short-lived host applications.
/// </summary>
public sealed class InMemoryMcpOAuthRuntime : IMcpOAuthRuntime
{
    private readonly Dictionary<string, TokenContainer> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, McpOAuthClientRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryMcpOAuthRuntime(
        AuthorizationRedirectDelegate? authorizationRedirectDelegate = null,
        Func<IReadOnlyList<Uri>, Uri?>? authServerSelector = null,
        ScopeSelectorDelegate? scopeSelector = null)
    {
        AuthorizationRedirectDelegate = authorizationRedirectDelegate;
        AuthServerSelector = authServerSelector;
        ScopeSelector = scopeSelector;
    }

    public AuthorizationRedirectDelegate? AuthorizationRedirectDelegate { get; }

    public Func<IReadOnlyList<Uri>, Uri?>? AuthServerSelector { get; }

    public ScopeSelectorDelegate? ScopeSelector { get; }

    public McpOAuthClientRegistration? GetClientRegistration(MCPServerConfig server)
    {
        return _registrations.TryGetValue(GetCacheKey(server), out var registration)
            ? registration
            : null;
    }

    public AuthorizationRedirectDelegate? CreateAuthorizationRedirectDelegate(MCPServerConfig server) => AuthorizationRedirectDelegate;

    public ITokenCache? CreateTokenCache(MCPServerConfig server) => new InMemoryServerTokenCache(_tokens, GetCacheKey(server));

    public Func<IReadOnlyList<Uri>, Uri?>? CreateAuthServerSelector(MCPServerConfig server) => AuthServerSelector;

    public ScopeSelectorDelegate? CreateScopeSelector(MCPServerConfig server) => ScopeSelector;

    public Func<DynamicClientRegistrationResponse, CancellationToken, Task>? CreateDynamicClientRegistrationResponseDelegate(MCPServerConfig server)
    {
        var cacheKey = GetCacheKey(server);
        return (response, _) =>
        {
            _registrations[cacheKey] = new McpOAuthClientRegistration
            {
                ClientId = response.ClientId,
                ClientSecret = response.ClientSecret
            };
            return Task.CompletedTask;
        };
    }

    private static string GetCacheKey(MCPServerConfig server) => string.IsNullOrWhiteSpace(server.Name) ? "server" : server.Name;

    private sealed class InMemoryServerTokenCache : ITokenCache
    {
        private readonly Dictionary<string, TokenContainer> _tokens;
        private readonly string _cacheKey;

        public InMemoryServerTokenCache(Dictionary<string, TokenContainer> tokens, string cacheKey)
        {
            _tokens = tokens;
            _cacheKey = cacheKey;
        }

        public ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
        {
            _tokens[_cacheKey] = tokens;
            return default;
        }

        public ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<TokenContainer?>(_tokens.TryGetValue(_cacheKey, out var tokens) ? tokens : null);
        }
    }
}

/// <summary>
/// JSON-file MCP OAuth runtime for simple durable host-side token and registration persistence.
/// </summary>
public sealed class JsonMcpOAuthRuntime : IMcpOAuthRuntime
{
    private readonly string _cacheDirectory;

    public JsonMcpOAuthRuntime(
        string cacheDirectory,
        AuthorizationRedirectDelegate? authorizationRedirectDelegate = null,
        Func<IReadOnlyList<Uri>, Uri?>? authServerSelector = null,
        ScopeSelectorDelegate? scopeSelector = null)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            throw new ArgumentException("Cache directory is required.", nameof(cacheDirectory));
        }

        _cacheDirectory = ExpandHomeDirectory(cacheDirectory);
        AuthorizationRedirectDelegate = authorizationRedirectDelegate;
        AuthServerSelector = authServerSelector;
        ScopeSelector = scopeSelector;
    }

    public AuthorizationRedirectDelegate? AuthorizationRedirectDelegate { get; }

    public Func<IReadOnlyList<Uri>, Uri?>? AuthServerSelector { get; }

    public ScopeSelectorDelegate? ScopeSelector { get; }

    public McpOAuthClientRegistration? GetClientRegistration(MCPServerConfig server)
    {
        var path = GetRegistrationPath(server);
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, MCPJsonSerializerContext.Default.McpOAuthClientRegistration);
    }

    public AuthorizationRedirectDelegate? CreateAuthorizationRedirectDelegate(MCPServerConfig server) => AuthorizationRedirectDelegate;

    public ITokenCache? CreateTokenCache(MCPServerConfig server) => new JsonFileTokenCache(GetTokenPath(server));

    public Func<IReadOnlyList<Uri>, Uri?>? CreateAuthServerSelector(MCPServerConfig server) => AuthServerSelector;

    public ScopeSelectorDelegate? CreateScopeSelector(MCPServerConfig server) => ScopeSelector;

    public Func<DynamicClientRegistrationResponse, CancellationToken, Task>? CreateDynamicClientRegistrationResponseDelegate(MCPServerConfig server)
    {
        var path = GetRegistrationPath(server);
        return async (response, cancellationToken) =>
        {
            var registration = new McpOAuthClientRegistration
            {
                ClientId = response.ClientId,
                ClientSecret = response.ClientSecret
            };

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(
                stream,
                registration,
                MCPJsonSerializerContext.Default.McpOAuthClientRegistration,
                cancellationToken).ConfigureAwait(false);
        };
    }

    private string GetTokenPath(MCPServerConfig server) => Path.Combine(_cacheDirectory, $"{SanitizeFileName(GetCacheKey(server))}.tokens.json");

    private string GetRegistrationPath(MCPServerConfig server) => Path.Combine(_cacheDirectory, $"{SanitizeFileName(GetCacheKey(server))}.client.json");

    private static string GetCacheKey(MCPServerConfig server)
    {
        if (!string.IsNullOrWhiteSpace(server.Name))
        {
            return server.Name;
        }

        return !string.IsNullOrWhiteSpace(server.Endpoint) ? server.Endpoint : "server";
    }

    private static string ExpandHomeDirectory(string path)
    {
        if (path == "~")
        {
            return global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(
                global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.UserProfile),
                path[2..]);
        }

        return path;
    }

    private static string SanitizeFileName(string value)
    {
        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        var invalidChars = Path.GetInvalidFileNameChars();
        var index = 0;
        var lastWasUnderscore = false;

        foreach (var ch in value)
        {
            var sanitized = invalidChars.Contains(ch) || char.IsWhiteSpace(ch) ? '_' : ch;
            if (sanitized == '_' && lastWasUnderscore)
            {
                continue;
            }

            buffer[index++] = sanitized;
            lastWasUnderscore = sanitized == '_';
        }

        var result = new string(buffer[..index]).Trim('_');
        return string.IsNullOrEmpty(result) ? "server" : result;
    }

    private sealed class JsonFileTokenCache : ITokenCache
    {
        private readonly string _path;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public JsonFileTokenCache(string path)
        {
            _path = path;
        }

        public async ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
        {
            var entry = McpOAuthTokenCacheEntry.FromTokenContainer(tokens);

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                await using var stream = File.Create(_path);
                await JsonSerializer.SerializeAsync(
                    stream,
                    entry,
                    MCPJsonSerializerContext.Default.McpOAuthTokenCacheEntry,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(_path))
                {
                    return null;
                }

                await using var stream = File.OpenRead(_path);
                var entry = await JsonSerializer.DeserializeAsync(
                    stream,
                    MCPJsonSerializerContext.Default.McpOAuthTokenCacheEntry,
                    cancellationToken).ConfigureAwait(false);

                return entry?.ToTokenContainer();
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}

public sealed class McpOAuthTokenCacheEntry
{
    [JsonPropertyName("tokenType")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expiresIn")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("obtainedAt")]
    public DateTimeOffset ObtainedAt { get; set; }

    public static McpOAuthTokenCacheEntry FromTokenContainer(TokenContainer tokens)
    {
        return new McpOAuthTokenCacheEntry
        {
            TokenType = tokens.TokenType,
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            ExpiresIn = tokens.ExpiresIn,
            Scope = tokens.Scope,
            ObtainedAt = tokens.ObtainedAt
        };
    }

    public TokenContainer ToTokenContainer()
    {
        return new TokenContainer
        {
            TokenType = TokenType,
            AccessToken = AccessToken,
            RefreshToken = RefreshToken,
            ExpiresIn = ExpiresIn,
            Scope = Scope,
            ObtainedAt = ObtainedAt
        };
    }
}
