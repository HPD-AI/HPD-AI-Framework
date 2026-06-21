using HPD.Graph.Connectors.Abstractions.Actions;
using HPD.Graph.Connectors.Abstractions.Connections;

namespace HPD.Graph.Connectors.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HpdConnectorAttribute(string connectorId) : Attribute
{
    public string ConnectorId { get; } = connectorId;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? Version { get; init; }
    public string? IconUri { get; init; }
    public Type? JsonContextType { get; init; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class HpdOpenApiSpecAttribute(string specPath) : Attribute
{
    public string SpecPath { get; } = specPath;
    public string[] IncludeOperations { get; init; } = [];
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HpdConnectionAttribute(string connectionType) : Attribute
{
    public string ConnectionType { get; } = connectionType;
    public string? AppId { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public ConnectionAuthKind AuthKind { get; init; } = ConnectionAuthKind.Custom;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class HpdActionConfigAttribute(string actionType) : Attribute
{
    public string ActionType { get; } = actionType;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public ConnectorOperationTraits Traits { get; init; } = ConnectorOperationTraits.None;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HpdConnectorActionAttribute(string actionType) : Attribute
{
    public string ActionType { get; } = actionType;
    public Type? ConfigType { get; init; }
    public string? DisplayName { get; init; }
    public ConnectorOperationTraits Traits { get; init; } = ConnectorOperationTraits.None;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HpdWebhookSourceAttribute(string sourceType) : Attribute
{
    public string SourceType { get; } = sourceType;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? AppId { get; init; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HpdPollingSourceAttribute(string sourceType) : Attribute
{
    public string SourceType { get; } = sourceType;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? AppId { get; init; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HpdWebhookOrPollingSourceAttribute(string sourceType) : Attribute
{
    public string SourceType { get; } = sourceType;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? AppId { get; init; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class HpdConnectorPreDispatchAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class HpdConnectorBodyExtractorAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HpdConnectorAssetCatalogAttribute(string catalogProviderName) : Attribute
{
    public string CatalogProviderName { get; } = catalogProviderName;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HpdConnectorMaterializationAttribute(string materializationType) : Attribute
{
    public string MaterializationType { get; } = materializationType;
    public Type? ConfigType { get; init; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HpdConnectorAssetCheckAttribute(string checkName) : Attribute
{
    public string CheckName { get; } = checkName;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HpdArtifactIOManagerAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class HpdConnectorOptionAttribute(string optionProviderName) : Attribute
{
    public string OptionProviderName { get; } = optionProviderName;
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ConnectorConnectionAttribute(string connectionType) : Attribute
{
    public string ConnectionType { get; } = connectionType;
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ConnectorOptionAttribute(string optionProviderName) : Attribute
{
    public string OptionProviderName { get; } = optionProviderName;
}
