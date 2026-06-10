namespace HPD.OpenApi.Core;

/// <summary>
/// Context passed to <see cref="OpenApiCoreConfig.OperationSelectionPredicate"/> for each operation
/// found in the spec. Return true to include the operation, false to skip it.
/// </summary>
public sealed class OperationSelectionContext
{
    /// <summary>Operation ID from the spec (operationId field). May be null for unnamed operations.</summary>
    public string? Id { get; init; }

    /// <summary>Path template (e.g., "/pets/{petId}").</summary>
    public required string Path { get; init; }

    /// <summary>HTTP method in uppercase (e.g., "GET", "POST").</summary>
    public required string Method { get; init; }

    /// <summary>Operation description or summary from the spec.</summary>
    public string? Description { get; init; }
}
