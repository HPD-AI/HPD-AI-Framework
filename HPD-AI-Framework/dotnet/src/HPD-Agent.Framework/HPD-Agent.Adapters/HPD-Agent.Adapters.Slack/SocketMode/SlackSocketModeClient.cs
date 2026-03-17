using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Adapters.Slack.SocketMode;

// ── Response types ─────────────────────────────────────────────────────────────

file record ConnectionsOpenResponse(
    [property: JsonPropertyName("ok")]    bool Ok,
    [property: JsonPropertyName("url")]   string? Url,
    [property: JsonPropertyName("error")] string? Error
);

// ── Client ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Handles the two Socket Mode protocol operations:
/// <list type="bullet">
///   <item><c>OpenConnectionUrlAsync</c> — calls <c>POST apps.connections.open</c> to get a one-time wss:// URL.</item>
///   <item><c>AckAsync</c> — serialises an envelope ACK payload for the caller to send.</item>
/// </list>
/// </summary>
public sealed class SlackSocketModeClient(
    IOptions<SlackAdapterConfig> options,
    IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Calls <c>POST https://slack.com/api/apps.connections.open</c> with the AppToken
    /// and returns the one-time <c>wss://</c> URI.
    /// Must be called on every connection attempt — Slack's URLs are single-use.
    /// </summary>
    public async Task<Uri> OpenConnectionUrlAsync(CancellationToken ct)
    {
        var appToken = options.Value.AppToken
            ?? throw new InvalidOperationException(
                "SlackAdapterConfig.AppToken must be set to use Socket Mode.");

        using var http = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://slack.com/api/apps.connections.open");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", appToken);
        // apps.connections.open takes no request body
        request.Content = new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<ConnectionsOpenResponse>(json, _jsonOptions)
            ?? throw new InvalidOperationException("apps.connections.open returned null response.");

        if (!result.Ok || result.Url is null)
            throw new InvalidOperationException(
                $"apps.connections.open failed: {result.Error ?? "unknown error"}");

        return new Uri(result.Url);
    }

    /// <summary>
    /// Returns the raw UTF-8 ACK payload for the given envelope ID.
    /// The caller (<see cref="SlackSocketModeService"/>) sends it via
    /// <c>AdapterWebSocketService.SendAsync</c> (which holds the send lock).
    /// </summary>
    public static ReadOnlyMemory<byte> BuildAckPayload(string envelopeId)
        => Encoding.UTF8.GetBytes($"{{\"envelope_id\":\"{envelopeId}\"}}");
}
