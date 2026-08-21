using System.Text.Json.Serialization;

namespace HPD.Base;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BaseTextHttpQueryRequest))]
[JsonSerializable(typeof(BaseTextHttpResult))]
[JsonSerializable(typeof(BaseTextHttpError))]
[JsonSerializable(typeof(BaseTextHttpRebuildRequest))]
[JsonSerializable(typeof(BaseTextIndexStatus))]
[JsonSerializable(typeof(BaseTextIndexStatus[]))]
[JsonSerializable(typeof(BaseTextRebuildResult))]
internal sealed partial class BaseTextHttpJsonContext : JsonSerializerContext;
