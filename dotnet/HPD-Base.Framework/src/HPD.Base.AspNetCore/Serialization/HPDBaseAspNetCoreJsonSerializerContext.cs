using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Source-generated JSON metadata for ASP.NET projection-local HPD.BASE wire types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(BaseMergePatchSelectionHttpRequest))]
[JsonSerializable(typeof(BaseDeleteSelectionHttpRequest))]
[JsonSerializable(typeof(BaseSelectionMutationHttpResult))]
public partial class HPDBaseAspNetCoreJsonSerializerContext : JsonSerializerContext
{
}
