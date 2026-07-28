using System.Text.Json.Serialization;
using HPD.Base.Serialization;

namespace HPD.Base.Dependencies.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters =
    [
        typeof(LowerCamelJsonStringEnumConverter<BaseDependencyKind>),
        typeof(LowerCamelJsonStringEnumConverter<BaseDependencyVisibility>)
    ])]
[JsonSerializable(typeof(BaseDependencyTemplate))]
[JsonSerializable(typeof(BaseDependencyTemplate[]))]
[JsonSerializable(typeof(BaseDependencyReference))]
[JsonSerializable(typeof(BaseDependencyReference[]))]
[JsonSerializable(typeof(BaseDependencySet))]
[JsonSerializable(typeof(BaseDependencyInvalidation))]
public sealed partial class HPDBaseDependenciesJsonSerializerContext : JsonSerializerContext;
