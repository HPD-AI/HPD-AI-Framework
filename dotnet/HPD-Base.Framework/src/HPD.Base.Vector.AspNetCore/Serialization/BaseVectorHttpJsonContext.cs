using System.Text.Json.Serialization;

namespace HPD.Base.Vector.AspNetCore;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, UseStringEnumConverter = true)]
[JsonSerializable(typeof(BaseVectorHttpQueryRequest))]
[JsonSerializable(typeof(BaseVectorHttpQueryResponse))]
[JsonSerializable(typeof(BaseVectorHttpError))]
[JsonSerializable(typeof(BaseVectorHttpRebuildRequest))]
[JsonSerializable(typeof(BaseVectorIndexStatus))]
[JsonSerializable(typeof(BaseVectorIndexStatus[]))]
[JsonSerializable(typeof(BaseVectorRebuildResult))]
internal sealed partial class BaseVectorHttpJsonContext : JsonSerializerContext;
