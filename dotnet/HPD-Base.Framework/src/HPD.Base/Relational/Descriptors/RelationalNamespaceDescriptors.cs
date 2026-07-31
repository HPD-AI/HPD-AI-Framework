using System.Text.Json;

namespace HPD.Base;

public sealed record RelationalDatabaseDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string NativeName { get; init; }
    public string? NativePath { get; init; }
    public RelationalNamespaceKind Kind { get; init; } = RelationalNamespaceKind.Database;
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicSafe { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalCatalogDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string NativeName { get; init; }
    public string? NativePath { get; init; }
    public string? DatabaseRef { get; init; }
    public RelationalNamespaceKind Kind { get; init; } = RelationalNamespaceKind.Catalog;
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicSafe { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalSchemaDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string NativeName { get; init; }
    public string? NativePath { get; init; }
    public string? DatabaseRef { get; init; }
    public string? CatalogRef { get; init; }
    public RelationalNamespaceKind Kind { get; init; } = RelationalNamespaceKind.Schema;
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicSafe { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
