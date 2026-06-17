using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HPD.Agent.ClientTools;
using HPD.Agent.Serialization;

namespace HPD.Agent.TUI.Runtime;

public sealed class HostedAgentTuiRuntime : IHpdAgentTuiRuntime, IAgentTuiSessionBranchRuntime, IAgentTuiAgentRuntime, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly AgentTuiRuntimeScope _defaultScope;

    public HostedAgentTuiRuntime(HostedAgentTuiRuntimeOptions options)
        : this(CreateHttpClient(options), options, ownsHttpClient: true)
    {
    }

    public HostedAgentTuiRuntime(
        HttpClient httpClient,
        HostedAgentTuiRuntimeOptions options)
        : this(httpClient, options, ownsHttpClient: false)
    {
    }

    private HostedAgentTuiRuntime(
        HttpClient httpClient,
        HostedAgentTuiRuntimeOptions options,
        bool ownsHttpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _http = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _defaultScope = options.DefaultScope ?? new AgentTuiRuntimeScope(
            "default",
            "local-session",
            "main");

        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = NormalizeBaseAddress(options.BaseAddress);
        }
    }

    public bool CanSwitchAgents => true;

    public async Task<AgentTuiRuntimeScope> EnsureScopeAsync(
        AgentTuiRuntimeScope? requested,
        CancellationToken cancellationToken = default)
    {
        var scope = requested ?? _defaultScope;
        var sessionPath = $"sessions/{Escape(scope.SessionId)}";

        using var get = await _http.GetAsync(sessionPath, cancellationToken)
            .ConfigureAwait(false);
        if (get.StatusCode == HttpStatusCode.OK)
        {
            return scope;
        }

        if (get.StatusCode != HttpStatusCode.NotFound)
        {
            await ThrowForUnexpectedResponseAsync(get, "load session", cancellationToken)
                .ConfigureAwait(false);
        }

        var createJson = $$"""{"sessionId":{{JsonString(scope.SessionId)}}}""";
        using var create = await PostJsonEnvelopeAsync(
                "sessions",
                createJson,
                cancellationToken)
            .ConfigureAwait(false);
        if (create.StatusCode is not HttpStatusCode.Created and not HttpStatusCode.OK)
        {
            await ThrowForUnexpectedResponseAsync(create, "create session", cancellationToken)
                .ConfigureAwait(false);
        }

        return scope;
    }

    public async Task<IReadOnlyList<AgentTuiAgentInfo>> ListAgentsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("agents", cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "list agents", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadArrayAsync(response, ParseAgentInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiAgentInfo?> GetAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"agents/{Escape(agentId)}", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "load agent", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseAgentInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiAgentInfo> CreateAgentAsync(
        AgentTuiCreateAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var json = SerializeJson(JsonObject(
            ("name", JsonValue.Create(request.Name)),
            ("config", JsonSerializer.SerializeToNode(request.Config, HPDJsonContext.Default.AgentConfig)),
            ("metadata", ToJsonObject(request.Metadata))));
        using var response = await PostJsonEnvelopeAsync("agents", json, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is not HttpStatusCode.Created and not HttpStatusCode.OK)
        {
            await ThrowForUnexpectedResponseAsync(response, "create agent", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseAgentInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiAgentInfo> UpdateAgentAsync(
        string agentId,
        AgentTuiUpdateAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var json = SerializeJson(JsonObject(
            ("config", JsonSerializer.SerializeToNode(request.Config, HPDJsonContext.Default.AgentConfig))));
        using var response = await SendJsonEnvelopeAsync(
                HttpMethod.Put,
                $"agents/{Escape(agentId)}",
                json,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "update agent", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseAgentInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync($"agents/{Escape(agentId)}", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "delete agent", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<AgentTuiSessionInfo>> ListSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("sessions", cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "list sessions", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadArrayAsync(response, ParseSessionInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentTuiSessionInfo>> SearchSessionsAsync(
        AgentTuiSessionSearch? search = null,
        CancellationToken cancellationToken = default)
    {
        search ??= new AgentTuiSessionSearch();
        var json = SerializeJson(JsonObject(
            ("metadata", ToJsonObject(search.Metadata)),
            ("offset", JsonValue.Create(search.Offset)),
            ("limit", JsonValue.Create(search.Limit))));
        using var response = await PostJsonEnvelopeAsync("sessions/search", json, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "search sessions", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadArrayAsync(response, ParseSessionInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiSessionInfo?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"sessions/{Escape(sessionId)}", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "load session", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseSessionInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiSessionInfo> CreateSessionAsync(
        string? sessionId = null,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        var metadata = string.IsNullOrWhiteSpace(title)
            ? "null"
            : $$"""{"title":{{JsonString(title)}}}""";
        var json = $$"""{"sessionId":{{JsonNullableString(sessionId)}},"metadata":{{metadata}}}""";
        using var response = await PostJsonEnvelopeAsync("sessions", json, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is not HttpStatusCode.Created and not HttpStatusCode.OK)
        {
            await ThrowForUnexpectedResponseAsync(response, "create session", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseSessionInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RenameSessionAsync(
        string sessionId,
        string title,
        CancellationToken cancellationToken = default)
    {
        await UpdateSessionAsync(
                sessionId,
                new AgentTuiSessionUpdate(new Dictionary<string, object?> { ["title"] = title }),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiSessionInfo> UpdateSessionAsync(
        string sessionId,
        AgentTuiSessionUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var json = SerializeJson(JsonObject(
            ("metadata", ToJsonObject(update.Metadata))));
        using var response = await SendJsonEnvelopeAsync(
                HttpMethod.Patch,
                $"sessions/{Escape(sessionId)}",
                json,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "update session", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseSessionInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync($"sessions/{Escape(sessionId)}", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "delete session", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<AgentTuiBranchInfo>> ListBranchesAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"sessions/{Escape(sessionId)}/branches", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "list branches", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadArrayAsync(response, ParseBranchInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiBranchInfo?> GetBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
                $"sessions/{Escape(sessionId)}/branches/{Escape(branchId)}",
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "load branch", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseBranchInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiBranchInfo> CreateBranchAsync(
        string agentId,
        string sessionId,
        string? branchId = null,
        string? name = null,
        CancellationToken cancellationToken = default)
        => await CreateBranchAsync(
                agentId,
                sessionId,
                new AgentTuiCreateBranchRequest(branchId, name),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<AgentTuiBranchInfo> CreateBranchAsync(
        string agentId,
        string sessionId,
        AgentTuiCreateBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = string.IsNullOrWhiteSpace(request.BranchId) ? Guid.NewGuid().ToString("N")[..12] : request.BranchId;
        var json = SerializeJson(JsonObject(
            ("branchId", JsonValue.Create(id)),
            ("name", JsonValue.Create(request.Name)),
            ("description", JsonValue.Create(request.Description)),
            ("tags", ToJsonArray(request.Tags)),
            ("metadata", ToJsonObject(request.Metadata))));
        using var response = await PostJsonEnvelopeAsync(
                $"agents/{Escape(agentId)}/sessions/{Escape(sessionId)}/branches",
                json,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is not HttpStatusCode.Created and not HttpStatusCode.OK)
        {
            await ThrowForUnexpectedResponseAsync(response, "create branch", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseBranchInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiBranchInfo> ForkBranchAsync(
        string agentId,
        string sessionId,
        string sourceBranchId,
        AgentTuiForkBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var json = SerializeJson(JsonObject(
            ("newBranchId", JsonValue.Create(request.NewBranchId)),
            ("fromMessageId", JsonValue.Create(request.FromMessageId)),
            ("name", JsonValue.Create(request.Name)),
            ("description", JsonValue.Create(request.Description)),
            ("tags", ToJsonArray(request.Tags)),
            ("metadata", ToJsonObject(request.Metadata))));
        using var response = await PostJsonEnvelopeAsync(
                $"agents/{Escape(agentId)}/sessions/{Escape(sessionId)}/branches/{Escape(sourceBranchId)}/fork",
                json,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is not HttpStatusCode.Created and not HttpStatusCode.OK)
        {
            await ThrowForUnexpectedResponseAsync(response, "fork branch", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseBranchInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiBranchInfo> UpdateBranchAsync(
        string sessionId,
        string branchId,
        AgentTuiBranchUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var json = SerializeJson(JsonObject(
            ("name", JsonValue.Create(update.Name)),
            ("description", JsonValue.Create(update.Description)),
            ("tags", ToJsonArray(update.Tags)),
            ("metadata", ToJsonObject(update.Metadata))));
        using var response = await SendJsonEnvelopeAsync(
                HttpMethod.Patch,
                $"sessions/{Escape(sessionId)}/branches/{Escape(branchId)}",
                json,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "update branch", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseBranchInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentTuiBranchInfo>> GetSiblingBranchesAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
                $"sessions/{Escape(sessionId)}/branches/{Escape(branchId)}/siblings",
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "load sibling branches", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadArrayAsync(response, ParseBranchInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteBranchAsync(
        string sessionId,
        string branchId,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync(
                $"sessions/{Escape(sessionId)}/branches/{Escape(branchId)}?recursive={recursive.ToString().ToLowerInvariant()}",
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "delete branch", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<AgentEvent> ObserveAsync(
        AgentTuiRuntimeScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"agents/{Escape(scope.AgentId)}/sessions/{Escape(scope.SessionId)}/branches/{Escape(scope.BranchId)}/events/live");
        using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "observe live events", cancellationToken)
                .ConfigureAwait(false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken)
                .ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var json = line[5..].Trim();
            if (json.Length == 0)
            {
                continue;
            }

            if (AgentEventSerializer.FromJson(json) is AgentEvent evt)
            {
                yield return evt;
            }
        }
    }

    public async Task SubmitInputAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(input);

        var json = AgentEventSerializer.ToJson(input);
        using var response = await PostJsonEnvelopeAsync(
                $"agents/{Escape(scope.AgentId)}/sessions/{Escape(scope.SessionId)}/branches/{Escape(scope.BranchId)}/inputs",
                json,
                cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is not HttpStatusCode.Accepted and not HttpStatusCode.OK and not HttpStatusCode.NoContent)
        {
            await ThrowForUnexpectedResponseAsync(response, "submit input", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task RespondAsync(
        AgentTuiRuntimeScope scope,
        AgentEvent response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(response);

        if (response is not HPD.Events.IResponseEvent)
        {
            throw new NotSupportedException(
                $"Response event '{response.GetType().Name}' is not a request response event.");
        }

        var json = AgentEventSerializer.ToJson(response);
        using var httpResponse = await PostJsonEnvelopeAsync(
                $"agents/{Escape(scope.AgentId)}/sessions/{Escape(scope.SessionId)}/branches/{Escape(scope.BranchId)}/responses",
                json,
                cancellationToken)
            .ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(httpResponse, "send response", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<AgentEvent>> GetBranchEventsAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        using var response = await _http.GetAsync(
                $"sessions/{Escape(scope.SessionId)}/branches/{Escape(scope.BranchId)}/events",
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "load branch events", cancellationToken)
                .ConfigureAwait(false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var events = new List<AgentEvent>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (AgentEventSerializer.FromJson(element.GetRawText()) is AgentEvent evt)
            {
                events.Add(evt);
            }
        }

        return events;
    }

    public async Task<AgentTuiBranchRun?> GetActiveRunAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        using var response = await _http.GetAsync(
                $"agents/{Escape(scope.AgentId)}/sessions/{Escape(scope.SessionId)}/branches/{Escape(scope.BranchId)}/runs/active",
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "load active run", cancellationToken)
                .ConfigureAwait(false);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json) ||
            string.Equals(json.Trim(), "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var runtimeRunId = GetRequiredString(root, "runtimeRunId");
        var agentId = GetRequiredString(root, "agentId");
        var sessionId = GetRequiredString(root, "sessionId");
        var branchId = GetRequiredString(root, "branchId");
        var status = GetRequiredString(root, "status");
        var startedAt = GetRequiredDateTimeOffset(root, "startedAt");
        var completedAt = GetOptionalDateTimeOffset(root, "completedAt");
        var errorType = default(string?);
        var errorMessage = default(string?);
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            errorType = GetOptionalString(error, "type");
            errorMessage = GetOptionalString(error, "message");
        }

        return string.IsNullOrWhiteSpace(runtimeRunId)
            ? null
            : new AgentTuiBranchRun(
                runtimeRunId,
                agentId,
                sessionId,
                branchId,
                status,
                startedAt,
                completedAt,
                errorType,
                errorMessage);
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static HttpClient CreateHttpClient(HostedAgentTuiRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var http = options.MessageHandler is null
            ? new HttpClient()
            : new HttpClient(options.MessageHandler);
        http.BaseAddress = NormalizeBaseAddress(options.BaseAddress);
        http.Timeout = options.RequestTimeout;
        return http;
    }

    private static Uri NormalizeBaseAddress(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        var value = baseAddress.ToString();
        if (!value.EndsWith("/", StringComparison.Ordinal))
        {
            value += "/";
        }

        return new Uri(value, UriKind.Absolute);
    }

    private async Task<HttpResponseMessage> PostJsonEnvelopeAsync(
        string path,
        string json,
        CancellationToken cancellationToken)
        => await SendJsonEnvelopeAsync(HttpMethod.Post, path, json, cancellationToken)
            .ConfigureAwait(false);

    private async Task<HttpResponseMessage> SendJsonEnvelopeAsync(
        HttpMethod method,
        string path,
        string json,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return await _http.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string SerializeJson(JsonObject value)
        => JsonSerializer.Serialize(value, HPDJsonContext.Default.JsonObject);

    private static JsonObject JsonObject(params (string Key, JsonNode? Value)[] values)
    {
        var json = new JsonObject();
        foreach (var (key, value) in values)
        {
            json[key] = value;
        }

        return json;
    }

    private static JsonArray? ToJsonArray(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return null;
        }

        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add((JsonNode?)JsonValue.Create(value));
        }

        return array;
    }

    private static async Task ThrowForUnexpectedResponseAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        var body = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"Hosted TUI runtime failed to {operation}. HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static string JsonString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var ch in value)
        {
            builder.Append(ch switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => $"\\u{(int)ch:x4}",
                _ => ch.ToString()
            });
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string JsonNullableString(string? value)
        => string.IsNullOrWhiteSpace(value) ? "null" : JsonString(value);

    private static async Task<IReadOnlyList<T>> ReadArrayAsync<T>(
        HttpResponseMessage response,
        Func<JsonElement, T> parse,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<T>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            values.Add(parse(element));
        }

        return values;
    }

    private static async Task<T> ReadObjectAsync<T>(
        HttpResponseMessage response,
        Func<JsonElement, T> parse,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return parse(document.RootElement);
    }

    private static AgentTuiSessionInfo ParseSessionInfo(JsonElement element)
    {
        var metadata = ReadObjectMap(element, "metadata");
        return new AgentTuiSessionInfo(
            GetRequiredString(element, "id"),
            GetRequiredDateTimeOffset(element, "createdAt"),
            GetRequiredDateTimeOffset(element, "lastActivity"),
            metadata.TryGetValue("title", out var title) ? title?.ToString() : null,
            metadata);
    }

    private static AgentTuiAgentInfo ParseAgentInfo(JsonElement element)
    {
        var metadata = ReadObjectMap(element, "metadata");
        var config = element.TryGetProperty("config", out var configElement) &&
                     configElement.ValueKind == JsonValueKind.Object
            ? configElement.Deserialize(HPDJsonContext.Default.AgentConfig)
            : null;
        return new AgentTuiAgentInfo(
            GetRequiredString(element, "id"),
            GetRequiredString(element, "name"),
            GetRequiredDateTimeOffset(element, "createdAt"),
            GetRequiredDateTimeOffset(element, "updatedAt"),
            metadata,
            config);
    }

    private static AgentTuiBranchInfo ParseBranchInfo(JsonElement element)
        => new(
            GetRequiredString(element, "id"),
            GetRequiredString(element, "sessionId"),
            GetRequiredString(element, "name"),
            GetOptionalString(element, "description"),
            GetRequiredDateTimeOffset(element, "createdAt"),
            GetRequiredDateTimeOffset(element, "lastActivity"),
            GetOptionalInt32(element, "messageCount") ?? 0,
            GetOptionalBoolean(element, "isOriginal") ?? false,
            GetOptionalString(element, "forkedFrom"),
            GetOptionalString(element, "forkedAtMessageId"),
            GetOptionalInt32(element, "forkedAtMessageIndex"),
            GetOptionalInt32(element, "totalForks") ?? 0,
            ReadStringArray(element, "tags"),
            ReadStringMap(element, "ancestors"),
            GetOptionalInt32(element, "siblingIndex") ?? 0,
            GetOptionalInt32(element, "totalSiblings") ?? 1,
            GetOptionalString(element, "originalBranchId"),
            GetOptionalString(element, "previousSiblingId"),
            GetOptionalString(element, "nextSiblingId"),
            ReadObjectMap(element, "metadata"));

    private static JsonObject? ToJsonObject(
        IReadOnlyDictionary<string, object?>? values)
    {
        if (values is null)
        {
            return null;
        }

        var json = new JsonObject();
        foreach (var pair in values)
        {
            json[pair.Key] = ToJsonNode(pair.Value);
        }

        return json;
    }

    private static JsonNode? ToJsonNode(object? value)
        => value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            JsonElement element => JsonNode.Parse(element.GetRawText()),
            string stringValue => JsonValue.Create(stringValue),
            bool boolValue => JsonValue.Create(boolValue),
            int intValue => JsonValue.Create(intValue),
            long longValue => JsonValue.Create(longValue),
            double doubleValue => JsonValue.Create(doubleValue),
            float floatValue => JsonValue.Create(floatValue),
            decimal decimalValue => JsonValue.Create(decimalValue),
            DateTime dateTimeValue => JsonValue.Create(dateTimeValue),
            DateTimeOffset dateTimeOffsetValue => JsonValue.Create(dateTimeOffsetValue),
            Guid guidValue => JsonValue.Create(guidValue),
            _ => JsonValue.Create(value.ToString())
        };

    private static IReadOnlyDictionary<string, object?> ReadObjectMap(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var item in property.EnumerateObject())
        {
            values[item.Name] = item.Value.ValueKind switch
            {
                JsonValueKind.String => item.Value.GetString(),
                JsonValueKind.Number when item.Value.TryGetInt64(out var longValue) => longValue,
                JsonValueKind.Number when item.Value.TryGetDouble(out var doubleValue) => doubleValue,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => item.Value.GetRawText()
            };
        }

        return values;
    }

    private static IReadOnlyList<string>? ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } value)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static IReadOnlyDictionary<string, string>? ReadStringMap(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in property.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String && item.Value.GetString() is { } value)
            {
                values[item.Name] = value;
            }
        }

        return values;
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
        => GetOptionalString(element, propertyName) ?? string.Empty;

    private static string? GetOptionalString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static DateTimeOffset GetRequiredDateTimeOffset(JsonElement element, string propertyName)
        => GetOptionalDateTimeOffset(element, propertyName) ?? DateTimeOffset.MinValue;

    private static DateTimeOffset? GetOptionalDateTimeOffset(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetDateTimeOffset()
            : null;

    private static int? GetOptionalInt32(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : null;

    private static bool? GetOptionalBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
}
