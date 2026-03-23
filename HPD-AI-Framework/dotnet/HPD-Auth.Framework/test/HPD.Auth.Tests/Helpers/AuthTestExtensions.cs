using System.Net.Http.Json;
using System.Text.Json;

namespace HPD.Auth.Tests.Helpers;

/// <summary>
/// HTTP helpers shared by endpoint integration tests.
/// </summary>
internal static class AuthTestExtensions
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Signs up a user and returns the token response as a <see cref="JsonDocument"/>.
    /// RequireEmailConfirmation must be false on the factory for this to return tokens.
    /// </summary>
    public static async Task<JsonDocument> SignUpAsync(
        this HttpClient client,
        string email,
        string password = "Password1!")
    {
        var resp = await client.PostAsJsonAsync("/api/auth/signup", new { email, password });
        var content = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    /// <summary>
    /// Logs in with the password grant and returns the raw <see cref="JsonDocument"/>.
    /// </summary>
    public static async Task<JsonDocument> LoginAsync(
        this HttpClient client,
        string email,
        string password = "Password1!")
    {
        // TokenRequest uses PascalCase properties; ASP.NET minimal APIs deserialize with
        // camelCase naming policy, so we must send camelCase (grantType, not grant_type).
        var resp = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "password", username = email, password });
        var content = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    /// <summary>
    /// Logs in and returns the access_token string, or throws if login fails.
    /// </summary>
    public static async Task<string> GetAccessTokenAsync(
        this HttpClient client,
        string email,
        string password = "Password1!")
    {
        var doc = await client.LoginAsync(email, password);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// Returns a new <see cref="HttpClient"/> that sends a Bearer token on every request.
    /// </summary>
    public static void SetBearerToken(this HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    }

    /// <summary>
    /// Parses the JSON body of an <see cref="HttpResponseMessage"/> into a <see cref="JsonDocument"/>.
    /// </summary>
    public static async Task<JsonDocument> ReadJsonAsync(this HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    /// <summary>
    /// Deserializes the body of a response into the given type.
    /// </summary>
    public static Task<T?> ReadAs<T>(this HttpResponseMessage response)
        => response.Content.ReadFromJsonAsync<T>(_json);
}
