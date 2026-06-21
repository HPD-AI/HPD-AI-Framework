using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Auth.Core.Models;
using HPD.Auth.Endpoints;

namespace HPD.Auth.Serialization;

/// <summary>
/// Source-generated JSON serialization context for HPD.Auth core endpoint DTOs.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
// Core auth requests
[JsonSerializable(typeof(SignUpRequest))]
[JsonSerializable(typeof(LogoutRequest))]
[JsonSerializable(typeof(UpdateUserRequest))]
[JsonSerializable(typeof(TokenRequest))]
[JsonSerializable(typeof(RecoverRequest))]
[JsonSerializable(typeof(VerifyRequest))]
[JsonSerializable(typeof(ResendRequest))]
[JsonSerializable(typeof(RevokeSessionsRequest))]
// Core auth responses
[JsonSerializable(typeof(AuthError))]
[JsonSerializable(typeof(MessageResponse))]
[JsonSerializable(typeof(TwoFactorRequiredResponse))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(UserTokenDto))]
[JsonSerializable(typeof(SessionResponse))]
[JsonSerializable(typeof(List<SessionResponse>))]
// Common embedded payloads
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(List<string>))]
internal partial class HPDAuthJsonSerializerContext : JsonSerializerContext
{
}
