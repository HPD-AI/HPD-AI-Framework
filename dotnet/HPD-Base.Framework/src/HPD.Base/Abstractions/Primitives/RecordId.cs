namespace HPD.Base;

/// <summary>
/// Identifies one record within a BASE collection.
/// </summary>
public readonly record struct RecordId(string Value)
{
    public static RecordId Create(string value) =>
        TryParse(value, out RecordId result)
            ? result
            : throw new ArgumentException("The record identifier is invalid.", nameof(value));

    public static RecordId Parse(string value) => Create(value);

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

    public override string ToString() => Value;
}

internal static class BasePrimitiveId
{
    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 256 &&
        !value.Any(char.IsControl);
}
