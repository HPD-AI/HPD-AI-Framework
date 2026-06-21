using System.Collections.Concurrent;
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
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramFile = Telegram.Bot.Types.TGFile;
using TelegramMessage = Telegram.Bot.Types.Message;
using TelegramUpdate = Telegram.Bot.Types.Update;

[assembly: InternalsVisibleTo("HPD-Agent.Bots.Tests")]

namespace HPD.Agent.Bots.Telegram;

[HpdBot("telegram")]
[HpdStreaming(StreamingStrategy.PostAndEdit, DebounceMs = 500)]
public partial class TelegramBot
{
    private readonly TelegramBotConfig _config;
    private readonly SessionManager? _sessionManager;
    private readonly AgentManager? _agentManager;
    private readonly PlatformSessionMapper? _sessionMapper;
    private readonly ITelegramBotClient _bot;
    private readonly TelegramFormatConverter _formatter;
    private readonly IOptionsMonitor<BotStreamingOptions>? _streamingOptions;
    private readonly ConcurrentDictionary<string, List<TelegramParsedMessage>> _messageCache = new();
    private readonly string? _secretToken;
    private string? _botUserId;
    private string _userName;

    public TelegramBot(
        IOptions<TelegramBotConfig> options,
        SessionManager? sessionManager = null,
        AgentManager? agentManager = null,
        PlatformSessionMapper? sessionMapper = null,
        ITelegramBotClient? bot = null,
        TelegramFormatConverter? formatter = null,
        IOptionsMonitor<BotStreamingOptions>? streamingOptions = null,
        IHttpClientFactory? httpClientFactory = null)
    {
        _config = options.Value;
        _sessionManager = sessionManager;
        _agentManager = agentManager;
        _sessionMapper = sessionMapper;
        _formatter = formatter ?? new TelegramFormatConverter();
        _streamingOptions = streamingOptions;
        _secretToken = _config.ResolveSecretToken();
        _userName = NormalizeUserName(_config.ResolveUserName());

        _bot = bot ?? CreateBotClient(_config, httpClientFactory);
    }

    public event Action<TelegramButtonClickEvent>? OnButtonClick;
    public event Action<TelegramReactionEvent>? OnReaction;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            var me = await _bot.GetMe(ct);
            _botUserId = me.Id.ToString();
            if (string.IsNullOrWhiteSpace(_config.ResolveUserName()) && !string.IsNullOrWhiteSpace(me.Username))
                _userName = NormalizeUserName(me.Username);
        }
        catch (ApiRequestException ex)
        {
            throw MapTelegramException(ex);
        }
    }

    public Task ProcessUpdateAsync(TelegramUpdate update, CancellationToken ct = default)
        => ProcessUpdateCoreAsync(update, ct);

    [HpdBotPreDispatch]
    private async Task<BotAdapterResponse?> VerifySecretTokenAsync(BotRequestContext ctx, byte[] bodyBytes)
    {
        await Task.CompletedTask;
        _ = bodyBytes;

        if (string.IsNullOrEmpty(_secretToken))
            return null;

        var header = ctx.Header("x-telegram-bot-api-secret-token") ?? "";
        var expected = Encoding.UTF8.GetBytes(_secretToken);
        var actual = Encoding.UTF8.GetBytes(header);

        return expected.Length == actual.Length &&
            CryptographicOperations.FixedTimeEquals(actual, expected)
                ? null
                : BotAdapterResponse.Status(401);
    }

    [HpdBotEnvelopeExtractor]
    private (string? eventType, byte[] dispatchBytes) ExtractTelegramUpdate(BotRequestContext ctx, byte[] bodyBytes)
    {
        _ = ctx;
        try
        {
            using var doc = JsonDocument.Parse(bodyBytes);
            return (EventName(doc.RootElement), bodyBytes);
        }
        catch (JsonException)
        {
            return (null, bodyBytes);
        }
    }

    [HpdBotEventHandler("message")]
    private async Task<BotAdapterResponse> HandleMessageAsync(BotRequestContext ctx, JsonDocument updateJson)
    {
        var update = DeserializeUpdate(updateJson.RootElement);
        var message = update.Message ?? update.ChannelPost;
        if (message is null)
            return BotAdapterResponse.Ok();

        await ProcessMessageAsync(message, runAgent: true, ctx.CancellationToken);
        return BotAdapterResponse.Ok();
    }

    [HpdBotEventHandler("edited_message")]
    private Task<BotAdapterResponse> HandleEditedMessageAsync(BotRequestContext ctx, JsonDocument updateJson)
    {
        var update = DeserializeUpdate(updateJson.RootElement);
        var message = update.EditedMessage ?? update.EditedChannelPost;
        if (message is not null)
        {
            var parsed = ParseTelegramMessage(message, ResolveThreadId(message));
            CacheMessage(parsed);
        }

        _ = ctx;
        return Task.FromResult(BotAdapterResponse.Ok());
    }

    [HpdBotEventHandler("callback_query")]
    private async Task<BotAdapterResponse> HandleCallbackQueryAsync(BotRequestContext ctx, JsonDocument updateJson)
    {
        var update = DeserializeUpdate(updateJson.RootElement);
        if (update.CallbackQuery is { } query)
            await ProcessCallbackQueryAsync(query, ctx.CancellationToken);

        return BotAdapterResponse.Ok();
    }

    [HpdBotEventHandler("message_reaction")]
    private Task<BotAdapterResponse> HandleMessageReactionAsync(BotRequestContext ctx, JsonDocument updateJson)
    {
        var update = DeserializeUpdate(updateJson.RootElement);
        if (update.MessageReaction is { } reaction)
            ProcessReaction(reaction);

        _ = ctx;
        return Task.FromResult(BotAdapterResponse.Ok());
    }

    public async Task<TelegramParsedMessage> PostMessageAsync(
        string threadId,
        string text,
        CardElement? card = null,
        IReadOnlyList<DataContent>? files = null,
        CancellationToken ct = default)
    {
        var thread = TelegramThreadId.ParseFlexible(threadId);
        var replyMarkup = card is null ? null : TelegramCardConverter.ToInlineKeyboard(card);
        var outboundText = card is null ? text : _formatter.RenderCardFallback(card);

        try
        {
            TelegramMessage raw;
            if (files is { Count: > 0 })
            {
                if (files.Count > 1)
                    throw new ArgumentException("Telegram bot supports a single file upload per message.", nameof(files));

                raw = await SendDocumentAsync(thread, files[0], outboundText, replyMarkup, ct);
            }
            else
            {
                var chunks = SplitMessage(outboundText);
                if (chunks.Count == 0)
                    throw new ArgumentException("Telegram message text cannot be empty.", nameof(text));

                TelegramParsedMessage? lastParsed = null;
                for (var i = 0; i < chunks.Count; i++)
                {
                    raw = await _bot.SendMessage(
                        chatId: long.Parse(thread.ChatId),
                        text: chunks[i],
                        parseMode: ParseMode.None,
                        replyMarkup: i == chunks.Count - 1 ? replyMarkup : null,
                        messageThreadId: ParseNullableInt(thread.MessageThreadId),
                        cancellationToken: ct);

                    lastParsed = ParseTelegramMessage(raw, ResolveThreadId(raw));
                    CacheMessage(lastParsed);
                }

                return lastParsed!;
            }

            var parsed = ParseTelegramMessage(raw, ResolveThreadId(raw));
            CacheMessage(parsed);
            return parsed;
        }
        catch (ApiRequestException ex)
        {
            throw MapTelegramException(ex);
        }
    }

    public async Task<TelegramParsedMessage> EditMessageAsync(
        string threadId,
        string messageId,
        string text,
        CardElement? card = null,
        CancellationToken ct = default)
    {
        var thread = TelegramThreadId.ParseFlexible(threadId);
        var (chatId, telegramMessageId) = DecodeMessageId(messageId, thread.ChatId);
        var replyMarkup = card is null ? TelegramCardConverter.EmptyKeyboard() : TelegramCardConverter.ToInlineKeyboard(card);
        var outboundText = card is null ? text : _formatter.RenderCardFallback(card);
        outboundText = TelegramMarkdownV2.Truncate(outboundText, TelegramMarkdownV2.MessageLimit, TelegramRenderMode.Plain);

        if (string.IsNullOrWhiteSpace(outboundText))
            throw new ArgumentException("Telegram message text cannot be empty.", nameof(text));

        try
        {
            var raw = await _bot.EditMessageText(
                chatId: long.Parse(chatId),
                messageId: telegramMessageId,
                text: outboundText,
                parseMode: ParseMode.None,
                replyMarkup: replyMarkup,
                cancellationToken: ct);

            var parsed = ParseTelegramMessage(raw, ResolveThreadId(raw));
            CacheMessage(parsed);
            return parsed;
        }
        catch (ApiRequestException ex)
        {
            throw MapTelegramException(ex);
        }
    }

    public async Task DeleteMessageAsync(string threadId, string messageId, CancellationToken ct = default)
    {
        var thread = TelegramThreadId.ParseFlexible(threadId);
        var (chatId, telegramMessageId) = DecodeMessageId(messageId, thread.ChatId);

        try
        {
            await _bot.DeleteMessage(long.Parse(chatId), telegramMessageId, ct);
            DeleteCachedMessage(messageId);
        }
        catch (ApiRequestException ex)
        {
            throw MapTelegramException(ex);
        }
    }

    public async Task AddReactionAsync(string threadId, string messageId, string emoji, CancellationToken ct = default)
    {
        var thread = TelegramThreadId.ParseFlexible(threadId);
        var (chatId, telegramMessageId) = DecodeMessageId(messageId, thread.ChatId);

        try
        {
            await _bot.SetMessageReaction(
                long.Parse(chatId),
                telegramMessageId,
                [ToTelegramReaction(emoji)],
                cancellationToken: ct);
        }
        catch (ApiRequestException ex)
        {
            throw MapTelegramException(ex);
        }
    }

    public async Task RemoveReactionAsync(string threadId, string messageId, string emoji, CancellationToken ct = default)
    {
        _ = emoji;
        var thread = TelegramThreadId.ParseFlexible(threadId);
        var (chatId, telegramMessageId) = DecodeMessageId(messageId, thread.ChatId);

        try
        {
            await _bot.SetMessageReaction(long.Parse(chatId), telegramMessageId, [], cancellationToken: ct);
        }
        catch (ApiRequestException ex)
        {
            throw MapTelegramException(ex);
        }
    }

    public async Task StartTypingAsync(string threadId, CancellationToken ct = default)
    {
        var thread = TelegramThreadId.ParseFlexible(threadId);
        try
        {
            await _bot.SendChatAction(
                long.Parse(thread.ChatId),
                ChatAction.Typing,
                messageThreadId: ParseNullableInt(thread.MessageThreadId),
                cancellationToken: ct);
        }
        catch (ApiRequestException ex)
        {
            throw MapTelegramException(ex);
        }
    }

    public Task<TelegramFetchResult> FetchMessagesAsync(
        string threadId,
        TelegramFetchOptions? options = null,
        CancellationToken ct = default)
    {
        _ = ct;
        var messages = _messageCache.TryGetValue(threadId, out var cached) ? cached : [];
        return Task.FromResult(Paginate(messages, options ?? new TelegramFetchOptions()));
    }

    public Task<TelegramFetchResult> FetchChannelMessagesAsync(
        string channelId,
        TelegramFetchOptions? options = null,
        CancellationToken ct = default)
    {
        _ = ct;
        var channel = TelegramThreadId.ParseFlexible(channelId);
        var messages = _messageCache
            .Where(kv => TelegramThreadId.ParseFlexible(kv.Key).ChatId == channel.ChatId)
            .SelectMany(kv => kv.Value)
            .DistinctBy(message => message.Id)
            .OrderBy(message => message.Date)
            .ThenBy(message => MessageSequence(message.Id))
            .ToList();
        return Task.FromResult(Paginate(messages, options ?? new TelegramFetchOptions()));
    }

    public Task<TelegramParsedMessage?> FetchMessageAsync(
        string threadId,
        string messageId,
        CancellationToken ct = default)
    {
        _ = threadId;
        _ = ct;
        return Task.FromResult(FindCachedMessage(messageId));
    }

    public Task<string> OpenDmAsync(string userId, CancellationToken ct = default)
    {
        _ = ct;
        return Task.FromResult(TelegramThreadId.FormatChat(long.Parse(userId)));
    }

    public async Task<TelegramUserInfo?> GetUserAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var chat = await _bot.GetChat(long.Parse(userId), ct);
            return chat.Type == ChatType.Private
                ? MapChat(chat)
                : null;
        }
        catch (ApiRequestException ex)
        {
            throw MapTelegramException(ex);
        }
    }

    public async Task<TelegramThreadInfo> FetchThreadAsync(string threadId, CancellationToken ct = default)
    {
        var parsed = TelegramThreadId.ParseFlexible(threadId);
        try
        {
            var chat = await _bot.GetChat(long.Parse(parsed.ChatId), ct);
            return new TelegramThreadInfo(
                Id: TelegramThreadId.FormatThread(parsed.ChatId, parsed.MessageThreadId),
                ChannelId: parsed.ChannelId,
                ChannelName: ChatDisplayName(chat),
                IsDM: chat.Type == ChatType.Private,
                MessageThreadId: parsed.MessageThreadId,
                Raw: chat);
        }
        catch (ApiRequestException ex)
        {
            throw MapTelegramException(ex);
        }
    }

    public async Task<TelegramChannelInfo> FetchChannelInfoAsync(string channelId, CancellationToken ct = default)
    {
        var parsed = TelegramThreadId.ParseFlexible(channelId);
        try
        {
            var chat = await _bot.GetChat(long.Parse(parsed.ChatId), ct);
            int? memberCount = null;
            try
            {
                memberCount = await _bot.GetChatMemberCount(long.Parse(parsed.ChatId), ct);
            }
            catch (ApiRequestException)
            {
                memberCount = null;
            }

            return new TelegramChannelInfo(
                Id: parsed.ChannelId,
                Name: ChatDisplayName(chat),
                IsDM: chat.Type == ChatType.Private,
                MemberCount: memberCount,
                Raw: chat);
        }
        catch (ApiRequestException ex)
        {
            throw MapTelegramException(ex);
        }
    }

    public Task<TelegramParsedMessage> PostChannelMessageAsync(
        string channelId,
        string text,
        CardElement? card = null,
        IReadOnlyList<DataContent>? files = null,
        CancellationToken ct = default)
        => PostMessageAsync(channelId, text, card, files, ct);

    public static string ChannelIdFromThreadId(string threadId)
        => TelegramThreadId.ParseFlexible(threadId).ChannelId;

    public static bool IsDm(string threadId)
        => TelegramThreadId.ParseFlexible(threadId).IsDM;

    public TelegramParsedMessage ParseMessage(TelegramMessage raw)
    {
        var parsed = ParseTelegramMessage(raw, ResolveThreadId(raw));
        CacheMessage(parsed);
        return parsed;
    }

    public string RenderFormatted(string text)
        => _formatter.RenderMarkdownV2Text(text);

    internal TelegramParsedMessage ParseTelegramMessage(TelegramMessage raw, string threadId)
    {
        var plainText = raw.Text ?? raw.Caption ?? string.Empty;
        var entities = raw.Entities ?? raw.CaptionEntities ?? [];
        var text = ApplyTelegramEntities(plainText, entities);
        var author = raw.From is not null
            ? MapUser(raw.From)
            : MapChat(raw.SenderChat ?? raw.Chat);

        return new TelegramParsedMessage(
            Id: EncodeMessageId(raw.Chat.Id, raw.Id),
            ThreadId: threadId,
            Text: text,
            Author: author,
            Date: raw.Date,
            Edited: raw.EditDate is not null,
            EditedAt: raw.EditDate,
            IsMention: IsBotMentioned(raw, plainText),
            Attachments: ExtractAttachments(raw),
            Raw: raw);
    }

    internal static string ApplyTelegramEntities(string text, IReadOnlyList<MessageEntity> entities)
    {
        if (entities.Count == 0 || string.IsNullOrEmpty(text))
            return text;

        var result = text;
        foreach (var entity in entities
            .OrderByDescending(e => e.Offset)
            .ThenBy(e => e.Length))
        {
            if (entity.Offset < 0 || entity.Length < 0 || entity.Offset + entity.Length > result.Length)
                continue;

            var entityText = result.Substring(entity.Offset, entity.Length);
            var replacement = entity.Type switch
            {
                MessageEntityType.Bold => $"**{entityText}**",
                MessageEntityType.Italic => $"_{entityText}_",
                MessageEntityType.Strikethrough => $"~~{entityText}~~",
                MessageEntityType.Code => $"`{entityText}`",
                MessageEntityType.Pre => $"```{entity.Language ?? string.Empty}\n{entityText}\n```",
                MessageEntityType.TextLink when !string.IsNullOrWhiteSpace(entity.Url) =>
                    $"[{EscapeMarkdownEntityLabel(entityText)}]({entity.Url})",
                MessageEntityType.TextMention when entity.User is not null =>
                    $"@{DisplayName(entity.User)}",
                _ => null,
            };

            if (replacement is not null)
                result = result[..entity.Offset] + replacement + result[(entity.Offset + entity.Length)..];
        }

        return result;
    }

    internal static string EncodeMessageId(long chatId, int messageId)
        => $"{chatId}:{messageId}";

    internal static (string ChatId, int MessageId) DecodeMessageId(string messageId, string fallbackChatId)
    {
        var parts = messageId.Split(':', 2);
        return parts.Length == 2 && int.TryParse(parts[1], out var parsedMessageId)
            ? (parts[0], parsedMessageId)
            : (fallbackChatId, int.Parse(messageId));
    }

    internal static IReadOnlyList<string> SplitMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        if (text.Length <= TelegramMarkdownV2.MessageLimit)
            return [text];

        var chunks = new List<string>();
        var remaining = text;
        while (remaining.Length > TelegramMarkdownV2.MessageLimit)
        {
            var splitAt = LastIndexBefore(remaining, "\n\n", TelegramMarkdownV2.MessageLimit);
            if (splitAt <= 0)
                splitAt = LastIndexBefore(remaining, "\n", TelegramMarkdownV2.MessageLimit);
            if (splitAt <= 0)
                splitAt = TelegramMarkdownV2.MessageLimit;

            chunks.Add(remaining[..splitAt].Trim());
            remaining = remaining[splitAt..].TrimStart();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
            chunks.Add(remaining);
        return chunks;
    }

    private static int LastIndexBefore(string value, string needle, int before)
        => value[..Math.Min(value.Length, before)].LastIndexOf(needle, StringComparison.Ordinal);

    private async Task ProcessUpdateCoreAsync(TelegramUpdate update, CancellationToken ct)
    {
        switch (update.Type)
        {
            case UpdateType.Message:
            case UpdateType.ChannelPost:
                if ((update.Message ?? update.ChannelPost) is { } message)
                    await ProcessMessageAsync(message, runAgent: true, ct);
                break;

            case UpdateType.EditedMessage:
            case UpdateType.EditedChannelPost:
                if ((update.EditedMessage ?? update.EditedChannelPost) is { } edited)
                    CacheMessage(ParseTelegramMessage(edited, ResolveThreadId(edited)));
                break;

            case UpdateType.CallbackQuery:
                if (update.CallbackQuery is { } query)
                    await ProcessCallbackQueryAsync(query, ct);
                break;

            case UpdateType.MessageReaction:
                if (update.MessageReaction is { } reaction)
                    ProcessReaction(reaction);
                break;
        }
    }

    private async Task ProcessMessageAsync(TelegramMessage message, bool runAgent, CancellationToken ct)
    {
        var platformThreadId = ResolveThreadId(message);
        var parsed = ParseTelegramMessage(message, platformThreadId);
        CacheMessage(parsed);

        if (!runAgent || !ShouldProcessMessage(message, parsed.Text))
            return;

        if (_sessionMapper is null || _sessionManager is null || _agentManager is null)
            return;

        var (sessionId, threadId) = await _sessionMapper.ResolveAsync(platformThreadId, ct);
        _ = StreamToTelegramAsync(sessionId, threadId, parsed, CancellationToken.None);
    }

    private async Task ProcessCallbackQueryAsync(CallbackQuery query, CancellationToken ct)
    {
        try
        {
            await _bot.AnswerCallbackQuery(query.Id, cancellationToken: ct);
        }
        catch (ApiRequestException ex)
        {
            throw MapTelegramException(ex);
        }

        if (query.Message is null)
            return;

        var threadId = ResolveThreadId(query.Message);
        var messageId = EncodeMessageId(query.Message.Chat.Id, query.Message.Id);
        var (actionId, value) = TelegramCardConverter.DecodeCallbackData(query.Data);

        OnButtonClick?.Invoke(new TelegramButtonClickEvent(
            actionId,
            value,
            threadId,
            messageId,
            MapUser(query.From),
            query));
    }

    private void ProcessReaction(MessageReactionUpdated reaction)
    {
        var threadId = TelegramThreadId.FormatThread(reaction.Chat.Id, null);
        var messageId = EncodeMessageId(reaction.Chat.Id, reaction.MessageId);
        var oldSet = reaction.OldReaction.Select(ReactionKey).ToHashSet(StringComparer.Ordinal);
        var newSet = reaction.NewReaction.Select(ReactionKey).ToHashSet(StringComparer.Ordinal);
        var user = reaction.User is not null ? MapUser(reaction.User) : MapChat(reaction.ActorChat ?? reaction.Chat);

        foreach (var item in reaction.NewReaction)
        {
            var key = ReactionKey(item);
            if (!oldSet.Contains(key))
                OnReaction?.Invoke(new TelegramReactionEvent(threadId, messageId, key, true, user, reaction));
        }

        foreach (var item in reaction.OldReaction)
        {
            var key = ReactionKey(item);
            if (!newSet.Contains(key))
                OnReaction?.Invoke(new TelegramReactionEvent(threadId, messageId, key, false, user, reaction));
        }
    }

    private async Task StreamToTelegramAsync(
        string sessionId,
        string threadId,
        TelegramParsedMessage message,
        CancellationToken ct)
    {
        if (_sessionManager is null || _agentManager is null)
            return;

        try
        {
            var context = new TelegramStreamContext(message.ThreadId);
            var runner = new BotStreamingRunner(_sessionManager, _agentManager);
            var streaming = ResolveStreamingOptions();
            var attachments = await DownloadAttachmentsAsync(message.Attachments, ct);
            await runner.RunAsync(
                new BotStreamingRequest<TelegramStreamContext>(
                    AgentId: _config.ResolveAgentId(),
                    SessionId: sessionId,
                    ThreadId: threadId,
                    Text: message.Text,
                    Context: context,
                    Strategy: streaming.Strategy,
                    DebounceMs: streaming.DebounceMs,
                    Attachments: attachments),
                new BotStreamingCallbacks<TelegramStreamContext>
                {
                    InitializeAsync = InitializeTelegramStreamAsync,
                    UpdateTextAsync = UpdateTelegramTextAsync,
                    CompleteTextAsync = UpdateTelegramTextAsync,
                    CompleteCardAsync = CompleteTelegramCardAsync,
                    HandlePermissionAsync = async (_, agent, req, token) =>
                        await agent.RespondAsync(new PermissionResponseEvent(
                            PermissionId: req.PermissionId,
                            SourceName: "telegram",
                            Approved: false), token),
                },
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private BotStreamingOptions ResolveStreamingOptions()
    {
        var defaults = _streamingOptions?.Get("telegram")
            ?? new BotStreamingOptions { Strategy = StreamingStrategy.PostAndEdit, DebounceMs = 500 };
        return new BotStreamingOptions
        {
            Strategy = defaults.Strategy,
            DebounceMs = _config.StreamingDebounceMs is > 0
                ? _config.StreamingDebounceMs.Value
                : defaults.DebounceMs,
        };
    }

    private async Task InitializeTelegramStreamAsync(TelegramStreamContext context, CancellationToken ct)
    {
        var placeholder = await PostMessageAsync(context.ThreadId, "...", ct: ct);
        context.MessageId = placeholder.Id;
    }

    private async Task UpdateTelegramTextAsync(TelegramStreamContext context, string content, CancellationToken ct)
    {
        if (context.MessageId is null)
            return;

        var text = string.IsNullOrWhiteSpace(content) ? "..." : content;
        await EditMessageAsync(context.ThreadId, context.MessageId, text, ct: ct);
    }

    private async Task CompleteTelegramCardAsync(TelegramStreamContext context, CardElement card, CancellationToken ct)
    {
        if (context.MessageId is null)
            return;

        await EditMessageAsync(context.ThreadId, context.MessageId, CardFallbackText.From(card), card, ct);
    }

    private async Task<TelegramMessage> SendDocumentAsync(
        TelegramThreadId thread,
        DataContent file,
        string text,
        InlineKeyboardMarkup? replyMarkup,
        CancellationToken ct)
    {
        await using var stream = new MemoryStream(file.Data.ToArray());
        var input = InputFile.FromStream(stream, file.Name);
        var caption = string.IsNullOrWhiteSpace(text)
            ? null
            : TelegramMarkdownV2.Truncate(text, TelegramMarkdownV2.CaptionLimit, TelegramRenderMode.Plain);

        return await _bot.SendDocument(
            chatId: long.Parse(thread.ChatId),
            document: input,
            caption: caption,
            parseMode: ParseMode.None,
            replyMarkup: replyMarkup,
            messageThreadId: ParseNullableInt(thread.MessageThreadId),
            cancellationToken: ct);
    }

    private IReadOnlyList<TelegramFileAttachment> ExtractAttachments(TelegramMessage raw)
    {
        var result = new List<TelegramFileAttachment>();

        var photo = raw.Photo?.OrderBy(p => p.Width * p.Height).LastOrDefault();
        if (photo is not null)
        {
            result.Add(new TelegramFileAttachment(
                "image",
                photo.FileId,
                photo.FileUniqueId,
                photo.FileSize,
                Width: photo.Width,
                Height: photo.Height,
                Raw: photo));
        }

        if (raw.Video is { } video)
        {
            result.Add(new TelegramFileAttachment(
                "video",
                video.FileId,
                video.FileUniqueId,
                video.FileSize,
                video.FileName,
                video.MimeType,
                video.Width,
                video.Height,
                video.Duration,
                video));
        }

        if (raw.Audio is { } audio)
        {
            result.Add(new TelegramFileAttachment(
                "audio",
                audio.FileId,
                audio.FileUniqueId,
                audio.FileSize,
                audio.FileName,
                audio.MimeType,
                Duration: audio.Duration,
                Raw: audio));
        }

        if (raw.Voice is { } voice)
        {
            result.Add(new TelegramFileAttachment(
                "audio",
                voice.FileId,
                voice.FileUniqueId,
                voice.FileSize,
                MimeType: voice.MimeType,
                Duration: voice.Duration,
                Raw: voice));
        }

        if (raw.Document is { } document)
        {
            result.Add(new TelegramFileAttachment(
                "file",
                document.FileId,
                document.FileUniqueId,
                document.FileSize,
                document.FileName,
                document.MimeType,
                Raw: document));
        }

        return result;
    }

    public async Task<IReadOnlyList<DataContent>> DownloadAttachmentsAsync(
        IReadOnlyList<TelegramFileAttachment> attachments,
        CancellationToken ct = default)
    {
        if (attachments.Count == 0)
            return [];

        var result = new List<DataContent>(attachments.Count);
        foreach (var attachment in attachments)
            result.Add(await DownloadAttachmentAsync(attachment, ct));
        return result;
    }

    private async Task<DataContent> DownloadAttachmentAsync(TelegramFileAttachment attachment, CancellationToken ct)
    {
        try
        {
            var file = await _bot.GetFile(attachment.FileId, ct);
            if (string.IsNullOrWhiteSpace(file.FilePath))
                throw new BotNotFoundException($"Telegram file '{attachment.FileId}' has no downloadable path.");

            await using var ms = new MemoryStream();
            await _bot.DownloadFile(file.FilePath, ms, ct);
            var data = ms.ToArray();
            var mediaType = attachment.MimeType ?? GuessMediaType(attachment);

            DataContent content = attachment.Kind switch
            {
                "image" => new ImageContent(data, mediaType),
                "audio" => new AudioContent(data, mediaType),
                "video" => new VideoContent(data, mediaType),
                "file" when IsDocumentLike(attachment) => new DocumentContent(data, mediaType),
                _ => new DataContent(data, mediaType),
            };
            content.Name = attachment.Name;
            return content;
        }
        catch (ApiRequestException ex)
        {
            throw MapTelegramException(ex);
        }
    }

    private static TelegramFetchResult Paginate(
        IReadOnlyList<TelegramParsedMessage> messages,
        TelegramFetchOptions options)
    {
        var limit = Math.Max(1, Math.Min(options.Limit ?? 50, 100));
        if (messages.Count == 0)
            return new TelegramFetchResult([]);

        var indexById = messages
            .Select((message, index) => new { message.Id, Index = index })
            .ToDictionary(x => x.Id, x => x.Index, StringComparer.Ordinal);

        if (options.Direction == TelegramFetchDirection.Backward)
        {
            var end = options.Cursor is not null && indexById.TryGetValue(options.Cursor, out var cursorIndex)
                ? cursorIndex
                : messages.Count;
            var start = Math.Max(0, end - limit);
            var page = messages.Skip(start).Take(end - start).ToArray();
            return new TelegramFetchResult(page, start > 0 ? page.FirstOrDefault()?.Id : null);
        }

        var startForward = options.Cursor is not null && indexById.TryGetValue(options.Cursor, out var forwardCursorIndex)
            ? forwardCursorIndex + 1
            : 0;
        var pageForward = messages.Skip(startForward).Take(limit).ToArray();
        var nextCursor = startForward + pageForward.Length < messages.Count
            ? pageForward.LastOrDefault()?.Id
            : null;
        return new TelegramFetchResult(pageForward, nextCursor);
    }

    private void CacheMessage(TelegramParsedMessage message)
    {
        var list = _messageCache.GetOrAdd(message.ThreadId, _ => []);
        lock (list)
        {
            var index = list.FindIndex(item => item.Id == message.Id);
            if (index >= 0)
                list[index] = message;
            else
                list.Add(message);

            list.Sort(CompareMessages);
        }
    }

    private TelegramParsedMessage? FindCachedMessage(string messageId)
    {
        foreach (var messages in _messageCache.Values)
        {
            lock (messages)
            {
                var found = messages.FirstOrDefault(message => message.Id == messageId);
                if (found is not null)
                    return found;
            }
        }

        return null;
    }

    private void DeleteCachedMessage(string messageId)
    {
        foreach (var (threadId, messages) in _messageCache)
        {
            lock (messages)
            {
                messages.RemoveAll(message => message.Id == messageId);
                if (messages.Count == 0)
                    _messageCache.TryRemove(threadId, out _);
            }
        }
    }

    private bool ShouldProcessMessage(TelegramMessage message, string text)
        => message.Chat.Type == ChatType.Private || IsBotMentioned(message, text);

    private bool IsBotMentioned(TelegramMessage message, string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var entities = message.Entities ?? message.CaptionEntities ?? [];
        foreach (var entity in entities)
        {
            if (entity.Offset < 0 || entity.Length < 0 || entity.Offset + entity.Length > text.Length)
                continue;

            var entityText = text.Substring(entity.Offset, entity.Length);
            if (entity.Type == MessageEntityType.Mention &&
                string.Equals(entityText, $"@{_userName}", StringComparison.OrdinalIgnoreCase))
                return true;

            if (entity.Type == MessageEntityType.TextMention &&
                entity.User?.Id.ToString() == _botUserId)
                return true;

            if (entity.Type == MessageEntityType.BotCommand &&
                entityText.EndsWith($"@{_userName}", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return text.Contains($"@{_userName}", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveThreadId(TelegramMessage message)
        => TelegramThreadId.FormatThread(message.Chat.Id, message.MessageThreadId);

    private static TelegramUserInfo MapUser(User user)
        => new(
            user.Id.ToString(),
            user.Username ?? user.FirstName,
            DisplayName(user),
            user.IsBot);

    private static TelegramUserInfo MapChat(Chat chat)
    {
        var name = ChatDisplayName(chat);
        var userName = chat.Type == ChatType.Private
            ? chat.Username ?? chat.FirstName ?? chat.Id.ToString()
            : name;
        return new TelegramUserInfo(chat.Id.ToString(), userName, name, false);
    }

    private static string DisplayName(User user)
        => string.Join(" ", new[] { user.FirstName, user.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string ChatDisplayName(Chat chat)
    {
        if (!string.IsNullOrWhiteSpace(chat.Title))
            return chat.Title;

        var privateName = string.Join(" ", new[] { chat.FirstName, chat.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (!string.IsNullOrWhiteSpace(privateName))
            return privateName;

        return chat.Username ?? chat.Id.ToString();
    }

    private static string EscapeMarkdownEntityLabel(string text)
        => text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static TelegramUpdate DeserializeUpdate(JsonElement updateJson)
    {
        var responseJson = "{\"ok\":true,\"result\":[" + updateJson.GetRawText() + "]}";
        var response = JsonSerializer.Deserialize(
            responseJson,
            JsonBotSerializerContext.Default.ApiResponseUpdateArray);

        return response?.Result is { Length: > 0 } updates
            ? updates[0]
            : throw new JsonException("Telegram update body was empty.");
    }

    private static string EventName(JsonElement update)
    {
        if (update.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (update.TryGetProperty("callback_query", out _))
            return "callback_query";
        if (update.TryGetProperty("message_reaction", out _))
            return "message_reaction";
        if (update.TryGetProperty("edited_message", out _) || update.TryGetProperty("edited_channel_post", out _))
            return "edited_message";
        if (update.TryGetProperty("message", out _) || update.TryGetProperty("channel_post", out _))
            return "message";

        return string.Empty;
    }

    private static string EventName(TelegramUpdate? update)
        => update?.Type switch
        {
            UpdateType.CallbackQuery => "callback_query",
            UpdateType.MessageReaction => "message_reaction",
            UpdateType.EditedMessage or UpdateType.EditedChannelPost => "edited_message",
            UpdateType.Message or UpdateType.ChannelPost => "message",
            _ => string.Empty,
        };

    private static ITelegramBotClient CreateBotClient(TelegramBotConfig config, IHttpClientFactory? httpClientFactory)
    {
        var options = new TelegramBotClientOptions(config.ResolveBotToken(), config.ResolveApiBaseUrl());
        return new TelegramBotClient(options, httpClientFactory?.CreateClient("telegram"));
    }

    private static Exception MapTelegramException(ApiRequestException ex)
        => ex.ErrorCode switch
        {
            401 => new BotAuthenticationException(ex.Message, ex),
            403 => new BotPermissionException(ex.Message, ex),
            404 => new BotNotFoundException(ex.Message, ex),
            429 => new BotRateLimitException(ex.Message, ex),
            _ => new TelegramBotException(ex.Message, ex),
        };

    private static ReactionType ToTelegramReaction(string emoji)
    {
        if (emoji.StartsWith("custom:", StringComparison.Ordinal))
            return new ReactionTypeCustomEmoji { CustomEmojiId = emoji["custom:".Length..] };

        return new ReactionTypeEmoji { Emoji = BotEmojiResolver.ToUnicode(emoji) };
    }

    private static string ReactionKey(ReactionType reaction)
        => reaction switch
        {
            ReactionTypeEmoji emoji => emoji.Emoji,
            ReactionTypeCustomEmoji custom => $"custom:{custom.CustomEmojiId}",
            _ => reaction.ToString() ?? "unknown",
        };

    private static int? ParseNullableInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : null;

    private static int CompareMessages(TelegramParsedMessage left, TelegramParsedMessage right)
    {
        var date = left.Date.CompareTo(right.Date);
        return date != 0 ? date : MessageSequence(left.Id).CompareTo(MessageSequence(right.Id));
    }

    private static int MessageSequence(string messageId)
    {
        var parts = messageId.Split(':', 2);
        return parts.Length == 2 && int.TryParse(parts[1], out var sequence) ? sequence : 0;
    }

    private static string NormalizeUserName(string? value)
        => string.IsNullOrWhiteSpace(value) ? "bot" : value.Trim().TrimStart('@');

    private static string GuessMediaType(TelegramFileAttachment attachment)
        => attachment.Kind switch
        {
            "image" => "image/jpeg",
            "audio" => "audio/mpeg",
            "video" => "video/mp4",
            "file" when attachment.Name is { } name => MimeTypeRegistry.GetMimeTypeFromPath(name) ?? "application/octet-stream",
            _ => "application/octet-stream",
        };

    private static bool IsDocumentLike(TelegramFileAttachment attachment)
        => attachment.MimeType?.StartsWith("application/", StringComparison.OrdinalIgnoreCase) == true ||
            attachment.MimeType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true;

    private sealed class TelegramStreamContext(string threadId)
    {
        public string ThreadId { get; } = threadId;
        public string? MessageId { get; set; }
    }
}

public sealed class TelegramBotException : BotException
{
    public TelegramBotException(string message, Exception inner) : base(message, inner)
    {
    }
}
