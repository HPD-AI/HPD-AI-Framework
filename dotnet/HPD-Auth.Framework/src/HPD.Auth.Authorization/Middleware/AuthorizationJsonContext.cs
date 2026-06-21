using System.Text.Json.Serialization;

namespace HPD.Auth.Authorization.Middleware;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AuthorizationErrorResponse))]
[JsonSerializable(typeof(ForbiddenAuthorizationErrorResponse))]
internal sealed partial class AuthorizationJsonContext : JsonSerializerContext;

internal sealed record AuthorizationErrorResponse(
    string Error,
    string Message);

internal sealed record ForbiddenAuthorizationErrorResponse(
    string Error,
    string Message,
    List<string> Reasons);
