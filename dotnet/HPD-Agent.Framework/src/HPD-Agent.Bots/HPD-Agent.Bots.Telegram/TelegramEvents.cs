using Telegram.Bot.Types;

namespace HPD.Agent.Bots.Telegram;

public sealed record TelegramUserInfo(
    string UserId,
    string UserName,
    string FullName,
    bool IsBot);

public sealed record TelegramButtonClickEvent(
    string ActionId,
    string? Value,
    string ThreadId,
    string MessageId,
    TelegramUserInfo User,
    CallbackQuery Payload);

public sealed record TelegramReactionEvent(
    string ThreadId,
    string MessageId,
    string Emoji,
    bool Added,
    TelegramUserInfo User,
    MessageReactionUpdated Payload);

public sealed record TelegramParsedMessage(
    string Id,
    string ThreadId,
    string Text,
    TelegramUserInfo Author,
    DateTime Date,
    bool Edited,
    DateTime? EditedAt,
    bool IsMention,
    IReadOnlyList<TelegramFileAttachment> Attachments,
    Message Raw);

public sealed record TelegramFileAttachment(
    string Kind,
    string FileId,
    string? FileUniqueId = null,
    long? Size = null,
    string? Name = null,
    string? MimeType = null,
    int? Width = null,
    int? Height = null,
    int? Duration = null,
    object? Raw = null);

public sealed record TelegramFetchOptions(
    int? Limit = null,
    string? Cursor = null,
    TelegramFetchDirection Direction = TelegramFetchDirection.Backward);

public enum TelegramFetchDirection
{
    Backward,
    Forward,
}

public sealed record TelegramFetchResult(
    IReadOnlyList<TelegramParsedMessage> Messages,
    string? NextCursor = null);

public sealed record TelegramThreadInfo(
    string Id,
    string ChannelId,
    string ChannelName,
    bool IsDM,
    string? MessageThreadId,
    Chat Raw);

public sealed record TelegramChannelInfo(
    string Id,
    string Name,
    bool IsDM,
    int? MemberCount,
    Chat Raw);
