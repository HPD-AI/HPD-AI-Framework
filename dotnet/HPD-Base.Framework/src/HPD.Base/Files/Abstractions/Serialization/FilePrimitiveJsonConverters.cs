using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Represents a file bucket ID JSON converter.</summary>
public sealed class FileBucketIdJsonConverter : JsonConverter<FileBucketId>
{
    /// <summary>Executes the read operation.</summary>
    public override FileBucketId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(FilePrimitiveJsonConverterHelpers.ReadString(reader, nameof(FileBucketId)));
    /// <summary>Executes the write operation.</summary>
    public override void Write(Utf8JsonWriter writer, FileBucketId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

/// <summary>Represents a file object ID JSON converter.</summary>
public sealed class FileObjectIdJsonConverter : JsonConverter<FileObjectId>
{
    /// <summary>Executes the read operation.</summary>
    public override FileObjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(FilePrimitiveJsonConverterHelpers.ReadString(reader, nameof(FileObjectId)));
    /// <summary>Executes the write operation.</summary>
    public override void Write(Utf8JsonWriter writer, FileObjectId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

/// <summary>Represents a file object key JSON converter.</summary>
public sealed class FileObjectKeyJsonConverter : JsonConverter<FileObjectKey>
{
    /// <summary>Executes the read operation.</summary>
    public override FileObjectKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(FilePrimitiveJsonConverterHelpers.ReadString(reader, nameof(FileObjectKey)));
    /// <summary>Executes the write operation.</summary>
    public override void Write(Utf8JsonWriter writer, FileObjectKey value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

/// <summary>Represents a file object revision JSON converter.</summary>
public sealed class FileObjectRevisionJsonConverter : JsonConverter<FileObjectRevision>
{
    /// <summary>Executes the read operation.</summary>
    public override FileObjectRevision Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(FilePrimitiveJsonConverterHelpers.ReadString(reader, nameof(FileObjectRevision)));
    /// <summary>Executes the write operation.</summary>
    public override void Write(Utf8JsonWriter writer, FileObjectRevision value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

/// <summary>Represents a file object checksum JSON converter.</summary>
public sealed class FileObjectChecksumJsonConverter : JsonConverter<FileObjectChecksum>
{
    /// <summary>Executes the read operation.</summary>
    public override FileObjectChecksum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(FilePrimitiveJsonConverterHelpers.ReadString(reader, nameof(FileObjectChecksum)));
    /// <summary>Executes the write operation.</summary>
    public override void Write(Utf8JsonWriter writer, FileObjectChecksum value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

/// <summary>Represents a file provider ref JSON converter.</summary>
public sealed class FileProviderRefJsonConverter : JsonConverter<FileProviderRef>
{
    /// <summary>Executes the read operation.</summary>
    public override FileProviderRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(FilePrimitiveJsonConverterHelpers.ReadString(reader, nameof(FileProviderRef)));
    /// <summary>Executes the write operation.</summary>
    public override void Write(Utf8JsonWriter writer, FileProviderRef value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

file static class FilePrimitiveJsonConverterHelpers
{
    /// <summary>Executes the read string operation.</summary>
    public static string ReadString(Utf8JsonReader reader, string typeName)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException(typeName + " must be encoded as a JSON string.");

        return reader.GetString() ?? string.Empty;
    }
}
