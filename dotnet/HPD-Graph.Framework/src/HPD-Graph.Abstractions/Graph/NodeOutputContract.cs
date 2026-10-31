using System.Text.Json.Serialization;
using HPD.Graph.Abstractions.Validation;

namespace HPD.Graph.Abstractions.Graph;

/// <summary>Portable description of one node's emitted port/key projection.</summary>
public sealed record NodeOutputContract
{
    public IReadOnlyList<OutputValueContract> Values { get; init; } = [];
    public bool AllowAdditionalOutputs { get; init; }
}

public sealed record OutputValueContract
{
    public int Port { get; init; }
    public required string Key { get; init; }
    public bool Required { get; init; } = true;
    public bool Nullable { get; init; }
    public required string TypeName { get; init; }
    public string? SchemaIdentity { get; init; }
    public string? SchemaVersion { get; init; }
    [JsonIgnore] public Type? RuntimeType { get; init; }
    [JsonIgnore] public IInputValidator? SchemaValidator { get; init; }
}
