namespace HPD.ML.Serialization.Zip;

using System.Text.Json.Serialization;

/// <summary>
/// Source-generated JSON serializer context for AOT compatibility.
/// </summary>
[JsonSerializable(typeof(Manifest))]
[JsonSerializable(typeof(TransformEntry))]
[JsonSerializable(typeof(List<TransformEntry>))]
[JsonSerializable(typeof(SchemaInfo))]
internal partial class SerializerJsonContext : JsonSerializerContext { }
