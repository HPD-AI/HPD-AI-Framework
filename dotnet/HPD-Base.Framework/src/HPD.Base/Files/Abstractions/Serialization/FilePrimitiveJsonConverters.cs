using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base.Files.Objects;

namespace HPD.Base.Files.Objects;

public sealed class FileBucketIdJsonConverter : JsonConverter<FileBucketId>
{
    public override FileBucketId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(FilePrimitiveJsonConverterHelpers.ReadString(reader, nameof(FileBucketId)));
    public override void Write(Utf8JsonWriter writer, FileBucketId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

public sealed class FileObjectIdJsonConverter : JsonConverter<FileObjectId>
{
    public override FileObjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(FilePrimitiveJsonConverterHelpers.ReadString(reader, nameof(FileObjectId)));
    public override void Write(Utf8JsonWriter writer, FileObjectId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

public sealed class FileObjectKeyJsonConverter : JsonConverter<FileObjectKey>
{
    public override FileObjectKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(FilePrimitiveJsonConverterHelpers.ReadString(reader, nameof(FileObjectKey)));
    public override void Write(Utf8JsonWriter writer, FileObjectKey value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

public sealed class FileObjectRevisionJsonConverter : JsonConverter<FileObjectRevision>
{
    public override FileObjectRevision Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(FilePrimitiveJsonConverterHelpers.ReadString(reader, nameof(FileObjectRevision)));
    public override void Write(Utf8JsonWriter writer, FileObjectRevision value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

public sealed class FileObjectChecksumJsonConverter : JsonConverter<FileObjectChecksum>
{
    public override FileObjectChecksum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(FilePrimitiveJsonConverterHelpers.ReadString(reader, nameof(FileObjectChecksum)));
    public override void Write(Utf8JsonWriter writer, FileObjectChecksum value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

public sealed class FileProviderRefJsonConverter : JsonConverter<FileProviderRef>
{
    public override FileProviderRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(FilePrimitiveJsonConverterHelpers.ReadString(reader, nameof(FileProviderRef)));
    public override void Write(Utf8JsonWriter writer, FileProviderRef value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

file static class FilePrimitiveJsonConverterHelpers
{
    public static string ReadString(Utf8JsonReader reader, string typeName)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException(typeName + " must be encoded as a JSON string.");

        return reader.GetString() ?? string.Empty;
    }
}
