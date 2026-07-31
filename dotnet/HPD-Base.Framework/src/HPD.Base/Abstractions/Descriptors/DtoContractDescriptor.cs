using HPD.Base;

namespace HPD.Base.Descriptors;

public sealed record DtoContractDescriptor
{
    public required string Id { get; init; }
    public required string ContractVersion { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public string? JsonContextOwner { get; init; }
    public string? TypeScriptName { get; init; }
}
