using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HPD.Agent.Bots.Contracts;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Bots.WhatsApp;

public sealed class WhatsAppApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WhatsAppBotConfig _config;
    private readonly string _accessToken;
    private readonly string _phoneNumberId;
    private readonly string _baseUrl;

    public WhatsAppApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<WhatsAppBotConfig> options)
    {
        _httpClientFactory = httpClientFactory;
        _config = options.Value;
        _accessToken = _config.ResolveAccessToken();
        _phoneNumberId = _config.ResolvePhoneNumberId();
        _baseUrl = $"{_config.ResolveApiUrl()}/{_config.ApiVersion.Trim('/')}";
    }

    public Task<WhatsAppSendResponse> SendTextAsync(string to, string text, CancellationToken ct = default)
        => PostMessageAsync(
            new WhatsAppTextMessageRequest(
                "whatsapp",
                "individual",
                to,
                "text",
                new WhatsAppOutboundText(false, text)),
            WhatsAppBotJsonContext.Default.WhatsAppTextMessageRequest,
            WhatsAppBotJsonContext.Default.WhatsAppSendResponse,
            ct);

    public Task<WhatsAppSendResponse> SendInteractiveAsync(
        string to,
        WhatsAppInteractiveMessage interactive,
        CancellationToken ct = default)
        => PostMessageAsync(
            new WhatsAppInteractiveMessageRequest(
                "whatsapp",
                "individual",
                to,
                "interactive",
                new WhatsAppOutboundInteractive(
                    "button",
                    new WhatsAppOutboundTextBody(interactive.Body),
                    new WhatsAppOutboundAction(
                        interactive.Buttons
                            .Select(button => new WhatsAppOutboundButton(
                                "reply",
                                new WhatsAppReply(button.Id, button.Title)))
                            .ToArray()),
                    string.IsNullOrWhiteSpace(interactive.Header)
                        ? null
                        : new WhatsAppOutboundHeader("text", interactive.Header),
                    string.IsNullOrWhiteSpace(interactive.Footer)
                        ? null
                        : new WhatsAppOutboundTextBody(interactive.Footer))),
            WhatsAppBotJsonContext.Default.WhatsAppInteractiveMessageRequest,
            WhatsAppBotJsonContext.Default.WhatsAppSendResponse,
            ct);

    public Task SendReactionAsync(string to, string messageId, string? emoji, CancellationToken ct = default)
        => PostMessageAsync(
            new WhatsAppReactionRequest(
                "whatsapp",
                "individual",
                to,
                "reaction",
                new WhatsAppOutboundReaction(messageId, emoji ?? string.Empty)),
            WhatsAppBotJsonContext.Default.WhatsAppReactionRequest,
            WhatsAppBotJsonContext.Default.JsonDocument,
            ct);

    public Task MarkReadAsync(string messageId, bool showTypingIndicator = false, CancellationToken ct = default)
        => SendMessageAsync(
            HttpMethod.Put,
            new WhatsAppReadRequest(
                "whatsapp",
                "read",
                messageId,
                showTypingIndicator ? new WhatsAppTypingIndicator("text") : null),
            WhatsAppBotJsonContext.Default.WhatsAppReadRequest,
            WhatsAppBotJsonContext.Default.JsonDocument,
            ct);

    public async Task<byte[]> DownloadMediaAsync(string mediaId, CancellationToken ct = default)
    {
        var media = await GetAsync($"{BaseUrl}/{mediaId}", WhatsAppBotJsonContext.Default.WhatsAppMediaResponse, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, media.Url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        using var response = await CreateClient().SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            await ThrowGraphExceptionAsync(response, ct);

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private string BaseUrl => _baseUrl;

    private string MessagesUrl => $"{BaseUrl}/{_phoneNumberId}/messages";

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("whatsapp");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return client;
    }

    private async Task<T> GetAsync<T>(string url, JsonTypeInfo<T> jsonTypeInfo, CancellationToken ct)
    {
        using var response = await CreateClient().GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            await ThrowGraphExceptionAsync(response, ct);

        return await response.Content.ReadFromJsonAsync(jsonTypeInfo, ct)
            ?? throw new BotValidationException("WhatsApp Graph API returned an empty response.");
    }

    private Task<TResponse> PostMessageAsync<TRequest, TResponse>(
        TRequest body,
        JsonTypeInfo<TRequest> requestJsonTypeInfo,
        JsonTypeInfo<TResponse> responseJsonTypeInfo,
        CancellationToken ct)
        => SendMessageAsync(HttpMethod.Post, body, requestJsonTypeInfo, responseJsonTypeInfo, ct);

    private async Task<TResponse> SendMessageAsync<TRequest, TResponse>(
        HttpMethod method,
        TRequest body,
        JsonTypeInfo<TRequest> requestJsonTypeInfo,
        JsonTypeInfo<TResponse> responseJsonTypeInfo,
        CancellationToken ct)
    {
        using var json = JsonContent.Create(body, requestJsonTypeInfo);
        using var request = new HttpRequestMessage(method, MessagesUrl) { Content = json };
        using var response = await CreateClient().SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            await ThrowGraphExceptionAsync(response, ct);

        return await response.Content.ReadFromJsonAsync(responseJsonTypeInfo, ct)
            ?? throw new BotValidationException("WhatsApp Graph API returned an empty response.");
    }

    private static async Task ThrowGraphExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var message = response.ReasonPhrase ?? "WhatsApp Graph API request failed.";
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<WhatsAppGraphErrorEnvelope>(
                WhatsAppBotJsonContext.Default.WhatsAppGraphErrorEnvelope,
                ct);
            if (envelope?.Error.Message is { Length: > 0 } graphMessage)
                message = graphMessage;
        }
        catch (JsonException)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(text))
                message = text;
        }

        throw (int)response.StatusCode switch
        {
            401 => new BotAuthenticationException(message),
            403 => new BotPermissionException(message),
            404 => new BotNotFoundException(message),
            429 => new BotRateLimitException(message),
            >= 400 and < 500 => new BotValidationException(message),
            _ => new BotValidationException(message),
        };
    }
}
