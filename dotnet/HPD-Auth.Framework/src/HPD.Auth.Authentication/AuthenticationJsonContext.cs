using System.Text.Json.Serialization;

namespace HPD.Auth.Authentication;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AuthenticationErrorResponse))]
internal sealed partial class AuthenticationJsonContext : JsonSerializerContext;

internal sealed record AuthenticationErrorResponse(
    string Error,
    string ErrorDescription);
