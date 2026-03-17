using System.Text.Json;
using HPDAgent.Graph.Abstractions.Serialization;

namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Builder for multi-port outputs.
/// Provides fluent API for constructing port-based routing.
/// </summary>
public sealed class PortOutputs
{
    private readonly Dictionary<int, Dictionary<string, object>> _ports = new();

    /// <summary>
    /// Add output to a specific port.
    /// </summary>
    /// <param name="portNumber">Port number (0-indexed)</param>
    /// <param name="output">Output dictionary</param>
    /// <returns>This builder for chaining</returns>
    public PortOutputs Add(int portNumber, Dictionary<string, object> output)
    {
        if (portNumber < 0)
            throw new ArgumentException("Port number must be non-negative", nameof(portNumber));

        _ports[portNumber] = output;
        return this;
    }

    /// <summary>
    /// Add output to a specific port (anonymous object overload).
    /// Uses source-generated JSON serialization for conversion (fast + AOT-compatible).
    /// </summary>
    /// <param name="portNumber">Port number (0-indexed)</param>
    /// <param name="output">Anonymous object to convert to dictionary</param>
    /// <returns>This builder for chaining</returns>
    public PortOutputs Add(int portNumber, object output)
    {
        var dict = ObjectToDictionary(output);
        return Add(portNumber, dict);
    }

    /// <summary>
    /// Builds the immutable port outputs dictionary.
    /// </summary>
    internal IReadOnlyDictionary<int, Dictionary<string, object>> Build()
        => _ports;

    private static Dictionary<string, object> ObjectToDictionary(object obj)
    {
        // Use source-generated JSON serialization for performance
        var json = JsonSerializer.Serialize(obj, GraphJsonSerializerContext.Default.Object);
        return JsonSerializer.Deserialize(json, GraphJsonSerializerContext.Default.DictionaryStringObject)!;
    }
}
