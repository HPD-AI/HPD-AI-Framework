using System.Text.Json;
using HPDAgent.Graph.Connectors.Abstractions.Configuration;

namespace HPDAgent.Graph.Connectors.Abstractions.Actions;

public sealed record ConnectorActionDescriptor
{
    public required string ActionType { get; init; }
    public required string HandlerName { get; init; }
    public required string AppId { get; init; }
    public required string DisplayName { get; init; }
    public Type? ConfigType { get; init; }
    public JsonElement? ConfigSchema { get; init; }
    public IReadOnlyList<ConnectorFieldDescriptor> Fields { get; init; } = [];
    public ConnectorOperationTraits Traits { get; init; } = ConnectorOperationTraits.None;
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

[Flags]
public enum ConnectorOperationTraits
{
    None = 0,
    ReadOnly = 1 << 0,
    Destructive = 1 << 1,
    Idempotent = 1 << 2,
    OpenWorld = 1 << 3,
    RequiresApproval = 1 << 4
}
