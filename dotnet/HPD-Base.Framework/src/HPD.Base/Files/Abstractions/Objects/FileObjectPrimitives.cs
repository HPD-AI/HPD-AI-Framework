using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Represents a file bucket ID.</summary>
[JsonConverter(typeof(FileBucketIdJsonConverter))]
public readonly record struct FileBucketId(string Value)
{
    /// <summary>Executes the create operation.</summary>
    public static FileBucketId Create(string value) => new(FilePrimitiveId.Require(value, nameof(value)));
    /// <summary>Executes the parse operation.</summary>
    public static FileBucketId Parse(string value) => Create(value);
    /// <summary>Executes the try parse operation.</summary>
    public static bool TryParse(string? value, out FileBucketId result) =>
        FilePrimitiveId.Try(value, static valid => new FileBucketId(valid), out result);
    /// <summary>Executes the to string operation.</summary>
    public override string ToString() => Value;
}

/// <summary>Represents a file object ID.</summary>
[JsonConverter(typeof(FileObjectIdJsonConverter))]
public readonly record struct FileObjectId(string Value)
{
    /// <summary>Executes the create operation.</summary>
    public static FileObjectId Create(string value) => new(FilePrimitiveId.Require(value, nameof(value)));
    /// <summary>Executes the parse operation.</summary>
    public static FileObjectId Parse(string value) => Create(value);
    /// <summary>Executes the try parse operation.</summary>
    public static bool TryParse(string? value, out FileObjectId result) =>
        FilePrimitiveId.Try(value, static valid => new FileObjectId(valid), out result);
    /// <summary>Executes the to string operation.</summary>
    public override string ToString() => Value;
}

/// <summary>Represents a file object key.</summary>
[JsonConverter(typeof(FileObjectKeyJsonConverter))]
public readonly record struct FileObjectKey(string Value)
{
    /// <summary>Executes the to string operation.</summary>
    public override string ToString() => Value;
}

/// <summary>Represents a file object revision.</summary>
[JsonConverter(typeof(FileObjectRevisionJsonConverter))]
public readonly record struct FileObjectRevision(string Value)
{
    /// <summary>Executes the to string operation.</summary>
    public override string ToString() => Value;
}

/// <summary>Represents a file object checksum.</summary>
[JsonConverter(typeof(FileObjectChecksumJsonConverter))]
public readonly record struct FileObjectChecksum(string Value)
{
    /// <summary>Executes the to string operation.</summary>
    public override string ToString() => Value;
}

/// <summary>Represents a file provider ref.</summary>
[JsonConverter(typeof(FileProviderRefJsonConverter))]
public readonly record struct FileProviderRef(string Value)
{
    /// <summary>Executes the to string operation.</summary>
    public override string ToString() => Value;
}

internal static class FilePrimitiveId
{
    /// <summary>Executes the require operation.</summary>
    public static string Require(string value, string parameterName) =>
        IsValid(value)
            ? value
            : throw new ArgumentException("The file identifier is invalid.", parameterName);

    /// <summary>Executes the try operation.</summary>
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
