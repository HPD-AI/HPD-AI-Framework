using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HPD.OpenApi.Core.Model;

namespace HPD.OpenApi.Core;

/// <summary>
/// Builds and sends HTTP requests for REST API operations.
/// Adapted from SK's RestApiOperationRunner.
///
/// Framework-agnostic: accepts <see cref="IDictionary{TKey,TValue}"/> so that HPD.OpenApi.Core
/// has no dependency on Microsoft.Extensions.AI. AIFunctionArguments implements
/// <c>IDictionary&lt;string, object?&gt;</c> directly — the agent consumer passes it in with
/// zero conversion cost.
/// </summary>
public sealed class OpenApiOperationRunner
{
    private readonly HttpClient _httpClient;
    private readonly Func<HttpRequestMessage, CancellationToken, Task>? _authCallback;
    private readonly Func<HttpResponseMessage, string?, OpenApiErrorResponse?>? _errorDetector;
    private readonly string? _userAgent;
    private readonly bool _enableDynamicPayload;
    private readonly bool _enablePayloadNamespacing;

    public OpenApiOperationRunner(
        HttpClient httpClient,
        Func<HttpRequestMessage, CancellationToken, Task>? authCallback = null,
        string? userAgent = null,
        bool enableDynamicPayload = true,
        bool enablePayloadNamespacing = false,
        Func<HttpResponseMessage, string?, OpenApiErrorResponse?>? errorDetector = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authCallback = authCallback;
        _userAgent = userAgent;
        _enableDynamicPayload = enableDynamicPayload;
        _enablePayloadNamespacing = enablePayloadNamespacing;
        _errorDetector = errorDetector;
    }

    /// <summary>
    /// Executes a REST API operation and returns the response as a parsed object.
    /// Returns <see cref="OpenApiErrorResponse"/> for non-success HTTP status codes or
    /// when the error detector identifies a logical error. Never throws for HTTP errors.
    /// </summary>
    public async Task<object?> RunAsync(
        RestApiOperation operation,
        IDictionary<string, object?> arguments,
        Uri? serverUrlOverride,
        CancellationToken cancellationToken)
    {
        var url = BuildOperationUrl(operation, arguments, serverUrlOverride);
        using var request = new HttpRequestMessage(operation.Method, url);

        foreach (var param in operation.Parameters
            .Where(p => p.Location == RestApiParameterLocation.Header))
        {
            if (arguments.TryGetValue(param.Name, out var value) && value != null)
                request.Headers.TryAddWithoutValidation(param.Name, value.ToString());
        }

        var content = BuildPayload(operation, arguments);
        if (content != null) request.Content = content;

        if (_authCallback != null)
            await _authCallback(request, cancellationToken);

        if (!string.IsNullOrEmpty(_userAgent))
            request.Headers.UserAgent.ParseAdd(_userAgent);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ProcessResponseAsync(response, operation, cancellationToken);
    }

    /// <summary>
    /// Returns the raw <see cref="HttpResponseMessage"/> without consuming the body.
    /// Caller is responsible for disposing. Use when streaming or custom response
    /// handling is needed (e.g. binary downloads, custom deserialization).
    /// </summary>
    public async Task<HttpResponseMessage> RunRawAsync(
        RestApiOperation operation,
        IDictionary<string, object?> arguments,
        Uri? serverUrlOverride,
        CancellationToken cancellationToken)
    {
        var url = BuildOperationUrl(operation, arguments, serverUrlOverride);
        // Note: request is NOT using 'using' here — caller owns disposal of response + request.
        var request = new HttpRequestMessage(operation.Method, url);

        foreach (var param in operation.Parameters
            .Where(p => p.Location == RestApiParameterLocation.Header))
        {
            if (arguments.TryGetValue(param.Name, out var value) && value != null)
                request.Headers.TryAddWithoutValidation(param.Name, value.ToString());
        }

        var content = BuildPayload(operation, arguments);
        if (content != null) request.Content = content;

        if (_authCallback != null)
            await _authCallback(request, cancellationToken);

        if (!string.IsNullOrEmpty(_userAgent))
            request.Headers.UserAgent.ParseAdd(_userAgent);

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private Uri BuildOperationUrl(
        RestApiOperation operation,
        IDictionary<string, object?> arguments,
        Uri? serverUrlOverride)
    {
        var serverUrl = serverUrlOverride?.ToString()
            ?? operation.ServerUrl
            ?? throw new InvalidOperationException(
                $"No server URL available for operation '{operation.Id}'. " +
                "Set ServerUrlOverride on the config or ensure the spec includes a server URL.");

        // Substitute server variables (e.g. https://{region}.api.example.com/{version})
        // Priority per variable: ArgumentName lookup → variable name lookup → default → error
        if (operation.ServerVariables is { Count: > 0 } vars)
        {
            foreach (var (varName, variable) in vars)
            {
                string? resolved = null;

                // 1. Try ArgumentName first
                if (!string.IsNullOrEmpty(variable.ArgumentName)
                    && arguments.TryGetValue(variable.ArgumentName!, out var argVal)
                    && argVal is string argStr
                    && variable.IsValid(argStr))
                {
                    resolved = argStr;
                }
                // 2. Try variable name
                else if (arguments.TryGetValue(varName, out var varVal)
                    && varVal is string varStr
                    && variable.IsValid(varStr))
                {
                    resolved = varStr;
                }
                // 3. Fall back to default
                else if (!string.IsNullOrEmpty(variable.Default))
                {
                    resolved = variable.Default;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"No value provided for required server variable '{variable.ArgumentName ?? varName}' " +
                        $"of operation '{operation.Id}', and the variable has no default.");
                }

                serverUrl = serverUrl.Replace($"{{{varName}}}", resolved);
            }
        }

        var url = serverUrl.TrimEnd('/') + operation.Path;

        foreach (var param in operation.Parameters
            .Where(p => p.Location == RestApiParameterLocation.Path))
        {
            if (arguments.TryGetValue(param.Name, out var value))
                url = url.Replace($"{{{param.Name}}}",
                    Uri.EscapeDataString(value?.ToString() ?? ""));
        }

        var queryParams = new List<string>();
        foreach (var param in operation.Parameters
            .Where(p => p.Location == RestApiParameterLocation.Query))
        {
            if (!arguments.TryGetValue(param.Name, out var value) || value == null)
                continue;

            var encodedName = Uri.EscapeDataString(param.Name);

            // Array values: serialize according to the Expand (explode) flag.
            // Expand=true  → repeated keys: status=available&status=pending
            // Expand=false → comma-delimited: tags=cat,dog
            if (value is System.Collections.IEnumerable enumerable and not string)
            {
                var items = enumerable.Cast<object?>()
                    .Where(v => v != null)
                    .Select(v => Uri.EscapeDataString(v!.ToString()!))
                    .ToList();

                if (items.Count == 0) continue;

                if (param.Expand)
                    queryParams.AddRange(items.Select(item => $"{encodedName}={item}"));
                else
                    queryParams.Add($"{encodedName}={string.Join("%2C", items)}");
            }
            else
            {
                queryParams.Add($"{encodedName}={Uri.EscapeDataString(value.ToString() ?? "")}");
            }
        }

        if (queryParams.Count > 0) url += "?" + string.Join("&", queryParams);
        return new Uri(url);
    }

    private HttpContent? BuildPayload(
        RestApiOperation operation,
        IDictionary<string, object?> arguments)
    {
        if (operation.Payload is null) return null;

        if (!_enableDynamicPayload)
        {
            if (arguments.TryGetValue("payload", out var payloadArg) && payloadArg is string payloadStr)
                return new StringContent(payloadStr, Encoding.UTF8, operation.Payload.MediaType);
            return null;
        }

        if (operation.Payload.MediaType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            var body = BuildJsonPayload(operation.Payload.Properties, arguments, null);
            return new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        if (operation.Payload.MediaType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase))
        {
            if (arguments.TryGetValue("payload", out var val) && val is string text)
                return new StringContent(text, Encoding.UTF8, "text/plain");
        }

        return null;
    }

    private JsonObject BuildJsonPayload(
        IList<RestApiPayloadProperty> properties,
        IDictionary<string, object?> arguments,
        string? propertyNamespace)
    {
        var result = new JsonObject();
        foreach (var prop in properties)
        {
            var argName = _enablePayloadNamespacing && !string.IsNullOrEmpty(propertyNamespace)
                ? $"{propertyNamespace}.{prop.Name}"
                : prop.Name;

            if (prop.Type == "object")
            {
                result.Add(prop.Name, BuildJsonPayload(prop.Properties, arguments, argName));
                continue;
            }

            if (arguments.TryGetValue(argName, out var value) && value != null)
            {
                result.Add(prop.Name, JsonValue.Create(value));
                continue;
            }

            if (prop.IsRequired)
                throw new InvalidOperationException(
                    $"Required payload property '{prop.Name}' is missing.");
        }
        return result;
    }

    private async Task<object?> ProcessResponseAsync(
        HttpResponseMessage response,
        RestApiOperation operation,
        CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);
        var statusCode = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            return new OpenApiErrorResponse
            {
                Error = true,
                StatusCode = statusCode,
                ReasonPhrase = response.ReasonPhrase,
                Body = content,
                RetryAfter = ExtractRetryAfter(response)
            };
        }

        if (_errorDetector != null)
        {
            var detected = _errorDetector(response, content);
            if (detected != null) return detected;
        }

        // Parse the body — prefer structured JSON so middleware can filter fields.
        object? parsedContent = null;
        if (!string.IsNullOrEmpty(content))
        {
            try { parsedContent = JsonDocument.Parse(content).RootElement.Clone(); }
            catch (JsonException) { parsedContent = content; }
        }

        // Match the response schema from the spec: exact status code → wildcard (e.g. 2XX) → "default".
        var expectedSchema = ResolveResponseSchema(operation.ResponseSchemas, statusCode);

        return new OpenApiOperationResponse
        {
            Content = parsedContent,
            StatusCode = statusCode,
            ExpectedSchema = expectedSchema
        };
    }

    /// <summary>
    /// Matches a response schema from the spec to the actual HTTP status code.
    /// Priority: exact match → wildcard (e.g. 2XX) → "default".
    /// </summary>
    private static JsonElement? ResolveResponseSchema(
        IReadOnlyDictionary<string, JsonElement> schemas, int statusCode)
    {
        if (schemas.Count == 0) return null;

        var statusStr = statusCode.ToString();

        // Exact match (e.g. "200")
        if (schemas.TryGetValue(statusStr, out var exact)) return exact;

        // Wildcard match (e.g. "2XX")
        var wildcardKey = $"{statusStr[0]}XX";
        if (schemas.TryGetValue(wildcardKey, out var wildcard)) return wildcard;

        // Default
        if (schemas.TryGetValue("default", out var def)) return def;

        return null;
    }

    private static TimeSpan? ExtractRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is null) return null;
        if (response.Headers.RetryAfter.Delta.HasValue) return response.Headers.RetryAfter.Delta.Value;
        if (response.Headers.RetryAfter.Date.HasValue)
        {
            var delay = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1);
        }
        return null;
    }
}
