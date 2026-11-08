using System.Text.Json;

namespace HPD.Agent.SDK.Contract;

/// <summary>
/// The normalized, language-neutral contract consumed by SDK generators.
/// </summary>
public sealed record OpenApiContract(
    string OpenApiVersion,
    string Title,
    string ApiVersion,
    string OpenApiSha256,
    string? EventDigest,
    int? EventCount,
    IReadOnlyDictionary<string, JsonElement> Schemas,
    IReadOnlyList<OperationIr> Operations,
    IReadOnlyList<UnionVariant> PublicInputs,
    IReadOnlyList<UnionVariant> ResponseInputs,
    IReadOnlyList<UnionVariant> Events);

/// <summary>
/// Describes one normalized OpenAPI operation and its HPD transport semantics.
/// </summary>
public sealed record OperationIr(
    string OperationId,
    string Method,
    string Path,
    string Tag,
    string Summary,
    IReadOnlyList<ParameterIr> Parameters,
    RequestBodyIr? RequestBody,
    string SuccessStatus,
    string? SuccessMediaType,
    JsonElement? SuccessSchema,
    bool HasNotFoundResponse,
    bool IsServerSentEvents,
    bool IsWebSocketUpgrade,
    int? EventCount,
    string? EventDigest,
    bool ReplaySafe);

/// <summary>
/// Describes one operation parameter.
/// </summary>
public sealed record ParameterIr(
    string Name,
    string Location,
    bool Required,
    JsonElement Schema);

/// <summary>
/// Describes the selected request body representation.
/// </summary>
public sealed record RequestBodyIr(
    bool Required,
    string MediaType,
    JsonElement Schema);

/// <summary>
/// Describes one discriminator-addressable branch of an HPD event or input union.
/// </summary>
public sealed record UnionVariant(
    string ComponentId,
    string Discriminator);
