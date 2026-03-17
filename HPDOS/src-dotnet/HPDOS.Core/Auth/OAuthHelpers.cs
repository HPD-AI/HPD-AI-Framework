using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPDOS.Core.Auth;

public static class OAuthHelpers
{
    public static string GenerateRandomString(int length = 32)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        return Base64UrlEncode(bytes);
    }

    public static string GenerateCodeVerifier() => GenerateRandomString(32);

    public static string GenerateCodeChallenge(string codeVerifier)
    {
        var bytes = Encoding.ASCII.GetBytes(codeVerifier);
        var hash = SHA256.HashData(bytes);
        return Base64UrlEncode(hash);
    }

    public static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        return Convert.FromBase64String(output);
    }

    public static bool OpenBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", url);
            else
                return false;
            return true;
        }
        catch { return false; }
    }

    public static string BuildUrl(string baseUrl, Dictionary<string, string> parameters)
    {
        if (parameters.Count == 0) return baseUrl;
        var query = string.Join("&", parameters
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        return $"{baseUrl}?{query}";
    }

    public static Dictionary<string, JsonElement>? ParseJwtClaims(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return null;
            var payload = Base64UrlDecode(parts[1]);
            var json = Encoding.UTF8.GetString(payload);
            return JsonSerializer.Deserialize(json, OAuthJsonContext.Default.DictionaryStringJsonElement);
        }
        catch { return null; }
    }

    public static string? GetJwtClaim(Dictionary<string, JsonElement>? claims, params string[] claimNames)
    {
        if (claims == null) return null;
        foreach (var name in claimNames)
        {
            if (!claims.TryGetValue(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0)
            {
                var first = value[0];
                if (first.ValueKind == JsonValueKind.String) return first.GetString();
                if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("id", out var id))
                    return id.GetString();
            }
        }
        return null;
    }

    public static string MaskToken(string? token, int visibleChars = 8)
    {
        if (string.IsNullOrEmpty(token)) return "(empty)";
        if (token.Length <= visibleChars) return new string('•', token.Length);
        return new string('•', Math.Min(8, token.Length - visibleChars)) + token[^visibleChars..];
    }

    public static string FormatTimeRemaining(TimeSpan timeSpan)
    {
        if (timeSpan <= TimeSpan.Zero) return "expired";
        if (timeSpan.TotalDays >= 1) { var d = (int)timeSpan.TotalDays; return d == 1 ? "1 day" : $"{d} days"; }
        if (timeSpan.TotalHours >= 1) { var h = (int)timeSpan.TotalHours; return h == 1 ? "1 hour" : $"{h} hours"; }
        if (timeSpan.TotalMinutes >= 1) { var m = (int)timeSpan.TotalMinutes; return m == 1 ? "1 minute" : $"{m} minutes"; }
        return "less than a minute";
    }
}

public static class OAuthHttpExtensions
{
    public static async Task<TokenResponse> ExchangeCodeForTokensAsync(
        this HttpClient httpClient,
        string tokenEndpoint,
        string code,
        string clientId,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken = default)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier
        });
        var response = await httpClient.PostAsync(tokenEndpoint, content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new OAuthException($"Token exchange failed: {response.StatusCode} - {json}");
        return JsonSerializer.Deserialize(json, OAuthJsonContext.Default.TokenResponse)
               ?? throw new OAuthException("Failed to parse token response");
    }

    public static async Task<TokenResponse> RefreshTokenAsync(
        this HttpClient httpClient,
        string tokenEndpoint,
        string refreshToken,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken
        });
        var response = await httpClient.PostAsync(tokenEndpoint, content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new OAuthException($"Token refresh failed: {response.StatusCode} - {json}");
        return JsonSerializer.Deserialize(json, OAuthJsonContext.Default.TokenResponse)
               ?? throw new OAuthException("Failed to parse token response");
    }
}

public class TokenResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("id_token")]
    public string? IdToken { get; set; }

    public long GetExpiresAtUnixMs(int? defaultExpiresIn = 3600)
    {
        var expiresIn = ExpiresIn ?? defaultExpiresIn ?? 3600;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (expiresIn * 1000);
    }
}

public class OAuthException : Exception
{
    public OAuthException(string message) : base(message) { }
    public OAuthException(string message, Exception innerException) : base(message, innerException) { }
}

[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal partial class OAuthJsonContext : JsonSerializerContext { }
