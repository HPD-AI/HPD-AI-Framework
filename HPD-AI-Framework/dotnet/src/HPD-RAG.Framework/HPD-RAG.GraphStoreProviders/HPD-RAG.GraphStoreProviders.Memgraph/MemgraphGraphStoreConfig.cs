using System.Text.Json.Serialization;

namespace HPD.RAG.GraphStoreProviders.Memgraph;

/// <summary>
/// Memgraph-specific graph store configuration.
/// Memgraph speaks the Bolt protocol; the Neo4j .NET driver is used as the transport.
/// These values are read from <see cref="HPD.RAG.Core.Providers.GraphStore.GraphStoreConfig.ProviderOptionsJson"/>.
///
/// JSON example:
/// <code>
/// { "uri": "bolt://localhost:7687", "username": "memgraph", "password": "secret", "database": "memgraph" }
/// </code>
/// </summary>
public sealed class MemgraphGraphStoreConfig
{
    /// <summary>Bolt URI, e.g. bolt://localhost:7687.</summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    /// <summary>Memgraph username.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>Memgraph password.</summary>
    [JsonPropertyName("password")]
    public string? Password { get; set; }

    /// <summary>Target database name. Defaults to "memgraph".</summary>
    [JsonPropertyName("database")]
    public string Database { get; set; } = "memgraph";
}
