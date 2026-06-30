using System.Text.Json.Serialization;

namespace HPD.Base.Files.Objects;

[JsonConverter(typeof(FileBucketIdJsonConverter))]
public readonly record struct FileBucketId(string Value)
{
    public override string ToString() => Value;
}

[JsonConverter(typeof(FileObjectIdJsonConverter))]
public readonly record struct FileObjectId(string Value)
{
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
