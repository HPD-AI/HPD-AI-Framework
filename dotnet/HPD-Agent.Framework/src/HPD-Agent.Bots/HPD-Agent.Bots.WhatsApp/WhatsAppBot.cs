using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Bots.Cards;
using HPD.Agent.Bots.Contracts;
using HPD.Agent.Bots.Session;
using HPD.Agent.Bots.Streaming;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

[assembly: InternalsVisibleTo("HPD-Agent.Bots.Tests")]

namespace HPD.Agent.Bots.WhatsApp;

[HpdBot("whatsapp")]
[HpdHttpMethods("GET", "POST")]
[HpdStreaming(StreamingStrategy.BufferAndPost, DebounceMs = 0)]
public partial class WhatsAppBot
{
    private const int MessageLimit = 4096;
    private readonly WhatsAppBotConfig _config;
    private readonly SessionManager? _sessionManager;
    private readonly AgentManager? _agentManager;
    private readonly PlatformSessionMapper? _sessionMapper;
    private readonly WhatsAppApiClient? _api;
    private readonly WhatsAppFormatConverter _formatter;
    private readonly string _appSecret;
    private readonly string _verifyToken;
    private readonly string _phoneNumberId;

    public WhatsAppBot(
        IOptions<WhatsAppBotConfig> options,
        SessionManager? sessionManager = null,
        AgentManager? agentManager = null,
        PlatformSessionMapper? sessionMapper = null,
        WhatsAppApiClient? api = null,
        WhatsAppFormatConverter? formatter = null)
    {
        _config = options.Value;
        _sessionManager = sessionManager;
        _agentManager = agentManager;
        _sessionMapper = sessionMapper;
        _api = api;
        _formatter = formatter ?? new WhatsAppFormatConverter();
        _appSecret = _config.ResolveAppSecret();
        _verifyToken = _config.ResolveVerifyToken();
        _phoneNumberId = _config.ResolvePhoneNumberId();
    }

    public event Action<WhatsAppButtonClickEvent>? OnButtonClick;
    public event Action<WhatsAppReactionEvent>? OnReaction;

    [HpdBotPreDispatch]
    private async Task<BotAdapterResponse?> VerifyWhatsAppRequestAsync(BotRequestContext ctx, byte[] bodyBytes)
    {
        await Task.CompletedTask;

        if (string.Equals(ctx.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            var mode = ctx.QueryValue("hub.mode") ?? "";
            var token = ctx.QueryValue("hub.verify_token") ?? "";
            var challenge = ctx.QueryValue("hub.challenge") ?? "";
            return string.Equals(mode, "subscribe", StringComparison.Ordinal) &&
                string.Equals(token, _verifyToken, StringComparison.Ordinal)
                    ? BotAdapterResponse.Text(challenge)
                    : BotAdapterResponse.Status(403);
        }

        if (!string.Equals(ctx.Method, "POST", StringComparison.OrdinalIgnoreCase))
            return BotAdapterResponse.Status(405);

        return VerifySignature(bodyBytes, ctx.Header("x-hub-signature-256") ?? "")
            ? null
            : BotAdapterResponse.Status(401);
    }

    [HpdBotEnvelopeExtractor]
    private (string? eventType, byte[] dispatchBytes) ExtractWhatsAppEnvelope(BotRequestContext ctx, byte[] bodyBytes)
    {
        _ = ctx;
        try
        {
            using var doc = JsonDocument.Parse(bodyBytes);
            var value = doc.RootElement
                .GetProperty("entry")[0]
                .GetProperty("changes")[0]
                .GetProperty("value");

            if (value.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0)
                return (messages[0].GetProperty("type").GetString(), bodyBytes);

            return (null, bodyBytes);
        }
        catch (JsonException)
        {
            return (null, bodyBytes);
        }
        catch (InvalidOperationException)
        {
            return (null, bodyBytes);
        }
        catch (KeyNotFoundException)
        {
            return (null, bodyBytes);
        }
    }

    [HpdBotEventHandler("text")]
    [HpdBotEventHandler("image")]
    [HpdBotEventHandler("document")]
    [HpdBotEventHandler("audio")]
    [HpdBotEventHandler("voice")]
    [HpdBotEventHandler("video")]
    [HpdBotEventHandler("sticker")]
    [HpdBotEventHandler("location")]
    private async Task<BotAdapterResponse> HandleMessageAsync(BotRequestContext ctx, WhatsAppWebhookPayload payload)
        => await ProcessWebhookMessagesAsync(ctx, payload);

    [HpdBotEventHandler("interactive")]
    private async Task<BotAdapterResponse> HandleInteractiveAsync(BotRequestContext ctx, WhatsAppWebhookPayload payload)
        => await ProcessWebhookMessagesAsync(ctx, payload);

    [HpdBotEventHandler("button")]
    private async Task<BotAdapterResponse> HandleButtonAsync(BotRequestContext ctx, WhatsAppWebhookPayload payload)
        => await ProcessWebhookMessagesAsync(ctx, payload);

    [HpdBotEventHandler("reaction")]
    private async Task<BotAdapterResponse> HandleReactionAsync(BotRequestContext ctx, WhatsAppWebhookPayload payload)
        => await ProcessWebhookMessagesAsync(ctx, payload);

    private async Task<BotAdapterResponse> ProcessWebhookMessagesAsync(BotRequestContext ctx, WhatsAppWebhookPayload payload)
    {
        foreach (var (value, inbound) in EnumerateMessages(payload))
        {
            switch (inbound.Type)
            {
                case "text":
                case "image":
                case "document":
                case "audio":
                case "voice":
                case "video":
                case "sticker":
                case "location":
                    await ProcessInboundMessageAsync(ctx, value, inbound);
                    break;
                case "interactive":
                    ProcessInteractiveMessage(value, inbound);
                    break;
                case "button":
                    ProcessButtonMessage(value, inbound);
                    break;
                case "reaction":
                    ProcessReactionMessage(value, inbound);
                    break;
            }
        }

        return BotAdapterResponse.Ok();
    }

    private async Task ProcessInboundMessageAsync(
        BotRequestContext ctx,
        WhatsAppWebhookValue value,
        WhatsAppInboundMessage inbound)
    {
        var contact = value.Contacts?.FirstOrDefault(contact => contact.WaId == inbound.From)
            ?? value.Contacts?.FirstOrDefault();
        var platformThreadId = WhatsAppThreadId.Format(value.Metadata.PhoneNumberId, inbound.From);
        var parsed = ParseWhatsAppMessage(value.Metadata.PhoneNumberId, inbound, contact);

        if (_api is not null)
            await _api.MarkReadAsync(inbound.Id, showTypingIndicator: true, ctx.CancellationToken);

        if (_sessionMapper is not null && _sessionManager is not null && _agentManager is not null)
        {
            var (sessionId, threadId) = await _sessionMapper.ResolveAsync(platformThreadId, ctx.CancellationToken);
            _ = StreamToWhatsAppAsync(sessionId, threadId, parsed, CancellationToken.None);
        }
    }

    private void ProcessInteractiveMessage(WhatsAppWebhookValue value, WhatsAppInboundMessage inbound)
    {
        var reply = inbound.Interactive?.ButtonReply ?? inbound.Interactive?.ListReply;
        if (reply is null)
            return;

        var contact = value.Contacts?.FirstOrDefault(contact => contact.WaId == inbound.From)
            ?? value.Contacts?.FirstOrDefault();
        var threadId = WhatsAppThreadId.Format(value.Metadata.PhoneNumberId, inbound.From);
        var (actionId, callbackValue) = WhatsAppCardConverter.DecodeCallbackData(reply.Id);

        OnButtonClick?.Invoke(new WhatsAppButtonClickEvent(
            actionId,
            callbackValue ?? reply.Title,
            threadId,
            inbound.Context?.Id,
            BuildUser(inbound.From, contact),
            inbound));
    }

    private void ProcessButtonMessage(WhatsAppWebhookValue value, WhatsAppInboundMessage inbound)
    {
        if (inbound.Button is null)
            return;

        var contact = value.Contacts?.FirstOrDefault(contact => contact.WaId == inbound.From)
            ?? value.Contacts?.FirstOrDefault();
        var threadId = WhatsAppThreadId.Format(value.Metadata.PhoneNumberId, inbound.From);

        OnButtonClick?.Invoke(new WhatsAppButtonClickEvent(
            inbound.Button.Payload,
            inbound.Button.Text,
            threadId,
            inbound.Context?.Id,
            BuildUser(inbound.From, contact),
            inbound));
    }

    private void ProcessReactionMessage(WhatsAppWebhookValue value, WhatsAppInboundMessage inbound)
    {
        if (inbound.Reaction is null)
            return;

        var contact = value.Contacts?.FirstOrDefault(contact => contact.WaId == inbound.From)
            ?? value.Contacts?.FirstOrDefault();
        var threadId = WhatsAppThreadId.Format(value.Metadata.PhoneNumberId, inbound.From);
        var emoji = inbound.Reaction.Emoji ?? string.Empty;

        OnReaction?.Invoke(new WhatsAppReactionEvent(
            threadId,
            inbound.Reaction.MessageId,
            emoji,
            !string.IsNullOrEmpty(emoji),
            BuildUser(inbound.From, contact),
            inbound));
    }

    public async Task<string> PostMessageAsync(
        string threadId,
        string text,
        CardElement? card = null,
        CancellationToken ct = default)
    {
        if (_api is null)
            throw new InvalidOperationException("WhatsAppApiClient is required for outbound messages.");

        var thread = WhatsAppThreadId.Parse(threadId);
        var chunks = SplitMessage(card is null ? _formatter.RenderPlain(text) : _formatter.RenderCardFallback(card));
        if (chunks.Count == 0)
            throw new BotValidationException("WhatsApp message text cannot be empty.");

        string? lastMessageId = null;
        if (card is not null && chunks.Count == 1 && WhatsAppCardConverter.ToWhatsApp(card) is WhatsAppCardResult.Interactive interactive)
        {
            var sent = await _api.SendInteractiveAsync(thread.UserWaId, interactive.Message, ct);
            return sent.Messages?.FirstOrDefault()?.Id ?? string.Empty;
        }

        foreach (var chunk in chunks)
        {
            var sent = await _api.SendTextAsync(thread.UserWaId, chunk, ct);
            lastMessageId = sent.Messages?.FirstOrDefault()?.Id;
        }

        return lastMessageId ?? string.Empty;
    }

    public Task EditMessageAsync(string threadId, string messageId, string text, CancellationToken ct = default)
    {
        _ = threadId;
        _ = messageId;
        _ = text;
        _ = ct;
        throw new NotSupportedException("WhatsApp does not support editing messages.");
    }

    public Task DeleteMessageAsync(string threadId, string messageId, CancellationToken ct = default)
    {
        _ = threadId;
        _ = messageId;
        _ = ct;
        throw new NotSupportedException("WhatsApp does not support deleting messages.");
    }

    public async Task AddReactionAsync(string threadId, string messageId, string emoji, CancellationToken ct = default)
    {
        if (_api is null)
            throw new InvalidOperationException("WhatsAppApiClient is required for outbound reactions.");

        var thread = WhatsAppThreadId.Parse(threadId);
        await _api.SendReactionAsync(
            thread.UserWaId,
            messageId,
            BotEmojiResolver.ConvertPlaceholders(emoji, BotEmojiFormat.Unicode),
            ct);
    }

    public async Task RemoveReactionAsync(string threadId, string messageId, string emoji, CancellationToken ct = default)
    {
        _ = emoji;
        if (_api is null)
            throw new InvalidOperationException("WhatsAppApiClient is required for outbound reactions.");

        var thread = WhatsAppThreadId.Parse(threadId);
        await _api.SendReactionAsync(thread.UserWaId, messageId, string.Empty, ct);
    }

    public Task StartTypingAsync(string threadId, string? sourceMessageId = null, CancellationToken ct = default)
    {
        _ = threadId;
        return _api is not null && !string.IsNullOrWhiteSpace(sourceMessageId)
            ? _api.MarkReadAsync(sourceMessageId, showTypingIndicator: true, ct)
            : Task.CompletedTask;
    }

    public Task<WhatsAppFetchResult> FetchMessagesAsync(string threadId, CancellationToken ct = default)
    {
        _ = threadId;
        _ = ct;
        return Task.FromResult(new WhatsAppFetchResult([]));
    }

    public Task<string> OpenDmAsync(string userWaId, CancellationToken ct = default)
    {
        _ = ct;
        return Task.FromResult(WhatsAppThreadId.Format(_phoneNumberId, userWaId));
    }

    public Task<WhatsAppThreadInfo> FetchThreadAsync(string threadId, CancellationToken ct = default)
    {
        _ = ct;
        var parsed = WhatsAppThreadId.Parse(threadId);
        return Task.FromResult(new WhatsAppThreadInfo(
            threadId,
            parsed.ChannelId,
            $"WhatsApp: {parsed.UserWaId}",
            IsDM: true,
            Metadata: new { parsed.PhoneNumberId, parsed.UserWaId }));
    }

    public Task<WhatsAppChannelInfo> FetchChannelInfoAsync(string channelId, CancellationToken ct = default)
    {
        _ = ct;
        return Task.FromResult(new WhatsAppChannelInfo(
            channelId,
            $"WhatsApp: {channelId["whatsapp:".Length..]}",
            IsDM: false,
            MemberCount: null,
            Metadata: new { ChannelId = channelId }));
    }

    public static string ChannelIdFromThreadId(string threadId)
        => WhatsAppThreadId.ChannelIdFromThreadId(threadId);

    public static bool IsDm(string threadId)
    {
        _ = WhatsAppThreadId.Parse(threadId);
        return true;
    }

    public WhatsAppParsedMessage ParseMessage(WhatsAppRawMessage raw)
        => ParseWhatsAppMessage(raw.PhoneNumberId, raw.Message, raw.Contact);

    public string RenderFormatted(string text)
        => _formatter.RenderFormatted(text);

    internal WhatsAppParsedMessage ParseWhatsAppMessage(
        string phoneNumberId,
        WhatsAppInboundMessage inbound,
        WhatsAppContact? contact)
    {
        var threadId = WhatsAppThreadId.Format(phoneNumberId, inbound.From);
        return new WhatsAppParsedMessage(
            inbound.Id,
            threadId,
            ExtractTextContent(inbound),
            BuildUser(inbound.From, contact),
            ParseTimestamp(inbound.Timestamp),
            IsMention: false,
            BuildAttachments(inbound),
            inbound);
    }

    internal bool VerifySignature(byte[] body, string signature)
    {
        if (string.IsNullOrWhiteSpace(signature) ||
            !signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return false;

        var expected = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(_appSecret), body)).ToLowerInvariant();
        var actualBytes = Encoding.UTF8.GetBytes(signature);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    internal static string ExtractTextContent(WhatsAppInboundMessage message)
        => message.Type switch
        {
            "text" => message.Text?.Body ?? string.Empty,
            "image" => message.Image?.Caption ?? "[Image]",
            "document" => message.Document?.Caption ?? $"[Document: {message.Document?.FileName ?? "file"}]",
            "audio" => "[Audio message]",
            "voice" => "[Voice message]",
            "video" => message.Video?.Caption ?? "[Video]",
            "sticker" => "[Sticker]",
            "location" when message.Location is { } location => FormatLocation(location),
            _ => string.Empty,
        };

    internal static IReadOnlyList<WhatsAppAttachment> BuildAttachments(WhatsAppInboundMessage message)
    {
        var result = new List<WhatsAppAttachment>();
        AddMedia(result, "image", message.Image);
        AddMedia(result, "document", message.Document, message.Document?.FileName);
        AddMedia(result, "audio", message.Audio);
        AddMedia(result, "voice", message.Voice);
        AddMedia(result, "video", message.Video);
        AddMedia(result, "sticker", message.Sticker);
        if (message.Location is { } location)
        {
            result.Add(new WhatsAppAttachment(
                "location",
                $"{location.Latitude},{location.Longitude}",
                "application/geo+json",
                Caption: FormatLocation(location),
                Raw: new WhatsAppLocationAttachment(location.Latitude, location.Longitude, location.Name, location.Address)));
        }

        return result;
    }

    public async Task<IReadOnlyList<DataContent>> DownloadAttachmentsAsync(
        IReadOnlyList<WhatsAppAttachment> attachments,
        CancellationToken ct = default)
    {
        if (_api is null || attachments.Count == 0)
            return [];

        var result = new List<DataContent>(attachments.Count);
        foreach (var attachment in attachments)
        {
            if (attachment.Kind == "location")
            {
                var location = attachment.Raw as WhatsAppLocationAttachment;
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                    location,
                    WhatsAppBotJsonContext.Default.WhatsAppLocationAttachment));
                result.Add(new DataContent(bytes, "application/geo+json") { Name = "location.geojson" });
                continue;
            }

            var data = await _api.DownloadMediaAsync(attachment.MediaId, ct);
            DataContent content = attachment.Kind switch
            {
                "image" or "sticker" => new ImageContent(data, attachment.MimeType),
                "audio" or "voice" => new AudioContent(data, attachment.MimeType),
                "video" => new VideoContent(data, attachment.MimeType),
                "document" => new DocumentContent(data, attachment.MimeType ?? "application/octet-stream"),
                _ => new DataContent(data, attachment.MimeType ?? "application/octet-stream"),
            };
            content.Name = attachment.FileName;
            result.Add(content);
        }

        return result;
    }

    internal static IReadOnlyList<string> SplitMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        if (text.Length <= MessageLimit)
            return [text];

        var chunks = new List<string>();
        var remaining = text;
        while (remaining.Length > MessageLimit)
        {
            var splitAt = LastIndexBefore(remaining, "\n\n", MessageLimit);
            if (splitAt <= 0)
                splitAt = LastIndexBefore(remaining, "\n", MessageLimit);
            if (splitAt <= 0)
                splitAt = MessageLimit;

            chunks.Add(remaining[..splitAt].Trim());
            remaining = remaining[splitAt..].TrimStart();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
            chunks.Add(remaining);
        return chunks;
    }

    private static int LastIndexBefore(string value, string needle, int before)
        => value[..Math.Min(value.Length, before)].LastIndexOf(needle, StringComparison.Ordinal);

    private async Task StreamToWhatsAppAsync(
        string sessionId,
        string threadId,
        WhatsAppParsedMessage message,
        CancellationToken ct)
    {
        if (_sessionManager is null || _agentManager is null)
            return;

        try
        {
            var runner = new BotStreamingRunner(_sessionManager, _agentManager);
            var context = new WhatsAppStreamContext(message.ThreadId);
            await runner.RunAsync(
                new BotStreamingRequest<WhatsAppStreamContext>(
                    AgentId: _config.ResolveAgentId(),
                    SessionId: sessionId,
                    ThreadId: threadId,
                    Text: message.Text,
                    Context: context,
                    Strategy: StreamingStrategy.BufferAndPost,
                    DebounceMs: 0,
                    Attachments: await DownloadAttachmentsAsync(message.Attachments, ct)),
                new BotStreamingCallbacks<WhatsAppStreamContext>
                {
                    InitializeAsync = (_, _) => Task.CompletedTask,
                    UpdateTextAsync = (_, _, _) => Task.CompletedTask,
                    CompleteTextAsync = async (stream, content, token) =>
                        await PostMessageAsync(stream.ThreadId, content, ct: token),
                    CompleteCardAsync = async (stream, card, token) =>
                        await PostMessageAsync(stream.ThreadId, CardFallbackText.From(card), card, token),
                    HandlePermissionAsync = async (_, agent, req, token) =>
                        await agent.AnswerRequestAsync(new PermissionResponseEvent(
                            PermissionId: req.PermissionId,
                            SourceName: "whatsapp",
                            ChoiceId: "deny_once"), token),
                },
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private static IEnumerable<(WhatsAppWebhookValue Value, WhatsAppInboundMessage Message)> EnumerateMessages(
        WhatsAppWebhookPayload payload)
    {
        foreach (var entry in payload.Entry)
        {
            foreach (var change in entry.Changes)
            {
                if (change.Value.Messages is null)
                    continue;

                foreach (var message in change.Value.Messages)
                    yield return (change.Value, message);
            }
        }
    }

    private static void AddMedia(
        List<WhatsAppAttachment> result,
        string kind,
        WhatsAppMediaContent? media,
        string? fileName = null)
    {
        if (media is null)
            return;

        result.Add(new WhatsAppAttachment(
            kind,
            media.Id,
            media.MimeType,
            fileName,
            media.Caption,
            media.Sha256,
            media));
    }

    private static WhatsAppUserInfo BuildUser(string waId, WhatsAppContact? contact)
    {
        var name = contact?.Profile.Name;
        return new WhatsAppUserInfo(
            waId,
            name ?? waId,
            name ?? waId,
            IsBot: false);
    }

    private static DateTimeOffset ParseTimestamp(string timestamp)
        => long.TryParse(timestamp, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.MinValue;

    private static string FormatLocation(WhatsAppLocationContent location)
    {
        var label = location.Name ?? "Location";
        if (!string.IsNullOrWhiteSpace(location.Address))
            return $"[{label}: {location.Address}]";
        return $"[{label}: {location.Latitude},{location.Longitude}]";
    }

    private sealed record WhatsAppStreamContext(string ThreadId);
}
