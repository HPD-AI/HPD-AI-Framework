using System.Text.Json.Serialization;

namespace HPD.RAG.GraphStoreProviders.Neo4j;

/// <summary>
/// Neo4j-specific graph store configuration.
/// These values are read from <see cref="HPD.RAG.Core.Providers.GraphStore.GraphStoreConfig.ProviderOptionsJson"/>.
///
/// JSON example:
/// <code>
/// { "uri": "bolt://localhost:7687", "username": "neo4j", "password": "secret", "database": "neo4j" }
/// </code>
/// </summary>
public sealed class Neo4jGraphStoreConfig
{
    /// <summary>Bolt URI, e.g. bolt://localhost:7687 or neo4j://localhost:7687.</summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    /// <summary>Neo4j username.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>Neo4j password.</summary>
    [JsonPropertyName("password")]
    public string? Password { get; set; }

    /// <summary>Target database name. Defaults to "neo4j".</summary>
    [JsonPropertyName("database")]
    public string Database { get; set; } = "neo4j";
}
