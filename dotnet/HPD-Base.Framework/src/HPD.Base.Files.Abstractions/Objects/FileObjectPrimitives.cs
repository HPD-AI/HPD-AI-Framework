using System.Text.Json.Serialization;

namespace HPD.Base.Files.Objects;

[JsonConverter(typeof(FileBucketIdJsonConverter))]
public readonly record struct FileBucketId(string Value)
{
    public static FileBucketId Create(string value) => new(FilePrimitiveId.Require(value, nameof(value)));
    public static FileBucketId Parse(string value) => Create(value);
    public static bool TryParse(string? value, out FileBucketId result) =>
        FilePrimitiveId.Try(value, static valid => new FileBucketId(valid), out result);
    public override string ToString() => Value;
}

[JsonConverter(typeof(FileObjectIdJsonConverter))]
public readonly record struct FileObjectId(string Value)
{
    public static FileObjectId Create(string value) => new(FilePrimitiveId.Require(value, nameof(value)));
    public static FileObjectId Parse(string value) => Create(value);
    public static bool TryParse(string? value, out FileObjectId result) =>
        FilePrimitiveId.Try(value, static valid => new FileObjectId(valid), out result);
    public override string ToString() => Value;
}

[JsonConverter(typeof(FileObjectKeyJsonConverter))]
public readonly record struct FileObjectKey(string Value)
{
    public override string ToString() => Value;
}

[JsonConverter(typeof(FileObjectRevisionJsonConverter))]
public readonly record struct FileObjectRevision(string Value)
{
    public override string ToString() => Value;
}

[JsonConverter(typeof(FileObjectChecksumJsonConverter))]
public readonly record struct FileObjectChecksum(string Value)
{
    public override string ToString() => Value;
}

[JsonConverter(typeof(FileProviderRefJsonConverter))]
public readonly record struct FileProviderRef(string Value)
{
    public override string ToString() => Value;
}

internal static class FilePrimitiveId
{
    public static string Require(string value, string parameterName) =>
        IsValid(value)
            ? value
            : throw new ArgumentException("The file identifier is invalid.", parameterName);

    public static bool Try<T>(
        string? value,
        Func<string, T> create,
        out T result)
    {
        if (!IsValid(value))
        {
            result = default!;
            return false;
        }

        result = create(value!);
        return true;
    }

    private static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 256 &&
        !value.Any(char.IsControl);
}
