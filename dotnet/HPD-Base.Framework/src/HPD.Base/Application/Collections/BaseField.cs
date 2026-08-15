namespace HPD.Base;

/// <summary>
/// Describes one typed field in an application collection contract.
/// </summary>
/// <typeparam name="TRecord">The persisted record type.</typeparam>
/// <typeparam name="TValue">The field value type.</typeparam>
public sealed class BaseField<TRecord, TValue> : IBaseFieldContract
{
    internal BaseField(
        string id,
        string applicationName,
        string wireName,
        bool nullable,
        BaseFieldOperator operators)
    {
        BaseApplicationId.Validate(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(wireName);

        Id = id;
        ApplicationName = applicationName;
        WireName = wireName;
        Nullable = nullable;
        Operators = operators;
    }

    /// <summary>
    /// Gets the stable logical field identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the exact application-facing property identity.
    /// </summary>
    public string ApplicationName { get; }

    /// <summary>Gets the exact serializer-owned wire identity.</summary>
    public string WireName { get; }

    /// <summary>
    /// Gets whether the persisted field accepts null.
    /// </summary>
    public bool Nullable { get; }

    /// <summary>
    /// Gets the query operations valid for this field.
    /// </summary>
    public BaseFieldOperator Operators { get; }
    Type IBaseFieldContract.ValueType => typeof(TValue);
}

internal interface IBaseFieldContract
{
    string Id { get; }
    string ApplicationName { get; }
    string WireName { get; }
    Type ValueType { get; }
}

/// <summary>
/// Identifies query operations supported by a typed field.
/// </summary>
[Flags]
public enum BaseFieldOperator
{
    /// <summary>No query operation is declared.</summary>
None = 0,

    /// <summary>Equality comparison is supported.</summary>
Equal = 1 << 0,

    /// <summary>Ordering comparisons are supported.</summary>
Order = 1 << 1,

    /// <summary>Text search is supported.</summary>
Text = 1 << 2,

    /// <summary>Membership comparison is supported.</summary>
Membership = 1 << 3,
}
