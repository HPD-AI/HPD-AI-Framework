using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Agent.Client;

/// <summary>Invariant HTTP transport supporting bounded retries and observable SSE resume.</summary>
public sealed class HpdAgentHttpTransport
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseAddress;
    private readonly IReadOnlyDictionary<string, string> _defaultHeaders;
    private readonly HpdAgentRetryOptions _retry;

    internal HpdAgentHttpTransport(
        HttpClient httpClient,
        Uri baseAddress,
        IReadOnlyDictionary<string, string> defaultHeaders,
        HpdAgentRetryOptions retry)
    {
        _httpClient = httpClient;
        _baseAddress = baseAddress;
        _defaultHeaders = defaultHeaders;
        _retry = retry;
    }

    public async Task SendAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        JsonElement? body,
        bool replaySafe,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(
                method,
                path,
                query,
                body,
                replaySafe,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        JsonElement? body,
        bool replaySafe,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(
                method,
                path,
                query,
                body,
                replaySafe,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        if (typeof(T) == typeof(JsonElement))
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return (T)(object)document.RootElement.Clone();
        }
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new JsonException($"HPD Agent response '{typeof(T)}' was null.");
    }

    /// <summary>Returns null for a declared 404 response and deserializes every other success.</summary>
    public async Task<T?> SendJsonOrDefaultAsync<T>(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        using var response = await SendWithRetryAsync(
                method,
                path,
                query,
                body: null,
                replaySafe: false,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<byte[]> SendBytesAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        bool replaySafe,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(
                method,
                path,
                query,
                body: null,
                replaySafe,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> SendMultipartJsonAsync<T>(
        string path,
        IReadOnlyDictionary<string, string?>? query,
        Stream stream,
        string fieldName,
        string fileName,
        string contentType,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        // A stream body is not replayed after the first attempt unless the caller
        // explicitly supplies a seekable stream and a future replay-safe contract.
        using var request = CreateRequest(HttpMethod.Post, path, query, _defaultHeaders);
        using var multipart = new MultipartFormDataContent();
        using var file = new StreamContent(stream);
        if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
            file.Headers.ContentType = mediaType;
        multipart.Add(file, fieldName, fileName);
        request.Content = multipart;
        using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(responseStream, typeInfo, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new JsonException($"HPD Agent response '{typeof(T)}' was null.");
    }

    public Task<HpdAgentRawResponse> SendRawAsync(
        HttpMethod method,
        string relativePath,
        string? jsonBody,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return SendRawCoreAsync(method, relativePath, jsonBody, cancellationToken);
    }

    public async IAsyncEnumerable<HpdAgentSseMessage> StreamSseAsync(
        string path,
        IReadOnlyDictionary<string, string?>? query,
        string? lastEventId,
        HpdAgentSseReconnectOptions? reconnectOptions,
        Action<HpdAgentSseReconnectDiagnostic>? onReconnect,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var options = reconnectOptions ?? new HpdAgentSseReconnectOptions();
        var reconnects = 0;
        var currentAfter = lastEventId;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptQuery = CopyQuery(query);
            if (currentAfter is not null)
                attemptQuery["after"] = currentAfter;
            var receivedAny = false;
            string? terminalReason = null;
            await using var enumerator = StreamSseOnceAsync(
                    path,
                    attemptQuery,
                    currentAfter,
                    cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                HpdAgentSseMessage current;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                        break;
                    current = enumerator.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }
                catch (Exception exception) when (IsReconnectFailure(exception))
                {
                    terminalReason = exception.Message;
                    break;
                }

                receivedAny = true;
                if (!string.IsNullOrWhiteSpace(current.Id))
                    currentAfter = current.Id;
                yield return current;
            }

            if (!options.ReconnectOnEnd && terminalReason is null)
                yield break;
            if (reconnects >= options.MaxReconnects)
            {
                if (terminalReason is not null)
                    throw new HpdAgentStreamException(terminalReason);
                yield break;
            }

            reconnects++;
            var delay = RetryDelay(reconnects, options.InitialDelay, options.MaxDelay);
            onReconnect?.Invoke(new HpdAgentSseReconnectDiagnostic(
                reconnects,
                options.MaxReconnects,
                terminalReason ?? (receivedAny ? "Stream ended." : "No events received."),
                currentAfter,
                delay,
                terminalReason is null));
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    internal HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        IReadOnlyDictionary<string, string>? headers,
        JsonElement? body = null)
    {
        var request = new HttpRequestMessage(method, BuildUri(path, query));
        if (headers is not null)
        {
            foreach (var header in headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        if (body is { } value)
        {
            request.Content = new ByteArrayContent(
                JsonSerializer.SerializeToUtf8Bytes(value, JsonElementContext.Default.JsonElement));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        return request;
    }

    private async Task<HpdAgentRawResponse> SendRawCoreAsync(
        HttpMethod method,
        string relativePath,
        string? jsonBody,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, relativePath, null, _defaultHeaders);
        if (jsonBody is not null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new HpdAgentRawResponse(
            response.StatusCode,
            CopyHeaders(response),
            body);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        JsonElement? body,
        bool replaySafe,
        CancellationToken cancellationToken)
    {
        var canRetry = replaySafe || IsIdempotent(method);
        var attempts = canRetry ? _retry.MaxRetries + 1 : 1;
        Exception? lastException = null;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = CreateRequest(method, path, query, _defaultHeaders, body);
            try
            {
                var response = await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (attempt + 1 >= attempts || !IsRetryableStatus(response.StatusCode))
                    return response;

                var delay = RetryDelay(response, attempt + 1);
                response.Dispose();
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                canRetry &&
                exception is HttpRequestException or IOException or TaskCanceledException)
            {
                lastException = exception;
                if (attempt + 1 >= attempts)
                    break;
                var delay = RetryDelay(attempt + 1);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        throw new HttpRequestException("HPD Agent request failed after retries.", lastException);
    }

    private async IAsyncEnumerable<HpdAgentSseMessage> StreamSseOnceAsync(
        string path,
        IReadOnlyDictionary<string, string?>? query,
        string? lastEventId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path, query, _defaultHeaders);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (lastEventId is not null)
            request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
        using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var eventName = "message";
        string? id = null;
        var data = new StringBuilder();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                if (data.Length > 0 &&
                    TryCreateSseMessage(eventName, id, data, out var finalMessage))
                {
                    yield return finalMessage;
                }
                yield break;
            }
            if (line.Length == 0)
            {
                if (data.Length > 0 &&
                    TryCreateSseMessage(eventName, id, data, out var message))
                {
                    yield return message;
                }
                eventName = "message";
                id = null;
                data.Clear();
                continue;
            }
            if (line[0] == ':')
                continue;
            var separator = line.IndexOf(':');
            var field = separator < 0 ? line : line[..separator];
            var value = separator < 0
                ? string.Empty
                : line[(separator + 1)..].TrimStart();
            switch (field)
            {
                case "event":
                    eventName = value;
                    break;
                case "id":
                    id = value;
                    break;
                case "data":
                    if (data.Length > 0)
                        data.Append('\n');
                    data.Append(value);
                    break;
            }
        }
    }

    private Uri BuildUri(string path, IReadOnlyDictionary<string, string?>? query)
    {
        var relative = path.TrimStart('/');
        if (query is { Count: > 0 })
        {
            var values = query
                .Where(static pair => pair.Value is not null)
                .Select(static pair =>
                    Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value!))
                .ToArray();
            if (values.Length > 0)
                relative += "?" + string.Join("&", values);
        }
        return new Uri(_baseAddress.ToString().TrimEnd('/') + "/" + relative);
    }

    private static Dictionary<string, string?> CopyQuery(
        IReadOnlyDictionary<string, string?>? query) =>
        query is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(query, StringComparer.Ordinal);

    private static bool TryCreateSseMessage(
        string eventName,
        string? id,
        StringBuilder data,
        out HpdAgentSseMessage message)
    {
        try
        {
            using var document = JsonDocument.Parse(data.ToString());
            message = new HpdAgentSseMessage(eventName, id, document.RootElement.Clone());
            return true;
        }
        catch (JsonException)
        {
            message = null!;
            return false;
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HpdAgentApiException(response.StatusCode, body, CopyHeaders(response));
    }

    private static IReadOnlyDictionary<string, string[]> CopyHeaders(HttpResponseMessage response)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers.Concat(response.Content.Headers))
            result[header.Key] = header.Value.ToArray();
        return result;
    }

    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get ||
        method == HttpMethod.Head ||
        method == HttpMethod.Options ||
        method == HttpMethod.Put ||
        method == HttpMethod.Delete;

    private static bool IsRetryableStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.Conflict or
            HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static bool IsReconnectFailure(Exception exception) =>
        exception is HttpRequestException or IOException or HpdAgentStreamException ||
        exception is HpdAgentApiException api &&
        IsRetryableStatus(api.StatusCode);

    private TimeSpan RetryDelay(int attempt) =>
        RetryDelay(attempt, _retry.InitialDelay, _retry.MaxDelay);

    private TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.TryGetValues("Retry-After-Ms", out var milliseconds) &&
            int.TryParse(milliseconds.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
            value >= 0)
        {
            return TimeSpan.FromMilliseconds(Math.Min(value, _retry.MaxDelay.TotalMilliseconds));
        }
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return delta > _retry.MaxDelay ? _retry.MaxDelay : delta;
        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var remaining = date - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return TimeSpan.Zero;
            return remaining > _retry.MaxDelay ? _retry.MaxDelay : remaining;
        }
        return RetryDelay(attempt);
    }

    private static TimeSpan RetryDelay(
        int attempt,
        TimeSpan initialDelay,
        TimeSpan maxDelay)
    {
        var exponent = Math.Min(attempt - 1, 20);
        var milliseconds = initialDelay.TotalMilliseconds * Math.Pow(2, exponent);
        var jitter = 0.8 + Random.Shared.NextDouble() * 0.4;
        var result = TimeSpan.FromMilliseconds(milliseconds * jitter);
        return result > maxDelay ? maxDelay : result;
    }
}

/// <summary>Reports a stream that ended or failed after reconnect policy was exhausted.</summary>
public class HpdAgentStreamException : Exception
{
    public HpdAgentStreamException(string message)
        : base(message)
    {
    }

    public HpdAgentStreamException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

[JsonSerializable(typeof(JsonElement))]
internal sealed partial class JsonElementContext : JsonSerializerContext;
