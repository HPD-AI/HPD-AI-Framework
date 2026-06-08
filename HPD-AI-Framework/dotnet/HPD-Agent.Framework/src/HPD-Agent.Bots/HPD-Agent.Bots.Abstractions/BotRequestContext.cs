namespace HPD.Agent.Bots;

/// <summary>
/// Transport-neutral context passed to generated bot adapter hooks and handlers.
/// </summary>
public sealed class BotRequestContext
{
    /// <summary>Creates a context for one bot adapter dispatch.</summary>
    public BotRequestContext(
        BotInboundEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
        CancellationToken = cancellationToken;
    }

    /// <summary>The inbound envelope being processed.</summary>
    public BotInboundEnvelope Envelope { get; }

    /// <summary>Cancellation for the current dispatch.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Inbound method or transport verb.</summary>
    public string Method => Envelope.Method;

    /// <summary>Optional inbound path.</summary>
    public string? Path => Envelope.Path;

    /// <summary>Inbound headers.</summary>
    public IReadOnlyDictionary<string, string[]> Headers => Envelope.Headers;

    /// <summary>Inbound query parameters.</summary>
    public IReadOnlyDictionary<string, string[]> Query => Envelope.Query;

    /// <summary>Host-specific per-dispatch values.</summary>
    public IReadOnlyDictionary<string, object?> Items => Envelope.Items;

    /// <summary>Returns the first header value for <paramref name="name"/>.</summary>
    public string? Header(string name)
        => TryFirst(Headers, name);

    /// <summary>Returns the first query value for <paramref name="name"/>.</summary>
    public string? QueryValue(string name)
        => TryFirst(Query, name);

    private static string? TryFirst(IReadOnlyDictionary<string, string[]> values, string name)
    {
        if (!values.TryGetValue(name, out var items) || items.Length == 0)
            return null;

        return items[0];
    }
}
