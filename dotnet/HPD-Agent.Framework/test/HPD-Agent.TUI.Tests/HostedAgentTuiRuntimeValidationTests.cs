using System.Net;
using System.Text;
using FluentAssertions;
using HPD.Agent.Serialization;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI.Tests;

public sealed class HostedAgentTuiRuntimeValidationTests
{
    [Theory]
    [InlineData(null, "session", "main")]
    [InlineData("", "session", "main")]
    [InlineData("agent", null, "main")]
    [InlineData("agent", "", "main")]
    [InlineData("agent", "session", null)]
    [InlineData("agent", "session", "")]
    public void RuntimeScope_RejectsMissingIds(string? agentId, string? sessionId, string? threadId)
    {
        var act = () => new AgentTuiRuntimeScope(agentId!, sessionId!, threadId!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ListSessionsAsync_RejectsMalformedSessionPayload()
    {
        using var http = new HttpClient(new JsonHandler("""
            [
              {
                "createdAt": "2026-07-09T00:00:00Z",
                "lastActivity": "2026-07-09T00:00:00Z",
                "metadata": {}
              }
            ]
            """))
        {
            BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
        };
        await using var runtime = new HostedAgentTuiRuntime(
            http,
            new HostedAgentTuiRuntimeOptions
            {
                BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
            });

        var act = () => runtime.ListSessionsAsync();

        await act.Should().ThrowAsync<System.Text.Json.JsonException>()
            .WithMessage("*id*");
    }

    [Fact]
    public async Task ListThreadsAsync_ExplainsUnknownEventPayloadAsStaleBackend()
    {
        using var http = new HttpClient(new ResponseHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""
                {"errors":{"ListThreadsError":["JSON payload is not a known agent event."]}}
                """, Encoding.UTF8, "application/json")
        }))
        {
            BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
        };
        await using var runtime = new HostedAgentTuiRuntime(
            http,
            new HostedAgentTuiRuntimeOptions
            {
                BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
            });

        var act = () => runtime.ListThreadsAsync("session-1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*restart the backend*current agent event registrations*");
    }

    [Fact]
    public async Task EnsureDurableScopeAsync_CreatesMissingThreadInExistingSession()
    {
        var handler = new ScopeInitializationHandler();
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
        };
        await using var runtime = new HostedAgentTuiRuntime(http, new HostedAgentTuiRuntimeOptions
        {
            BaseAddress = http.BaseAddress
        });
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");

        var result = await runtime.EnsureDurableScopeAsync(scope);

        result.Should().Be(scope);
        handler.Requests.Should().Equal(
            "GET sessions/session",
            "GET sessions/session/threads/main",
            "POST agents/agent/sessions/session/threads");
        handler.CreateThreadBody.Should().Be("{\"threadId\":\"main\"}");
    }

    [Fact]
    public async Task ResolveInitialScopeAsync_DoesNotMaterializeTheDefaultTransientScope()
    {
        var handler = new ScopeInitializationHandler();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
        };
        await using var runtime = new HostedAgentTuiRuntime(http, new HostedAgentTuiRuntimeOptions
        {
            BaseAddress = http.BaseAddress,
            DefaultScope = scope
        });

        var resolution = await runtime.ResolveInitialScopeAsync(requested: null);

        resolution.Should().Be(new AgentTuiScopeResolution(scope, IsDurable: false));
        handler.Requests.Should().Equal(
            "GET sessions/session",
            "GET sessions/session/threads/main");
        handler.CreateThreadBody.Should().BeNull();
    }

    [Fact]
    public async Task GetThreadStateAsync_RejectsMissingThreadInsteadOfReturningGenerationZero()
    {
        using var http = new HttpClient(new ResponseHandler(new HttpResponseMessage(HttpStatusCode.NotFound)))
        {
            BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
        };
        await using var runtime = new HostedAgentTuiRuntime(http, new HostedAgentTuiRuntimeOptions
        {
            BaseAddress = http.BaseAddress
        });

        var act = () => runtime.GetThreadStateAsync(
            new AgentTuiRuntimeScope("agent", "session", "main"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not have a durable journal*");
    }

    [Fact]
    public async Task ObserveAsync_ReconnectsAfterEofUsingLastCommittedSequence()
    {
        var first = new ThreadRunStartedEvent("run-1", "agent", DateTimeOffset.UtcNow)
        {
            SessionId = "session",
            ThreadId = "main",
            ThreadSequenceNumber = 1
        };
        var second = new ThreadRunCompletedEvent("run-1", "agent", Cancelled: false)
        {
            SessionId = "session",
            ThreadId = "main",
            ThreadSequenceNumber = 2
        };
        var handler = new SequentialSseHandler(first, second);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
        };
        await using var runtime = new HostedAgentTuiRuntime(http, new HostedAgentTuiRuntimeOptions
        {
            BaseAddress = http.BaseAddress
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observed = new List<AgentEvent>();

        await foreach (var batch in runtime.ObserveAsync(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            after: ThreadJournalCursor.Start(1),
            initialObservedCursor: new ThreadJournalCursor(1, 2),
            cancellationToken: timeout.Token))
        {
            observed.AddRange(batch.Events);
            if (observed.Count == 2)
            {
                break;
            }
        }

        observed.Select(static evt => evt.ThreadSequenceNumber).Should().Equal(1, 2);
        handler.Requests.Should().Equal("?after=1:0", "?after=1:1");
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class ResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(response);
    }

    private sealed class SequentialSseHandler(params AgentEvent[] events) : HttpMessageHandler
    {
        private int _index;

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri?.Query ?? string.Empty);
            var evt = events[_index++];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"id: 1:{evt.ThreadSequenceNumber}\ndata: {AgentEventSerializer.ToJson(evt)}\n\n",
                    Encoding.UTF8,
                    "text/event-stream")
            });
        }
    }

    private sealed class ScopeInitializationHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public string? CreateThreadBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery
                .Replace("/api/hpd-agent/", string.Empty, StringComparison.Ordinal) ?? string.Empty;
            Requests.Add($"{request.Method} {path}");

            if (request.Method == HttpMethod.Get && path == "sessions/session")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            if (request.Method == HttpMethod.Get && path == "sessions/session/threads/main")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && path == "agents/agent/sessions/session/threads")
            {
                CreateThreadBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
