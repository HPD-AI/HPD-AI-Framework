namespace HPDAgent.Graph.Abstractions.Registry;

/// <summary>
/// Registry for managing multiple graph definitions in a single code location.
/// and discover graphs for execution and materialization.
///
/// Design Philosophy:
/// - Graphs are registered once at application startup
/// - Orchestrator remains stateless - no need to "remember" last executed graph
/// - MaterializeAsync explicitly references graph by ID
/// - Thread-safe for concurrent access
/// </summary>
public interface IGraphRegistry
{
    /// <summary>
    /// Register a graph with a unique identifier.
    /// Throws if a graph with the same ID already exists.
    /// </summary>
    /// <param name="graphId">Unique identifier for the graph</param>
    /// <param name="graph">The graph definition to register</param>
    /// <exception cref="ArgumentException">If graphId is null/empty or graph with same ID exists</exception>
    void RegisterGraph(string graphId, Graph.Graph graph);

    /// <summary>
    /// Get a graph by its identifier.
    /// Returns null if graph is not found.
    /// </summary>
    /// <param name="graphId">The graph identifier</param>
    /// <returns>The graph definition, or null if not found</returns>
    Graph.Graph? GetGraph(string graphId);

    /// <summary>
    /// Check if a graph with the given ID is registered.
    /// </summary>
    /// <param name="graphId">The graph identifier</param>
    /// <returns>True if graph exists, false otherwise</returns>
    bool ContainsGraph(string graphId);

    /// <summary>
    /// Get all registered graph IDs.
    /// Useful for discovery and listing available graphs.
    /// </summary>
    /// <returns>Collection of all registered graph IDs</returns>
    IEnumerable<string> GetGraphIds();

    /// <summary>
    /// Unregister a graph by its identifier.
    /// Returns true if graph was found and removed, false otherwise.
    /// </summary>
    /// <param name="graphId">The graph identifier</param>
    /// <returns>True if graph was removed, false if not found</returns>
    bool UnregisterGraph(string graphId);

    /// <summary>
    /// Get the total number of registered graphs.
    /// </summary>
    int Count { get; }
}
