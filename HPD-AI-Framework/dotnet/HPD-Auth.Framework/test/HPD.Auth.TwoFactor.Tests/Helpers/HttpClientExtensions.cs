using System.Net.Http.Json;
using System.Text.Json;

namespace HPD.Auth.TwoFactor.Tests.Helpers;

public static class HttpClientExtensions
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static Task<HttpResponseMessage> PostJsonAsync<T>(
        this HttpClient client, string url, T body)
        => client.PostAsync(url, JsonContent.Create(body, options: _json));

    public static Task<HttpResponseMessage> PatchJsonAsync<T>(
        this HttpClient client, string url, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, url)
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

    public static async Task<JsonElement> ReadJsonElementAsync(this HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(content, _json);
    }
}
