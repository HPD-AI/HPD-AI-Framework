using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HPD.Agent.Bots.Contracts;
using HPD.Agent.Bots.Discord.Payloads;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Bots.Discord;

public sealed class DiscordApiClient(
    IOptions<DiscordBotConfig> options,
    IHttpClientFactory httpClientFactory)
{
    private const string ApiBase = "https://discord.com/api/v10";
    private readonly DiscordBotConfig _config = options.Value;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public async Task<string> PostMessageAsync(string channelId, string content, CancellationToken ct)
        => await PostMessageAsync(channelId, new DiscordMessagePayload(Content: content), ct);

    public async Task<string> PostMessageAsync(string channelId, DiscordMessagePayload payload, CancellationToken ct)
    {
        using var response = await SendJsonAsync(HttpMethod.Post, $"/channels/{channelId}/messages", payload, DiscordBotJsonContext.Default.DiscordMessagePayload, botAuth: true, ct);
        var doc = await ReadJsonAsync(response, ct);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
    }

    public async Task EditMessageAsync(string channelId, string messageId, DiscordMessagePayload payload, CancellationToken ct)
        => await SendAndDiscardAsync(HttpMethod.Patch, $"/channels/{channelId}/messages/{messageId}", payload, DiscordBotJsonContext.Default.DiscordMessagePayload, botAuth: true, ct);

    public async Task DeleteMessageAsync(string channelId, string messageId, CancellationToken ct)
        => await SendAndDiscardAsync(HttpMethod.Delete, $"/channels/{channelId}/messages/{messageId}", ct, botAuth: true);

    public Task EditInteractionResponseAsync(string applicationId, string token, DiscordMessagePayload payload, CancellationToken ct)
        => SendAndDiscardAsync(HttpMethod.Patch, $"/webhooks/{applicationId}/{token}/messages/@original", payload, DiscordBotJsonContext.Default.DiscordMessagePayload, botAuth: false, ct);

    public async Task<string?> GetInteractionMessageIdAsync(string applicationId, string token, CancellationToken ct)
    {
        using var response = await SendJsonAsync(HttpMethod.Get, $"/webhooks/{applicationId}/{token}/messages/@original", ct, botAuth: false);
        var doc = await ReadJsonAsync(response, ct);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    public async Task<string> PostInteractionFollowupAsync(string applicationId, string token, DiscordMessagePayload payload, CancellationToken ct)
    {
        using var response = await SendJsonAsync(HttpMethod.Post, $"/webhooks/{applicationId}/{token}", payload, DiscordBotJsonContext.Default.DiscordMessagePayload, botAuth: false, ct);
        var doc = await ReadJsonAsync(response, ct);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
    }

    public Task AddReactionAsync(string channelId, string messageId, string emoji, CancellationToken ct)
        => SendAndDiscardAsync(HttpMethod.Put, $"/channels/{channelId}/messages/{messageId}/reactions/{Uri.EscapeDataString(emoji)}/@me", ct, botAuth: true);

    public Task RemoveReactionAsync(string channelId, string messageId, string emoji, CancellationToken ct)
        => SendAndDiscardAsync(HttpMethod.Delete, $"/channels/{channelId}/messages/{messageId}/reactions/{Uri.EscapeDataString(emoji)}/@me", ct, botAuth: true);

    public Task TriggerTypingAsync(string channelId, CancellationToken ct)
        => SendAndDiscardAsync(HttpMethod.Post, $"/channels/{channelId}/typing", ct, botAuth: true);

    public async Task<string> OpenDMAsync(string userId, CancellationToken ct)
    {
        using var response = await SendJsonAsync(HttpMethod.Post, "/users/@me/channels", new DiscordOpenDmRequest(userId), DiscordBotJsonContext.Default.DiscordOpenDmRequest, botAuth: true, ct);
        var doc = await ReadJsonAsync(response, ct);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
    }

    public async Task<string> CreateThreadAsync(string parentChannelId, string messageId, string name, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(nameof(DiscordApiClient));
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiBase + $"/channels/{parentChannelId}/messages/{messageId}/threads");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", _config.BotToken);
        request.Content = JsonContent.Create(new DiscordCreateThreadRequest(name), DiscordBotJsonContext.Default.DiscordCreateThreadRequest);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = response.Content is null ? "" : await response.Content.ReadAsStringAsync(ct);
            if (IsThreadAlreadyCreated(body))
                return messageId;

            await EnsureSuccessAsync(response, body);
        }

        var doc = await ReadJsonAsync(response, ct);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
    }

    public Task<DiscordChannelInfo?> FetchChannelAsync(string channelId, CancellationToken ct)
    {
        return GetJsonAsync(
            $"/channels/{channelId}",
            DiscordBotJsonContext.Default.DiscordChannelInfo,
            botAuth: true,
            ct);
    }

    public async Task<DiscordPageResult<DiscordMessage>> FetchMessagesAsync(string channelId, int limit, string? before, CancellationToken ct)
        => await FetchMessagesAsync(channelId, limit, before, after: null, ct);

    public async Task<DiscordPageResult<DiscordMessage>> FetchMessagesAsync(string channelId, int limit, string? before, string? after, CancellationToken ct)
    {
        var boundedLimit = Math.Clamp(limit, 1, 100);
        var path = $"/channels/{channelId}/messages?limit={boundedLimit}";
        if (!string.IsNullOrWhiteSpace(before))
            path += $"&before={Uri.EscapeDataString(before)}";
        else if (!string.IsNullOrWhiteSpace(after))
            path += $"&after={Uri.EscapeDataString(after)}";

        var messages = await GetJsonAsync(
            path,
            DiscordBotJsonContext.Default.ListDiscordMessage,
            botAuth: true,
            ct);

        return new DiscordPageResult<DiscordMessage>(
            messages ?? [],
            Before: messages is { Count: > 0 } ? messages[^1].Id : null,
            After: messages is { Count: > 0 } ? messages[0].Id : null);
    }

    public async Task<DiscordPageResult<DiscordThreadSummary>> ListThreadsAsync(string guildId, string channelId, int limit, CancellationToken ct)
    {
        var boundedLimit = Math.Clamp(limit, 1, 100);
        var threads = new List<DiscordThreadSummary>();

        var active = await GetJsonAsync(
            $"/guilds/{guildId}/threads/active",
            DiscordBotJsonContext.Default.DiscordThreadListResponse,
            botAuth: true,
            ct);
        if (active?.Threads is not null)
            threads.AddRange(active.Threads.Where(t => t.ParentId == channelId));

        var archived = await GetJsonAsync(
            $"/channels/{channelId}/threads/archived/public?limit={boundedLimit}",
            DiscordBotJsonContext.Default.DiscordThreadListResponse,
            botAuth: true,
            ct);
        if (archived?.Threads is not null)
            threads.AddRange(archived.Threads);

        return new DiscordPageResult<DiscordThreadSummary>(
            threads
                .GroupBy(t => t.Id)
                .Select(g => g.First())
                .Take(boundedLimit)
                .ToList());
    }

    public Task<DiscordUserProfile?> FetchUserAsync(string userId, CancellationToken ct)
    {
        return GetJsonAsync(
            $"/users/{Uri.EscapeDataString(userId)}",
            DiscordBotJsonContext.Default.DiscordUserProfile,
            botAuth: true,
            ct);
    }

    public async Task<string> PostMessageWithFilesAsync(string channelId, DiscordMessagePayload payload, IReadOnlyList<DiscordFileUpload> files, CancellationToken ct)
    {
        if (files.Count == 0)
            return await PostMessageAsync(channelId, payload, ct);

        var client = _httpClientFactory.CreateClient(nameof(DiscordApiClient));
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiBase + $"/channels/{channelId}/messages");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", _config.BotToken);

        using var content = new MultipartFormDataContent();
        content.Add(JsonContent.Create(payload, DiscordBotJsonContext.Default.DiscordMessagePayload), "payload_json");

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var streamContent = new StreamContent(file.Content);
            if (!string.IsNullOrWhiteSpace(file.ContentType))
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            content.Add(streamContent, $"files[{i}]", file.FileName);
        }

        request.Content = content;
        using var response = await client.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        var doc = await ReadJsonAsync(response, ct);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
    }

    internal async Task SendGatewayEventAsync(string forwardUrl, string gatewayEventType, byte[] bodyBytes, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(nameof(DiscordApiClient));
        using var request = new HttpRequestMessage(HttpMethod.Post, forwardUrl);
        request.Headers.Add("X-Discord-Gateway-Token", _config.BotToken);
        request.Headers.Add("X-Discord-Gateway-Event", gatewayEventType);
        request.Content = new ByteArrayContent(bodyBytes);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var response = await client.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task SendAndDiscardAsync<T>(HttpMethod method, string path, T payload, JsonTypeInfo<T> jsonTypeInfo, bool botAuth, CancellationToken ct)
    {
        using var response = await SendJsonAsync(method, path, payload, jsonTypeInfo, botAuth, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task SendAndDiscardAsync(HttpMethod method, string path, CancellationToken ct, bool botAuth)
    {
        using var response = await SendJsonAsync(method, path, ct, botAuth);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task<HttpResponseMessage> SendJsonAsync<T>(HttpMethod method, string path, T payload, JsonTypeInfo<T> jsonTypeInfo, bool botAuth, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(nameof(DiscordApiClient));
        using var request = new HttpRequestMessage(method, ApiBase + path);
        if (botAuth)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", _config.BotToken);
        request.Content = JsonContent.Create(payload, jsonTypeInfo);

        var response = await client.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return response;
    }

    private async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string path, CancellationToken ct, bool botAuth)
    {
        var client = _httpClientFactory.CreateClient(nameof(DiscordApiClient));
        using var request = new HttpRequestMessage(method, ApiBase + path);
        if (botAuth)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", _config.BotToken);
        var response = await client.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return response;
    }

    private async Task<T?> GetJsonAsync<T>(string path, JsonTypeInfo<T> jsonTypeInfo, bool botAuth, CancellationToken ct)
    {
        using var response = await SendJsonAsync(HttpMethod.Get, path, ct, botAuth);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, ct);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = response.Content is null ? "" : await response.Content.ReadAsStringAsync(ct);
        await EnsureSuccessAsync(response, body);
    }

    private static Task EnsureSuccessAsync(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode) return Task.CompletedTask;

        if (response.StatusCode == (HttpStatusCode)429)
            throw new BotRateLimitException(body);

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("code", out var code))
                    DiscordErrorHandler.ThrowMapped(code.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture), new HttpRequestException(body));
            }
            catch (JsonException)
            {
            }
        }

        response.EnsureSuccessStatusCode();
        return Task.CompletedTask;
    }

    private static bool IsThreadAlreadyCreated(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("code", out var code) &&
                code.ValueKind == JsonValueKind.Number &&
                code.GetInt32() == 160004;
        }
        catch (JsonException)
        {
            return body.Contains("160004", StringComparison.Ordinal);
        }
    }
}

internal record DiscordOpenDmRequest(
    [property: JsonPropertyName("recipient_id")] string RecipientId);

internal record DiscordCreateThreadRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("auto_archive_duration")] int AutoArchiveDuration = 1440);

internal record DiscordThreadListResponse(
    [property: JsonPropertyName("threads")] List<DiscordThreadSummary> Threads);

internal static class DiscordJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
