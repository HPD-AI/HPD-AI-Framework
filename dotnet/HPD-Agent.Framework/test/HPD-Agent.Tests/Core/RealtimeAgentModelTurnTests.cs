using System.Runtime.CompilerServices;
using HPD.Agent.Middleware;
using HPD.Agent.Middleware.Function;
using HPD.Agent.Providers;
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
            await agent.DisposeAsync();
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
    public async Task RunAsync_RealtimeTransport_SimpleText_CompletesTurnAndCommitsThreadText()
    {
        var session = new ScriptedRealtimeSession(
            [
                [
                    TextDelta("resp-final", "Hello"),
                    TextDelta("resp-final", " realtime"),
                    TextDone("resp-final")
                ]
            ]);
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var config = DefaultConfig();
        config.SessionStore = store;
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
            await agent.DisposeAsync();
        }

        var capturedEvents = capture.Snapshot();
        Assert.Contains(capturedEvents, evt => evt is TextMessageStartEvent);
        Assert.Contains(capturedEvents, evt => evt is TextMessageEndEvent);
        Assert.Contains(capturedEvents, evt => evt is AgentTurnFinishedEvent);
        Assert.Contains(capturedEvents, evt => evt is MessageTurnFinishedEvent);
        var assistantText = capturedEvents
            .OfType<TextDeltaEvent>()
            .GroupBy(evt => evt.MessageId)
            .Select(group => string.Concat(group.Select(evt => evt.Text)))
            .Last();
        Assert.Equal("Hello realtime", assistantText);

        var thread = await store.ProjectThreadAsync("session-1", "main", ThreadProjectionPurpose.ThreadHistory, TestCancellationToken);
        Assert.NotNull(thread);
        Assert.Equal("Say hello.", thread.Messages[0].Text);
        Assert.Equal("Hello realtime", thread.Messages[1].Text);
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_InputTranscription_EmitsEventsAndCommitsUserThreadText()
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
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var config = DefaultConfig();
        config.SessionStore = store;
        var agent = TestAgentFactory.Create(config, circuitBreakerThreshold: 10);
        var capture = SubscribeEvents(agent);
        var audioMessage = new ChatMessage(
            ChatRole.User,
            [new AudioContent(CreatePcm16Wav(), "audio/wav")])
        {
            MessageId = "user-audio-1"
        };

        try
        {
            await agent.CreateSessionAsync("session-transcript", cancellationToken: TestCancellationToken);
            await agent.RunAsync(
                new UserMessagesInputEvent { Messages = [audioMessage],
                    SessionId = "session-transcript",
                    ThreadId = "main",
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
            await agent.DisposeAsync();
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

        var thread = await store.ProjectThreadAsync("session-transcript", "main", ThreadProjectionPurpose.ThreadHistory, TestCancellationToken);
        var document = await store.CollectThreadEventsAsync("session-transcript", "main", TestCancellationToken);
        Assert.NotNull(document);
        Assert.Contains(document, evt => evt is TextDeltaEvent text && text.MessageId == "user-audio-1");
        Assert.NotNull(thread);
        var userMessage = Assert.Single(thread.Messages, message => message.MessageId == "user-audio-1");
        Assert.Equal(ChatRole.User, userMessage.Role);
        Assert.Equal(
            "How are you doing today?",
            Assert.Single(userMessage.Contents.OfType<TextContent>()).Text);
        Assert.Single(userMessage.Contents.OfType<UriContent>());
        Assert.Contains(thread.Messages, message => message.Role == ChatRole.Assistant && message.Text == "Doing well.");
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_InputTranscriptionAfterFinalText_CommitsUserThreadText()
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
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var config = DefaultConfig();
        config.SessionStore = store;
        var agent = TestAgentFactory.Create(config, circuitBreakerThreshold: 10);
        var capture = SubscribeEvents(agent);
        var audioMessage = new ChatMessage(
            ChatRole.User,
            [new AudioContent(CreatePcm16Wav(), "audio/wav")])
        {
            MessageId = "user-audio-after-final"
        };

        try
        {
            await agent.CreateSessionAsync("session-transcript-after-final", cancellationToken: TestCancellationToken);
            await agent.RunAsync(
                new UserMessagesInputEvent { Messages = [audioMessage],
                    SessionId = "session-transcript-after-final",
                    ThreadId = "main",
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
            await agent.DisposeAsync();
        }

        var capturedEvents = capture.Snapshot();
        Assert.Contains(capturedEvents.OfType<UserAudioTranscriptCompletedEvent>(), evt =>
            evt.MessageId == "user-audio-after-final" &&
            evt.Text == "How are you doing today?");

        var thread = await store.ProjectThreadAsync(
            "session-transcript-after-final",
            "main", ThreadProjectionPurpose.ThreadHistory,
            TestCancellationToken);
        Assert.NotNull(thread);
        var userMessage = Assert.Single(thread.Messages, message => message.MessageId == "user-audio-after-final");
        Assert.Equal(ChatRole.User, userMessage.Role);
        Assert.Equal(
            "How are you doing today?",
            Assert.Single(userMessage.Contents.OfType<TextContent>()).Text);
        Assert.Contains(thread.Messages, message => message.Role == ChatRole.Assistant && message.Text == "Doing well.");
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_InputTranscriptionAfterMiddlewareReplacement_CommitsPreparedUserThreadText()
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
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var config = DefaultConfig();
        config.SessionStore = store;
        var agent = TestAgentFactory.CreateWithMiddlewares(
            config,
            middlewares: [new ReplacingUserMessageMiddleware()],
            circuitBreakerThreshold: 10);
        var capture = SubscribeEvents(agent);
        var audioMessage = new ChatMessage(
            ChatRole.User,
            [new AudioContent(CreatePcm16Wav(), "audio/wav")]);

        try
        {
            await agent.CreateSessionAsync("session-transcript-replaced", cancellationToken: TestCancellationToken);
            await agent.RunAsync(
                new UserMessagesInputEvent { Messages = [audioMessage],
                    SessionId = "session-transcript-replaced",
                    ThreadId = "main",
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
            await agent.DisposeAsync();
        }

        var thread = await store.ProjectThreadAsync(
            "session-transcript-replaced",
            "main", ThreadProjectionPurpose.ThreadHistory,
            TestCancellationToken);
        Assert.NotNull(thread);
        var userMessage = Assert.Single(thread.Messages, message => message.Role == ChatRole.User);
        Assert.Equal(
            "How are you doing today?",
            Assert.Single(userMessage.Contents.OfType<TextContent>()).Text);

        var completed = Assert.Single(capture.Snapshot().OfType<UserAudioTranscriptCompletedEvent>());
        Assert.Equal(userMessage.MessageId, completed.MessageId);
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_TextDone_DoesNotDuplicateThreadText()
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
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var config = DefaultConfig();
        config.SessionStore = store;
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
            await agent.DisposeAsync();
        }

        var thread = await store.ProjectThreadAsync("session-dup", "main", ThreadProjectionPurpose.ThreadHistory, TestCancellationToken);
        Assert.NotNull(thread);
        Assert.Equal("The final answer is 20.", thread.Messages.Last(m => m.Role == ChatRole.Assistant).Text);
    }

    [Fact]
    public async Task RunAsync_RealtimeTransport_PersistsToolCallAndToolResultMessages()
    {
        var session = new ScriptedRealtimeSession(
            [
                [ToolCallDone("resp-add", "call-add", "Add", new Dictionary<string, object?> { ["left"] = 2, ["right"] = 3 })],
                [TextDelta("resp-final", "The answer is 5."), TextDone("resp-final")]
            ]);
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var config = DefaultConfig();
        config.SessionStore = store;
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
            await agent.DisposeAsync();
        }

        var thread = await store.ProjectThreadAsync("session-tools", "main", ThreadProjectionPurpose.ThreadHistory, TestCancellationToken);
        Assert.NotNull(thread);
        Assert.Equal(4, thread.Messages.Count);
        Assert.Equal(ChatRole.User, thread.Messages[0].Role);

        var call = Assert.Single(thread.Messages[1].Contents.OfType<FunctionCallContent>());
        Assert.Equal("call-add", call.CallId);
        Assert.Equal("Add", call.Name);

        var result = Assert.Single(thread.Messages[2].Contents.OfType<FunctionResultContent>());
        Assert.Equal("call-add", result.CallId);
        Assert.Equal(5, ReadIntResult(result));

        Assert.Equal("The answer is 5.", thread.Messages[3].Text);
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
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var config = DefaultConfig();
        config.SessionStore = store;
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
            await agent.DisposeAsync();
        }

        var sentUserTexts = session.Sent
            .OfType<CreateResponseRealtimeClientMessage>()
            .SelectMany(message => message.Items ?? [])
            .Where(item => item.Role == ChatRole.User)
            .Select(item => Assert.Single(item.Contents.OfType<TextContent>()).Text)
            .ToArray();

        Assert.Equal(["Say first done.", "Now add 10 and 7."], sentUserTexts);
        Assert.Single(SentContents<FunctionResultContent>(session), result => result.CallId == "call-add");

        var thread = await store.ProjectThreadAsync("session-multi", "main", ThreadProjectionPurpose.ThreadHistory, TestCancellationToken);
        Assert.NotNull(thread);
        Assert.Contains(thread.Messages, message => message.Text == "The answer is 17.");
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
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var config = DefaultConfig();
        config.SessionStore = store;
        var agent = TestAgentFactory.Create(config, circuitBreakerThreshold: 10);

        await agent.CreateSessionAsync("session-error", cancellationToken: TestCancellationToken);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.RunAsync(
                "Trigger provider failure.",
                "session-error",
                "main",
                runConfig: CreateRealtimeMathRunConfig(session),
                cancellationToken: TestCancellationToken));
        await agent.DisposeAsync();

        Assert.Contains("provider.error", ex.Message);

        var thread = await store.ProjectThreadAsync("session-error", "main", ThreadProjectionPurpose.ThreadHistory, TestCancellationToken);
        Assert.NotNull(thread);
        Assert.Single(thread.Messages);
        Assert.Equal("Trigger provider failure.", thread.Messages[0].Text);
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
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var config = DefaultConfig();
        config.SessionStore = store;
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
            await agent.DisposeAsync();
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

        var thread = await store.ProjectThreadAsync("session-parallel", "main", ThreadProjectionPurpose.ThreadHistory, TestCancellationToken);
        Assert.NotNull(thread);
        Assert.Equal(2, thread.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Count());
        Assert.Equal(2, thread.Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Count());
        Assert.Equal("The answers are 5 and 20.", thread.Messages.Last(m => m.Role == ChatRole.Assistant).Text);
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
            await agent.DisposeAsync();
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
            await agent.DisposeAsync();
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
            await agent.DisposeAsync();
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
            await agent.DisposeAsync();
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
            await agent.DisposeAsync();
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
            await agent.AnswerRequestAsync(
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
            await agent.DisposeAsync();
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
            await agent.DisposeAsync();
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
            await agent.DisposeAsync();
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
            await agent.DisposeAsync();
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
            Clients = new AgentClientsConfig
            {
                Transport = AgentModelTransportMode.Realtime,
                Realtime = new RealtimeClientConfig
                {
                    Override = ClientOverride<IRealtimeClient>.Borrow(new FakeRealtimeClient(session)),
                    Transcription = realtimeTranscriptionOptions is null
                        ? null
                        : new RealtimeTranscriptionRunConfig
                        {
                            ModelName = realtimeTranscriptionOptions.ModelId,
                            SpeechLanguage = realtimeTranscriptionOptions.SpeechLanguage,
                            Prompt = realtimeTranscriptionOptions.Prompt
                        }
                }
            },
            Tools = new AgentToolsRunConfig { Additional = tools, Mode = ChatToolMode.Auto },
            Streaming = new StreamingRunConfig { CoalesceDeltas = coalesceDeltas },
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

    private static byte[] CreatePcm16Wav()
    {
        const int sampleRate = 16000;
        const int channelCount = 1;
        short[] samples = [0, 1200, -1200, 0];
        var dataLength = samples.Length * sizeof(short);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channelCount);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channelCount * sizeof(short));
        writer.Write((short)(channelCount * sizeof(short)));
        writer.Write((short)16);
        writer.Write("data".ToCharArray());
        writer.Write(dataLength);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        return stream.ToArray();
    }

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
