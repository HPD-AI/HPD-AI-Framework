using System.Net.Http.Json;
using System.Text.Json;

namespace HPD.Auth.Admin.Tests.Helpers;

/// <summary>
/// Convenience helpers used across all admin test classes.
/// </summary>
public static class HttpClientExtensions
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static Task<HttpResponseMessage> PostJsonAsync<T>(
        this HttpClient client, string url, T body)
        => client.PostAsync(url, JsonContent.Create(body, options: _json));

    public static Task<HttpResponseMessage> PutJsonAsync<T>(
        this HttpClient client, string url, T body)
        => client.PutAsync(url, JsonContent.Create(body, options: _json));

    public static Task<HttpResponseMessage> DeleteJsonAsync<T>(
        this HttpClient client, string url, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url)
        {
            Content = JsonContent.Create(body, options: _json)
        };
        return client.SendAsync(request);
    }

    public static async Task<T?> ReadJsonAsync<T>(this HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(content, _json);
    }
}
