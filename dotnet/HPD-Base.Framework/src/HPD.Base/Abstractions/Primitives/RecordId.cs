namespace HPD.Base;

/// <summary>
/// Identifies one record within a BASE collection.
/// </summary>
public readonly record struct RecordId(string Value)
{
    /// <summary>Executes the create operation.</summary>
    public static RecordId Create(string value) =>
        TryParse(value, out RecordId result)
            ? result
            : throw new ArgumentException("The record identifier is invalid.", nameof(value));

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

    /// <summary>Executes the to string operation.</summary>
    public override string ToString() => Value;
}

internal static class BasePrimitiveId
{
    /// <summary>Executes the is valid operation.</summary>
    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 256 &&
        !value.Any(char.IsControl);
}
