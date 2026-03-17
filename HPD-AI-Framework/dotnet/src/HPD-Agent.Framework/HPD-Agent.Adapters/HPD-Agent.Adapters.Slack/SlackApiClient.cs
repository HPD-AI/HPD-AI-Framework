using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Adapters.Slack.Payloads;
using HPD.Agent.Secrets;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Adapters.Slack;

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
    IOptions<SlackAdapterConfig> options,
    ISecretResolver secretResolver,
    IHttpClientFactory httpClientFactory)
{
    private const string ApiBase = "https://slack.com/api/";
    private readonly SlackAdapterConfig _config = options.Value;

    // Bot user ID cache — populated on first call to FetchBotUserIdAsync
    private string? _botUserId;

    // ── Core messaging ─────────────────────────────────────────────────────────

    public async Task<string> PostMessageAsync(
        string channel, string threadTs, string text, CancellationToken ct)
    {
        var body = new { channel, thread_ts = NullIfEmpty(threadTs), text };
        return await PostAndGetTsAsync("chat.postMessage", body, null, ct);
    }

    public async Task<string> PostMessageAsync(
        string channel, string threadTs, IReadOnlyList<SlackBlock> blocks, CancellationToken ct)
    {
        var body = new { channel, thread_ts = NullIfEmpty(threadTs), blocks };
        return await PostAndGetTsAsync("chat.postMessage", body, null, ct);
    }

    public Task UpdateMessageAsync(string channel, string ts, string text, CancellationToken ct)
    {
        var body = new { channel, ts, text };
        return PostAsync("chat.update", body, null, ct);
    }

    public Task UpdateMessageAsync(
        string channel, string ts, IReadOnlyList<SlackBlock> blocks, CancellationToken ct)
    {
        var body = new { channel, ts, blocks };
        return PostAsync("chat.update", body, null, ct);
    }

    public Task UpdateMessageAsync(
        string channel, string ts, string fallbackText,
        IReadOnlyList<SlackBlock> blocks, CancellationToken ct)
    {
        var body = new { channel, ts, text = fallbackText, blocks };
        return PostAsync("chat.update", body, null, ct);
    }

    public Task DeleteMessageAsync(string channel, string ts, CancellationToken ct)
    {
        var body = new { channel, ts };
        return PostAsync("chat.delete", body, null, ct);
    }

    public Task PostEphemeralAsync(
        string channel, string userId, string text, CancellationToken ct)
    {
        var body = new { channel, user = userId, text };
        return PostAsync("chat.postEphemeral", body, null, ct);
    }

    // ── Ephemeral message routing ──────────────────────────────────────────────
    // Ephemeral messages can't be edited/deleted via chat.update — they require the
    // response_url Slack provides at block_actions time. We encode it into the message
    // ID so callers can route edits/deletes without external state.

    public string EncodeEphemeralMessageId(string messageTs, string responseUrl, string userId)
    {
        var json = JsonSerializer.Serialize(new { responseUrl, userId });
        return $"ephemeral:{messageTs}:{Convert.ToBase64String(Encoding.UTF8.GetBytes(json))}";
    }

    public (string ResponseUrl, string UserId) DecodeEphemeralMessageId(string messageId)
    {
        var parts = messageId.Split(':', 3);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
        using var doc = JsonDocument.Parse(json);
        return (doc.RootElement.GetProperty("responseUrl").GetString()!,
                doc.RootElement.GetProperty("userId").GetString()!);
    }

    public bool IsEphemeralMessageId(string messageId) =>
        messageId.StartsWith("ephemeral:", StringComparison.Ordinal);

    public Task SendToResponseUrlAsync(
        string responseUrl, string action,
        IReadOnlyList<SlackBlock>? blocks, CancellationToken ct)
    {
        // POST directly to responseUrl — no auth header needed for response_url calls.
        var body = blocks is not null
            ? (object)new { replace_original = action == "replace", blocks, delete_original = action == "delete" }
            : new { delete_original = true };
        return PostRawAsync(responseUrl, body, ct);
    }

    // ── Reactions ─────────────────────────────────────────────────────────────

    public Task AddReactionAsync(string channel, string ts, string emoji, CancellationToken ct)
    {
        var body = new { channel, timestamp = ts, name = emoji };
        return PostAsync("reactions.add", body, null, ct);
    }

    public Task RemoveReactionAsync(string channel, string ts, string emoji, CancellationToken ct)
    {
        var body = new { channel, timestamp = ts, name = emoji };
        return PostAsync("reactions.remove", body, null, ct);
    }

    // ── Modals ─────────────────────────────────────────────────────────────────

    public async Task<string> OpenModalAsync(string triggerId, SlackView view, CancellationToken ct)
    {
        var body = new { trigger_id = triggerId, view };
        using var response = await PostJsonAsync("views.open", body, null, ct);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        return doc!.RootElement.GetProperty("view").GetProperty("id").GetString()!;
    }

    public async Task<string> UpdateModalAsync(string viewId, SlackView view, CancellationToken ct)
    {
        var body = new { view_id = viewId, view };
        using var response = await PostJsonAsync("views.update", body, null, ct);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
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
        var body = new
        {
            channel_id = channelId,
            thread_ts  = NullIfEmpty(threadTs),
            recipient_user_id = recipientUserId,
            recipient_team_id = recipientTeamId
        };
        return await PostAndGetTsAsync("chat.startStream", body, null, ct);
    }

    public Task AppendStreamAsync(string channelId, string ts, string markdownText, CancellationToken ct)
    {
        var body = new { channel_id = channelId, ts, markdown_text = markdownText };
        return PostAsync("chat.appendStream", body, null, ct);
    }

    public Task StopStreamAsync(
        string channelId, string ts, string markdownText,
        IReadOnlyList<SlackBlock>? blocks, CancellationToken ct)
    {
        var body = blocks is not null
            ? (object)new { channel_id = channelId, ts, markdown_text = markdownText, blocks }
            : new { channel_id = channelId, ts, markdown_text = markdownText };
        return PostAsync("chat.stopStream", body, null, ct);
    }

    // ── Assistants API ─────────────────────────────────────────────────────────
    // Uses channel_id (not channel) — matches assistant.threads.* method naming.

    public Task SetAssistantStatusAsync(
        string channelId, string threadTs, string status,
        IReadOnlyList<string>? loadingMessages, CancellationToken ct)
    {
        var body = loadingMessages is not null
            ? (object)new { channel_id = channelId, thread_ts = threadTs, status, loading_messages = loadingMessages }
            : new { channel_id = channelId, thread_ts = threadTs, status };
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
        var body = new { channel_id = channelId, thread_ts = threadTs, title };
        return PostAsync("assistant.threads.setTitle", body, null, ct);
    }

    public Task SetSuggestedPromptsAsync(
        string channelId, string threadTs,
        IReadOnlyList<SlackSuggestedPrompt> prompts,
        string? title,
        CancellationToken ct)
    {
        var body = title is not null
            ? (object)new { channel_id = channelId, thread_ts = threadTs, prompts, title }
            : new { channel_id = channelId, thread_ts = threadTs, prompts };
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
        var completions = new List<object>(files.Count);

        foreach (var file in files)
        {
            // Step 1: get upload URL
            var urlBody = new { filename = file.FileName, length = file.Content.Length };
            using var urlResp = await PostJsonAsync("files.getUploadURLExternal", urlBody, null, ct);
            using var urlDoc  = await urlResp.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            var uploadUrl = urlDoc!.RootElement.GetProperty("upload_url").GetString()!;
            var fileId    = urlDoc!.RootElement.GetProperty("file_id").GetString()!;

            // Step 2: upload content — direct POST, no Authorization header
            using var http = httpClientFactory.CreateClient();
            using var content = new ReadOnlyMemoryContent(file.Content);
            content.Headers.ContentType = new(file.MimeType);
            using var uploadResp = await http.PostAsync(uploadUrl, content, ct);
            uploadResp.EnsureSuccessStatusCode();

            completions.Add(new { id = fileId, title = file.Title ?? file.FileName });
        }

        // Step 3: complete all files in a single call
        var completeBody = new
        {
            files      = completions,
            channel_id = channelId,
            thread_ts  = NullIfEmpty(threadTs ?? "")
        };
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
        using var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        return doc!.RootElement.TryGetProperty("channel", out var ch)
            ? ch.Deserialize<SlackChannelInfo>()
            : null;
    }

    // ── User info ──────────────────────────────────────────────────────────────

    public async Task<SlackUserInfo?> FetchUserInfoAsync(string userId, CancellationToken ct)
    {
        var qs = $"?user={Uri.EscapeDataString(userId)}";
        using var http = await CreateAuthenticatedClientAsync(null, ct);
        using var resp = await http.GetAsync($"{ApiBase}users.info{qs}", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        return doc!.RootElement.TryGetProperty("user", out var u)
            ? u.Deserialize<SlackUserInfo>()
            : null;
    }

    public async Task<string?> FetchBotUserIdAsync(CancellationToken ct)
    {
        if (_botUserId is not null) return _botUserId;
        using var http = await CreateAuthenticatedClientAsync(null, ct);
        using var resp = await http.GetAsync($"{ApiBase}auth.test", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        _botUserId = doc!.RootElement.GetProperty("user_id").GetString();
        return _botUserId;
    }

    // ── Direct messages ────────────────────────────────────────────────────────

    public async Task<string> OpenDMAsync(string userId, CancellationToken ct)
    {
        var body = new { users = userId };
        using var response = await PostJsonAsync("conversations.open", body, null, ct);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        return doc!.RootElement.GetProperty("channel").GetProperty("id").GetString()!;
    }

    // ── Home tab ───────────────────────────────────────────────────────────────

    public Task PublishHomeViewAsync(string userId, SlackView view, CancellationToken ct)
    {
        var body = new { user_id = userId, view };
        return PostAsync("views.publish", body, null, ct);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task PostAsync(string method, object body, string? teamId, CancellationToken ct)
    {
        using var response = await PostJsonAsync(method, body, teamId, ct);
        ThrowIfSlackError(response, await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct));
    }

    private async Task<string> PostAndGetTsAsync(
        string method, object body, string? teamId, CancellationToken ct)
    {
        using var response = await PostJsonAsync(method, body, teamId, ct);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        ThrowIfSlackError(response, doc);
        return doc!.RootElement.GetProperty("ts").GetString()!;
    }

    private async Task<HttpResponseMessage> PostJsonAsync(
        string method, object body, string? teamId, CancellationToken ct)
    {
        using var http = await CreateAuthenticatedClientAsync(teamId, ct);
        var response = await http.PostAsJsonAsync($"{ApiBase}{method}", body, ct);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private async Task PostRawAsync(string url, object body, CancellationToken ct)
    {
        using var http = httpClientFactory.CreateClient();
        var response = await http.PostAsJsonAsync(url, body, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<SlackPageResult<T>> GetPageAsync<T>(
        string method, string queryString, string itemsField, CancellationToken ct)
    {
        using var http = await CreateAuthenticatedClientAsync(null, ct);
        using var resp = await http.GetAsync($"{ApiBase}{method}{queryString}", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);

        var items = doc!.RootElement.GetProperty(itemsField)
            .EnumerateArray()
            .Select(e => e.Deserialize<T>()!)
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
}
