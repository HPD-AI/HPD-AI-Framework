using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Bots.WhatsApp;

[JsonSerializable(typeof(JsonDocument))]
[JsonSerializable(typeof(WhatsAppWebhookPayload))]
[JsonSerializable(typeof(WhatsAppWebhookEntry))]
[JsonSerializable(typeof(WhatsAppWebhookChange))]
[JsonSerializable(typeof(WhatsAppWebhookValue))]
[JsonSerializable(typeof(WhatsAppMetadata))]
[JsonSerializable(typeof(WhatsAppContact))]
[JsonSerializable(typeof(WhatsAppProfile))]
[JsonSerializable(typeof(WhatsAppInboundMessage))]
[JsonSerializable(typeof(WhatsAppTextContent))]
[JsonSerializable(typeof(WhatsAppMediaContent))]
[JsonSerializable(typeof(WhatsAppDocumentContent))]
[JsonSerializable(typeof(WhatsAppLocationContent))]
[JsonSerializable(typeof(WhatsAppInteractiveReply))]
[JsonSerializable(typeof(WhatsAppReply))]
[JsonSerializable(typeof(WhatsAppButtonReply))]
[JsonSerializable(typeof(WhatsAppReactionContent))]
[JsonSerializable(typeof(WhatsAppContext))]
[JsonSerializable(typeof(WhatsAppStatus))]
[JsonSerializable(typeof(WhatsAppSendResponse))]
[JsonSerializable(typeof(WhatsAppMediaResponse))]
[JsonSerializable(typeof(WhatsAppGraphErrorEnvelope))]
[JsonSerializable(typeof(WhatsAppCallbackPayload))]
[JsonSerializable(typeof(WhatsAppLocationAttachment))]
[JsonSerializable(typeof(WhatsAppTextMessageRequest))]
[JsonSerializable(typeof(WhatsAppInteractiveMessageRequest))]
[JsonSerializable(typeof(WhatsAppReactionRequest))]
[JsonSerializable(typeof(WhatsAppReadRequest))]
internal partial class WhatsAppBotJsonContext : JsonSerializerContext;

internal sealed record WhatsAppCallbackPayload(
    [property: JsonPropertyName("a")] string ActionId,
    [property: JsonPropertyName("v")] string? Value = null);
