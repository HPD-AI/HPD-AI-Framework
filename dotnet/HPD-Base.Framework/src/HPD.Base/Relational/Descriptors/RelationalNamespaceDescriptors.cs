using System.Text.Json;

namespace HPD.Base;

/// <summary>Represents a relational database descriptor.</summary>
public sealed record RelationalDatabaseDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the native name.</summary>
    public required string NativeName { get; init; }
    /// <summary>Gets or sets the native path.</summary>
    public string? NativePath { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public RelationalNamespaceKind Kind { get; init; } = RelationalNamespaceKind.Database;
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational catalog descriptor.</summary>
public sealed record RelationalCatalogDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the native name.</summary>
    public required string NativeName { get; init; }
    /// <summary>Gets or sets the native path.</summary>
    public string? NativePath { get; init; }
    /// <summary>Gets or sets the database ref.</summary>
    public string? DatabaseRef { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public RelationalNamespaceKind Kind { get; init; } = RelationalNamespaceKind.Catalog;
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational schema descriptor.</summary>
public sealed record RelationalSchemaDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the native name.</summary>
    public required string NativeName { get; init; }
    /// <summary>Gets or sets the native path.</summary>
    public string? NativePath { get; init; }
    /// <summary>Gets or sets the database ref.</summary>
    public string? DatabaseRef { get; init; }
    /// <summary>Gets or sets the catalog ref.</summary>
    public string? CatalogRef { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public RelationalNamespaceKind Kind { get; init; } = RelationalNamespaceKind.Schema;
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
