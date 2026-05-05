using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Bots.Discord.Gateway;

public sealed class DiscordGatewayClient(
    IOptions<DiscordBotConfig> options,
    IHttpClientFactory httpClientFactory)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(7);
    private readonly DiscordBotConfig _config = options.Value;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private Uri? _cachedGatewayUri;
    private DateTimeOffset _cachedAt;

    public async Task<Uri> GetGatewayUriAsync(CancellationToken ct)
    {
        if (_cachedGatewayUri is not null &&
            DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
        {
            return _cachedGatewayUri;
        }

        var client = _httpClientFactory.CreateClient(nameof(DiscordApiClient));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v10/gateway/bot");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", _config.GatewayToken ?? _config.BotToken);

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var gateway = await response.Content.ReadFromJsonAsync(
            DiscordBotJsonContext.Default.DiscordGatewayBotResponse,
            ct);

        var url = gateway?.Url ?? "wss://gateway.discord.gg";
        _cachedGatewayUri = new Uri(url.Contains('?')
            ? url
            : $"{url}/?v=10&encoding=json");
        _cachedAt = DateTimeOffset.UtcNow;
        return _cachedGatewayUri;
    }
}

internal record DiscordGatewayBotResponse(
    [property: JsonPropertyName("url")] string Url);
