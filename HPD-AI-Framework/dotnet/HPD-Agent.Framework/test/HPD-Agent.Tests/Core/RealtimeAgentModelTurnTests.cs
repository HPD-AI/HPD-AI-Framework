using System.Runtime.CompilerServices;
using HPD.Agent.Middleware;
using HPD.Agent.Middleware.Function;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Core;

#pragma warning disable MEAI001

public sealed class RealtimeAgentModelTurnTests : AgentTestBase
{
    [Fact]
    public async Task RunAsync_RealtimeTransport_ExecutesToolsThroughAgentLoopAndContinuesRealtimeSession()
    {
        var session = new ScriptedRealtimeSession(
            [
                [ToolCallDone("resp-add", "call-add", "Add", new Dictionary<string, object?> { ["left"] = 2, ["right"] = 3 })],
                [ToolCallDone("resp-multiply", "call-multiply", "Multiply", new Dictionary<string, object?> { ["left"] = 5, ["right"] = 4 })],
                [TextDelta("resp-final", "The answer is 20."), TextDone("resp-final")]
        ]);
        var agent = TestAgentFactory.Create(circuitBreakerThreshold: 10);
        var capture = SubscribeEvents(agent);

        try
        {
            await agent.RunAsync(
                "Use math tools.",
                runConfig: CreateRealtimeMathRunConfig(session));
        }
        finally
        {
            capture.Dispose();
            agent.Dispose();
        }

        var resultMessages = SentContents<FunctionResultContent>(session).ToList();
        Assert.Collection(
            resultMessages,
            result =>
            {
                Assert.Equal("call-add", result.CallId);
                Assert.Equal(5, ReadIntResult(result));
            },
            result =>
            {
                Assert.Equal("call-multiply", result.CallId);
                Assert.Equal(20, ReadIntResult(result));
            });

        var capturedEvents = capture.Snapshot();
        Assert.Contains(capturedEvents.OfType<TextDeltaEvent>(), evt => evt.Text == "The answer is 20.");
        Assert.Equal(["call-add", "call-multiply"], capturedEvents.OfType<ToolCallStartEvent>().Select(evt => evt.CallId).ToArray());
        Assert.Equal(["call-add", "call-multiply"], capturedEvents.OfType<ToolCallResultEvent>().Select(evt => evt.CallId).ToArray());
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_SimpleText_CompletesTurnAndCommitsBranchText()
    {
        var session = new ScriptedRealtimeSession(
            [
                [
                    TextDelta("resp-final", "Hello"),
                    TextDelta("resp-final", " realtime"),
                    TextDone("resp-final")
                ]
            ]);
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var config = DefaultConfig();
        config.SessionRepository = repository;
        var agent = TestAgentFactory.Create(config, circuitBreakerThreshold: 10);
        var capture = SubscribeEvents(agent);

        try
        {
            await agent.CreateSessionAsync("session-1", cancellationToken: TestCancellationToken);
            await agent.RunAsync(
                "Say hello.",
                "session-1",
                "main",
                runConfig: CreateRealtimeMathRunConfig(session),
                cancellationToken: TestCancellationToken);
        }
        finally
        {
            capture.Dispose();
            agent.Dispose();
        }

        var capturedEvents = capture.Snapshot();
        Assert.Contains(capturedEvents, evt => evt is TextMessageStartEvent);
        Assert.Contains(capturedEvents, evt => evt is TextMessageEndEvent);
        Assert.Contains(capturedEvents, evt => evt is AgentTurnFinishedEvent);
        Assert.Contains(capturedEvents, evt => evt is MessageTurnFinishedEvent);
        Assert.Equal("Hello realtime", string.Concat(capturedEvents.OfType<TextDeltaEvent>().Select(evt => evt.Text)));

        var branch = await repository.LoadBranchAsync("session-1", "main", TestCancellationToken);
        Assert.NotNull(branch);
        Assert.Equal("Say hello.", branch.Messages[0].Text);
        Assert.Equal("Hello realtime", branch.Messages[1].Text);
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_InputTranscription_EmitsEventsAndCommitsUserBranchText()
    {
        var session = new ScriptedRealtimeSession(
            [
                [
                    InputTranscriptDelta("item-user", "How are"),
                    InputTranscriptDone("item-user", "How are you doing today?"),
                    TextDelta("resp-final", "Doing well."),
                    TextDone("resp-final")
                ]
            ]);
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var config = DefaultConfig();
        config.SessionRepository = repository;
        var agent = TestAgentFactory.Create(config, circuitBreakerThreshold: 10);
        var capture = SubscribeEvents(agent);
        var audioMessage = new ChatMessage(
            ChatRole.User,
            [new AudioContent(new byte[] { 1, 2, 3 }, "audio/wav")])
        {
            MessageId = "user-audio-1"
        };

        try
        {
            await agent.CreateSessionAsync("session-transcript", cancellationToken: TestCancellationToken);
            await agent.RunAsync(
                new UserMessagesInputEvent([audioMessage])
                {
                    SessionId = "session-transcript",
                    BranchId = "main",
                    RunConfig = CreateRealtimeMathRunConfig(session, realtimeTranscriptionOptions: new TranscriptionOptions
                    {
                        ModelId = "whisper-1",
                        SpeechLanguage = "en"
                    })
                },
                TestCancellationToken);
        }
        finally
        {
            capture.Dispose();
            agent.Dispose();
        }

        Assert.Equal("whisper-1", session.Options?.TranscriptionOptions?.ModelId);

        var capturedEvents = capture.Snapshot();
        Assert.Contains(capturedEvents.OfType<UserAudioTranscriptDeltaEvent>(), evt =>
            evt.MessageId == "user-audio-1" &&
            evt.Text == "How are" &&
            evt.ProviderItemId == "item-user");
        Assert.Contains(capturedEvents.OfType<UserAudioTranscriptCompletedEvent>(), evt =>
            evt.MessageId == "user-audio-1" &&
            evt.Text == "How are you doing today?");

        var branch = await repository.LoadBranchAsync("session-transcript", "main", TestCancellationToken);
        var document = await repository.LoadBranchDocumentAsync("session-transcript", "main", TestCancellationToken);
        Assert.NotNull(document);
        Assert.Contains(document.Events, evt => evt is TextDeltaEvent text && text.MessageId == "user-audio-1");
        Assert.NotNull(branch);
        var userMessage = Assert.Single(branch.Messages, message => message.MessageId == "user-audio-1");
        Assert.Equal(ChatRole.User, userMessage.Role);
        Assert.Equal(
            "How are you doing today?",
            Assert.Single(userMessage.Contents.OfType<TextContent>()).Text);
        Assert.Single(userMessage.Contents.OfType<UriContent>());
        Assert.Contains(branch.Messages, message => message.Role == ChatRole.Assistant && message.Text == "Doing well.");
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_InputTranscriptionAfterFinalText_CommitsUserBranchText()
    {
        var session = new ScriptedRealtimeSession(
            [
                [
                    TextDelta("resp-final", "Doing well."),
                    TextDone("resp-final"),
                    InputTranscriptDone("item-user", "How are you doing today?"),
                    ResponseDone("resp-final")
                ]
            ]);
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var config = DefaultConfig();
        config.SessionRepository = repository;
        var agent = TestAgentFactory.Create(config, circuitBreakerThreshold: 10);
        var capture = SubscribeEvents(agent);
        var audioMessage = new ChatMessage(
            ChatRole.User,
            [new AudioContent(new byte[] { 1, 2, 3 }, "audio/wav")])
        {
            MessageId = "user-audio-after-final"
        };

        try
        {
            await agent.CreateSessionAsync("session-transcript-after-final", cancellationToken: TestCancellationToken);
            await agent.RunAsync(
                new UserMessagesInputEvent([audioMessage])
                {
                    SessionId = "session-transcript-after-final",
                    BranchId = "main",
                    RunConfig = CreateRealtimeMathRunConfig(session, realtimeTranscriptionOptions: new TranscriptionOptions
                    {
                        ModelId = "whisper-1",
                        SpeechLanguage = "en"
                    })
                },
                TestCancellationToken);
        }
        finally
        {
            capture.Dispose();
            agent.Dispose();
        }

        var capturedEvents = capture.Snapshot();
        Assert.Contains(capturedEvents.OfType<UserAudioTranscriptCompletedEvent>(), evt =>
            evt.MessageId == "user-audio-after-final" &&
            evt.Text == "How are you doing today?");

        var branch = await repository.LoadBranchAsync(
            "session-transcript-after-final",
            "main",
            TestCancellationToken);
        Assert.NotNull(branch);
        var userMessage = Assert.Single(branch.Messages, message => message.MessageId == "user-audio-after-final");
        Assert.Equal(ChatRole.User, userMessage.Role);
        Assert.Equal(
            "How are you doing today?",
            Assert.Single(userMessage.Contents.OfType<TextContent>()).Text);
        Assert.Contains(branch.Messages, message => message.Role == ChatRole.Assistant && message.Text == "Doing well.");
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_InputTranscriptionAfterMiddlewareReplacement_CommitsPreparedUserBranchText()
    {
        var session = new ScriptedRealtimeSession(
            [
                [
                    TextDelta("resp-final", "Doing well."),
                    TextDone("resp-final"),
                    InputTranscriptDone("item-user", "How are you doing today?"),
                    ResponseDone("resp-final")
                ]
            ]);
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var config = DefaultConfig();
        config.SessionRepository = repository;
        var agent = TestAgentFactory.CreateWithMiddlewares(
            config,
            middlewares: [new ReplacingUserMessageMiddleware()],
            circuitBreakerThreshold: 10);
        var capture = SubscribeEvents(agent);
        var audioMessage = new ChatMessage(
            ChatRole.User,
            [new AudioContent(new byte[] { 1, 2, 3 }, "audio/wav")]);

        try
        {
            await agent.CreateSessionAsync("session-transcript-replaced", cancellationToken: TestCancellationToken);
            await agent.RunAsync(
                new UserMessagesInputEvent([audioMessage])
                {
                    SessionId = "session-transcript-replaced",
                    BranchId = "main",
                    RunConfig = CreateRealtimeMathRunConfig(session, realtimeTranscriptionOptions: new TranscriptionOptions
                    {
                        ModelId = "whisper-1",
                        SpeechLanguage = "en"
                    })
                },
                TestCancellationToken);
        }
        finally
        {
            capture.Dispose();
            agent.Dispose();
        }

        var branch = await repository.LoadBranchAsync(
            "session-transcript-replaced",
            "main",
            TestCancellationToken);
        Assert.NotNull(branch);
        var userMessage = Assert.Single(branch.Messages, message => message.Role == ChatRole.User);
        Assert.Equal(
            "How are you doing today?",
            Assert.Single(userMessage.Contents.OfType<TextContent>()).Text);

        var completed = Assert.Single(capture.Snapshot().OfType<UserAudioTranscriptCompletedEvent>());
        Assert.Equal(userMessage.MessageId, completed.MessageId);
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_TextDone_DoesNotDuplicateBranchText()
    {
        var session = new ScriptedRealtimeSession(
            [
                [
                    TextDelta("resp-final", "The final answer is 20."),
                    new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDone)
                    {
                        ResponseId = "resp-final",
                        Text = "The final answer is 20."
                    }
                ]
            ]);
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var config = DefaultConfig();
        config.SessionRepository = repository;
        var agent = TestAgentFactory.Create(config, circuitBreakerThreshold: 10);

        try
        {
            await agent.CreateSessionAsync("session-dup", cancellationToken: TestCancellationToken);
            await agent.RunAsync(
                "Answer once.",
                "session-dup",
                "main",
                runConfig: CreateRealtimeMathRunConfig(session),
                cancellationToken: TestCancellationToken);
        }
        finally
        {
            agent.Dispose();
        }

        var branch = await repository.LoadBranchAsync("session-dup", "main", TestCancellationToken);
        Assert.NotNull(branch);
        Assert.Equal("The final answer is 20.", branch.Messages.Last(m => m.Role == ChatRole.Assistant).Text);
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_PersistsToolCallAndToolResultMessages()
    {
        var session = new ScriptedRealtimeSession(
            [
                [ToolCallDone("resp-add", "call-add", "Add", new Dictionary<string, object?> { ["left"] = 2, ["right"] = 3 })],
                [TextDelta("resp-final", "The answer is 5."), TextDone("resp-final")]
            ]);
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var config = DefaultConfig();
        config.SessionRepository = repository;
        var agent = TestAgentFactory.Create(config, circuitBreakerThreshold: 10);

        try
        {
            await agent.CreateSessionAsync("session-tools", cancellationToken: TestCancellationToken);
            await agent.RunAsync(
                "Use Add.",
                "session-tools",
                "main",
                runConfig: CreateRealtimeMathRunConfig(session),
                cancellationToken: TestCancellationToken);
        }
        finally
        {
            agent.Dispose();
        }

        var branch = await repository.LoadBranchAsync("session-tools", "main", TestCancellationToken);
        Assert.NotNull(branch);
        Assert.Equal(4, branch.Messages.Count);
        Assert.Equal(ChatRole.User, branch.Messages[0].Role);

        var call = Assert.Single(branch.Messages[1].Contents.OfType<FunctionCallContent>());
        Assert.Equal("call-add", call.CallId);
        Assert.Equal("Add", call.Name);

        var result = Assert.Single(branch.Messages[2].Contents.OfType<FunctionResultContent>());
        Assert.Equal("call-add", result.CallId);
        Assert.Equal(5, ReadIntResult(result));

        Assert.Equal("The answer is 5.", branch.Messages[3].Text);
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_MultiTurn_SendsFollowUpUserTextToExistingSession()
    {
        var session = new ScriptedRealtimeSession(
            [
                [TextDelta("resp-1", "First done."), TextDone("resp-1")],
                [ToolCallDone("resp-add", "call-add", "Add", new Dictionary<string, object?> { ["left"] = 10, ["right"] = 7 })],
                [TextDelta("resp-2", "The answer is 17."), TextDone("resp-2")]
            ]);
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var config = DefaultConfig();
        config.SessionRepository = repository;
        var agent = TestAgentFactory.Create(config, circuitBreakerThreshold: 10);

        try
        {
            await agent.CreateSessionAsync("session-multi", cancellationToken: TestCancellationToken);
            await agent.RunAsync(
                "Say first done.",
                "session-multi",
                "main",
                runConfig: CreateRealtimeMathRunConfig(session),
                cancellationToken: TestCancellationToken);
            await agent.RunAsync(
                "Now add 10 and 7.",
                "session-multi",
                "main",
                runConfig: CreateRealtimeMathRunConfig(session),
                cancellationToken: TestCancellationToken);
        }
        finally
        {
            agent.Dispose();
        }

        var sentUserTexts = session.Sent
            .OfType<CreateResponseRealtimeClientMessage>()
            .SelectMany(message => message.Items ?? [])
            .Where(item => item.Role == ChatRole.User)
            .Select(item => Assert.Single(item.Contents.OfType<TextContent>()).Text)
            .ToArray();

        Assert.Equal(["Say first done.", "Now add 10 and 7."], sentUserTexts);
        Assert.Single(SentContents<FunctionResultContent>(session), result => result.CallId == "call-add");

        var branch = await repository.LoadBranchAsync("session-multi", "main", TestCancellationToken);
        Assert.NotNull(branch);
        Assert.Contains(branch.Messages, message => message.Text == "The answer is 17.");
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_ProviderError_FailsTurnWithoutAssistantTextCommit()
    {
        var session = new ScriptedRealtimeSession(
            [
                [
                    new ErrorRealtimeServerMessage
                    {
                        OriginatingMessageId = "client-msg-1",
                        Error = new ErrorContent("provider failed")
                        {
                            ErrorCode = "provider.error"
                        }
                    }
                ]
            ]);
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var config = DefaultConfig();
        config.SessionRepository = repository;
        var agent = TestAgentFactory.Create(config, circuitBreakerThreshold: 10);

        await agent.CreateSessionAsync("session-error", cancellationToken: TestCancellationToken);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.RunAsync(
                "Trigger provider failure.",
                "session-error",
                "main",
                runConfig: CreateRealtimeMathRunConfig(session),
                cancellationToken: TestCancellationToken));
        agent.Dispose();

        Assert.Contains("provider.error", ex.Message);

        var branch = await repository.LoadBranchAsync("session-error", "main", TestCancellationToken);
        Assert.NotNull(branch);
        Assert.Single(branch.Messages);
        Assert.Equal("Trigger provider failure.", branch.Messages[0].Text);
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_MultipleToolCallsInOneResponse_ExecutesAllAndPersistsResults()
    {
        var session = new ScriptedRealtimeSession(
            [
                [
                    ToolCallsDone(
                        "resp-tools",
                        [
                            new FunctionCallContent(
                                "call-add",
                                "Add",
                                new Dictionary<string, object?> { ["left"] = 2, ["right"] = 3 }),
                            new FunctionCallContent(
                                "call-multiply",
                                "Multiply",
                                new Dictionary<string, object?> { ["left"] = 4, ["right"] = 5 })
                        ])
                ],
                [TextDelta("resp-final", "The answers are 5 and 20."), TextDone("resp-final")]
            ]);
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var config = DefaultConfig();
        config.SessionRepository = repository;
        var agent = TestAgentFactory.Create(config, circuitBreakerThreshold: 10);

        try
        {
            await agent.CreateSessionAsync("session-parallel", cancellationToken: TestCancellationToken);
            await agent.RunAsync(
                "Call two math tools.",
                "session-parallel",
                "main",
                runConfig: CreateRealtimeMathRunConfig(session),
                cancellationToken: TestCancellationToken);
        }
        finally
        {
            agent.Dispose();
        }

        var resultMessages = SentContents<FunctionResultContent>(session).ToList();
        Assert.Collection(
            resultMessages,
            result =>
            {
                Assert.Equal("call-add", result.CallId);
                Assert.Equal(5, ReadIntResult(result));
            },
            result =>
            {
                Assert.Equal("call-multiply", result.CallId);
                Assert.Equal(20, ReadIntResult(result));
            });

        var branch = await repository.LoadBranchAsync("session-parallel", "main", TestCancellationToken);
        Assert.NotNull(branch);
        Assert.Equal(2, branch.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Count());
        Assert.Equal(2, branch.Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Count());
        Assert.Equal("The answers are 5 and 20.", branch.Messages.Last(m => m.Role == ChatRole.Assistant).Text);
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_CoalesceDeltas_EmitsSingleTextDelta()
    {
        var session = new ScriptedRealtimeSession(
            [
                [
                    TextDelta("resp-final", "Hello"),
                    TextDelta("resp-final", " "),
                    TextDelta("resp-final", "realtime"),
                    TextDone("resp-final")
                ]
            ]);
        var agent = TestAgentFactory.Create(circuitBreakerThreshold: 10);
        var capture = SubscribeEvents(agent);

        try
        {
            await agent.RunAsync(
                "Say hello.",
                runConfig: CreateRealtimeMathRunConfig(session, coalesceDeltas: true),
                cancellationToken: TestCancellationToken);
        }
        finally
        {
            capture.Dispose();
            agent.Dispose();
        }

        var deltas = capture.Snapshot().OfType<TextDeltaEvent>().Select(evt => evt.Text).ToArray();
        Assert.Equal(["Hello realtime"], deltas);
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_ModelTurnMiddlewareSeesRealtimeRequest()
    {
        var session = new ScriptedRealtimeSession(
            [
                [TextDelta("resp-final", "Done."), TextDone("resp-final")]
            ]);
        var middleware = new ModelTurnSpyMiddleware();
        var agent = TestAgentFactory.CreateWithMiddlewares(
            middlewares: [middleware],
            circuitBreakerThreshold: 10);

        try
        {
            await agent.RunAsync(
                "Use realtime.",
                runConfig: CreateRealtimeMathRunConfig(session),
                cancellationToken: TestCancellationToken);
        }
        finally
        {
            agent.Dispose();
        }

        Assert.True(middleware.WasCalled);
        Assert.Equal(AgentModelTransport.Realtime, middleware.Transport);
        Assert.NotNull(middleware.RealtimeModel);
        Assert.Equal(3, middleware.ToolCount);
        Assert.Contains(middleware.Messages, message => message.Role == ChatRole.User && message.Text == "Use realtime.");
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_CircuitBreakerStopsRepeatedIdenticalToolCalls()
    {
        var session = new ScriptedRealtimeSession(
            [
                [ToolCallDone("resp-add-1", "call-add-1", "Add", new Dictionary<string, object?> { ["left"] = 1, ["right"] = 1 })],
                [ToolCallDone("resp-add-2", "call-add-2", "Add", new Dictionary<string, object?> { ["left"] = 1, ["right"] = 1 })]
            ]);
        var agent = TestAgentFactory.Create(circuitBreakerThreshold: 2);
        var capture = SubscribeEvents(agent);

        try
        {
            await agent.RunAsync(
                "Keep calling Add.",
                runConfig: CreateRealtimeMathRunConfig(session),
                cancellationToken: TestCancellationToken);
        }
        finally
        {
            capture.Dispose();
            agent.Dispose();
        }

        var capturedEvents = capture.Snapshot();
        Assert.Single(SentContents<FunctionResultContent>(session), result => result.CallId == "call-add-1");
        Assert.Contains(capturedEvents.OfType<CircuitBreakerTriggeredEvent>(), evt => evt.FunctionName == "Add");
        Assert.Contains(capturedEvents.OfType<TextDeltaEvent>(), evt => evt.Text.Contains("Circuit breaker", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_FunctionRetryMiddlewareRetriesToolCall()
    {
        var attempts = 0;
        int Flaky()
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("temporary failure");
            }

            return 42;
        }

        var flakyTool = AIFunctionFactory.Create((Func<int>)Flaky, name: "Flaky", description: "Fails once, then succeeds.");
        var session = new ScriptedRealtimeSession(
            [
                [ToolCallDone("resp-flaky", "call-flaky", "Flaky", new Dictionary<string, object?>())],
                [TextDelta("resp-final", "Recovered with 42."), TextDone("resp-final")]
            ]);
        var retryConfig = new ErrorHandlingConfig
        {
            MaxRetries = 1,
            RetryDelay = TimeSpan.Zero,
            NormalizeErrors = true
        };
        var agent = TestAgentFactory.CreateWithMiddlewares(
            middlewares: [new RetryMiddleware(retryConfig)],
            circuitBreakerThreshold: 10);
        var capture = SubscribeEvents(agent);

        try
        {
            await agent.RunAsync(
                "Call flaky.",
                runConfig: CreateRealtimeRunConfig(session, [flakyTool]),
                cancellationToken: TestCancellationToken);
        }
        finally
        {
            capture.Dispose();
            agent.Dispose();
        }

        var result = Assert.Single(SentContents<FunctionResultContent>(session));
        Assert.Equal("call-flaky", result.CallId);
        Assert.Equal(42, ReadIntResult(result));
        Assert.Equal(2, attempts);
        Assert.Contains(capture.Snapshot().OfType<FunctionRetryEvent>(), evt => evt.FunctionName == "Flaky");
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_ConsecutiveToolErrorsTerminateAfterLimit()
    {
        int AlwaysFails(int attempt) => throw new InvalidOperationException($"failure {attempt}");

        var errorTool = AIFunctionFactory.Create(
            (Func<int, int>)AlwaysFails,
            name: "ErrorTool",
            description: "Always fails.");
        var session = new ScriptedRealtimeSession(
            [
                [ToolCallDone("resp-error-1", "call-error-1", "ErrorTool", new Dictionary<string, object?> { ["attempt"] = 1 })],
                [ToolCallDone("resp-error-2", "call-error-2", "ErrorTool", new Dictionary<string, object?> { ["attempt"] = 2 })],
                [ToolCallDone("resp-error-3", "call-error-3", "ErrorTool", new Dictionary<string, object?> { ["attempt"] = 3 })],
                [TextDelta("resp-unreached", "Should not be reached."), TextDone("resp-unreached")]
            ]);
        var agent = TestAgentFactory.Create(circuitBreakerThreshold: 10);
        var capture = SubscribeEvents(agent);

        try
        {
            await agent.RunAsync(
                "Keep calling the failing tool.",
                runConfig: CreateRealtimeRunConfig(session, [errorTool]),
                cancellationToken: TestCancellationToken);
        }
        finally
        {
            capture.Dispose();
            agent.Dispose();
        }

        var capturedEvents = capture.Snapshot();
        Assert.Equal(3, SentContents<FunctionResultContent>(session).Count());
        Assert.Contains(capturedEvents.OfType<MaxConsecutiveErrorsExceededEvent>(), evt => evt.ConsecutiveErrors == 3);
        Assert.Contains(capturedEvents.OfType<TextDeltaEvent>(), evt => evt.Text.Contains("consecutive errors", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(capturedEvents.OfType<TextDeltaEvent>(), evt => evt.Text.Contains("Should not be reached.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_MaxIterationContinuationDeniedStopsBeforeNextRealtimeResponse()
    {
        var session = new ScriptedRealtimeSession(
            [
                [ToolCallDone("resp-add-1", "call-add-1", "Add", new Dictionary<string, object?> { ["left"] = 1, ["right"] = 2 })],
                [ToolCallDone("resp-add-2", "call-add-2", "Add", new Dictionary<string, object?> { ["left"] = 3, ["right"] = 4 })]
            ]);
        var config = DefaultConfig();
        config.MaxAgenticIterations = 1;
        var agent = TestAgentFactory.CreateWithMiddlewares(
            config,
            middlewares: [new ContinuationPermissionMiddleware(maxIterations: config.MaxAgenticIterations)],
            circuitBreakerThreshold: 10);
        var capture = SubscribeEvents(agent);
        using var continuationResponder = agent.Subscribe<ContinuationRequestEvent>(async ValueTask (ContinuationRequestEvent request) =>
        {
            await agent.RespondAsync(
                new ContinuationResponseEvent(request.ContinuationId, request.SourceName, Approved: false),
                TestCancellationToken);
        });

        try
        {
            await agent.RunAsync(
                "Use too many tools.",
                runConfig: CreateRealtimeMathRunConfig(session),
                cancellationToken: TestCancellationToken);
        }
        finally
        {
            capture.Dispose();
            agent.Dispose();
        }

        var capturedEvents = capture.Snapshot();
        Assert.Single(SentContents<FunctionResultContent>(session), result => result.CallId == "call-add-1");
        Assert.Contains(capturedEvents.OfType<ContinuationRequestEvent>(), evt => evt.MaxIterations == 1);
        Assert.Contains(capturedEvents.OfType<TextDeltaEvent>(), evt => evt.Text.Contains("Maximum iteration limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_UnknownToolDefault_AllowsModelRecovery()
    {
        var session = new ScriptedRealtimeSession(
            [
                [ToolCallDone("resp-unknown", "call-unknown", "MissingTool", new Dictionary<string, object?>())],
                [TextDelta("resp-final", "Recovered without that tool."), TextDone("resp-final")]
            ]);
        var agent = TestAgentFactory.Create(circuitBreakerThreshold: 10);

        try
        {
            await agent.RunAsync(
                "Call a missing tool.",
                runConfig: CreateRealtimeMathRunConfig(session),
                cancellationToken: TestCancellationToken);
        }
        finally
        {
            agent.Dispose();
        }

        var result = Assert.Single(SentContents<FunctionResultContent>(session));
        Assert.Equal("call-unknown", result.CallId);
        Assert.Contains("not found", ReadStringResult(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_UnknownToolTerminate_DoesNotContinueModelLoop()
    {
        var session = new ScriptedRealtimeSession(
            [
                [ToolCallDone("resp-unknown", "call-unknown", "MissingTool", new Dictionary<string, object?>())],
                [TextDelta("resp-unreached", "Should not be reached."), TextDone("resp-unreached")]
            ]);
        var config = DefaultConfig();
        config.AgenticLoop ??= new AgenticLoopConfig();
        config.AgenticLoop.TerminateOnUnknownCalls = true;
        var agent = TestAgentFactory.Create(config, circuitBreakerThreshold: 10);
        var capture = SubscribeEvents(agent);

        try
        {
            await agent.RunAsync(
                "Call a missing tool.",
                runConfig: CreateRealtimeMathRunConfig(session),
                cancellationToken: TestCancellationToken);
        }
        finally
        {
            capture.Dispose();
            agent.Dispose();
        }

        var capturedEvents = capture.Snapshot();
        Assert.Empty(SentContents<FunctionResultContent>(session));
        Assert.Contains(capturedEvents.OfType<ToolCallStartEvent>(), evt => evt.CallId == "call-unknown");
        Assert.DoesNotContain(capturedEvents.OfType<TextDeltaEvent>(), evt => evt.Text.Contains("Should not be reached.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_FunctionTimeoutMiddlewareSubmitsTimedOutToolResult()
    {
        async Task<int> Slow()
        {
            await Task.Delay(TimeSpan.FromSeconds(5), TestCancellationToken);
            return 99;
        }

        var slowTool = AIFunctionFactory.Create((Func<Task<int>>)Slow, name: "Slow", description: "Slow tool.");
        var session = new ScriptedRealtimeSession(
            [
                [ToolCallDone("resp-slow", "call-slow", "Slow", new Dictionary<string, object?>())],
                [TextDelta("resp-final", "Timeout handled."), TextDone("resp-final")]
            ]);
        var agent = TestAgentFactory.CreateWithMiddlewares(
            middlewares: [new FunctionTimeoutMiddleware(TimeSpan.FromMilliseconds(25))],
            circuitBreakerThreshold: 10);

        try
        {
            await agent.RunAsync(
                "Call slow.",
                runConfig: CreateRealtimeRunConfig(session, [slowTool]),
                cancellationToken: TestCancellationToken);
        }
        finally
        {
            agent.Dispose();
        }

        var result = Assert.Single(SentContents<FunctionResultContent>(session));
        Assert.Equal("call-slow", result.CallId);
        Assert.Contains("timed out", ReadStringResult(result), StringComparison.OrdinalIgnoreCase);
    }

    private static AgentRunConfig CreateRealtimeMathRunConfig(
        ScriptedRealtimeSession session,
        bool? coalesceDeltas = null,
        TranscriptionOptions? realtimeTranscriptionOptions = null)
        => CreateRealtimeRunConfig(session, CreateMathTools(), coalesceDeltas, realtimeTranscriptionOptions);

    private static AgentRunConfig CreateRealtimeRunConfig(
        ScriptedRealtimeSession session,
        IReadOnlyList<AIFunction> tools,
        bool? coalesceDeltas = null,
        TranscriptionOptions? realtimeTranscriptionOptions = null)
        => new()
        {
            ModelTransport = AgentModelTransportMode.Realtime,
            OverrideRealtimeClient = new FakeRealtimeClient(session),
            AdditionalTools = tools,
            ToolModeOverride = ChatToolMode.Auto,
            CoalesceDeltas = coalesceDeltas,
            RealtimeTranscriptionOptions = realtimeTranscriptionOptions
        };

    private static IReadOnlyList<AIFunction> CreateMathTools() =>
    [
        AIFunctionFactory.Create((Func<int, int, int>)Add, name: "Add", description: "Adds two integers."),
        AIFunctionFactory.Create((Func<int, int, int>)Multiply, name: "Multiply", description: "Multiplies two integers."),
        AIFunctionFactory.Create((Func<int, int, int>)Subtract, name: "Subtract", description: "Subtracts right from left.")
    ];

    private static ResponseOutputItemRealtimeServerMessage ToolCallDone(
        string responseId,
        string callId,
        string name,
        IDictionary<string, object?> args)
        => new(RealtimeServerMessageType.ResponseOutputItemDone)
        {
            ResponseId = responseId,
            Item = new RealtimeConversationItem(
                [
                    new FunctionCallContent(
                        callId,
                        name,
                        args)
                ])
        };

    private static ResponseOutputItemRealtimeServerMessage ToolCallsDone(
        string responseId,
        IReadOnlyList<FunctionCallContent> calls)
        => new(RealtimeServerMessageType.ResponseOutputItemDone)
        {
            ResponseId = responseId,
            Item = new RealtimeConversationItem(calls.Cast<AIContent>().ToList())
        };

    private static OutputTextAudioRealtimeServerMessage TextDelta(string responseId, string text)
        => new(RealtimeServerMessageType.OutputTextDelta)
        {
            ResponseId = responseId,
            Text = text
        };

    private static OutputTextAudioRealtimeServerMessage TextDone(string responseId)
        => new(RealtimeServerMessageType.OutputTextDone)
        {
            ResponseId = responseId
        };

    private static ResponseCreatedRealtimeServerMessage ResponseDone(string responseId)
        => new(RealtimeServerMessageType.ResponseDone)
        {
            ResponseId = responseId,
            Status = RealtimeResponseStatus.Completed
        };

    private static InputAudioTranscriptionRealtimeServerMessage InputTranscriptDelta(
        string itemId,
        string text)
        => new(RealtimeServerMessageType.InputAudioTranscriptionDelta)
        {
            ItemId = itemId,
            ContentIndex = 0,
            Transcription = text
        };

    private static InputAudioTranscriptionRealtimeServerMessage InputTranscriptDone(
        string itemId,
        string text)
        => new(RealtimeServerMessageType.InputAudioTranscriptionCompleted)
        {
            ItemId = itemId,
            ContentIndex = 0,
            Transcription = text
        };

    private static int Add(int left, int right) => left + right;

    private static int Multiply(int left, int right) => left * right;

    private static int Subtract(int left, int right) => left - right;

    private static int ReadIntResult(FunctionResultContent result)
        => result.Result switch
        {
            int value => value,
            System.Text.Json.JsonElement json when json.ValueKind == System.Text.Json.JsonValueKind.Number => json.GetInt32(),
            System.Text.Json.JsonElement json when json.ValueKind == System.Text.Json.JsonValueKind.String &&
                int.TryParse(json.GetString(), out var parsed) => parsed,
            _ => throw new InvalidOperationException($"Unexpected function result payload '{result.Result}'.")
        };

    private static string ReadStringResult(FunctionResultContent result)
        => result.Result switch
        {
            string value => value,
            System.Text.Json.JsonElement json when json.ValueKind == System.Text.Json.JsonValueKind.String =>
                json.GetString() ?? string.Empty,
            null => string.Empty,
            _ => result.Result.ToString() ?? string.Empty
        };

    private static EventCapture SubscribeEvents(Agent agent)
        => new(agent);

    private static IEnumerable<TContent> SentContents<TContent>(ScriptedRealtimeSession session)
        where TContent : AIContent
        => session.Sent
            .OfType<CreateConversationItemRealtimeClientMessage>()
            .SelectMany(message => message.Item.Contents)
            .OfType<TContent>();

    private sealed class EventCapture : IDisposable
    {
        private readonly object _gate = new();
        private readonly List<AgentEvent> _events = [];
        private readonly IDisposable _subscription;

        public EventCapture(Agent agent)
        {
            _subscription = agent.SubscribeAny(evt =>
            {
                lock (_gate)
                {
                    _events.Add(evt);
                }

                return ValueTask.CompletedTask;
            });
        }

        public AgentEvent[] Snapshot()
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }

        public void Dispose()
            => _subscription.Dispose();
    }

    private sealed class FakeRealtimeClient(ScriptedRealtimeSession session) : IRealtimeClient
    {
        public Task<IRealtimeClientSession> CreateSessionAsync(
            RealtimeSessionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.Options = options;
            return Task.FromResult<IRealtimeClientSession>(session);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class ModelTurnSpyMiddleware : IAgentMiddleware
    {
        public bool WasCalled { get; private set; }

        public AgentModelTransport? Transport { get; private set; }

        public IRealtimeClient? RealtimeModel { get; private set; }

        public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];

        public int ToolCount { get; private set; }

        public IAsyncEnumerable<AgentModelUpdate>? WrapModelTurnStreamingAsync(
            AgentModelTurnRequest request,
            Func<AgentModelTurnRequest, IAsyncEnumerable<AgentModelUpdate>> handler,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            WasCalled = true;
            Transport = request.Transport;
            RealtimeModel = request.RealtimeModel;
            Messages = request.Messages;
            ToolCount = request.Options.Tools?.Count ?? 0;
            return handler(request);
        }
    }

    private sealed class ReplacingUserMessageMiddleware : IAgentMiddleware
    {
        public Task BeforeMessageTurnAsync(
            BeforeMessageTurnContext context,
            CancellationToken cancellationToken)
        {
            if (context.UserMessage is { } message)
            {
                context.UserMessage = new ChatMessage(message.Role, message.Contents.ToArray())
                {
                    AdditionalProperties = message.AdditionalProperties,
                    AuthorName = message.AuthorName,
                    CreatedAt = message.CreatedAt,
                    MessageId = message.MessageId,
                    RawRepresentation = message.RawRepresentation
                };
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedRealtimeSession(
        IReadOnlyList<IReadOnlyList<RealtimeServerMessage>> responseBatches) : IRealtimeClientSession
    {
        private int _responseBatchIndex;

        public RealtimeSessionOptions? Options { get; set; }

        public List<RealtimeClientMessage> Sent { get; } = [];

        public Task SendAsync(
            RealtimeClientMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent.Add(message);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_responseBatchIndex >= responseBatches.Count)
            {
                yield break;
            }

            foreach (var message in responseBatches[_responseBatchIndex++])
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return message;
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }
}

#pragma warning restore MEAI001
