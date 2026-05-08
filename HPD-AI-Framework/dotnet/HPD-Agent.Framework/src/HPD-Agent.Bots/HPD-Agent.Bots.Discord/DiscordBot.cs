using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Bots.Cards;
using HPD.Agent.Bots.Session;
using HPD.Agent.Bots.Streaming;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Bots.Discord.Gateway;
using HPD.Agent.Bots.Discord.Payloads;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

[assembly: InternalsVisibleTo("HPD-Agent.Bots.Tests")]

namespace HPD.Agent.Bots.Discord;

public record DiscordButtonClickEvent(
    string CustomId,
    string ThreadId,
    string? MessageId,
    DiscordUserInfo User,
    DiscordInteraction Payload);

public record DiscordModalSubmitEvent(
    string CustomId,
    IReadOnlyDictionary<string, string> Values,
    DiscordUserInfo User,
    DiscordInteraction Payload);

public record DiscordReactionEvent(
    string ThreadId,
    string MessageId,
    string Emoji,
    bool Added,
    DiscordUserInfo User);

public record DiscordAutocompleteEvent(
    DiscordInteraction Payload,
    DiscordUserInfo? User);

public record DiscordUserInfo(
    string UserId,
    string UserName,
    string FullName,
    bool IsBot);

[HpdBot("discord")]
[HpdStreaming(StreamingStrategy.PostAndEdit, DebounceMs = 1000)]
[HpdSocketTransport(typeof(DiscordGatewayService), ConfigProperty = nameof(DiscordBotConfig.GatewayToken))]
public partial class DiscordBot(
    IOptions<DiscordBotConfig> options,
    SessionManager? sessionManager = null,
    AgentManager? agentManager = null,
    PlatformSessionMapper? sessionMapper = null,
    DiscordApiClient? api = null,
    DiscordFormatConverter? formatter = null,
    IOptionsMonitor<BotStreamingOptions>? streamingOptions = null)
{
    private readonly DiscordBotConfig _config = options.Value;
    private readonly ConcurrentDictionary<string, DiscordThreadParentCacheEntry> _threadParentCache = new();
    private static readonly TimeSpan ThreadParentCacheTtl = TimeSpan.FromMinutes(5);

    public event Action<DiscordButtonClickEvent>? OnButtonClick;
    public event Action<DiscordModalSubmitEvent>? OnModalSubmit;
    public event Action<DiscordReactionEvent>? OnReaction;
    public event Action<DiscordAutocompleteEvent>? OnAutocomplete;

    [HpdPreDispatch]
    private async Task<IResult?> VerifyDiscordRequestAsync(HttpContext ctx, byte[] bodyBytes)
    {
        await Task.CompletedTask;

        var gatewayToken = ctx.Request.Headers["X-Discord-Gateway-Token"].ToString();
        if (!string.IsNullOrEmpty(gatewayToken))
            return CryptographicEquals(gatewayToken, _config.BotToken) ? null : Results.Unauthorized();

        var signature = ctx.Request.Headers["X-Signature-Ed25519"].ToString();
        var timestamp = ctx.Request.Headers["X-Signature-Timestamp"].ToString();

        return DiscordSignatureVerifier.Verify(bodyBytes, signature, timestamp, _config.PublicKey)
            ? null
            : Results.Unauthorized();
    }

    [HpdBodyExtractor]
    private (string? eventType, byte[] dispatchBytes) ExtractDiscordEvent(
        HttpContext ctx, byte[] bodyBytes)
    {
        var gatewayEventType = ctx.Request.Headers["X-Discord-Gateway-Event"].ToString();
        if (!string.IsNullOrEmpty(gatewayEventType))
            return (gatewayEventType, bodyBytes);

        try
        {
            using var doc = JsonDocument.Parse(bodyBytes);
            if (doc.RootElement.TryGetProperty("type", out var typeEl) &&
                typeEl.ValueKind == JsonValueKind.Number)
            {
                var typeName = typeEl.GetInt32() switch
                {
                    1 => "ping",
                    2 => "application_command",
                    3 => "message_component",
                    4 => "application_command_autocomplete",
                    5 => "modal_submit",
                    _ => null,
                };
                return (typeName, bodyBytes);
            }
        }
        catch (JsonException)
        {
        }

        return (null, bodyBytes);
    }

    [HpdWebhookHandler("ping")]
    private Task<IResult> HandlePingAsync(HttpContext ctx, DiscordInteraction payload)
    {
        _ = ctx;
        _ = payload;
        return Task.FromResult(JsonResponse(new DiscordInteractionResponse(Type: 1)));
    }

    [HpdWebhookHandler("application_command")]
    private async Task<IResult> HandleSlashCommandAsync(HttpContext ctx, DiscordInteraction payload)
    {
        var user = GetUser(payload);
        if (user is null)
            return JsonResponse(new DiscordInteractionResponse(Type: 5));

        var threadId = ResolveThreadId(payload.GuildId, payload.Channel, payload.ChannelId);
        var input = BuildInteractionInput(payload, user);

        if (sessionMapper is not null)
        {
            var (sessionId, branchId) = await sessionMapper.ResolveAsync(threadId, ctx.RequestAborted);
            _ = StreamToDiscordAsync(
                sessionId,
                branchId,
                input,
                threadId,
                payload.Token,
                sourceMessageId: null,
                ctx.RequestAborted);
        }

        return JsonResponse(new DiscordInteractionResponse(Type: 5));
    }

    [HpdWebhookHandler("message_component")]
    private Task<IResult> HandleButtonClickAsync(HttpContext ctx, DiscordInteraction payload)
    {
        _ = ctx;
        var user = GetUser(payload);
        if (user is null) return Task.FromResult(Results.Ok());

        var customId = payload.Data is { } data && data.TryGetProperty("custom_id", out var cid)
            ? cid.GetString()
            : null;
        if (customId is null) return Task.FromResult(Results.Ok());

        OnButtonClick?.Invoke(new DiscordButtonClickEvent(
            CustomId: customId,
            ThreadId: ResolveThreadId(payload.GuildId, payload.Channel, payload.ChannelId),
            MessageId: payload.Message?.Id,
            User: MapUser(user),
            Payload: payload));

        return Task.FromResult(JsonResponse(new DiscordInteractionResponse(Type: 6)));
    }

    [HpdWebhookHandler("modal_submit")]
    private Task<IResult> HandleModalSubmitAsync(HttpContext ctx, DiscordInteraction payload)
    {
        _ = ctx;
        var user = GetUser(payload);
        if (user is null) return Task.FromResult(Results.Ok());

        var customId = payload.Data is { } data && data.TryGetProperty("custom_id", out var cid)
            ? cid.GetString() ?? ""
            : "";

        OnModalSubmit?.Invoke(new DiscordModalSubmitEvent(
            CustomId: customId,
            Values: ExtractModalValues(payload.Data),
            User: MapUser(user),
            Payload: payload));

        return Task.FromResult(JsonResponse(new DiscordInteractionResponse(Type: 6)));
    }

    [HpdWebhookHandler("application_command_autocomplete")]
    private Task<IResult> HandleAutocompleteAsync(HttpContext ctx, DiscordInteraction payload)
    {
        _ = ctx;
        var user = GetUser(payload);
        OnAutocomplete?.Invoke(new DiscordAutocompleteEvent(payload, user is null ? null : MapUser(user)));
        return Task.FromResult(JsonResponse(new DiscordInteractionResponse(Type: 8)));
    }

    [HpdWebhookHandler("GATEWAY_MESSAGE_CREATE")]
    private async Task<IResult> HandleGatewayMessageAsync(HttpContext ctx, DiscordGatewayMessage data)
    {
        if (data.Author.Bot == true) return Results.Ok();
        if (!IsMentioned(data)) return Results.Ok();

        var guildId = data.GuildId ?? "@me";
        var threadId = await ResolveGatewayThreadIdAsync(guildId, data, ctx.RequestAborted);
        var input = BuildGatewayInput(data);

        if (sessionMapper is not null)
        {
            var (sessionId, branchId) = await sessionMapper.ResolveAsync(threadId, ctx.RequestAborted);
            var sourceMessageId = data.ChannelType is 11 or 12 ? null : data.Id;

            _ = StreamToDiscordAsync(
                sessionId,
                branchId,
                input,
                threadId,
                interactionToken: null,
                sourceMessageId,
                ctx.RequestAborted);
        }

        return Results.Ok(new { ok = true });
    }

    [HpdWebhookHandler("GATEWAY_MESSAGE_REACTION_ADD")]
    [HpdWebhookHandler("GATEWAY_MESSAGE_REACTION_REMOVE")]
    private async Task<IResult> HandleGatewayReactionAsync(HttpContext ctx, DiscordGatewayReaction data)
    {
        var user = data.User ?? data.Member?.User;
        if (user is null || user.Bot == true) return Results.Ok();

        var added = ctx.Request.Headers["X-Discord-Gateway-Event"].ToString()
            == "GATEWAY_MESSAGE_REACTION_ADD";
        var guildId = data.GuildId ?? "@me";
        var threadId = await ResolveGatewayReactionThreadIdAsync(guildId, data, ctx.RequestAborted);

        OnReaction?.Invoke(new DiscordReactionEvent(
            ThreadId: threadId,
            MessageId: data.MessageId,
            Emoji: NormalizeDiscordEmoji(data.Emoji.Name ?? data.Emoji.Id ?? "unknown"),
            Added: added,
            User: MapUser(user)));

        return Results.Ok(new { ok = true });
    }

    private bool IsMentioned(DiscordGatewayMessage data)
    {
        if (data.IsMention == true) return true;
        if (data.Mentions.Any(m => m.Id == _config.ApplicationId)) return true;
        return _config.MentionRoleIds.Count > 0 &&
            data.MentionRoles is not null &&
            data.MentionRoles.Any(r => _config.MentionRoleIds.Contains(r));
    }

    private static string ResolveThreadId(string? guildId, DiscordChannel? channel, string? channelId)
    {
        var guild = guildId ?? "@me";
        var chId = channelId ?? "";
        var isThread = channel?.Type is 11 or 12;
        return isThread && channel?.ParentId is not null
            ? DiscordThreadId.Format(guild, channel.ParentId, chId)
            : DiscordThreadId.Format(guild, chId, "");
    }

    private async Task<string> ResolveGatewayThreadIdAsync(string guildId, DiscordGatewayMessage data, CancellationToken ct)
    {
        if (data.ChannelType is 11 or 12 && data.Thread is not null)
            return DiscordThreadId.Format(guildId, data.Thread.ParentId, data.ChannelId);

        if (data.ChannelType is 11 or 12)
        {
            var parentId = await FetchThreadParentIdAsync(data.ChannelId, ct);
            if (!string.IsNullOrEmpty(parentId))
                return DiscordThreadId.Format(guildId, parentId, data.ChannelId);
        }

        return DiscordThreadId.Format(guildId, data.ChannelId, "");
    }

    private async Task<string> ResolveGatewayReactionThreadIdAsync(string guildId, DiscordGatewayReaction data, CancellationToken ct)
    {
        if (data.ChannelType is 11 or 12)
        {
            var parentId = await FetchThreadParentIdAsync(data.ChannelId, ct);
            if (!string.IsNullOrEmpty(parentId))
                return DiscordThreadId.Format(guildId, parentId, data.ChannelId);
        }

        return DiscordThreadId.Format(guildId, data.ChannelId, "");
    }

    private async Task<string?> FetchThreadParentIdAsync(string threadChannelId, CancellationToken ct)
    {
        if (_threadParentCache.TryGetValue(threadChannelId, out var cached) &&
            cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cached.ParentId;
        }

        if (api is null)
            return null;

        var channel = await api.FetchChannelAsync(threadChannelId, ct);
        if (string.IsNullOrEmpty(channel?.ParentId))
            return null;

        _threadParentCache[threadChannelId] = new DiscordThreadParentCacheEntry(
            channel.ParentId,
            DateTimeOffset.UtcNow.Add(ThreadParentCacheTtl));
        return channel.ParentId;
    }

    private static DiscordUser? GetUser(DiscordInteraction payload)
        => payload.Member?.User ?? payload.User;

    private async Task StreamToDiscordAsync(
        string sessionId,
        string branchId,
        AgentInput input,
        string threadId,
        string? interactionToken,
        string? sourceMessageId,
        CancellationToken ct)
    {
        if (sessionManager is null || agentManager is null || api is null || formatter is null)
            return;

        try
        {
            var context = new DiscordStreamContext(
                SessionId: sessionId,
                BranchId: branchId,
                Input: input,
                ThreadId: threadId,
                InteractionToken: interactionToken,
                SourceMessageId: sourceMessageId);

            var runner = new BotStreamingRunner(sessionManager, agentManager);
            var streaming = ResolveStreamingOptions();
            await runner.RunAsync(
                new BotStreamingRequest<DiscordStreamContext>(
                    AgentName: _config.AgentName ?? "default",
                    SessionId: sessionId,
                    BranchId: branchId,
                    Text: input.Text,
                    Context: context,
                    Strategy: streaming.Strategy,
                    DebounceMs: streaming.DebounceMs),
                new BotStreamingCallbacks<DiscordStreamContext>
                {
                    InitializeAsync = InitializeDiscordStreamAsync,
                    UpdateTextAsync = UpdateDiscordTextAsync,
                    CompleteTextAsync = UpdateDiscordTextAsync,
                    CompleteCardAsync = CompleteDiscordCardAsync,
                    HandlePermissionAsync = async (_, agent, req, token) =>
                        await agent.RespondAsync(new PermissionResponseEvent(
                            PermissionId: req.PermissionId,
                            SourceName: "discord",
                            Approved: false), token),
                },
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DISCORD] StreamToDiscordAsync exception: {ex}");
        }
    }

    private BotStreamingOptions ResolveStreamingOptions()
    {
        var defaults = streamingOptions?.Get("discord")
            ?? new BotStreamingOptions { DebounceMs = 1000 };

        return new BotStreamingOptions
        {
            Strategy = defaults.Strategy,
            DebounceMs = _config.StreamingDebounceMs is > 0
                ? _config.StreamingDebounceMs.Value
                : defaults.DebounceMs,
        };
    }

    private async Task InitializeDiscordStreamAsync(DiscordStreamContext context, CancellationToken ct)
    {
        var parsed = DiscordThreadId.Parse(context.ThreadId);
        context.PostChannelId = parsed.PostChannelId;

        if (context.InteractionToken is not null)
            return;

        if (context.SourceMessageId is not null)
        {
            var threadName = $"Reply to {context.Input.UserName}";
            var newThreadId = await api!.CreateThreadAsync(
                context.PostChannelId,
                context.SourceMessageId,
                threadName,
                ct);
            context.PostChannelId = newThreadId;

            if (sessionMapper is not null)
            {
                await sessionMapper.BindThreadAsync(
                    DiscordThreadId.Format(parsed.GuildId, parsed.ChannelId, newThreadId),
                    context.SessionId,
                    context.BranchId,
                    ct);
            }
        }

        context.MessageId = await api!.PostMessageAsync(context.PostChannelId, "...", ct);
    }

    private async Task UpdateDiscordTextAsync(DiscordStreamContext context, string content, CancellationToken ct)
    {
        if (context.InteractionToken is not null)
        {
            await EditInteractionAsync(context.InteractionToken, content, ct);
            return;
        }

        if (context.MessageId is null)
            return;

        await api!.EditMessageAsync(
            context.PostChannelId,
            context.MessageId,
            new DiscordMessagePayload(
                Content: TruncateContent(formatter!.ToDiscordMarkdown(content))),
            ct);
    }

    private async Task CompleteDiscordCardAsync(DiscordStreamContext context, CardElement card, CancellationToken ct)
    {
        var (embed, actionRows) = new DiscordCardRenderer().Render(card);
        var payload = new DiscordMessagePayload(
            Content: TruncateContent(CardFallbackText.From(card)),
            Embeds: [embed],
            Components: actionRows.Length > 0 ? [.. actionRows] : null);

        if (context.InteractionToken is not null)
        {
            await EditInteractionAsync(context.InteractionToken, payload, ct);
            return;
        }

        if (context.MessageId is null)
            return;

        await api!.EditMessageAsync(context.PostChannelId, context.MessageId, payload, ct);
    }

    private async Task EditInteractionAsync(string interactionToken, string content, CancellationToken ct)
    {
        var payload = new DiscordMessagePayload(
            Content: TruncateContent(formatter?.ToDiscordMarkdown(content) ?? content));

        await EditInteractionAsync(interactionToken, payload, ct);
    }

    private async Task EditInteractionAsync(string interactionToken, DiscordMessagePayload payload, CancellationToken ct)
    {
        if (api is null) return;

        await api.EditInteractionResponseAsync(
            _config.ApplicationId,
            interactionToken,
            payload,
            ct);
    }

    private static AgentInput BuildInteractionInput(DiscordInteraction payload, DiscordUser user)
    {
        var (command, text) = ParseSlashCommand(payload.Data);
        return new AgentInput(
            Text: $"{command} {text}".Trim(),
            UserId: user.Id,
            UserName: user.GlobalName ?? user.Username,
            IsMention: true,
            Extensions: new Dictionary<string, string>
            {
                ["discord:interactionToken"] = payload.Token,
                ["discord:interactionId"] = payload.Id,
            });
    }

    private AgentInput BuildGatewayInput(DiscordGatewayMessage data)
        => new(
            Text: AppendAttachmentLinks(
                formatter?.ToPlainText(data.Content) ?? StripMentions(data.Content),
                data.Attachments),
            UserId: data.Author.Id,
            UserName: data.Author.GlobalName ?? data.Author.Username,
            IsMention: true,
            Attachments: data.Attachments);

    private static (string Command, string Text) ParseSlashCommand(JsonElement? data)
    {
        if (data is null) return ("/unknown", "");
        var name = data.Value.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var commandParts = new List<string> { $"/{name}" };
        var valueParts = new List<string>();

        if (data.Value.TryGetProperty("options", out var options))
            CollectOptions(options, commandParts, valueParts);

        return (string.Join(" ", commandParts), string.Join(" ", valueParts).Trim());
    }

    private static void CollectOptions(JsonElement options, List<string> commandParts, List<string> valueParts)
    {
        foreach (var opt in options.EnumerateArray())
        {
            if (opt.TryGetProperty("value", out var value))
                valueParts.Add(value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText());
            else if (opt.TryGetProperty("options", out var nested))
            {
                if (opt.TryGetProperty("name", out var name))
                    commandParts.Add(name.GetString() ?? "");
                CollectOptions(nested, commandParts, valueParts);
            }
        }
    }

    private static string StripMentions(string content)
        => System.Text.RegularExpressions.Regex.Replace(content, @"<[@&#!]?\d+>", "").Trim();

    private static IReadOnlyDictionary<string, string> ExtractModalValues(JsonElement? data)
    {
        var result = new Dictionary<string, string>();
        if (data is null || !data.Value.TryGetProperty("components", out var rows)) return result;

        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("components", out var inputs)) continue;
            foreach (var input in inputs.EnumerateArray())
            {
                var customId = input.TryGetProperty("custom_id", out var cid) ? cid.GetString() : null;
                var value = input.TryGetProperty("value", out var v) ? v.GetString() : null;
                if (customId is not null && value is not null)
                    result[customId] = value;
            }
        }

        return result;
    }

    private static DiscordUserInfo MapUser(DiscordUser user) => new(
        UserId: user.Id,
        UserName: user.Username,
        FullName: user.GlobalName ?? user.Username,
        IsBot: user.Bot ?? false);

    private static string TruncateContent(string content)
        => content.Length <= 2000 ? content : content[..1997] + "...";

    private static string AppendAttachmentLinks(string text, IReadOnlyList<DiscordAttachment> attachments)
    {
        if (attachments.Count == 0)
            return text;

        var builder = new StringBuilder(text.Trim());
        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.Url))
                continue;

            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(attachment.Filename);
            builder.Append(": ");
            builder.Append(attachment.Url);
        }

        return builder.ToString();
    }

    private static string NormalizeDiscordEmoji(string emojiName)
        => BotEmojiResolver.ToDiscordName(emojiName);

    private static bool CryptographicEquals(string left, string right)
    {
        var leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
        var rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static IResult JsonResponse(DiscordInteractionResponse response)
        => Results.Json(response, DiscordBotJsonContext.Default.DiscordInteractionResponse);

    private record AgentInput(
        string Text,
        string UserId,
        string UserName,
        bool IsMention,
        DiscordAttachment[]? Attachments = null,
        IReadOnlyDictionary<string, string>? Extensions = null);

    private sealed record DiscordStreamContext(
        string SessionId,
        string BranchId,
        AgentInput Input,
        string ThreadId,
        string? InteractionToken,
        string? SourceMessageId)
    {
        public string PostChannelId { get; set; } = "";
        public string? MessageId { get; set; }
    }

    private sealed record DiscordThreadParentCacheEntry(
        string ParentId,
        DateTimeOffset ExpiresAtUtc);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordInteraction))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordCommandOption))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordGatewayMessage))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordGatewayReaction))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordInteractionResponse))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordMessagePayload))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordEmbed))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordActionRow))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordButton))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordOpenDmRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordCreateThreadRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordChannelInfo))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordUserProfile))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<DiscordMessage>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordThreadListResponse))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordGatewayBotResponse))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordGatewayFrame))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordGatewayOutgoingFrame))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordGatewayIdentifyPayload))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordGatewayIdentifyProperties))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscordGatewayResumePayload))]
[System.Text.Json.Serialization.JsonSerializable(typeof(JsonElement))]
internal partial class DiscordBotJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
