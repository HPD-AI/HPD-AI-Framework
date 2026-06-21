using System.Collections.ObjectModel;

namespace HPD.Agent.Bots;

/// <summary>
/// Transport-neutral inbound bot message envelope.
/// </summary>
public sealed record BotInboundEnvelope
{
    /// <summary>Inbound method or transport verb, usually an HTTP method.</summary>
    public required string Method { get; init; }

    /// <summary>Optional path or route used by the host transport.</summary>
    public string? Path { get; init; }

    /// <summary>Raw inbound body bytes.</summary>
    public required byte[] Body { get; init; }

    /// <summary>Inbound headers keyed by case-insensitive name when provided by the host.</summary>
    public IReadOnlyDictionary<string, string[]> Headers { get; init; } =
        new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Inbound query parameters keyed by case-insensitive name when provided by the host.</summary>
    public IReadOnlyDictionary<string, string[]> Query { get; init; } =
        new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Host-specific values that should travel with this dispatch without becoming typed API.</summary>
    public IReadOnlyDictionary<string, object?> Items { get; init; } =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
}
