using System.Text;

namespace HPD.Base;

/// <summary>
/// Identifies one record within a BASE collection.
/// </summary>
public readonly struct RecordId : IEquatable<RecordId>
{
    private readonly string? _value;

    private RecordId(string value) => _value = new string(value.AsSpan());

    /// <summary>Gets the canonical owned record identifier.</summary>
    public string Value => IsValid
        ? _value!
        : throw new InvalidOperationException("The record identifier is invalid.");

    /// <summary>Gets whether this value contains one canonical record identifier.</summary>
    public bool IsValid => BasePrimitiveId.IsValid(_value);

    /// <summary>Executes the create operation.</summary>
    public static RecordId Create(string value) =>
        TryParse(value, out RecordId result)
            ? result
            : throw new ArgumentException("The record identifier is invalid.");

    /// <summary>Executes the parse operation.</summary>
    public static RecordId Parse(string value) => Create(value);

    /// <summary>Executes the try parse operation.</summary>
    public static bool TryParse(string? value, out RecordId result)
    {
        if (!BasePrimitiveId.IsValid(value))
        {
            result = default;
            return false;
        }

        result = new RecordId(value!);
        return true;
    }

    /// <inheritdoc />
    public bool Equals(RecordId other) =>
        string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RecordId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value ?? string.Empty);

    /// <summary>Compares two record identifiers for exact ordinal equality.</summary>
    public static bool operator ==(RecordId left, RecordId right) => left.Equals(right);

    /// <summary>Compares two record identifiers for exact ordinal inequality.</summary>
    public static bool operator !=(RecordId left, RecordId right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Value;
}

internal static class BasePrimitiveId
{
    /// <summary>Executes the is valid operation.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !value.IsNormalized(NormalizationForm.FormC)
            || Encoding.UTF8.GetByteCount(value) is < 1 or > 256)
            return false;
        ReadOnlySpan<char> remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            System.Buffers.OperationStatus status = Rune.DecodeFromUtf16(remaining, out Rune rune, out int consumed);
            if (status != System.Buffers.OperationStatus.Done
                || Rune.GetUnicodeCategory(rune) == System.Globalization.UnicodeCategory.Control)
                return false;
            remaining = remaining[consumed..];
        }
        return true;
    }
}
