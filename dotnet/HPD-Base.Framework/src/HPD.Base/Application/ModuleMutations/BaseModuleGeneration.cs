using System.Globalization;

namespace HPD.Base;

/// <summary>Opaque positive generation value for one registered module cell.</summary>
public sealed class BaseModuleGeneration : IEquatable<BaseModuleGeneration>
{
    private readonly long _value;
    private BaseModuleGeneration(long value)
    {
        if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
        _value = value;
    }

    internal long Value => _value;
    internal static BaseModuleGeneration Create(long value) => new(value);
    internal BaseModuleGeneration Increment() => new(checked(_value + 1));

    /// <summary>Returns the canonical unsigned-decimal wire representation.</summary>
    public string ToCanonicalString() => _value.ToString(CultureInfo.InvariantCulture);
    /// <inheritdoc />
    public bool Equals(BaseModuleGeneration? other) => other is not null && _value == other._value;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseModuleGeneration other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => _value.GetHashCode();
    /// <inheritdoc />
    public override string ToString() => ToCanonicalString();
}

/// <summary>Classifies the canonical scope of one module generation cell.</summary>
public enum BaseModuleGenerationScope
{
    /// <summary>Application-wide scope.</summary>
    Application = 0,
    /// <summary>Tenant scope.</summary>
    Tenant = 1,
    /// <summary>Project scope.</summary>
    Project = 2,
    /// <summary>Tenant plus caller-declared key.</summary>
    TenantAndKey = 3,
    /// <summary>Project plus caller-declared key.</summary>
    ProjectAndKey = 4,
}

/// <summary>Defines one provider-owned module generation cell.</summary>
public sealed record BaseModuleGenerationCellDefinition
{
    /// <summary>Gets the stable cell identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive cell version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the owning module identity.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the canonical scope kind.</summary>
    public required BaseModuleGenerationScope Scope { get; init; }
    /// <summary>Gets the maximum canonical UTF-8 key size.</summary>
    public required int MaximumKeyUtf8Bytes { get; init; }
    /// <summary>Gets the maximum cell instances allowed in one operation.</summary>
    public required int MaximumCellsPerOperation { get; init; }
}
