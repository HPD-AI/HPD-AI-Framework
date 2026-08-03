using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HPD.Agent.ClientTools;
using HPD.Agent.Serialization;

namespace HPD.Agent.TUI.Runtime;

public sealed class HostedAgentTuiRuntime : IHpdAgentTuiRuntime, IAgentTuiSessionThreadRuntime, IAgentTuiAgentRuntime, IAsyncDisposable
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

    public async Task<AgentTuiScopeResolution> ResolveInitialScopeAsync(
        AgentTuiRuntimeScope? requested,
        CancellationToken cancellationToken = default)
    {
        var scope = requested ?? _defaultScope;
        if (await ScopeExistsAsync(scope, cancellationToken).ConfigureAwait(false))
        {
            return new AgentTuiScopeResolution(scope, IsDurable: true);
        }

        return new AgentTuiScopeResolution(scope, IsDurable: false);
    }

    public async Task<AgentTuiRuntimeScope> EnsureDurableScopeAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default)
    {
        if (!await SessionExistsAsync(scope.SessionId, cancellationToken).ConfigureAwait(false))
        {
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
        }

        if (!await ThreadExistsAsync(scope.SessionId, scope.ThreadId, cancellationToken).ConfigureAwait(false))
        {
            var createThreadJson = $$"""{"threadId":{{JsonString(scope.ThreadId)}}}""";
            using var createThread = await PostJsonEnvelopeAsync(
                    $"agents/{Escape(scope.AgentId)}/sessions/{Escape(scope.SessionId)}/threads",
                    createThreadJson,
                    cancellationToken)
                .ConfigureAwait(false);
            if (createThread.StatusCode is not HttpStatusCode.Created and not HttpStatusCode.OK and not HttpStatusCode.Conflict)
            {
                await ThrowForUnexpectedResponseAsync(createThread, "create thread", cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return scope;
    }

    private async Task<bool> ScopeExistsAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken)
        => await SessionExistsAsync(scope.SessionId, cancellationToken).ConfigureAwait(false) &&
            await ThreadExistsAsync(scope.SessionId, scope.ThreadId, cancellationToken).ConfigureAwait(false);

    private async Task<bool> SessionExistsAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var sessionPath = $"sessions/{Escape(sessionId)}";

        using var get = await _http.GetAsync(sessionPath, cancellationToken)
            .ConfigureAwait(false);
        if (get.StatusCode == HttpStatusCode.OK)
        {
            return true;
        }

        if (get.StatusCode != HttpStatusCode.NotFound)
        {
            await ThrowForUnexpectedResponseAsync(get, "load session", cancellationToken)
                .ConfigureAwait(false);
        }

        return false;
    }

    private async Task<bool> ThreadExistsAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken)
    {
        using var get = await _http.GetAsync(
                $"sessions/{Escape(sessionId)}/threads/{Escape(threadId)}",
                cancellationToken)
            .ConfigureAwait(false);
        if (get.StatusCode == HttpStatusCode.OK)
        {
            return true;
        }

        if (get.StatusCode != HttpStatusCode.NotFound)
        {
            await ThrowForUnexpectedResponseAsync(get, "load thread", cancellationToken)
                .ConfigureAwait(false);
        }

        return false;
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

    public async Task<IReadOnlyList<AgentTuiThreadInfo>> ListThreadsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"sessions/{Escape(sessionId)}/threads", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "list threads", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadArrayAsync(response, ParseThreadInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiThreadInfo?> GetThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
                $"sessions/{Escape(sessionId)}/threads/{Escape(threadId)}",
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "load thread", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseThreadInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiThreadInfo> CreateThreadAsync(
        string agentId,
        string sessionId,
        string? threadId = null,
        string? name = null,
        CancellationToken cancellationToken = default)
        => await CreateThreadAsync(
                agentId,
                sessionId,
                new AgentTuiCreateThreadRequest(threadId, name),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<AgentTuiThreadInfo> CreateThreadAsync(
        string agentId,
        string sessionId,
        AgentTuiCreateThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = string.IsNullOrWhiteSpace(request.ThreadId) ? Guid.NewGuid().ToString("N")[..12] : request.ThreadId;
        var json = SerializeJson(JsonObject(
            ("threadId", JsonValue.Create(id)),
            ("name", JsonValue.Create(request.Name)),
            ("description", JsonValue.Create(request.Description)),
            ("tags", ToJsonArray(request.Tags)),
            ("metadata", ToJsonObject(request.Metadata))));
        using var response = await PostJsonEnvelopeAsync(
                $"agents/{Escape(agentId)}/sessions/{Escape(sessionId)}/threads",
                json,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is not HttpStatusCode.Created and not HttpStatusCode.OK)
        {
            await ThrowForUnexpectedResponseAsync(response, "create thread", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseThreadInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiThreadInfo> ForkThreadAsync(
        string agentId,
        string sessionId,
        string sourceThreadId,
        AgentTuiForkThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var json = SerializeJson(JsonObject(
            ("newThreadId", JsonValue.Create(request.NewThreadId)),
            ("fromMessageId", JsonValue.Create(request.FromMessageId)),
            ("name", JsonValue.Create(request.Name)),
            ("description", JsonValue.Create(request.Description)),
            ("tags", ToJsonArray(request.Tags)),
            ("metadata", ToJsonObject(request.Metadata))));
        using var response = await PostJsonEnvelopeAsync(
                $"agents/{Escape(agentId)}/sessions/{Escape(sessionId)}/threads/{Escape(sourceThreadId)}/fork",
                json,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is not HttpStatusCode.Created and not HttpStatusCode.OK)
        {
            await ThrowForUnexpectedResponseAsync(response, "fork thread", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseThreadInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiThreadInfo> UpdateThreadAsync(
        string sessionId,
        string threadId,
        AgentTuiThreadUpdate update,
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
                $"sessions/{Escape(sessionId)}/threads/{Escape(threadId)}",
                json,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "update thread", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseThreadInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiThreadGraph> GetThreadGraphAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
                $"sessions/{Escape(sessionId)}/thread-graph",
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new AgentTuiThreadGraph([], [], []);
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "load thread graph", cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadObjectAsync(response, ParseThreadGraph, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteThreadAsync(
        string sessionId,
        string threadId,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync(
                $"sessions/{Escape(sessionId)}/threads/{Escape(threadId)}?recursive={recursive.ToString().ToLowerInvariant()}",
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "delete thread", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<AgentTuiEventBatch> ObserveAsync(
        AgentTuiRuntimeScope scope,
        ThreadJournalCursor after,
        ThreadJournalCursor initialObservedCursor,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var cursor = after;
        var catchUpMode = after.SequenceNumber == 0
            ? AgentTuiEventDeliveryMode.Historical
            : AgentTuiEventDeliveryMode.CatchUp;
        while (!cancellationToken.IsCancellationRequested)
        {
            var pendingCatchUp = new List<AgentEvent>(256);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"agents/{Escape(scope.AgentId)}/sessions/{Escape(scope.SessionId)}/threads/{Escape(scope.ThreadId)}/events?after={cursor.Generation}:{cursor.SequenceNumber}");
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
            var eventName = "message";
            var eventCursor = cursor;
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (line.StartsWith("id:", StringComparison.Ordinal))
                {
                    eventCursor = ParseCursor(line[3..].Trim());
                    continue;
                }
                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    eventName = line[6..].Trim();
                    continue;
                }
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var json = line[5..].Trim();
                if (string.Equals(eventName, "thread-journal-rebased", StringComparison.Ordinal))
                {
                    using var control = JsonDocument.Parse(json);
                    var previous = control.RootElement.GetProperty("previousGeneration").GetInt64();
                    var current = control.RootElement.GetProperty("currentGeneration").GetInt64();
                    throw new ThreadJournalReplacedException(
                        new ThreadKey(scope.SessionId, scope.ThreadId),
                        new ThreadJournalCursor(previous, cursor.SequenceNumber),
                        ThreadJournalCursor.Start(current));
                }
                if (json.Length == 0 || AgentEventSerializer.FromJson(json) is not AgentEvent evt)
                {
                    continue;
                }

                if (string.Equals(eventName, "live-agent-event", StringComparison.Ordinal))
                {
                    if (pendingCatchUp.Count > 0)
                    {
                        var catchUp = pendingCatchUp.ToArray();
                        yield return CreateDeliveryBatch(catchUp, catchUpMode, initialObservedCursor, eventCursor.Generation);
                        cursor = new ThreadJournalCursor(eventCursor.Generation, catchUp[^1].ThreadSequenceNumber);
                        pendingCatchUp.Clear();
                    }

                    var liveCursor = evt.ThreadSequenceNumber > 0 ? eventCursor : cursor;
                    yield return new AgentTuiEventBatch(
                        [evt],
                        AgentTuiEventDeliveryMode.Live,
                        initialObservedCursor,
                        liveCursor,
                        liveCursor);
                    cursor = liveCursor;
                    continue;
                }

                if (eventCursor.Generation != cursor.Generation ||
                    evt.ThreadSequenceNumber > 0 && evt.ThreadSequenceNumber <= cursor.SequenceNumber)
                {
                    continue;
                }

                if (evt.ThreadSequenceNumber <= initialObservedCursor.SequenceNumber)
                {
                    pendingCatchUp.Add(evt);
                    if (pendingCatchUp.Count < 256 && evt.ThreadSequenceNumber < initialObservedCursor.SequenceNumber)
                        continue;

                    var catchUp = pendingCatchUp.ToArray();
                    yield return CreateDeliveryBatch(catchUp, catchUpMode, initialObservedCursor, eventCursor.Generation);
                    cursor = new ThreadJournalCursor(eventCursor.Generation, catchUp[^1].ThreadSequenceNumber);
                    pendingCatchUp.Clear();
                    continue;
                }

                if (pendingCatchUp.Count > 0)
                {
                    var catchUp = pendingCatchUp.ToArray();
                    yield return CreateDeliveryBatch(catchUp, catchUpMode, initialObservedCursor, eventCursor.Generation);
                    cursor = new ThreadJournalCursor(eventCursor.Generation, catchUp[^1].ThreadSequenceNumber);
                    pendingCatchUp.Clear();
                }

                yield return CreateDeliveryBatch([evt], AgentTuiEventDeliveryMode.Live, initialObservedCursor, eventCursor.Generation);
                cursor = new ThreadJournalCursor(eventCursor.Generation, evt.ThreadSequenceNumber);
            }

            if (pendingCatchUp.Count > 0)
            {
                var catchUp = pendingCatchUp.ToArray();
                yield return CreateDeliveryBatch(catchUp, catchUpMode, initialObservedCursor, cursor.Generation);
                cursor = new ThreadJournalCursor(cursor.Generation, catchUp[^1].ThreadSequenceNumber);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
    }

    private static AgentTuiEventBatch CreateDeliveryBatch(
        IReadOnlyList<AgentEvent> events,
        AgentTuiEventDeliveryMode deliveryMode,
        ThreadJournalCursor initialObservedCursor,
        long generation) =>
        new(
            events,
            deliveryMode,
            initialObservedCursor,
            new ThreadJournalCursor(generation, events[0].ThreadSequenceNumber),
            new ThreadJournalCursor(generation, events[^1].ThreadSequenceNumber));

    private static ThreadJournalCursor ParseCursor(string value)
    {
        var separator = value.IndexOf(':');
        if (separator <= 0 ||
            !long.TryParse(value.AsSpan(0, separator), out var generation) || generation <= 0 ||
            !long.TryParse(value.AsSpan(separator + 1), out var sequence) || sequence < 0)
        {
            throw new InvalidDataException($"Invalid journal cursor '{value}'.");
        }
        return new ThreadJournalCursor(generation, sequence);
    }

    public async Task<AgentTuiSubmitResult> SubmitInputAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(input);

        var json = AgentEventSerializer.ToJson(input);
        using var response = await PostJsonEnvelopeAsync(
                $"agents/{Escape(scope.AgentId)}/sessions/{Escape(scope.SessionId)}/threads/{Escape(scope.ThreadId)}/inputs",
                json,
                cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is not HttpStatusCode.Accepted and not HttpStatusCode.OK and not HttpStatusCode.NoContent)
        {
            await ThrowForUnexpectedResponseAsync(response, "submit input", cancellationToken)
                .ConfigureAwait(false);
        }

        using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var disposition = ParseInputDisposition(GetRequiredString(document.RootElement, "disposition"));
        var threadExecutionId = document.RootElement.TryGetProperty("threadExecutionId", out var executionIdElement) &&
            executionIdElement.ValueKind == JsonValueKind.String
            ? executionIdElement.GetString()
            : null;
        var startedAt = document.RootElement.TryGetProperty("startedAt", out var startedAtElement) &&
            startedAtElement.ValueKind == JsonValueKind.String &&
            startedAtElement.TryGetDateTimeOffset(out var parsedStartedAt)
                ? parsedStartedAt
                : DateTimeOffset.UtcNow;
        AgentTuiThreadExecution? activeExecution = null;
        if (document.RootElement.TryGetProperty("activeExecution", out var activeElement) &&
            activeElement.ValueKind == JsonValueKind.Object)
        {
            activeExecution = ParseThreadExecution(activeElement);
        }
        else if (disposition == AgentInputDisposition.Queued && threadExecutionId is not null)
        {
            activeExecution = new AgentTuiThreadExecution(
                threadExecutionId, scope.AgentId, scope.SessionId, scope.ThreadId, "active", startedAt);
        }

        return new AgentTuiSubmitResult(disposition, threadExecutionId, activeExecution);
    }

    private static AgentInputDisposition ParseInputDisposition(string value) => value switch
    {
        "completed" => AgentInputDisposition.Completed,
        "queued" => AgentInputDisposition.Queued,
        "accepted" => AgentInputDisposition.Accepted,
        "no_active_execution" => AgentInputDisposition.NoActiveExecution,
        "active_execution_mismatch" => AgentInputDisposition.ActiveExecutionMismatch,
        "active_input_not_steerable" => AgentInputDisposition.ActiveInputNotSteerable,
        "execution_finishing" => AgentInputDisposition.ExecutionFinishing,
        _ => throw new InvalidOperationException($"Unknown input disposition '{value}'.")
    };

    public async Task<ThreadContextUsage> EstimateContextUsageAsync(
        AgentTuiRuntimeScope scope,
        AgentRunConfig? runConfig = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var json = JsonSerializer.Serialize(new AgentTuiContextUsageRequest(runConfig), JsonOptions);
        using var response = await PostJsonEnvelopeAsync(
                $"agents/{Escape(scope.AgentId)}/sessions/{Escape(scope.SessionId)}/threads/{Escape(scope.ThreadId)}/context-usage",
                json,
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "estimate context usage", cancellationToken)
                .ConfigureAwait(false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<ThreadContextUsage>(
                stream,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false)
            ?? new ThreadContextUsage
            {
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId,
                Source = "empty-hosted-response"
            };
    }

    public async Task<AgentRespondResult> AnswerRequestAsync(
        AgentTuiRuntimeScope scope,
        AgentEvent response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(response);

        if (response is not IAgentResponseEvent)
        {
            throw new NotSupportedException(
                $"Response event '{response.GetType().Name}' is not a request response event.");
        }

        var json = AgentEventSerializer.ToJson(response);
        using var httpResponse = await PostJsonEnvelopeAsync(
                $"agents/{Escape(scope.AgentId)}/sessions/{Escape(scope.SessionId)}/threads/{Escape(scope.ThreadId)}/responses",
                json,
                cancellationToken)
            .ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(httpResponse, "send response", cancellationToken)
                .ConfigureAwait(false);
        }

        await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<AgentRespondResult>(
                stream,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Hosted response endpoint returned an empty Agent response result.");
    }

    public async Task<AgentTuiThreadState> GetThreadStateAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        using var response = await _http.GetAsync(
                $"agents/{Escape(scope.AgentId)}/sessions/{Escape(scope.SessionId)}/threads/{Escape(scope.ThreadId)}/state",
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Thread '{scope.SessionId}/{scope.ThreadId}' does not have a durable journal.");
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForUnexpectedResponseAsync(response, "load thread state", cancellationToken)
                .ConfigureAwait(false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Thread state response must be a JSON object.");
        }

        var root = document.RootElement;
        var activeExecution = root.TryGetProperty("activeExecution", out var activeExecutionElement)
            ? ParseThreadExecution(activeExecutionElement)
            : null;
        var observedCursor = root.TryGetProperty("observedCursor", out var cursorElement)
            ? new ThreadJournalCursor(
                cursorElement.GetProperty("generation").GetInt64(),
                cursorElement.GetProperty("sequenceNumber").GetInt64())
            : throw new JsonException("Thread state response is missing observedCursor.");
        if (observedCursor.Generation <= 0 || observedCursor.SequenceNumber < 0)
        {
            throw new JsonException("Thread state observedCursor must contain a positive generation and non-negative sequence.");
        }
        var pendingRequests = new List<AgentEvent>();
        if (root.TryGetProperty("pendingRequests", out var pendingElement) &&
            pendingElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var pending in pendingElement.EnumerateArray())
            {
                if (pending.TryGetProperty("request", out var requestElement) &&
                    AgentEventSerializer.FromJson(requestElement.GetRawText()) is AgentEvent request)
                {
                    pendingRequests.Add(request);
                }
            }
        }

        return new AgentTuiThreadState(
            observedCursor,
            activeExecution,
            pendingRequests);
    }

    private static AgentTuiThreadExecution? ParseThreadExecution(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var threadExecutionId = GetRequiredString(root, "threadExecutionId");
        var agentId = GetRequiredString(root, "agentId");
        var sessionId = GetRequiredString(root, "sessionId");
        var threadId = GetRequiredString(root, "threadId");
        var status = GetRequiredString(root, "status");
        var startedAt = GetRequiredDateTimeOffset(root, "startedAt");
        var finishedAt = GetOptionalDateTimeOffset(root, "finishedAt");
        var errorType = default(string?);
        var errorMessage = default(string?);
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            errorType = GetOptionalString(error, "type");
            errorMessage = GetOptionalString(error, "message");
        }

        return string.IsNullOrWhiteSpace(threadExecutionId)
            ? null
            : new AgentTuiThreadExecution(
                threadExecutionId,
                agentId,
                sessionId,
                threadId,
                status,
                startedAt,
                finishedAt,
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

    private sealed record AgentTuiContextUsageRequest(AgentRunConfig? RunConfig);

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
        var hint = IsUnknownAgentEventPayload(body)
            ? " This usually means the hosted backend is older than the thread history it is reading; restart the backend so it loads the current agent event registrations."
            : string.Empty;
        throw new InvalidOperationException(
            $"Hosted TUI runtime failed to {operation}. HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {body}{hint}");
    }

    private static bool IsUnknownAgentEventPayload(string? body)
        => body?.Contains("JSON payload is not a known agent event", StringComparison.OrdinalIgnoreCase) == true;

    private static string Escape(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Uri.EscapeDataString(value);
    }

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

    private static AgentTuiThreadInfo ParseThreadInfo(JsonElement element)
        => new(
            GetRequiredString(element, "id"),
            GetRequiredString(element, "sessionId"),
            GetRequiredString(element, "defaultAgentId"),
            GetRequiredString(element, "name"),
            GetOptionalString(element, "description"),
            GetRequiredDateTimeOffset(element, "createdAt"),
            GetRequiredDateTimeOffset(element, "lastActivity"),
            GetOptionalInt32(element, "messageCount") ?? 0,
            GetOptionalString(element, "forkedFrom"),
            GetOptionalString(element, "forkedAtMessageId"),
            GetOptionalInt32(element, "forkedAtMessageIndex"),
            GetOptionalInt32(element, "totalForks") ?? 0,
            ReadStringArray(element, "tags"),
            ReadStringMap(element, "ancestors"),
            GetOptionalEnum(element, "kind", ThreadKind.MainAgent),
            GetOptionalEnum(element, "visibility", ThreadVisibility.Visible),
            GetOptionalString(element, "parentSessionId"),
            GetOptionalString(element, "parentThreadId"),
            GetOptionalString(element, "subAgentName"),
            GetOptionalString(element, "subAgentTaskName"),
            GetOptionalString(element, "invocationId"),
            GetOptionalString(element, "subAgentSourceKind"),
            GetOptionalString(element, "parentToolCallId"),
            GetOptionalString(element, "contextPolicy"),
            ReadObjectMap(element, "metadata"));

    private static AgentTuiThreadGraph ParseThreadGraph(JsonElement element) =>
        new(
            ReadArray(element, "threads", ParseThreadInfo),
            ReadArray(element, "forkGroups", ParseForkGroup),
            ReadArray(element, "runtimeChildren", ParseRuntimeChild));

    private static AgentTuiThreadForkGroup ParseForkGroup(JsonElement element) =>
        new(
            GetRequiredString(element, "id"),
            GetRequiredString(element, "sourceThreadId"),
            GetOptionalString(element, "forkedAtMessageId"),
            GetOptionalInt32(element, "forkedAtMessageIndex"),
            GetRequiredInt32(element, "choiceMessageIndex"),
            ReadArray(element, "members", ParseForkGroupMember));

    private static AgentTuiThreadForkGroupMember ParseForkGroupMember(JsonElement element) =>
        new(
            GetRequiredString(element, "threadId"),
            GetRequiredString(element, "name"),
            GetOptionalInt32(element, "index") ?? 0,
            GetOptionalBoolean(element, "isSource") ?? false,
            GetOptionalString(element, "choiceMessageId"),
            GetOptionalInt32(element, "choiceMessageIndex"),
            GetOptionalInt32(element, "messageCount") ?? 0,
            GetRequiredDateTimeOffset(element, "createdAt"),
            GetRequiredDateTimeOffset(element, "lastActivity"));

    private static AgentTuiThreadRuntimeChild ParseRuntimeChild(JsonElement element) =>
        new(
            GetRequiredString(element, "threadId"),
            GetRequiredString(element, "sessionId"),
            GetRequiredString(element, "defaultAgentId"),
            GetRequiredString(element, "parentSessionId"),
            GetRequiredString(element, "parentThreadId"),
            GetRequiredString(element, "name"),
            GetOptionalEnum(element, "kind", ThreadKind.MainAgent),
            GetOptionalEnum(element, "visibility", ThreadVisibility.Visible),
            GetOptionalString(element, "subAgentName"),
            GetOptionalString(element, "subAgentTaskName"),
            GetOptionalString(element, "invocationId"),
            GetOptionalString(element, "subAgentSourceKind"),
            GetOptionalString(element, "parentToolCallId"),
            GetOptionalString(element, "contextPolicy"),
            GetOptionalString(element, "status"),
            GetOptionalInt32(element, "messageCount") ?? 0,
            GetRequiredDateTimeOffset(element, "createdAt"),
            GetRequiredDateTimeOffset(element, "lastActivity"));

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

    private static IReadOnlyList<T> ReadArray<T>(
        JsonElement element,
        string propertyName,
        Func<JsonElement, T> parse)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<T>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                values.Add(parse(item));
            }
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
    {
        var value = GetOptionalString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"Expected non-empty string property '{propertyName}'.");
        }

        return value;
    }

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

    private static int GetRequiredInt32(JsonElement element, string propertyName)
        => GetOptionalInt32(element, propertyName)
           ?? throw new InvalidOperationException($"Missing required integer property '{propertyName}'.");

    private static TEnum GetOptionalEnum<TEnum>(
        JsonElement element,
        string propertyName,
        TEnum fallback)
        where TEnum : struct, Enum
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return fallback;

        if (property.ValueKind == JsonValueKind.String &&
            Enum.TryParse<TEnum>(property.GetString(), ignoreCase: true, out var namedValue) &&
            Enum.IsDefined(namedValue))
        {
            return namedValue;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericValue))
        {
            var value = (TEnum)Enum.ToObject(typeof(TEnum), numericValue);
            if (Enum.IsDefined(value))
                return value;
        }

        return fallback;
    }

    private static bool? GetOptionalBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
}
