namespace HPD.Agent.Client;

/// <summary>Transport owner extended by deterministic generated service facades.</summary>
public abstract class HpdAgentClientBase : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Uri _baseAddress;
    private readonly IReadOnlyDictionary<string, string> _defaultHeaders;

    protected HpdAgentClientBase(HpdAgentClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.BaseAddress.IsAbsoluteUri)
            throw new ArgumentException("The API base address must be absolute.", nameof(options));
        if (options.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be positive.");
        if (options.Retry.MaxRetries is < 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxRetries must be between zero and ten.");

        _baseAddress = options.BaseAddress;
        _defaultHeaders = options.DefaultHeaders;
        _ownsHttpClient = options.HttpClient is null;
        _httpClient = options.HttpClient ?? new HttpClient(
            options.HttpMessageHandler ?? new HttpClientHandler(),
            disposeHandler: true);
        _httpClient.Timeout = options.Timeout;
        Responder = options.Responder;
        Transport = new HpdAgentHttpTransport(
            _httpClient,
            _baseAddress,
            _defaultHeaders,
            options.Retry);
    }

    /// <summary>Gets the invariant HTTP and SSE transport.</summary>
    public HpdAgentHttpTransport Transport { get; }

    /// <summary>Gets the configured absolute API base address.</summary>
    public Uri BaseAddress => _baseAddress;

    /// <summary>Gets headers applied to every HTTP and WebSocket connection.</summary>
    public IReadOnlyDictionary<string, string> DefaultHeaders => _defaultHeaders;

    /// <summary>Gets the client instance's responder routing settings.</summary>
    public HpdAgentResponderOptions Responder { get; }

    /// <summary>Sends a raw request and preserves status, headers, and body.</summary>
    public Task<HpdAgentRawResponse> SendRawAsync(
        HttpMethod method,
        string relativePath,
        string? jsonBody = null,
        CancellationToken cancellationToken = default) =>
        Transport.SendRawAsync(method, relativePath, jsonBody, cancellationToken);

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
