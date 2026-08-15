using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Opaque positive generation value for one registered module cell.</summary>
[JsonConverter(typeof(BaseModuleGenerationJsonConverter))]
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
    internal static BaseModuleGeneration ParseCanonical(string value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed)
            || parsed < 1
            || !string.Equals(value, parsed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            throw new InvalidOperationException("base.moduleMutation.receiptInvalid");
        return new(parsed);
    }
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

/// <summary>Provides the closed canonical JSON string codec for module generations.</summary>
public sealed class BaseModuleGenerationJsonConverter : JsonConverter<BaseModuleGeneration>
{
    /// <inheritdoc />
    public override BaseModuleGeneration Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || reader.GetString() is not { } value)
            throw new JsonException("A module generation must be a canonical JSON string.");
        try { return BaseModuleGeneration.ParseCanonical(value); }
        catch (InvalidOperationException exception) { throw new JsonException("A module generation is invalid.", exception); }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, BaseModuleGeneration value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToCanonicalString());
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
