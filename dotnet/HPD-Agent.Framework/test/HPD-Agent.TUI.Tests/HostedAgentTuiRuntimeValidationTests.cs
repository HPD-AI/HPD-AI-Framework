using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Serialization;
using HPD.Agent.Providers;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI.Tests;

public sealed class HostedAgentTuiRuntimeValidationTests
{
    private static readonly AgentEventComposition TestEventComposition = CoreAgentEventComposition.Instance;
    private static readonly ProviderComposition TestProviderComposition = ProviderComposition.Create([]);

    [Fact]
    public async Task ValidateBackendEventCatalogAsync_RejectsDifferentAuthority()
    {
        using var http = new HttpClient(new JsonHandler("""
            { "events": [], "digest": "different" }
            """))
        {
            BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
        };
        await using var runtime = new HostedAgentTuiRuntime(http, new HostedAgentTuiRuntimeOptions
        {
            EventComposition = TestEventComposition,
            ProviderComposition = TestProviderComposition,
            BaseAddress = http.BaseAddress
        });

        var action = () => runtime.ValidateBackendEventCatalogAsync();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different*TUI event catalog*");
    }
    [Fact]
    public async Task GetThreadStateAsync_ProjectsUnifiedOperations()
    {
        using var http = new HttpClient(new JsonHandler("""
            {
              "observedCursor": { "generation": 1, "sequenceNumber": 3 },
              "pendingRequests": [],
              "activeExecution": {
                "threadExecutionId": "run-1",
                "agentId": "agent",
                "sessionId": "session",
                "threadId": "main",
                "status": "active",
                "startedAt": "2026-08-26T12:00:00Z",
                "operations": [{
                  "operationId": "op-1",
                  "providerOperationId": "remote-7",
                  "name": "search",
                  "sourceKind": "mcp_task",
                  "providerStatus": "running",
                  "observationStatus": "attached",
                  "controlKind": "task",
                  "controlCapabilities": "cancel,update",
                  "controlHandleId": "remote-7",
                  "version": 2,
                  "registeredAt": "2026-08-26T12:00:00Z",
                  "startedAt": "2026-08-26T12:00:01Z",
                  "updatedAt": "2026-08-26T12:00:02Z",
                  "metadata": { "server": "search" }
                }]
              }
            }
            """))
        {
            BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
        };
        await using var runtime = new HostedAgentTuiRuntime(http, new HostedAgentTuiRuntimeOptions
        {
            EventComposition = TestEventComposition,
            ProviderComposition = TestProviderComposition,
            BaseAddress = http.BaseAddress
        });

        var state = await runtime.GetThreadStateAsync(
            new AgentTuiRuntimeScope("agent", "session", "main"));

        var operation = state.ActiveExecution!.Operations.Should().ContainSingle().Which;
        operation.OperationId.Should().Be("op-1");
        operation.ProviderOperationId.Should().Be("remote-7");
        operation.Metadata.Should().Contain("server", "search");
    }

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
            EventComposition = TestEventComposition,
            ProviderComposition = TestProviderComposition,
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
            EventComposition = TestEventComposition,
            ProviderComposition = TestProviderComposition,
                BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
            });

        var act = () => runtime.ListThreadsAsync("session-1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*restart the backend*current agent event registrations*");
    }

    [Fact]
    public async Task ListThreadsAsync_ParsesNumericThreadKindAndVisibility()
    {
        using var http = new HttpClient(new JsonHandler("""
            [
              {
                "id": "subagent/explore/task/invocation",
                "sessionId": "session-1",
                "defaultAgentId": "coding/explorer",
                "name": "explore",
                "createdAt": "2026-07-21T00:00:00Z",
                "lastActivity": "2026-07-21T00:01:00Z",
                "kind": 1,
                "visibility": 1
              }
            ]
            """))
        {
            BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
        };
        await using var runtime = new HostedAgentTuiRuntime(http, new HostedAgentTuiRuntimeOptions
        {
            EventComposition = TestEventComposition,
            ProviderComposition = TestProviderComposition,
            BaseAddress = http.BaseAddress
        });

        var thread = (await runtime.ListThreadsAsync("session-1")).Should().ContainSingle().Subject;

        thread.Kind.Should().Be(ThreadKind.SubAgent);
        thread.Visibility.Should().Be(ThreadVisibility.Hidden);
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
            EventComposition = TestEventComposition,
            ProviderComposition = TestProviderComposition,
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
            EventComposition = TestEventComposition,
            ProviderComposition = TestProviderComposition,
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
            EventComposition = TestEventComposition,
            ProviderComposition = TestProviderComposition,
            BaseAddress = http.BaseAddress
        });

        var act = () => runtime.GetThreadStateAsync(
            new AgentTuiRuntimeScope("agent", "session", "main"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not have a durable journal*");
    }

    [Fact]
    public async Task SubmitInputAsync_AcceptsNullableControlResultFields()
    {
        using var http = new HttpClient(new JsonHandler("""
            {
              "disposition": "no_active_execution",
              "threadExecutionId": null,
              "startedAt": null,
              "activeExecution": null
            }
            """))
        {
            BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
        };
        await using var runtime = new HostedAgentTuiRuntime(http, new HostedAgentTuiRuntimeOptions
        {
            EventComposition = TestEventComposition,
            ProviderComposition = TestProviderComposition,
            BaseAddress = http.BaseAddress
        });
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");

        var result = await runtime.SubmitInputAsync(
            scope,
            new UserMessagesInputEvent
            {
                Delivery = AgentInputDelivery.Steer,
                AgentId = scope.AgentId,
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId,
                ThreadExecutionId = "stale-execution"
            });

        result.Disposition.Should().Be(AgentInputDisposition.NoActiveExecution);
        result.ThreadExecutionId.Should().BeNull();
        result.ActiveExecution.Should().BeNull();
    }

    [Fact]
    public async Task ObserveAsync_ReconnectsAfterEofUsingLastCommittedSequence()
    {
        var first = new ThreadExecutionStartedEvent("run-1", "agent", DateTimeOffset.UtcNow)
        {
            SessionId = "session",
            ThreadId = "main",
            ThreadSequenceNumber = 1
        };
        var second = new ThreadExecutionFinishedEvent("run-1", "agent", ThreadExecutionOutcome.Succeeded, DateTimeOffset.UtcNow)
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
            EventComposition = TestEventComposition,
            ProviderComposition = TestProviderComposition,
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

    [Fact]
    public async Task ObserveAsync_DeliversLiveEventWithoutAdvancingReconnectCursor()
    {
        var live = new TextDeltaEvent("live", "message-live")
        {
            SessionId = "session",
            ThreadId = "subagent/explore/invocation-1",
            ThreadSequenceNumber = 8
        };
        var committed = new ThreadExecutionFinishedEvent("run-1", "agent", ThreadExecutionOutcome.Succeeded, DateTimeOffset.UtcNow)
        {
            SessionId = "session",
            ThreadId = "main",
            ThreadSequenceNumber = 5
        };
        var handler = new RawSequentialSseHandler(
            $"event: live-agent-event\ndata: {TestEventComposition.Codec.Serialize(live)}\n\n",
            $"id: 1:5\nevent: agent-event\ndata: {TestEventComposition.Codec.Serialize(committed)}\n\n");
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
        };
        await using var runtime = new HostedAgentTuiRuntime(http, new HostedAgentTuiRuntimeOptions
        {
            EventComposition = TestEventComposition,
            ProviderComposition = TestProviderComposition,
            BaseAddress = http.BaseAddress
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var batches = new List<AgentTuiEventBatch>();

        await foreach (var batch in runtime.ObserveAsync(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            after: new ThreadJournalCursor(1, 4),
            initialObservedCursor: new ThreadJournalCursor(1, 4),
            cancellationToken: timeout.Token))
        {
            batches.Add(batch);
            if (batches.Count == 2)
                break;
        }

        batches[0].Events.Should().ContainSingle().Which.Should().BeEquivalentTo(live);
        batches[0].LastCursor.Should().Be(new ThreadJournalCursor(1, 4));
        batches[1].Events.Should().ContainSingle().Which.Should().BeEquivalentTo(committed);
        batches[1].LastCursor.Should().Be(new ThreadJournalCursor(1, 5));
        handler.Requests.Should().Equal("?after=1:4", "?after=1:4");
    }

    [Fact]
    public async Task ObserveAsync_CommittedSelectedThreadLiveEventAdvancesReconnectCursor()
    {
        var live = new TextMessageEndEvent("message-live")
        {
            SessionId = "session",
            ThreadId = "main",
            ThreadSequenceNumber = 6
        };
        var next = new ThreadExecutionFinishedEvent("run-1", "agent", ThreadExecutionOutcome.Succeeded, DateTimeOffset.UtcNow)
        {
            SessionId = "session",
            ThreadId = "main",
            ThreadSequenceNumber = 7
        };
        var handler = new RawSequentialSseHandler(
            $"id: 1:6\nevent: live-agent-event\ndata: {TestEventComposition.Codec.Serialize(live)}\n\n",
            $"id: 1:7\nevent: agent-event\ndata: {TestEventComposition.Codec.Serialize(next)}\n\n");
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
        };
        await using var runtime = new HostedAgentTuiRuntime(http, new HostedAgentTuiRuntimeOptions
        {
            EventComposition = TestEventComposition,
            ProviderComposition = TestProviderComposition,
            BaseAddress = http.BaseAddress
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var batches = new List<AgentTuiEventBatch>();

        await foreach (var batch in runtime.ObserveAsync(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            after: new ThreadJournalCursor(1, 5),
            initialObservedCursor: new ThreadJournalCursor(1, 5),
            cancellationToken: timeout.Token))
        {
            batches.Add(batch);
            if (batches.Count == 2)
                break;
        }

        batches[0].LastCursor.Should().Be(new ThreadJournalCursor(1, 6));
        batches[1].LastCursor.Should().Be(new ThreadJournalCursor(1, 7));
        handler.Requests.Should().Equal("?after=1:5", "?after=1:6");
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
            if (request.RequestUri?.AbsolutePath.EndsWith("/event-catalog", StringComparison.Ordinal) == true)
                return Task.FromResult(CatalogResponse());
            Requests.Add(request.RequestUri?.Query ?? string.Empty);
            var evt = events[_index++];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"id: 1:{evt.ThreadSequenceNumber}\ndata: {TestEventComposition.Codec.Serialize(evt)}\n\n",
                    Encoding.UTF8,
                    "text/event-stream")
            });
        }
    }

    private sealed class RawSequentialSseHandler(params string[] payloads) : HttpMessageHandler
    {
        private int _index;

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/event-catalog", StringComparison.Ordinal) == true)
                return Task.FromResult(CatalogResponse());
            Requests.Add(request.RequestUri?.Query ?? string.Empty);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payloads[_index++], Encoding.UTF8, "text/event-stream")
            });
        }
    }

    private static HttpResponseMessage CatalogResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(TestEventComposition.Catalog, AgentEventJsonContext.Default.AgentEventCatalog),
            Encoding.UTF8,
            "application/json")
    };

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
