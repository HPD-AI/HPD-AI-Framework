using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Source-generated JSON metadata for ASP.NET projection-local HPD.BASE wire types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
public partial class HPDBaseAspNetCoreJsonSerializerContext : JsonSerializerContext
{
}
