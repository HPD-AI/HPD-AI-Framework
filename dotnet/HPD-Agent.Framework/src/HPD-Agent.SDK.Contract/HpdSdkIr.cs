using System.Text.Json;

namespace HPD.Agent.SDK.Contract;

/// <summary>
/// Versioned, language-neutral representation of an HPD agent SDK contract.
/// </summary>
public sealed record HpdSdkIr(
    string IrVersion,
    string GeneratorVersion,
    string SourceOpenApiSha256,
    string? EventDigest,
    string InputDigest,
    HpdApiIr Api,
    IReadOnlyList<HpdTypeDefinitionIr> Types,
    IReadOnlyList<HpdOperationIr> Operations,
    IReadOnlyList<HpdUnionIr> Unions,
    IReadOnlyList<HpdCapabilityIr> Capabilities,
    IReadOnlyList<HpdDiagnosticIr> Diagnostics);

/// <summary>API-level identity from the source OpenAPI document.</summary>
public sealed record HpdApiIr(
    string Title,
    string ApiVersion,
    string OpenApiVersion);

/// <summary>A named schema exported by the contract.</summary>
public sealed record HpdTypeDefinitionIr(
    string ComponentId,
    HpdTypeIr Schema);

/// <summary>
/// Language-neutral structural type. Optional fields are omitted when they do
/// not apply to <see cref="Kind"/>.
/// </summary>
public sealed record HpdTypeIr(
    string Kind,
    string? Primitive = null,
    string? Format = null,
    bool Nullable = false,
    string? Reference = null,
    string? Literal = null,
    IReadOnlyList<string>? EnumValues = null,
    IReadOnlyList<HpdPropertyIr>? Properties = null,
    IReadOnlyList<string>? RequiredProperties = null,
    HpdTypeIr? ItemType = null,
    HpdTypeIr? ValueType = null,
    bool AllowsAdditionalProperties = false,
    bool ObjectModel = false,
    string? Composition = null,
    IReadOnlyList<HpdTypeIr>? Branches = null,
    IReadOnlyDictionary<string, JsonElement>? Annotations = null);

/// <summary>A named object property.</summary>
public sealed record HpdPropertyIr(
    string Name,
    bool Required,
    HpdTypeIr Type);

/// <summary>A normalized API operation.</summary>
public sealed record HpdOperationIr(
    string OperationId,
    string Method,
    string Path,
    string Tag,
    string Summary,
    IReadOnlyList<HpdParameterIr> Parameters,
    HpdRequestBodyIr? RequestBody,
    string SuccessStatus,
    string? SuccessMediaType,
    HpdTypeIr? SuccessType,
    bool HasNotFoundResponse,
    string ContentKind,
    bool ReplaySafe);

/// <summary>A normalized operation parameter.</summary>
public sealed record HpdParameterIr(
    string Name,
    string Location,
    bool Required,
    HpdTypeIr Type);

/// <summary>A normalized request body.</summary>
public sealed record HpdRequestBodyIr(
    bool Required,
    string MediaType,
    HpdTypeIr Type);

/// <summary>A first-class event, public-input, or response-input union.</summary>
public sealed record HpdUnionIr(
    string Name,
    string Role,
    string UnknownVariantPolicy,
    IReadOnlyList<HpdUnionVariantIr> Variants);

/// <summary>A discriminator-addressable branch of an HPD union.</summary>
public sealed record HpdUnionVariantIr(
    string ComponentId,
    string Discriminator,
    HpdTypeIr Type);

/// <summary>A source capability and the operations that expose it.</summary>
public sealed record HpdCapabilityIr(
    string Name,
    bool Available,
    IReadOnlyList<string> Operations);

/// <summary>A normalization or semantic-loss diagnostic.</summary>
public sealed record HpdDiagnosticIr(
    string Severity,
    string Code,
    string Path,
    string Message);

/// <summary>IR schema versions understood by this contract library.</summary>
public static class HpdSdkIrVersions
{
    /// <summary>The current HPD Agent SDK IR schema version.</summary>
    public const string V1 = "1.0";
}
