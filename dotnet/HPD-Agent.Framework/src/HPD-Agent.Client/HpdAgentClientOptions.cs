namespace HPD.Agent.Client;

/// <summary>Connection, retry, and header settings for one generated HPD Agent client.</summary>
public sealed record HpdAgentClientOptions(Uri BaseAddress)
{
    /// <summary>Gets an optional caller-owned HTTP client.</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>Gets an optional handler used when the runtime creates its own HTTP client.</summary>
    public HttpMessageHandler? HttpMessageHandler { get; init; }

    /// <summary>Gets the overall HTTP timeout applied by the underlying client.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);

    /// <summary>Gets headers applied to every request.</summary>
    public IReadOnlyDictionary<string, string> DefaultHeaders { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets transport retry behavior.</summary>
    public HpdAgentRetryOptions Retry { get; init; } = new();

    /// <summary>Gets responder identity and capabilities used for targeted requests.</summary>
    public HpdAgentResponderOptions Responder { get; init; } = new();
}

/// <summary>Bounded retry policy for replay-safe HPD Agent requests.</summary>
public sealed record HpdAgentRetryOptions
{
    /// <summary>Gets the number of retries after the initial attempt.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Gets the initial exponential-backoff delay.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Gets the maximum retry delay after server guidance is applied.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>Observable reconnect policy for a committed SSE stream.</summary>
public sealed record HpdAgentSseReconnectOptions
{
    /// <summary>A policy that never reconnects after the current response ends.</summary>
    public static HpdAgentSseReconnectOptions Disabled { get; } = new()
    {
        MaxReconnects = 0,
        ReconnectOnEnd = false
    };

    /// <summary>Gets the maximum number of consecutive reconnect attempts.</summary>
    public int MaxReconnects { get; init; } = 5;

    /// <summary>Gets the initial reconnect delay.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets the maximum reconnect delay.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets whether a normally completed response is treated as a disconnected stream.</summary>
    public bool ReconnectOnEnd { get; init; } = true;
}

/// <summary>Observable evidence for one SSE reconnect decision.</summary>
public sealed record HpdAgentSseReconnectDiagnostic(
    int Attempt,
    int MaxReconnects,
    string Reason,
    string? LastEventId,
    TimeSpan Delay,
    bool StreamEnded);

/// <summary>One raw server-sent event. Unknown event types remain lossless in <see cref="Data"/>.</summary>
public sealed record HpdAgentSseMessage(string EventName, string? Id, System.Text.Json.JsonElement Data);
