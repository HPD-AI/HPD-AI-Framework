using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Client.Tests;

public sealed class HpdAgentHttpTransportTests
{
    [Fact]
    public async Task ReplaySafeGet_RetriesTransientResponse()
    {
        var handler = new SequenceHandler(
            static _ => response(HttpStatusCode.ServiceUnavailable, """{"error":"busy"}"""),
            static _ => response(HttpStatusCode.OK, """{"value":"ok"}""", "application/json"));
        using var client = Client(handler);

        var result = await client.Transport.SendJsonAsync(
            HttpMethod.Get,
            "/probe",
            query: null,
            body: null,
            replaySafe: false,
            ProbeJsonContext.Default.ProbeDto,
            CancellationToken.None);

        Assert.Equal("ok", result.Value);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task NonReplaySafePost_IsNotRetried()
    {
        var handler = new SequenceHandler(
            static _ => response(HttpStatusCode.ServiceUnavailable, """{"error":"busy"}"""));
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<HpdAgentApiException>(() =>
            client.Transport.SendAsync(
                HttpMethod.Post,
                "/probe",
                query: null,
                body: null,
                replaySafe: false,
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task SseReconnect_ResumesFromLastEventIdAndReportsDecision()
    {
        var handler = new SequenceHandler(
            static _ => EventStream("event: agent-event\nid: 1:1\ndata: {\"value\":1}\n\n"),
            static _ => EventStream("event: agent-event\nid: 1:2\ndata: {\"value\":2}\n\n"));
        using var client = Client(handler);
        var diagnostics = new List<HpdAgentSseReconnectDiagnostic>();
        var messages = new List<HpdAgentSseMessage>();

        await foreach (var message in client.Transport.StreamSseAsync(
            "/events",
            query: null,
            lastEventId: null,
            new HpdAgentSseReconnectOptions
            {
                MaxReconnects = 1,
                InitialDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero
            },
            diagnostics.Add,
            CancellationToken.None))
        {
            messages.Add(message);
        }

        Assert.Equal(2, messages.Count);
        Assert.Equal(["1:1", "1:2"], messages.Select(static value => value.Id));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("1:1", diagnostic.LastEventId);
        Assert.True(diagnostic.StreamEnded);
        Assert.Contains("after=1%3A1", handler.Requests[1].RequestUri!.Query, StringComparison.Ordinal);
        Assert.Equal("1:1", handler.Requests[1].Headers.GetValues("Last-Event-ID").Single());
    }

    [Fact]
    public async Task SseParsing_SkipsInvalidBlocksAndHandlesMultilineAndFinalEvents()
    {
        var body =
            "data: not valid json\n\n" +
            "data: {\"outer\":\n" +
            "data: \"世界\"}\n\n" +
            "event: probe\n" +
            "data:{\"final\":true}\n" +
            "retry: 5000\n";
        var handler = new SequenceHandler(_ => EventStream(body));
        using var client = Client(handler);
        var messages = new List<HpdAgentSseMessage>();

        await foreach (var message in client.Transport.StreamSseAsync(
            "/events",
            query: null,
            lastEventId: null,
            HpdAgentSseReconnectOptions.Disabled,
            onReconnect: null,
            cancellationToken: CancellationToken.None))
        {
            messages.Add(message);
        }

        Assert.Equal(2, messages.Count);
        Assert.Equal("世界", messages[0].Data.GetProperty("outer").GetString());
        Assert.Equal("probe", messages[1].EventName);
        Assert.True(messages[1].Data.GetProperty("final").GetBoolean());
    }

    [Fact]
    public void SseState_DeduplicatesCommittedEventsAndAdmitsLiveEvents()
    {
        var state = new HpdAgentSseState(new HpdAgentSseAppliedCursor(2, 4));

        var live = state.Apply(
            Message("live-agent-event", "ignored", """{"value":"live"}"""),
            eventSequenceNumber: null);
        var duplicate = state.Apply(
            Message("agent-event", "2:4", """{"value":"duplicate"}"""),
            eventSequenceNumber: 4);
        var committed = state.Apply(
            Message("agent-event", "2:5", """{"value":"next"}"""),
            eventSequenceNumber: 5);

        Assert.Equal(HpdAgentSseDecision.Live, live);
        Assert.Equal(HpdAgentSseDecision.Duplicate, duplicate);
        Assert.Equal(HpdAgentSseDecision.Committed, committed);
        Assert.Equal(new HpdAgentSseAppliedCursor(2, 5), state.AppliedCursor);
    }

    [Fact]
    public void SseState_RejectsCursorAndEventSequenceMismatch()
    {
        var state = new HpdAgentSseState();

        var error = Assert.Throws<HpdAgentStreamException>(() =>
            state.Apply(
                Message("agent-event", "1:2", """{"value":"wrong"}"""),
                eventSequenceNumber: 3));

        Assert.Contains("did not match", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SseState_RaisesTypedRebaseAndResetsGeneration()
    {
        var state = new HpdAgentSseState(new HpdAgentSseAppliedCursor(1, 9));

        var error = Assert.Throws<HpdAgentJournalRebasedException>(() =>
            state.Apply(
                Message(
                    "thread-journal-rebased",
                    null,
                    """{"previousGeneration":1,"currentGeneration":2}"""),
                eventSequenceNumber: null));

        Assert.Equal(1, error.PreviousGeneration);
        Assert.Equal(2, error.CurrentGeneration);
        Assert.Equal(new HpdAgentSseAppliedCursor(2, 0), state.AppliedCursor);
    }

    [Fact]
    public async Task ApiException_ParsesProblemDetailsAndValidationErrors()
    {
        var body =
            """
            {
              "type": "https://hpd.example/problems/thread-delete",
              "title": "Thread cannot be deleted",
              "status": 409,
              "detail": "The thread has children.",
              "instance": "/threads/main",
              "errors": { "HasChildren": ["Remove children first."] }
            }
            """;
        var handler = new SequenceHandler(
            _ => response(HttpStatusCode.Conflict, body, "application/problem+json"));
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<HpdAgentApiException>(() =>
            client.Transport.SendAsync(
                HttpMethod.Delete,
                "/threads/main",
                query: null,
                body: null,
                replaySafe: false,
                CancellationToken.None));

        Assert.Equal("https://hpd.example/problems/thread-delete", error.Code);
        Assert.Equal("The thread has children.", error.ProblemMessage);
        Assert.Equal(409, error.Problem!.Status);
        Assert.Equal("Remove children first.", error.Errors!["HasChildren"].Single());
    }

    [Fact]
    public async Task MultipartUpload_PreservesFieldFilenameAndContentType()
    {
        var handler = new CaptureRequestHandler(
            response(HttpStatusCode.OK, """{"value":"uploaded"}""", "application/json"));
        using var client = Client(handler);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("payload"));

        var result = await client.Transport.SendMultipartJsonAsync(
            "/content",
            query: null,
            content,
            fieldName: "file",
            fileName: "report.txt",
            contentType: "text/plain",
            ProbeJsonContext.Default.ProbeDto,
            CancellationToken.None);

        Assert.Equal("uploaded", result.Value);
        Assert.Equal("/content", handler.Uri!.AbsolutePath);
        Assert.StartsWith("multipart/form-data;", handler.ContentType, StringComparison.Ordinal);
        Assert.Contains("name=file", handler.Body, StringComparison.Ordinal);
        Assert.Contains("filename=report.txt", handler.Body, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/plain", handler.Body, StringComparison.Ordinal);
        Assert.Contains("payload", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientToolRegistry_NormalizesNamesAndResults()
    {
        var registry = new HpdAgentClientToolRegistry();
        registry.Register(
            "host.sample.echo",
            static (_, _) => ValueTask.FromResult(
                HpdAgentClientToolResult.Text("echo")));
        var invocation = new HpdAgentClientToolInvocation(
            "request-one",
            "sample.echo",
            "call-one",
            JsonDocument.Parse("{}").RootElement.Clone(),
            null,
            null,
            0);

        var result = await registry.InvokeAsync(invocation);

        Assert.True(registry.CanHandle("host.sample.echo"));
        Assert.Contains("client-tool:echo", registry.Capabilities);
        Assert.Equal("Completed", result.Outcome);
        var content = Assert.Single(result.Content!);
        Assert.Equal("text", content.GetProperty("type").GetString());
        Assert.Equal("echo", content.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ClientToolRegistry_UsesFallbackAndNormalizesFailures()
    {
        var fallback = new HpdAgentClientToolRegistry()
            .RegisterFallback(static (_, _) =>
                ValueTask.FromResult(HpdAgentClientToolResult.Failed("fallback failed")));
        var invocation = new HpdAgentClientToolInvocation(
            "request-one",
            "unknown",
            "call-one",
            JsonDocument.Parse("{}").RootElement.Clone(),
            null,
            null,
            0);

        var fallbackResult = await fallback.InvokeAsync(invocation);
        var missingResult = await new HpdAgentClientToolRegistry().InvokeAsync(invocation);

        Assert.Equal("Failed", fallbackResult.Outcome);
        Assert.Equal("fallback failed", fallbackResult.ErrorMessage);
        Assert.Equal("Failed", missingResult.Outcome);
        Assert.Contains("Unknown client tool", missingResult.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientToolRegistry_RegistersHarnessesAndWrapsJsonResults()
    {
        var registry = new HpdAgentClientToolRegistry();
        registry.RegisterHarness(
            new HpdAgentClientToolHarness(
                "host",
                [
                    new HpdAgentClientToolDefinition(
                        "active_view",
                        "Gets the active view.",
                        JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone())
                ]),
            static (_, _) => ValueTask.FromResult(
                HpdAgentClientToolResult.Json(
                    JsonDocument.Parse("""{"activeView":"chat"}""").RootElement.Clone())));
        var invocation = new HpdAgentClientToolInvocation(
            "request-one",
            "host.active_view",
            "call-one",
            JsonDocument.Parse("{}").RootElement.Clone(),
            null,
            null,
            0);

        var result = await registry.InvokeAsync(invocation);

        Assert.Single(registry.Harnesses);
        var content = Assert.Single(result.Content!);
        Assert.Equal("json", content.GetProperty("type").GetString());
        Assert.Equal("chat", content.GetProperty("value").GetProperty("activeView").GetString());
    }

    [Fact]
    public void ThreadProjector_CoalescesMessagesReasoningAndToolResults()
    {
        var events = new[]
        {
            Json("""
                {"type":"TEXT_MESSAGE_START","messageId":"m1","role":"assistant",
                 "createdAt":"2026-01-01T00:00:00Z"}
                """),
            Json("""{"type":"TEXT_DELTA","messageId":"m1","text":"hel"}"""),
            Json("""{"type":"TEXT_DELTA","messageId":"m1","text":"lo"}"""),
            Json("""{"type":"REASONING_DELTA","messageId":"m1","text":"because"}"""),
            Json("""
                {"type":"CONTENT_ADDED","messageId":"m1","content":
                 {"$type":"functionCall","callId":"c1","name":"lookup",
                  "arguments":{"query":"x"}}}
                """),
            Json("""
                {"type":"CONTENT_ADDED","messageId":"m1","content":
                 {"$type":"functionResult","callId":"c1","result":{"text":"found"}}}
                """)
        };

        var message = Assert.Single(HpdAgentThreadMessageProjector.Project(events));

        Assert.Equal("m1", message.Id);
        Assert.Equal("hello", message.Text);
        Assert.Equal("because", message.ReasoningText);
        var tool = Assert.Single(message.ToolCalls);
        Assert.Equal("lookup", tool.Name);
        Assert.Equal("found", tool.ResultText);
    }

    [Fact]
    public void ThreadProjector_ReplacesMessagesAndFiltersHiddenTranscriptItems()
    {
        var events = new[]
        {
            Json("""
                {"type":"USER_MESSAGE","messageId":"m1","text":"old",
                 "additionalProperties":{"hpd.message.visibility":"Hidden"}}
                """),
            Json("""
                {"type":"THREAD_MESSAGE_REPLACED","messageId":"m1",
                 "replacement":{"messageId":"m1","role":"user",
                   "contents":[{"$type":"text","text":"new"}]}}
                """)
        };

        var projected = Assert.Single(HpdAgentThreadMessageProjector.Project(events));
        var transcript = HpdAgentThreadMessageProjector.ProjectTranscript(events);

        Assert.Equal("new", projected.Text);
        Assert.Equal("Hidden", projected.Visibility);
        Assert.Empty(transcript);
    }

    [Fact]
    public void ThreadProjector_PreservesDurableContentMetadataAndPolicy()
    {
        var events = new[]
        {
            Json("""
                {"type":"TEXT_MESSAGE_START","messageId":"m1","role":"system",
                 "createdAt":"2026-01-01T00:00:00Z","authorName":"Runtime",
                 "source":"BackgroundNotification","visibility":"Diagnostic",
                 "persistence":"ThreadHistory",
                 "additionalProperties":{
                   "quote":{"text":"quoted"},
                   "hpd.message.visibility":"Diagnostic"
                 }}
                """),
            Json("""
                {"type":"CONTENT_ADDED","messageId":"m1",
                 "content":{"$type":"text","text":"materialized"}}
                """)
        };

        var message = Assert.Single(HpdAgentThreadMessageProjector.Project(events));

        Assert.Equal("materialized", message.Text);
        Assert.Equal("Runtime", message.AuthorName);
        Assert.Equal("BackgroundNotification", message.Source);
        Assert.Equal("Diagnostic", message.Visibility);
        Assert.Equal("ThreadHistory", message.Persistence);
        Assert.Equal("quoted", message.AdditionalProperties!["quote"].GetProperty("text").GetString());
    }

    [Fact]
    public void ThreadProjector_UsesToolResultPayloadPriority()
    {
        var events = new[]
        {
            FunctionCall("c1", "plain"),
            FunctionResult("c1", """{"text":"plain","json":{"ignored":true}}"""),
            FunctionCall("c2", "json"),
            FunctionResult("c2", """{"json":{"ok":true}}"""),
            FunctionCall("c3", "content"),
            FunctionResult("c3", """{"content":[{"type":"text","text":"nested"}]}""")
        };

        var message = Assert.Single(HpdAgentThreadMessageProjector.Project(events));

        Assert.Equal("plain", message.ToolCalls[0].ResultText);
        Assert.Equal("""{"ok":true}""", message.ToolCalls[1].ResultText);
        Assert.Equal(
            """[{"type":"text","text":"nested"}]""",
            message.ToolCalls[2].ResultText);
    }

    private static ProbeClient Client(HttpMessageHandler handler) =>
        new(new HpdAgentClientOptions(new Uri("https://example.test"))
        {
            HttpClient = new HttpClient(handler),
            Retry = new HpdAgentRetryOptions
            {
                MaxRetries = 1,
                InitialDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero
            }
        });

    private static HttpResponseMessage response(
        HttpStatusCode status,
        string body,
        string mediaType = "application/json") =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        };

    private static HttpResponseMessage EventStream(string body) =>
        response(HttpStatusCode.OK, body, "text/event-stream");

    private static HpdAgentSseMessage Message(string eventName, string? id, string json) =>
        new(eventName, id, JsonDocument.Parse(json).RootElement.Clone());

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement FunctionCall(string callId, string name) =>
        Json(
            "{\"type\":\"CONTENT_ADDED\",\"messageId\":\"m1\",\"content\":" +
            "{\"$type\":\"functionCall\",\"callId\":\"" + callId +
            "\",\"name\":\"" + name + "\",\"arguments\":{}}}");

    private static JsonElement FunctionResult(string callId, string result) =>
        Json(
            "{\"type\":\"CONTENT_ADDED\",\"messageId\":\"m1\",\"content\":" +
            "{\"$type\":\"functionResult\",\"callId\":\"" + callId +
            "\",\"result\":" + result + "}}");

    private sealed class ProbeClient(HpdAgentClientOptions options) : HpdAgentClientBase(options);

    private sealed class SequenceHandler(
        params Func<HttpRequestMessage, HttpResponseMessage>[] responders) : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests = [];

        public int Calls => _requests.Count;
        public IReadOnlyList<HttpRequestMessage> Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(_requests.Count, responders.Length - 1);
            _requests.Add(request);
            return Task.FromResult(responders[index](request));
        }
    }

    private sealed class CaptureRequestHandler(HttpResponseMessage responseMessage) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string ContentType { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            ContentType = request.Content?.Headers.ContentType?.ToString() ?? string.Empty;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseMessage;
        }
    }
}

public sealed record ProbeDto(string Value);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProbeDto))]
internal sealed partial class ProbeJsonContext : JsonSerializerContext;
