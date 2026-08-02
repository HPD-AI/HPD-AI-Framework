
namespace HPD.Base;

/// <summary>Represents a DTO contract descriptor.</summary>
public sealed record DtoContractDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the contract version.</summary>
    public required string ContractVersion { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the JSON context owner.</summary>
    public string? JsonContextOwner { get; init; }
    /// <summary>Gets or sets the type script name.</summary>
    public string? TypeScriptName { get; init; }
}
