using System.Text.Json.Serialization;
using HPD.Agent.Bots;

namespace HPD.Agent.Bots.WhatsApp;

[WebhookPayload]
public sealed record WhatsAppWebhookPayload(
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("entry")] WhatsAppWebhookEntry[] Entry);

[WebhookPayload]
public sealed record WhatsAppWebhookEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("changes")] WhatsAppWebhookChange[] Changes);

[WebhookPayload]
public sealed record WhatsAppWebhookChange(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("value")] WhatsAppWebhookValue Value);

[WebhookPayload]
public sealed record WhatsAppWebhookValue(
    [property: JsonPropertyName("messaging_product")] string MessagingProduct,
    [property: JsonPropertyName("metadata")] WhatsAppMetadata Metadata,
    [property: JsonPropertyName("contacts")] WhatsAppContact[]? Contacts = null,
    [property: JsonPropertyName("messages")] WhatsAppInboundMessage[]? Messages = null,
    [property: JsonPropertyName("statuses")] WhatsAppStatus[]? Statuses = null);

[WebhookPayload]
public sealed record WhatsAppMetadata(
    [property: JsonPropertyName("display_phone_number")] string DisplayPhoneNumber,
    [property: JsonPropertyName("phone_number_id")] string PhoneNumberId);

[WebhookPayload]
public sealed record WhatsAppContact(
    [property: JsonPropertyName("profile")] WhatsAppProfile Profile,
    [property: JsonPropertyName("wa_id")] string WaId);

[WebhookPayload]
public sealed record WhatsAppProfile(
    [property: JsonPropertyName("name")] string Name);

[WebhookPayload]
public sealed record WhatsAppInboundMessage(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] WhatsAppTextContent? Text = null,
    [property: JsonPropertyName("image")] WhatsAppMediaContent? Image = null,
    [property: JsonPropertyName("document")] WhatsAppDocumentContent? Document = null,
    [property: JsonPropertyName("audio")] WhatsAppMediaContent? Audio = null,
    [property: JsonPropertyName("voice")] WhatsAppMediaContent? Voice = null,
    [property: JsonPropertyName("video")] WhatsAppMediaContent? Video = null,
    [property: JsonPropertyName("sticker")] WhatsAppMediaContent? Sticker = null,
    [property: JsonPropertyName("location")] WhatsAppLocationContent? Location = null,
    [property: JsonPropertyName("interactive")] WhatsAppInteractiveReply? Interactive = null,
    [property: JsonPropertyName("button")] WhatsAppButtonReply? Button = null,
    [property: JsonPropertyName("reaction")] WhatsAppReactionContent? Reaction = null,
    [property: JsonPropertyName("context")] WhatsAppContext? Context = null);

[WebhookPayload]
public sealed record WhatsAppTextContent(
    [property: JsonPropertyName("body")] string Body);

[WebhookPayload]
public record WhatsAppMediaContent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("mime_type")] string? MimeType = null,
    [property: JsonPropertyName("sha256")] string? Sha256 = null,
    [property: JsonPropertyName("caption")] string? Caption = null);

[WebhookPayload]
public sealed record WhatsAppDocumentContent : WhatsAppMediaContent
{
    public WhatsAppDocumentContent(
        string id,
        string? mimeType = null,
        string? sha256 = null,
        string? caption = null,
        string? fileName = null) : base(id, mimeType, sha256, caption)
    {
        FileName = fileName;
    }

    [JsonPropertyName("filename")]
    public string? FileName { get; init; }
}

[WebhookPayload]
public sealed record WhatsAppLocationContent(
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("address")] string? Address = null);

[WebhookPayload]
public sealed record WhatsAppInteractiveReply(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("button_reply")] WhatsAppReply? ButtonReply = null,
    [property: JsonPropertyName("list_reply")] WhatsAppReply? ListReply = null);

[WebhookPayload]
public sealed record WhatsAppReply(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description = null);

[WebhookPayload]
public sealed record WhatsAppButtonReply(
    [property: JsonPropertyName("payload")] string Payload,
    [property: JsonPropertyName("text")] string Text);

[WebhookPayload]
public sealed record WhatsAppReactionContent(
    [property: JsonPropertyName("message_id")] string MessageId,
    [property: JsonPropertyName("emoji")] string? Emoji);

[WebhookPayload]
public sealed record WhatsAppContext(
    [property: JsonPropertyName("from")] string? From = null,
    [property: JsonPropertyName("id")] string? Id = null);

[WebhookPayload]
public sealed record WhatsAppStatus(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("timestamp")] string? Timestamp = null);

public sealed record WhatsAppSendResponse(
    [property: JsonPropertyName("messaging_product")] string? MessagingProduct,
    [property: JsonPropertyName("contacts")] WhatsAppSendContact[]? Contacts,
    [property: JsonPropertyName("messages")] WhatsAppSentMessage[]? Messages);

public sealed record WhatsAppSendContact(
    [property: JsonPropertyName("input")] string? Input,
    [property: JsonPropertyName("wa_id")] string? WaId);

public sealed record WhatsAppSentMessage(
    [property: JsonPropertyName("id")] string Id);

public sealed record WhatsAppMediaResponse(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("mime_type")] string? MimeType = null,
    [property: JsonPropertyName("sha256")] string? Sha256 = null,
    [property: JsonPropertyName("file_size")] long? FileSize = null,
    [property: JsonPropertyName("id")] string? Id = null);

public sealed record WhatsAppGraphErrorEnvelope(
    [property: JsonPropertyName("error")] WhatsAppGraphError Error);

public sealed record WhatsAppGraphError(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("type")] string? Type = null,
    [property: JsonPropertyName("code")] int? Code = null,
    [property: JsonPropertyName("error_subcode")] int? ErrorSubcode = null,
    [property: JsonPropertyName("fbtrace_id")] string? FbTraceId = null);

internal sealed record WhatsAppTextMessageRequest(
    [property: JsonPropertyName("messaging_product")] string MessagingProduct,
    [property: JsonPropertyName("recipient_type")] string RecipientType,
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] WhatsAppOutboundText Text);

internal sealed record WhatsAppOutboundText(
    [property: JsonPropertyName("preview_url")] bool PreviewUrl,
    [property: JsonPropertyName("body")] string Body);

internal sealed record WhatsAppInteractiveMessageRequest(
    [property: JsonPropertyName("messaging_product")] string MessagingProduct,
    [property: JsonPropertyName("recipient_type")] string RecipientType,
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("interactive")] WhatsAppOutboundInteractive Interactive);

internal sealed record WhatsAppOutboundInteractive(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("body")] WhatsAppOutboundTextBody Body,
    [property: JsonPropertyName("action")] WhatsAppOutboundAction Action,
    [property: JsonPropertyName("header")] WhatsAppOutboundHeader? Header = null,
    [property: JsonPropertyName("footer")] WhatsAppOutboundTextBody? Footer = null);

internal sealed record WhatsAppOutboundHeader(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);

internal sealed record WhatsAppOutboundTextBody(
    [property: JsonPropertyName("text")] string Text);

internal sealed record WhatsAppOutboundAction(
    [property: JsonPropertyName("buttons")] WhatsAppOutboundButton[] Buttons);

internal sealed record WhatsAppOutboundButton(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("reply")] WhatsAppReply Reply);

internal sealed record WhatsAppReactionRequest(
    [property: JsonPropertyName("messaging_product")] string MessagingProduct,
    [property: JsonPropertyName("recipient_type")] string RecipientType,
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("reaction")] WhatsAppOutboundReaction Reaction);

internal sealed record WhatsAppOutboundReaction(
    [property: JsonPropertyName("message_id")] string MessageId,
    [property: JsonPropertyName("emoji")] string Emoji);

internal sealed record WhatsAppReadRequest(
    [property: JsonPropertyName("messaging_product")] string MessagingProduct,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message_id")] string MessageId,
    [property: JsonPropertyName("typing_indicator")] WhatsAppTypingIndicator? TypingIndicator = null);

internal sealed record WhatsAppTypingIndicator(
    [property: JsonPropertyName("type")] string Type);
