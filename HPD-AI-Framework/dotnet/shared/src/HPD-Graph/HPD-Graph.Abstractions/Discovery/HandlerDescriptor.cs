using System.Text.Json;

namespace HPDAgent.Graph.Abstractions.Discovery;

public interface IGeneratedHandlerCatalog
{
    IReadOnlyDictionary<string, HandlerDescriptor> GetHandlers();
}

public sealed record HandlerDescriptor
{
    public required string HandlerName { get; init; }
    public required string DisplayName { get; init; }
    public required string Domain { get; init; }
    public required string HandlerType { get; init; }
    public required string ContextType { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<SocketDescriptor> Inputs { get; init; } = Array.Empty<SocketDescriptor>();
    public IReadOnlyList<SocketDescriptor> Outputs { get; init; } = Array.Empty<SocketDescriptor>();
    public ConfigDescriptor? Config { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record SocketDescriptor
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public required SocketDirection Direction { get; init; }
    public bool Required { get; init; } = true;
    public string? Description { get; init; }
    public string? DisplayName { get; init; }
    public JsonElement? DefaultValue { get; init; }
}

public enum SocketDirection
{
    Input,
    Output
}

public sealed record ConfigDescriptor
{
    public required string TypeName { get; init; }
    public string? SchemaId { get; init; }
    public JsonElement? JsonSchema { get; init; }
}

