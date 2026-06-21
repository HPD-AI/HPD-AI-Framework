using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Bots.Telegram;

[JsonSerializable(typeof(JsonDocument))]
[JsonSerializable(typeof(TelegramCallbackPayload))]
internal partial class TelegramBotJsonContext : JsonSerializerContext;

internal sealed record TelegramCallbackPayload(
    [property: JsonPropertyName("a")] string ActionId,
    [property: JsonPropertyName("v")] string? Value = null);
