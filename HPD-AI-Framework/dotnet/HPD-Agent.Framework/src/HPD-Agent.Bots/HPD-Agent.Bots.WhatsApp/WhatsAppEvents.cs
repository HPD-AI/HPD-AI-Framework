using HPD.Agent.Bots.Contracts;

namespace HPD.Agent.Bots.WhatsApp;

public sealed record WhatsAppUserInfo(
    string UserId,
    string UserName,
    string FullName,
    bool IsBot);

public sealed record WhatsAppParsedMessage(
    string Id,
    string ThreadId,
    string Text,
    WhatsAppUserInfo Author,
    DateTimeOffset Timestamp,
    bool IsMention,
    IReadOnlyList<WhatsAppAttachment> Attachments,
    WhatsAppInboundMessage Raw);

public sealed record WhatsAppAttachment(
    string Kind,
    string MediaId,
    string? MimeType = null,
    string? FileName = null,
    string? Caption = null,
    string? Sha256 = null,
    object? Raw = null);

public sealed record WhatsAppLocationAttachment(
    double Latitude,
    double Longitude,
    string? Name = null,
    string? Address = null);

public sealed record WhatsAppButtonClickEvent(
    string ActionId,
    string? Value,
    string ThreadId,
    string? MessageId,
    WhatsAppUserInfo User,
    WhatsAppInboundMessage Payload);

public sealed record WhatsAppReactionEvent(
    string ThreadId,
    string MessageId,
    string Emoji,
    bool Added,
    WhatsAppUserInfo User,
    WhatsAppInboundMessage Payload);

public sealed record WhatsAppThreadInfo(
    string Id,
    string ChannelId,
    string ChannelName,
    bool IsDM,
    object Metadata);

public sealed record WhatsAppChannelInfo(
    string Id,
    string Name,
    bool IsDM,
    int? MemberCount,
    object Metadata);

public sealed record WhatsAppFetchResult(
    IReadOnlyList<WhatsAppParsedMessage> Messages,
    string? NextCursor = null);

public sealed record WhatsAppRawMessage(
    string PhoneNumberId,
    WhatsAppInboundMessage Message,
    WhatsAppContact? Contact = null);
