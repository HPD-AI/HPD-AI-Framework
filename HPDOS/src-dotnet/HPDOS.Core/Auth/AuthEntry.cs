using System.Text.Json.Serialization;

namespace HPDOS.Core.Auth;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OAuthEntry), "oauth")]
[JsonDerivedType(typeof(ApiKeyEntry), "api")]
[JsonDerivedType(typeof(WellKnownEntry), "wellknown")]
public abstract record AuthEntry
{
    /// <summary>
    /// Human-readable label of the auth method used (e.g. "ChatGPT subscription (browser)", "API key").
    /// Null for entries created before this field existed or via env var.
    /// </summary>
    [JsonPropertyName("methodLabel")]
    public string? MethodLabel { get; init; }

    public abstract string GetCredential();
}

public sealed record OAuthEntry : AuthEntry
{
    [JsonPropertyName("access")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("refresh")]
    public required string RefreshToken { get; init; }

    [JsonPropertyName("expires")]
    public required long ExpiresAtUnixMs { get; init; }

    [JsonPropertyName("accountId")]
    public string? AccountId { get; init; }

    [JsonPropertyName("enterpriseUrl")]
    public string? EnterpriseUrl { get; init; }

    [JsonIgnore]
    public bool IsExpired => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= ExpiresAtUnixMs;

    public bool ExpiresWithin(TimeSpan duration) =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)duration.TotalMilliseconds >= ExpiresAtUnixMs;

    [JsonIgnore]
    public DateTimeOffset ExpiresAt => DateTimeOffset.FromUnixTimeMilliseconds(ExpiresAtUnixMs);

    [JsonIgnore]
    public TimeSpan TimeRemaining => ExpiresAt - DateTimeOffset.UtcNow;

    public override string GetCredential() => AccessToken;
}

public sealed record ApiKeyEntry : AuthEntry
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    public override string GetCredential() => Key;
}

public sealed record WellKnownEntry : AuthEntry
{
    [JsonPropertyName("envVar")]
    public required string EnvVarName { get; init; }

    [JsonPropertyName("token")]
    public string? CachedToken { get; init; }

    public override string GetCredential() =>
        Environment.GetEnvironmentVariable(EnvVarName) ?? CachedToken ?? string.Empty;
}
