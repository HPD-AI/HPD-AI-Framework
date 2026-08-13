namespace HPD.Base;

/// <summary>Declares the stable serializer contract identity of one structurally stateless property converter.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class BaseSerializerConverterAttribute : Attribute
{
    /// <summary>Creates a stable converter contract declaration.</summary>
    /// <param name="contractId">The stable publisher-owned contract identifier.</param>
    /// <param name="version">The positive representation version.</param>
    public BaseSerializerConverterAttribute(string contractId, int version)
    {
        BaseApplicationId.Validate(contractId, nameof(contractId));
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ContractId = contractId;
        Version = version;
    }

    /// <summary>Gets the stable contract identifier.</summary>
    public string ContractId { get; }
    /// <summary>Gets the positive representation version.</summary>
    public int Version { get; }
}
