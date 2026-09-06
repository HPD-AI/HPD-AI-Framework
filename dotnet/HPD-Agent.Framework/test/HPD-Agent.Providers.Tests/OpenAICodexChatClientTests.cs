#pragma warning disable OPENAI001
using OpenAI;
using OpenAI.Responses;
using System.Net;
using System.Text;
using System.Text.Json;
using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using HPD.Agent.Providers.OpenAI;

namespace HPD.Agent.Providers.Tests;

public sealed class OpenAICodexChatClientTests
{
    [Fact]
    public void ApplyMessagePolicy_LowersOnlySystemMessagesToDeveloperInPlace()
    {
        var user = new ChatMessage(ChatRole.User, "before") { MessageId = "user-1" };
        var system = new ChatMessage(ChatRole.System, "notification") { MessageId = "system-1" };
        var assistant = new ChatMessage(ChatRole.Assistant, "after") { MessageId = "assistant-1" };

        var result = OpenAICodexChatClient.ApplyMessagePolicy(
            [user, system, assistant]);

        Assert.Same(user, result[0]);
        Assert.Equal(new ChatRole("developer"), result[1].Role);
        Assert.Equal("notification", result[1].Text);
        Assert.Equal("system-1", result[1].MessageId);
        Assert.NotSame(system, result[1]);
        Assert.Equal(ChatRole.System, system.Role);
        Assert.Same(assistant, result[2]);
    }

    [Fact]
    public void ApplyMessagePolicy_RejectsNonTextPrivilegedContent()
    {
        var message = new ChatMessage(
            ChatRole.System,
            [new FunctionCallContent("call-1", "ReadFile", new Dictionary<string, object?>())]);

        var error = Assert.Throws<NotSupportedException>(() =>
            OpenAICodexChatClient.ApplyMessagePolicy([message]));

        Assert.Contains("only text content", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetResponseAsync_PreservesOptionsAndCancellationWhileLoweringMessages()
    {
        var inner = new CaptureChatClient();
        using var client = new OpenAICodexChatClient(inner, "fixture");
        var options = new ChatOptions { Instructions = "stable instructions" };
        using var cancellation = new CancellationTokenSource();

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, "runtime context")],
            options,
            cancellation.Token);

        Assert.Same(options, inner.LastOptions);
        Assert.Equal(cancellation.Token, inner.LastCancellationToken);
        Assert.Equal(new ChatRole("developer"), Assert.Single(inner.LastMessages!).Role);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_UsesTheSameMessagePolicy()
    {
        var inner = new CaptureChatClient();
        using var client = new OpenAICodexChatClient(inner, "fixture");

        await foreach (var _ in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.System, "runtime context")]))
        {
        }

        Assert.Equal(new ChatRole("developer"), Assert.Single(inner.LastMessages!).Role);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitOffUsesLowWithoutMutatingCallerOptions(bool streaming)
    {
        var inner = new CaptureChatClient();
        using var client = new OpenAICodexChatClient(inner, "fixture");
        var options = new ChatOptions
        {
            Instructions = "keep instructions",
            Reasoning = new Microsoft.Extensions.AI.ReasoningOptions
            {
                Effort = Microsoft.Extensions.AI.ReasoningEffort.None,
                Output = Microsoft.Extensions.AI.ReasoningOutput.Summary
            }
        };
        if (streaming)
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], options)) { }
        }
        else
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        Assert.Equal(Microsoft.Extensions.AI.ReasoningEffort.Low, inner.LastOptions!.Reasoning!.Effort);
        Assert.Equal(Microsoft.Extensions.AI.ReasoningOutput.Summary, inner.LastOptions.Reasoning.Output);
        Assert.Equal(options.Instructions, inner.LastOptions.Instructions);
        Assert.Equal(Microsoft.Extensions.AI.ReasoningEffort.None, options.Reasoning.Effort);
    }

    [Fact]
    public async Task ExplicitHighIsPreserved()
    {
        var inner = new CaptureChatClient();
        using var client = new OpenAICodexChatClient(inner, "fixture");
        var options = new ChatOptions { Reasoning = new Microsoft.Extensions.AI.ReasoningOptions { Effort = Microsoft.Extensions.AI.ReasoningEffort.High } };
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);
        Assert.Same(options, inner.LastOptions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PinnedSdk_BothOperationsUseStreamingAndPreserveCompletion(bool streaming)
    {
        var handler = new FixtureHandler("completed");
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);
        var response = streaming
            ? await client.GetStreamingResponseAsync([new(ChatRole.User, "hi")]).ToChatResponseAsync()
            : await client.GetResponseAsync([new(ChatRole.User, "hi")]);
        Assert.True(handler.Streaming);
        Assert.Equal("hello", response.Text);
        Assert.Equal("resp_test", response.ResponseId);
        Assert.Equal("fixture", response.ModelId);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Equal(12, response.Usage!.InputTokenCount);
        Assert.Equal(3, response.Usage.OutputTokenCount);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("eof")]
    [InlineData("failed")]
    [InlineData("error")]
    public async Task PinnedSdk_RejectsFailureAfterPartialText(string ending)
    {
        var handler = new FixtureHandler(ending);
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync([new(ChatRole.User, "hi")]));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task PinnedSdk_IncompleteIsNotReportedAsCompleted()
    {
        using var http = new HttpClient(new FixtureHandler("incomplete"));
        using var client = CreateClient(http);
        var response = await client.GetResponseAsync([new(ChatRole.User, "hi")]);
        Assert.Equal("hello", response.Text);
        Assert.Equal(ChatFinishReason.Length, response.FinishReason);
    }

    [Fact]
    public async Task AggregationPreservesMessageBoundariesReasoningToolsAndMetadata()
    {
        var terminal = ModelReaderWriter.Read<StreamingResponseUpdate>(BinaryData.FromString(Event("completed")));
        var inner = new SequenceClient([
            new(ChatRole.Assistant, [new TextReasoningContent("analysis")]) { MessageId = "reason", ResponseId = "response" },
            new(ChatRole.Assistant, "hello ") { MessageId = "answer", AdditionalProperties = new() { ["fixture"] = "value" } },
            new(ChatRole.Assistant, "world") { MessageId = "answer" },
            new(ChatRole.Assistant, [new FunctionCallContent("call", "ReadFile", new Dictionary<string, object?> { ["path"] = "a" })]) { MessageId = "tool" },
            new() { ResponseId = "response", ModelId = "fixture", ConversationId = "conversation", FinishReason = ChatFinishReason.ToolCalls,
                Contents = [new UsageContent(new UsageDetails { InputTokenCount = 12, OutputTokenCount = 3 })], RawRepresentation = terminal }
        ]);
        using var client = new OpenAICodexChatClient(inner, "fixture");
        var response = await client.GetResponseAsync([new(ChatRole.User, "hi")]);
        Assert.Equal(3, response.Messages.Count);
        Assert.IsType<TextReasoningContent>(Assert.Single(response.Messages[0].Contents));
        Assert.Equal("hello world", response.Messages[1].Text);
        Assert.Equal("value", response.Messages[1].AdditionalProperties!["fixture"]);
        Assert.Equal("ReadFile", Assert.IsType<FunctionCallContent>(Assert.Single(response.Messages[2].Contents)).Name);
        Assert.Equal("conversation", response.ConversationId);
        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        Assert.Equal(12, response.Usage!.InputTokenCount);
        Assert.Equal(1, inner.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlyStopAndCancellationDisposeStreamExactlyOnce(bool cancel)
    {
        var inner = new SequenceClient([new(ChatRole.Assistant, "partial"), new(ChatRole.Assistant, "more")]);
        using var client = new OpenAICodexChatClient(inner, "fixture");
        using var cancellation = new CancellationTokenSource();
        await using (var iterator = client.GetStreamingResponseAsync([new(ChatRole.User, "hi")], cancellationToken: cancellation.Token).GetAsyncEnumerator())
        {
            Assert.True(await iterator.MoveNextAsync());
            if (cancel)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await iterator.MoveNextAsync());
            }
        }
        Assert.Equal(1, inner.Disposals);
    }

    private sealed class SequenceClient(ChatResponseUpdate[] updates) : IChatClient
    {
        public int Disposals { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                foreach (var update in updates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.CompletedTask;
                    yield return update;
                }
            }
            finally { Disposals++; }
        }
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    [Fact]
    public async Task TransportPinsEndpointForcesStoreFalseAndReleasesEachCredential()
    {
        var source = new TestCredentialSource();
        var endpoint = new Uri("https://chatgpt.com/backend-api/codex/responses");
        var network = new FixtureHandler("completed");
        using var handler = new OpenAICodexProvider.OpenAICodexRequestSigningHandler(
            new ProviderCredentialBindingContext.RequestTime(source, null!), endpoint) { InnerHandler = network };
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.GetResponseAsync([new(ChatRole.User, "hi")])));
        Assert.Equal(2, source.Acquisitions);
        Assert.Equal(2, source.Releases);
        Assert.Equal(endpoint, network.LastUri);
        Assert.False(network.Stored);
        Assert.Equal("fixture-signature", network.Signature);
    }

    private sealed class TestCredentialSource : IProviderCredentialSource
    {
        private int acquisitions, releases;
        public int Acquisitions => acquisitions;
        public int Releases => releases;
        public ValueTask<ProviderCredentialPlan> PrepareAsync(ProviderCredentialRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public ValueTask<IProviderCredentialLease> AcquireAsync(ProviderCredentialPlan plan, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref acquisitions);
            return ValueTask.FromResult<IProviderCredentialLease>(new Lease(this));
        }
        private sealed class Lease(TestCredentialSource owner) : IProviderCredentialLease, IProviderRequestSignerLease, IProviderRequestSigner
        {
            private int disposed;
            public ProviderCredential Credential => new ProviderCredential.SignedRequest(this);
            public ProviderCredentialIdentity Identity => throw new NotSupportedException();
            public ProviderCredentialGeneration Generation => default;
            public DateTimeOffset? ExpiresAt => null;
            public CancellationToken RotationToken => default;
            public IProviderRequestSigner Signer => this;
            public ValueTask SignAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
            {
                request.Headers.Add("X-Fixture", "fixture-signature");
                return ValueTask.CompletedTask;
            }
            public ValueTask DisposeAsync()
            {
                Assert.Equal(0, Interlocked.Exchange(ref disposed, 1));
                Interlocked.Increment(ref owner.releases);
                return ValueTask.CompletedTask;
            }
        }
    }

    [Theory]
    [InlineData(CompactionOrigin.Explicit)]
    [InlineData(CompactionOrigin.Automatic)]
    [InlineData(CompactionOrigin.Fork)]
    public async Task CompactionUsesCodexStreamingContract(CompactionOrigin origin)
    {
        using var http = new HttpClient(new FixtureHandler("completed"));
        using var client = CreateClient(http);
        var thread = ThreadProjector.Project("session", "thread",
            [new ThreadCreatedEvent("agent", null, null, null, null, DateTime.UtcNow)], ThreadProjectionPurpose.ThreadHistory);
        thread.Messages.Add(new(ChatRole.User, "Work so far") { MessageId = "user" });
        var result = await new ThreadCompactionEngine().ExecuteAsync(
            new ThreadCompactionContext(thread, thread.Messages, null, client),
            new CompactionSpecification { Point = new CompactAtCurrentHead(), Strategy = new SummarizingCompaction(), CommitMode = CompactionCommitMode.Soft },
            "agent", 0, origin, CompactionContinuation.Continue);
        Assert.Equal(CompactionStatus.Completed, result.TerminalEvent.Status);
        Assert.Equal("hello", Assert.Single(thread.Messages).Text);
    }

    private static OpenAICodexChatClient CreateClient(HttpClient http) => new(
        new OpenAIClient(new ApiKeyCredential("fixture"), new OpenAIClientOptions
        {
            Endpoint = new Uri("https://example.invalid/v1/"),
            RetryPolicy = new ClientRetryPolicy(0),
            Transport = new HttpClientPipelineTransport(http)
        }).GetResponsesClient().AsIChatClient("fixture"), "fixture");

    private static string Event(string status) => JsonSerializer.Serialize(new
    {
        type = "response." + status, sequence_number = 3,
        response = new
        {
            id = "resp_test", created_at = 1700000000, model = "fixture", status = status == "created" ? "in_progress" : status,
            output = Array.Empty<object>(),
            usage = new { input_tokens = 12, output_tokens = 3, total_tokens = 15 },
            incomplete_details = status == "incomplete" ? new { reason = "max_output_tokens" } : null
        }
    });

    private sealed class FixtureHandler(string ending) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public bool? Stored { get; private set; }
        public string? Signature { get; private set; }
        public bool Streaming { get; private set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri;
            Signature = request.Headers.TryGetValues("X-Fixture", out var values) ? values.Single() : null;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Streaming = body.RootElement.GetProperty("stream").GetBoolean();
            Stored = body.RootElement.TryGetProperty("store", out var store) ? store.GetBoolean() : null;
            var events = new List<string>
            {
                Event("created"),
                """{"type":"response.output_item.added","sequence_number":1,"output_index":0,"item":{"type":"message","id":"msg_test","role":"assistant","status":"in_progress","content":[]}}""",
                """{"type":"response.output_text.delta","sequence_number":2,"item_id":"msg_test","output_index":0,"content_index":0,"delta":"hello"}"""
            };
            if (ending == "error")
                events.Add("""{"type":"error","sequence_number":3,"code":"server_error","message":"fixture failure"}""");
            else if (ending != "eof")
                events.Add(Event(ending));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Concat(events.Select(e => $"data: {e}\n\n")), Encoding.UTF8, "text/event-stream")
            };
        }
    }

    private sealed class CaptureChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }
        public ChatOptions? LastOptions { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            LastOptions = options;
            LastCancellationToken = cancellationToken;
            throw new InvalidOperationException("The completed SDK operation must never be used.");
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            LastOptions = options;
            LastCancellationToken = cancellationToken;
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok")
            {
                RawRepresentation = ModelReaderWriter.Read<StreamingResponseUpdate>(BinaryData.FromString(
                    Event("completed")))
            };
        }
    }
}
