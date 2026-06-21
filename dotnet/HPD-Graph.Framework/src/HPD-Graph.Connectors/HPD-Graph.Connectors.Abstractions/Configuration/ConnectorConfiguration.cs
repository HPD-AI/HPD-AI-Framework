using System.Text.Json;

namespace HPD.Graph.Connectors.Abstractions.Configuration;

public interface IConnectorConfig
{
}

public sealed record ConnectorConfigDescriptor
{
    public required string ConfigType { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public JsonElement? JsonSchema { get; init; }
    public IReadOnlyList<ConnectorFieldDescriptor> Fields { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public sealed record ConnectorFieldDescriptor
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public string? Label { get; init; }
    public string? Description { get; init; }
    public bool Required { get; init; }
    public string? ConnectionType { get; init; }
    public string? OptionProviderName { get; init; }
    public JsonElement? JsonSchema { get; init; }
    public JsonElement? DefaultValue { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}
