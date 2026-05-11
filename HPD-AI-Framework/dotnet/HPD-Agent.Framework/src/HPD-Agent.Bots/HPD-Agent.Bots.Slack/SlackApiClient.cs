using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HPD.Agent.Bots.Slack.Payloads;
using HPD.Agent.Secrets;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Bots.Slack;

// ── Supporting types ───────────────────────────────────────────────────────────

public record SlackSuggestedPrompt(
    [property: JsonPropertyName("title")]   string Title,
    [property: JsonPropertyName("message")] string Message
);

public record SlackFileUpload(
    string FileName,
    string MimeType,
    ReadOnlyMemory<byte> Content,
    string? Title = null
);

public record SlackMessage(
    [property: JsonPropertyName("type")]      string? Type,
    [property: JsonPropertyName("user")]      string? User,
    [property: JsonPropertyName("text")]      string? Text,
    [property: JsonPropertyName("ts")]        string Ts,
    [property: JsonPropertyName("thread_ts")] string? ThreadTs,
    [property: JsonPropertyName("reply_count")] int? ReplyCount = null
);

public record SlackPageResult<T>(
    IReadOnlyList<T> Items,
    string? NextCursor
);

public record SlackChannelInfo(
    [property: JsonPropertyName("id")]   string Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("is_im")] bool? IsIm
);

public record SlackUserInfo(
    [property: JsonPropertyName("id")]      string Id,
    [property: JsonPropertyName("name")]    string? Name,
    [property: JsonPropertyName("profile")] SlackUserProfile? Profile
);

public record SlackUserProfile(
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("real_name")]     string? RealName
);

internal sealed record SlackEphemeralMessageEnvelope(
    string ResponseUrl,
    string UserId);

internal sealed record SlackFileCompletion(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title);

// ── API client ─────────────────────────────────────────────────────────────────

/// <summary>
/// Wraps the Slack Web API. All calls use <see cref="IHttpClientFactory"/> with
/// <c>System.Text.Json</c> — AOT-compatible, no third-party Slack SDK dependency.
/// </summary>
/// <remarks>
/// Token resolution:
/// <list type="bullet">
///   <item>Single-workspace: resolves <c>"slack:BotToken"</c> (falls back to config BotToken).</item>
///   <item>Multi-workspace: resolves <c>"slack:BotToken:{teamId}"</c> via user-registered
///         <see cref="ISecretResolver"/> (wrap in <c>CachingSecretResolver</c> for TTL).</item>
/// </list>
/// On HTTP 401: call <c>secretResolver.Evict("slack:BotToken:{teamId}")</c> before retry.
/// </remarks>
public sealed class SlackApiClient(
    IOptions<SlackBotConfig> options,
    ISecretResolver secretResolver,
    IHttpClientFactory httpClientFactory)
{
    private const string ApiBase = "https://slack.com/api/";
    private readonly SlackBotConfig _config = options.Value;

    // Bot user ID cache — populated on first call to FetchBotUserIdAsync
    private string? _botUserId;

    // ── Core messaging ─────────────────────────────────────────────────────────

    public async Task<string> PostMessageAsync(
        string channel, string threadTs, string text, CancellationToken ct)
    {
        var body = JsonBody(
            ("channel", JsonValue.Create(channel)),
            ("thread_ts", StringOrNull(threadTs)),
            ("text", JsonValue.Create(text)));
        return await PostAndGetTsAsync("chat.postMessage", body, null, ct);
    }

    public async Task<string> PostMessageAsync(
        string channel, string threadTs, IReadOnlyList<SlackBlock> blocks, CancellationToken ct)
    {
        var body = JsonBody(
            ("channel", JsonValue.Create(channel)),
            ("thread_ts", StringOrNull(threadTs)),
            ("blocks", BlocksNode(blocks)));
        return await PostAndGetTsAsync("chat.postMessage", body, null, ct);
    }

    public Task UpdateMessageAsync(string channel, string ts, string text, CancellationToken ct)
    {
        var body = JsonBody(
            ("channel", JsonValue.Create(channel)),
            ("ts", JsonValue.Create(ts)),
            ("text", JsonValue.Create(text)));
        return PostAsync("chat.update", body, null, ct);
    }

    public Task UpdateMessageAsync(
        string channel, string ts, IReadOnlyList<SlackBlock> blocks, CancellationToken ct)
    {
        var body = JsonBody(
            ("channel", JsonValue.Create(channel)),
            ("ts", JsonValue.Create(ts)),
            ("blocks", BlocksNode(blocks)));
        return PostAsync("chat.update", body, null, ct);
    }

    public Task UpdateMessageAsync(
        string channel, string ts, string fallbackText,
        IReadOnlyList<SlackBlock> blocks, CancellationToken ct)
    {
        var body = JsonBody(
            ("channel", JsonValue.Create(channel)),
            ("ts", JsonValue.Create(ts)),
            ("text", JsonValue.Create(fallbackText)),
            ("blocks", BlocksNode(blocks)));
        return PostAsync("chat.update", body, null, ct);
    }

    public Task DeleteMessageAsync(string channel, string ts, CancellationToken ct)
    {
        var body = JsonBody(
            ("channel", JsonValue.Create(channel)),
            ("ts", JsonValue.Create(ts)));
        return PostAsync("chat.delete", body, null, ct);
    }

    public Task PostEphemeralAsync(
        string channel, string userId, string text, CancellationToken ct)
    {
        var body = JsonBody(
            ("channel", JsonValue.Create(channel)),
            ("user", JsonValue.Create(userId)),
            ("text", JsonValue.Create(text)));
        return PostAsync("chat.postEphemeral", body, null, ct);
    }

    // ── Ephemeral message routing ──────────────────────────────────────────────
    // Ephemeral messages can't be edited/deleted via chat.update — they require the
    // response_url Slack provides at block_actions time. We encode it into the message
    // ID so callers can route edits/deletes without external state.

    public string EncodeEphemeralMessageId(string messageTs, string responseUrl, string userId)
    {
        var json = JsonSerializer.Serialize(
            new SlackEphemeralMessageEnvelope(responseUrl, userId),
            SlackBotJsonContext.Default.SlackEphemeralMessageEnvelope);
        return $"ephemeral:{messageTs}:{Convert.ToBase64String(Encoding.UTF8.GetBytes(json))}";
    }

    public (string ResponseUrl, string UserId) DecodeEphemeralMessageId(string messageId)
    {
        var parts = messageId.Split(':', 3);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
        using var doc = JsonDocument.Parse(json);
        var envelope = doc.RootElement.Deserialize(SlackBotJsonContext.Default.SlackEphemeralMessageEnvelope)
            ?? throw new InvalidOperationException("Invalid Slack ephemeral message envelope.");
        return (envelope.ResponseUrl, envelope.UserId);
    }

    public bool IsEphemeralMessageId(string messageId) =>
        messageId.StartsWith("ephemeral:", StringComparison.Ordinal);

    public Task SendToResponseUrlAsync(
        string responseUrl, string action,
        IReadOnlyList<SlackBlock>? blocks, CancellationToken ct)
    {
        // POST directly to responseUrl — no auth header needed for response_url calls.
        var body = blocks is not null
            ? JsonBody(
                ("replace_original", JsonValue.Create(action == "replace")),
                ("blocks", BlocksNode(blocks)),
                ("delete_original", JsonValue.Create(action == "delete")))
            : JsonBody(("delete_original", JsonValue.Create(true)));
        return PostRawAsync(responseUrl, body, ct);
    }

    // ── Reactions ─────────────────────────────────────────────────────────────

    public Task AddReactionAsync(string channel, string ts, string emoji, CancellationToken ct)
    {
        var body = JsonBody(
            ("channel", JsonValue.Create(channel)),
            ("timestamp", JsonValue.Create(ts)),
            ("name", JsonValue.Create(BotEmojiResolver.ToSlackName(emoji))));
        return PostAsync("reactions.add", body, null, ct);
    }

    public Task RemoveReactionAsync(string channel, string ts, string emoji, CancellationToken ct)
    {
        var body = JsonBody(
            ("channel", JsonValue.Create(channel)),
            ("timestamp", JsonValue.Create(ts)),
            ("name", JsonValue.Create(BotEmojiResolver.ToSlackName(emoji))));
        return PostAsync("reactions.remove", body, null, ct);
    }

    // ── Modals ─────────────────────────────────────────────────────────────────

    public async Task<string> OpenModalAsync(string triggerId, SlackView view, CancellationToken ct)
    {
        var body = JsonBody(
            ("trigger_id", JsonValue.Create(triggerId)),
            ("view", ViewNode(view)));
        using var response = await PostJsonAsync("views.open", body, null, ct);
        using var doc = await ReadJsonDocumentAsync(response.Content, ct);
        return doc!.RootElement.GetProperty("view").GetProperty("id").GetString()!;
    }

    public async Task<string> UpdateModalAsync(string viewId, SlackView view, CancellationToken ct)
    {
        var body = JsonBody(
            ("view_id", JsonValue.Create(viewId)),
            ("view", ViewNode(view)));
        using var response = await PostJsonAsync("views.update", body, null, ct);
        using var doc = await ReadJsonDocumentAsync(response.Content, ct);
        return doc!.RootElement.GetProperty("view").GetProperty("id").GetString()!;
    }

    // ── Native streaming ───────────────────────────────────────────────────────
    // chat.startStream / chat.appendStream / chat.stopStream
    // Only available in Assistants threads. Falls back to PostAndEdit for channel messages.

    public async Task<string> StartStreamAsync(
        string channelId, string threadTs,
        string? recipientUserId, string? recipientTeamId,
        CancellationToken ct)
    {
        var body = JsonBody(
            ("channel_id", JsonValue.Create(channelId)),
            ("thread_ts", StringOrNull(threadTs)),
            ("recipient_user_id", StringOrNull(recipientUserId)),
            ("recipient_team_id", StringOrNull(recipientTeamId)));
        return await PostAndGetTsAsync("chat.startStream", body, null, ct);
    }

    public Task AppendStreamAsync(string channelId, string ts, string markdownText, CancellationToken ct)
    {
        var body = JsonBody(
            ("channel_id", JsonValue.Create(channelId)),
            ("ts", JsonValue.Create(ts)),
            ("markdown_text", JsonValue.Create(markdownText)));
        return PostAsync("chat.appendStream", body, null, ct);
    }

    public Task StopStreamAsync(
        string channelId, string ts, string markdownText,
        IReadOnlyList<SlackBlock>? blocks, CancellationToken ct)
    {
        var body = blocks is not null
            ? JsonBody(
                ("channel_id", JsonValue.Create(channelId)),
                ("ts", JsonValue.Create(ts)),
                ("markdown_text", JsonValue.Create(markdownText)),
                ("blocks", BlocksNode(blocks)))
            : JsonBody(
                ("channel_id", JsonValue.Create(channelId)),
                ("ts", JsonValue.Create(ts)),
                ("markdown_text", JsonValue.Create(markdownText)));
        return PostAsync("chat.stopStream", body, null, ct);
    }

    // ── Assistants API ─────────────────────────────────────────────────────────
    // Uses channel_id (not channel) — matches assistant.threads.* method naming.

    public Task SetAssistantStatusAsync(
        string channelId, string threadTs, string status,
        IReadOnlyList<string>? loadingMessages, CancellationToken ct)
    {
        var body = loadingMessages is not null
            ? JsonBody(
                ("channel_id", JsonValue.Create(channelId)),
                ("thread_ts", JsonValue.Create(threadTs)),
                ("status", JsonValue.Create(status)),
                ("loading_messages", StringArrayNode(loadingMessages)))
            : JsonBody(
                ("channel_id", JsonValue.Create(channelId)),
                ("thread_ts", JsonValue.Create(threadTs)),
                ("status", JsonValue.Create(status)));
        return PostAsync("assistant.threads.setStatus", body, null, ct);
    }

    public async Task TrySetAssistantStatusAsync(
        string channelId, string threadTs, string status, CancellationToken ct)
    {
        try { await SetAssistantStatusAsync(channelId, threadTs, status, null, ct); }
        catch { /* no-op on 400 — not all threads are Assistants threads */ }
    }

    public async Task TryClearAssistantStatusAsync(string channelId, string threadTs, CancellationToken ct)
    {
        try { await SetAssistantStatusAsync(channelId, threadTs, "", null, ct); }
        catch { /* no-op */ }
    }

    public Task SetAssistantTitleAsync(
        string channelId, string threadTs, string title, CancellationToken ct)
    {
        var body = JsonBody(
            ("channel_id", JsonValue.Create(channelId)),
            ("thread_ts", JsonValue.Create(threadTs)),
            ("title", JsonValue.Create(title)));
        return PostAsync("assistant.threads.setTitle", body, null, ct);
    }

    public Task SetSuggestedPromptsAsync(
        string channelId, string threadTs,
        IReadOnlyList<SlackSuggestedPrompt> prompts,
        string? title,
        CancellationToken ct)
    {
        var body = title is not null
            ? JsonBody(
                ("channel_id", JsonValue.Create(channelId)),
                ("thread_ts", JsonValue.Create(threadTs)),
                ("prompts", SuggestedPromptsNode(prompts)),
                ("title", JsonValue.Create(title)))
            : JsonBody(
                ("channel_id", JsonValue.Create(channelId)),
                ("thread_ts", JsonValue.Create(threadTs)),
                ("prompts", SuggestedPromptsNode(prompts)));
        return PostAsync("assistant.threads.setSuggestedPrompts", body, null, ct);
    }

    // ── File uploads ───────────────────────────────────────────────────────────
    // V2 upload protocol (3 steps):
    //   1. files.getUploadURLExternal(filename, length) → { upload_url, file_id }
    //   2. Direct HTTP POST to upload_url (no Authorization header)
    //   3. files.completeUploadExternal(files: [{id, title}], channel_id, thread_ts)

    public async Task UploadFilesAsync(
        IReadOnlyList<SlackFileUpload> files,
        string channelId, string? threadTs, CancellationToken ct)
    {
        var completions = new List<SlackFileCompletion>(files.Count);

        foreach (var file in files)
        {
            // Step 1: get upload URL
            var urlBody = JsonBody(
                ("filename", JsonValue.Create(file.FileName)),
                ("length", JsonValue.Create(file.Content.Length)));
            using var urlResp = await PostJsonAsync("files.getUploadURLExternal", urlBody, null, ct);
            using var urlDoc  = await ReadJsonDocumentAsync(urlResp.Content, ct);
            var uploadUrl = urlDoc!.RootElement.GetProperty("upload_url").GetString()!;
            var fileId    = urlDoc!.RootElement.GetProperty("file_id").GetString()!;

            // Step 2: upload content — direct POST, no Authorization header
            using var http = httpClientFactory.CreateClient();
            using var content = new ReadOnlyMemoryContent(file.Content);
            content.Headers.ContentType = new(file.MimeType);
            using var uploadResp = await http.PostAsync(uploadUrl, content, ct);
            uploadResp.EnsureSuccessStatusCode();

            completions.Add(new SlackFileCompletion(fileId, file.Title ?? file.FileName));
        }

        // Step 3: complete all files in a single call
        var completeBody = JsonBody(
            ("files", FileCompletionsNode(completions)),
            ("channel_id", JsonValue.Create(channelId)),
            ("thread_ts", StringOrNull(threadTs)));
        await PostAsync("files.completeUploadExternal", completeBody, null, ct);
    }

    // ── Thread history ─────────────────────────────────────────────────────────

    public async Task<SlackPageResult<SlackMessage>> FetchThreadMessagesForwardAsync(
        string channel, string ts, int limit, string? cursor, CancellationToken ct)
    {
        var qs = BuildQueryString(new()
        {
            ["channel"] = channel,
            ["ts"]      = ts,
            ["limit"]   = limit.ToString(),
            ["cursor"]  = cursor
        });
        return await GetPageAsync<SlackMessage>("conversations.replies", qs, "messages", ct);
    }

    public async Task<SlackPageResult<SlackMessage>> FetchThreadMessagesBackwardAsync(
        string channel, string ts, int limit, CancellationToken ct)
    {
        // Slack API only returns oldest-first. Fetch up to 1000 and return the tail.
        // Known Slack limitation — no workaround exists.
        var result = await FetchThreadMessagesForwardAsync(channel, ts, 1000, null, ct);
        var tail = result.Items.TakeLast(limit).ToList();
        return new SlackPageResult<SlackMessage>(tail, null);
    }

    // ── Channel history ────────────────────────────────────────────────────────

    public async Task<SlackPageResult<SlackMessage>> FetchChannelHistoryAsync(
        string channel, int limit, string? cursor, string? latest, CancellationToken ct)
    {
        var qs = BuildQueryString(new()
        {
            ["channel"] = channel,
            ["limit"]   = limit.ToString(),
            ["cursor"]  = cursor,
            ["latest"]  = latest
        });
        return await GetPageAsync<SlackMessage>("conversations.history", qs, "messages", ct);
    }

    public async Task<SlackPageResult<SlackMessage>> ListThreadsAsync(
        string channel, int limit, string? cursor, CancellationToken ct)
    {
        // Filters history for messages with reply_count > 0 (i.e. thread parents).
        var all = await FetchChannelHistoryAsync(channel, 1000, cursor, null, ct);
        var threads = all.Items.Where(m => m.ReplyCount > 0).Take(limit).ToList();
        return new SlackPageResult<SlackMessage>(threads, all.NextCursor);
    }

    // ── Channel info ───────────────────────────────────────────────────────────

    public async Task<SlackChannelInfo?> FetchChannelInfoAsync(string channel, CancellationToken ct)
    {
        var qs = $"?channel={Uri.EscapeDataString(channel)}";
        using var http = await CreateAuthenticatedClientAsync(null, ct);
        using var resp = await http.GetAsync($"{ApiBase}conversations.info{qs}", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = await ReadJsonDocumentAsync(resp.Content, ct);
        return doc!.RootElement.TryGetProperty("channel", out var ch)
            ? ch.Deserialize(SlackBotJsonContext.Default.SlackChannelInfo)
            : null;
    }

    // ── User info ──────────────────────────────────────────────────────────────

    public async Task<SlackUserInfo?> FetchUserInfoAsync(string userId, CancellationToken ct)
    {
        var qs = $"?user={Uri.EscapeDataString(userId)}";
        using var http = await CreateAuthenticatedClientAsync(null, ct);
        using var resp = await http.GetAsync($"{ApiBase}users.info{qs}", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = await ReadJsonDocumentAsync(resp.Content, ct);
        return doc!.RootElement.TryGetProperty("user", out var u)
            ? u.Deserialize(SlackBotJsonContext.Default.SlackUserInfo)
            : null;
    }

    public async Task<string?> FetchBotUserIdAsync(CancellationToken ct)
    {
        if (_botUserId is not null) return _botUserId;
        using var http = await CreateAuthenticatedClientAsync(null, ct);
        using var resp = await http.GetAsync($"{ApiBase}auth.test", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = await ReadJsonDocumentAsync(resp.Content, ct);
        _botUserId = doc!.RootElement.GetProperty("user_id").GetString();
        return _botUserId;
    }

    // ── Direct messages ────────────────────────────────────────────────────────

    public async Task<string> OpenDMAsync(string userId, CancellationToken ct)
    {
        var body = JsonBody(("users", JsonValue.Create(userId)));
        using var response = await PostJsonAsync("conversations.open", body, null, ct);
        using var doc = await ReadJsonDocumentAsync(response.Content, ct);
        return doc!.RootElement.GetProperty("channel").GetProperty("id").GetString()!;
    }

    // ── Home tab ───────────────────────────────────────────────────────────────

    public Task PublishHomeViewAsync(string userId, SlackView view, CancellationToken ct)
    {
        var body = JsonBody(
            ("user_id", JsonValue.Create(userId)),
            ("view", ViewNode(view)));
        return PostAsync("views.publish", body, null, ct);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task PostAsync(string method, JsonObject body, string? teamId, CancellationToken ct)
    {
        using var response = await PostJsonAsync(method, body, teamId, ct);
        using var doc = await ReadJsonDocumentAsync(response.Content, ct);
        ThrowIfSlackError(response, doc);
    }

    private async Task<string> PostAndGetTsAsync(
        string method, JsonObject body, string? teamId, CancellationToken ct)
    {
        using var response = await PostJsonAsync(method, body, teamId, ct);
        using var doc = await ReadJsonDocumentAsync(response.Content, ct);
        ThrowIfSlackError(response, doc);
        return doc!.RootElement.GetProperty("ts").GetString()!;
    }

    private async Task<HttpResponseMessage> PostJsonAsync(
        string method, JsonObject body, string? teamId, CancellationToken ct)
    {
        using var http = await CreateAuthenticatedClientAsync(teamId, ct);
        using var content = ToJsonContent(body);
        var response = await http.PostAsync($"{ApiBase}{method}", content, ct);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private async Task PostRawAsync(string url, JsonObject body, CancellationToken ct)
    {
        using var http = httpClientFactory.CreateClient();
        using var content = ToJsonContent(body);
        var response = await http.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<SlackPageResult<T>> GetPageAsync<T>(
        string method, string queryString, string itemsField, CancellationToken ct)
    {
        using var http = await CreateAuthenticatedClientAsync(null, ct);
        using var resp = await http.GetAsync($"{ApiBase}{method}{queryString}", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = await ReadJsonDocumentAsync(resp.Content, ct);

        var items = doc!.RootElement.GetProperty(itemsField)
            .EnumerateArray()
            .Select(e => e.Deserialize(GetPageItemTypeInfo<T>())!)
            .ToList();

        string? cursor = null;
        if (doc.RootElement.TryGetProperty("response_metadata", out var meta) &&
            meta.TryGetProperty("next_cursor", out var nc) &&
            nc.GetString() is { Length: > 0 } c)
        {
            cursor = c;
        }

        return new SlackPageResult<T>(items, cursor);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string? teamId, CancellationToken ct)
    {
        var token = await GetTokenAsync(teamId, ct);
        var http = httpClientFactory.CreateClient("slack");
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private async ValueTask<string> GetTokenAsync(string? teamId, CancellationToken ct)
    {
        var key = teamId is not null ? $"slack:BotToken:{teamId}" : "slack:BotToken";
        var resolved = await secretResolver.ResolveAsync(key, ct);
        return resolved?.Value ?? _config.BotToken;
    }

    private static void ThrowIfSlackError(HttpResponseMessage response, JsonDocument? doc)
    {
        if (doc is null) return;
        if (!doc.RootElement.TryGetProperty("ok", out var ok) || ok.GetBoolean()) return;

        var error = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "unknown_error";
        SlackErrorHandler.ThrowMapped(error!, new HttpRequestException($"Slack API error: {error}"));
    }

    private static string BuildQueryString(Dictionary<string, string?> parameters)
    {
        var parts = parameters
            .Where(p => p.Value is not null)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}");
        return "?" + string.Join("&", parts);
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    private static JsonObject JsonBody(params (string Name, JsonNode? Value)[] properties)
    {
        var body = new JsonObject();
        foreach (var (name, value) in properties)
        {
            if (value is not null)
                body[name] = value;
        }

        return body;
    }

    private static JsonValue? StringOrNull(string? value)
        => string.IsNullOrEmpty(value) ? null : JsonValue.Create(value);

    private static JsonNode? BlocksNode(IReadOnlyList<SlackBlock>? blocks)
        => blocks is null
            ? null
            : JsonSerializer.SerializeToNode(blocks.ToArray(), SlackBotJsonContext.Default.SlackBlockArray);

    private static JsonNode? ViewNode(SlackView? view)
        => view is null
            ? null
            : JsonSerializer.SerializeToNode(view, SlackBotJsonContext.Default.SlackView);

    private static JsonNode? SuggestedPromptsNode(IReadOnlyList<SlackSuggestedPrompt>? prompts)
        => prompts is null
            ? null
            : JsonSerializer.SerializeToNode(prompts.ToArray(), SlackBotJsonContext.Default.SlackSuggestedPromptArray);

    private static JsonNode? FileCompletionsNode(IReadOnlyList<SlackFileCompletion>? files)
        => files is null
            ? null
            : JsonSerializer.SerializeToNode(files.ToArray(), SlackBotJsonContext.Default.SlackFileCompletionArray);

    private static JsonArray? StringArrayNode(IReadOnlyList<string>? values)
    {
        if (values is null)
            return null;

        var array = new JsonArray();
        foreach (var value in values)
            array.Add((JsonNode?)JsonValue.Create(value));

        return array;
    }

    private static StringContent ToJsonContent(JsonObject body)
        => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> ReadJsonDocumentAsync(HttpContent content, CancellationToken ct)
    {
        var json = await content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json);
    }

    private static JsonTypeInfo<T> GetPageItemTypeInfo<T>()
        => typeof(T) == typeof(SlackMessage)
            ? (JsonTypeInfo<T>)(object)SlackBotJsonContext.Default.SlackMessage
            : throw new NotSupportedException($"Slack page item type '{typeof(T).Name}' is not registered.");
}
