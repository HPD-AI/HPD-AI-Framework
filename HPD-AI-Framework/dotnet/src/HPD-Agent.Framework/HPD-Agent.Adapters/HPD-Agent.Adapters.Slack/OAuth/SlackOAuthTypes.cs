using System.Text.Json.Serialization;

namespace HPD.Agent.Adapters.Slack.OAuth;

// ── oauth.v2.access response ───────────────────────────────────────────────────

internal sealed record SlackOAuthV2Response(
    [property: JsonPropertyName("ok")]           bool Ok,
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("team")]         SlackOAuthTeam? Team,
    [property: JsonPropertyName("error")]        string? Error
);

internal sealed record SlackOAuthTeam(
    [property: JsonPropertyName("id")]   string Id,
    [property: JsonPropertyName("name")] string? Name
);

// ── AOT JSON context ───────────────────────────────────────────────────────────

[JsonSerializable(typeof(SlackOAuthV2Response))]
[JsonSerializable(typeof(SlackOAuthTeam))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal partial class SlackOAuthJsonContext : JsonSerializerContext;
