using System.Runtime.CompilerServices;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Core;

#pragma warning disable MEAI001

public sealed class RealtimeModelTurnExecutorTests
{
    [Fact]
    public async Task RunAsync_SendsMessagesAndResponseRequest()
    {
        var session = new FakeRealtimeSession();
        session.ServerMessages.Enqueue(new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
        {
            ResponseId = "resp-1",
            Status = RealtimeResponseStatus.Completed
        });
        var executor = new RealtimeModelTurnExecutor();
        var request = CreateRequest(session);

        var updates = await ReadUpdatesAsync(executor.RunAsync(request));

        Assert.Collection(
            session.Sent,
            message =>
            {
                var createResponse = Assert.IsType<CreateResponseRealtimeClientMessage>(message);
                Assert.Equal("Be precise.", createResponse.Instructions);
                var item = Assert.Single(createResponse.Items!);
                Assert.Equal(ChatRole.User, item.Role);
                Assert.Equal("Use math.", Assert.Single(item.Contents.OfType<TextContent>()).Text);
            });

        var lifecycle = Assert.IsType<AgentResponseLifecycleUpdate>(Assert.Single(updates));
        Assert.Equal(AgentModelResponseState.Completed, lifecycle.State);
        Assert.Equal("resp-1", lifecycle.ResponseId);
    }

    [Fact]
    public async Task RunAsync_SendsCompatibleAudioThroughInputBufferAndTextThroughResponseItems()
    {
        var session = new FakeRealtimeSession();
        session.ServerMessages.Enqueue(new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
        {
            ResponseId = "resp-1",
            Status = RealtimeResponseStatus.Completed
        });
        var executor = new RealtimeModelTurnExecutor();
        var request = CreateRequest(
            session,
            [
                new ChatMessage(
                    ChatRole.User,
                    [
                        new TextContent("Please answer this audio."),
                        new AudioContent(new byte[] { 1, 2, 3, 4 }, "audio/pcm;rate=16000")
                    ])
                {
                    MessageId = "audio-user-1"
                }
            ]);

        _ = await ReadUpdatesAsync(executor.RunAsync(request));

        Assert.NotNull(session.Options?.InputAudioFormat);
        Assert.Equal("audio/pcm", session.Options.InputAudioFormat.MediaType);
        Assert.Equal(16000, session.Options.InputAudioFormat.SampleRate);
        Assert.Collection(
            session.Sent,
            message =>
            {
                var append = Assert.IsType<InputAudioBufferAppendRealtimeClientMessage>(message);
                Assert.Equal("audio/pcm", append.Content.MediaType);
                Assert.Equal([1, 2, 3, 4], append.Content.Data.ToArray());
            },
            message => Assert.IsType<InputAudioBufferCommitRealtimeClientMessage>(message),
            message =>
            {
                var createResponse = Assert.IsType<CreateResponseRealtimeClientMessage>(message);
                var item = Assert.Single(createResponse.Items!);
                Assert.Equal("audio-user-1", item.Id);
                Assert.Equal(ChatRole.User, item.Role);
                Assert.Equal("Please answer this audio.", Assert.Single(item.Contents.OfType<TextContent>()).Text);
                Assert.Empty(item.Contents.OfType<AudioContent>());
                Assert.Empty(item.Contents.OfType<DataContent>());
            });
    }

    [Fact]
    public async Task RunAsync_RejectsEncodedInputAudioForNativeRealtime()
    {
        var session = new FakeRealtimeSession();
        var executor = new RealtimeModelTurnExecutor();
        var request = CreateRequest(
            session,
            [
                new ChatMessage(
                    ChatRole.User,
                    [
                        new AudioContent(new byte[] { 1, 2, 3 }, MimeTypeRegistry.AudioMpeg)
                    ])
            ]);

        var error = await Assert.ThrowsAsync<NotSupportedException>(
            () => ReadUpdatesAsync(executor.RunAsync(request)));

        Assert.Contains("requires audio/pcm", error.Message);
        Assert.Empty(session.Sent);
    }

    [Fact]
    public async Task RunAsync_MapsRealtimeTextAudioAndFinalToolUpdate()
    {
        var session = new FakeRealtimeSession();
        session.ServerMessages.Enqueue(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDelta)
        {
            ResponseId = "resp-text",
            Text = "hello"
        });
        session.ServerMessages.Enqueue(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputAudioDelta)
        {
            ResponseId = "resp-audio",
            Audio = Convert.ToBase64String([1, 2, 3])
        });
        session.ServerMessages.Enqueue(new ResponseOutputItemRealtimeServerMessage(RealtimeServerMessageType.ResponseOutputItemDone)
        {
            ResponseId = "resp-tool",
            Item = new RealtimeConversationItem(
                [
                    new FunctionCallContent(
                        "call-add",
                        "Add",
                        new Dictionary<string, object?> { ["left"] = 2, ["right"] = 3 })
                ])
        });
        var executor = new RealtimeModelTurnExecutor();
        var request = CreateRequest(session);

        var updates = await ReadUpdatesAsync(executor.RunAsync(request));

        var text = Assert.IsType<AgentTextDeltaUpdate>(updates[0]);
        Assert.Equal("hello", text.Text);
        Assert.Equal("resp-text", text.ResponseId);

        var audio = Assert.IsType<AgentAudioDeltaUpdate>(updates[1]);
        Assert.Equal([1, 2, 3], audio.Audio.ToArray());
        Assert.Equal("resp-audio", audio.ResponseId);

        var tool = Assert.IsType<AgentToolCallUpdate>(updates[2]);
        Assert.True(tool.IsFinal);
        Assert.Equal("resp-tool", tool.ResponseId);
        Assert.Equal("call-add", tool.Call.CallId);
        Assert.Equal("Add", tool.Call.Name);
        Assert.Equal(3, updates.Count);
    }

    [Fact]
    public async Task RunAsync_MapsRealtimeInputTranscriptionUpdates()
    {
        var session = new FakeRealtimeSession();
        session.ServerMessages.Enqueue(new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionDelta)
        {
            ItemId = "item-user",
            ContentIndex = 0,
            Transcription = "hello"
        });
        session.ServerMessages.Enqueue(new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionCompleted)
        {
            ItemId = "item-user",
            ContentIndex = 0,
            Transcription = "hello there"
        });
        session.ServerMessages.Enqueue(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDone)
        {
            ResponseId = "resp-final"
        });
        var transcriptionOptions = new TranscriptionOptions
        {
            ModelId = "whisper-1",
            SpeechLanguage = "en"
        };
        var executor = new RealtimeModelTurnExecutor();
        var request = CreateRequest(session) with
        {
            RunConfig = new AgentRunConfig
            {
                Clients = new AgentClientsConfig { Realtime = new RealtimeClientConfig
                {
                    Transcription = new RealtimeTranscriptionRunConfig
                    {
                        ModelName = transcriptionOptions.ModelId,
                        SpeechLanguage = transcriptionOptions.SpeechLanguage,
                        Prompt = transcriptionOptions.Prompt
                    }
                } }
            }
        };

        var updates = await ReadUpdatesAsync(executor.RunAsync(request));

        Assert.Equal(transcriptionOptions.ModelId, session.Options?.TranscriptionOptions?.ModelId);
        Assert.Equal(transcriptionOptions.SpeechLanguage, session.Options?.TranscriptionOptions?.SpeechLanguage);
        Assert.Collection(
            updates,
            update =>
            {
                var transcript = Assert.IsType<AgentInputTranscriptUpdate>(update);
                Assert.Equal(AgentInputTranscriptStage.Partial, transcript.Stage);
                Assert.Equal("hello", transcript.Text);
                Assert.Equal("item-user", transcript.ItemId);
                Assert.Equal(0, transcript.ContentIndex);
            },
            update =>
            {
                var transcript = Assert.IsType<AgentInputTranscriptUpdate>(update);
                Assert.Equal(AgentInputTranscriptStage.Final, transcript.Stage);
                Assert.Equal("hello there", transcript.Text);
                Assert.True(transcript.IsFinal);
            },
            update =>
            {
                var text = Assert.IsType<AgentTextDeltaUpdate>(update);
                Assert.True(text.IsFinal);
            });
    }

    [Fact]
    public async Task RunAsync_TextDone_KeepsReadingUntilResponseDoneAndCapturesInputTranscript()
    {
        var session = new FakeRealtimeSession();
        session.ServerMessages.Enqueue(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDone)
        {
            ResponseId = "resp-final"
        });
        session.ServerMessages.Enqueue(new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionCompleted)
        {
            ItemId = "item-user",
            ContentIndex = 0,
            Transcription = "How are you doing today?"
        });
        session.ServerMessages.Enqueue(ResponseDone("resp-final"));
        var executor = new RealtimeModelTurnExecutor();
        var request = CreateRequest(session);

        var updates = await ReadUpdatesAsync(executor.RunAsync(request));

        Assert.Collection(
            updates,
            update =>
            {
                var text = Assert.IsType<AgentTextDeltaUpdate>(update);
                Assert.True(text.IsFinal);
            },
            update =>
            {
                var transcript = Assert.IsType<AgentInputTranscriptUpdate>(update);
                Assert.Equal(AgentInputTranscriptStage.Final, transcript.Stage);
                Assert.Equal("How are you doing today?", transcript.Text);
                Assert.Equal("item-user", transcript.ItemId);
            },
            update =>
            {
                var lifecycle = Assert.IsType<AgentResponseLifecycleUpdate>(update);
                Assert.Equal(AgentModelResponseState.Completed, lifecycle.State);
                Assert.Equal("resp-final", lifecycle.ResponseId);
            });
    }

    [Fact]
    public async Task RunAsync_ResponseDone_DrainsLateInputTranscriptForInputAudio()
    {
        var session = new FakeRealtimeSession();
        session.ServerMessages.Enqueue(new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
        {
            ResponseId = "resp-final",
            Status = RealtimeResponseStatus.Completed
        });
        session.ServerMessages.Enqueue(new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionCompleted)
        {
            ItemId = "audio-user-1",
            ContentIndex = 0,
            Transcription = "How are you doing today?"
        });
        var executor = new RealtimeModelTurnExecutor();
        var request = CreateRequest(
            session,
            [
                new ChatMessage(
                    ChatRole.User,
                    [new AudioContent(new byte[] { 1, 2, 3, 4 }, "audio/pcm;rate=16000")])
                {
                    MessageId = "audio-user-1"
                }
            ],
            new AgentRunConfig
            {
                Clients = new AgentClientsConfig { Realtime = new RealtimeClientConfig
                {
                    Transcription = new RealtimeTranscriptionRunConfig { ModelName = "whisper-1" }
                } }
            });

        var updates = await ReadUpdatesAsync(executor.RunAsync(request));

        Assert.Collection(
            updates,
            update =>
            {
                var lifecycle = Assert.IsType<AgentResponseLifecycleUpdate>(update);
                Assert.Equal(AgentModelResponseState.Completed, lifecycle.State);
            },
            update =>
            {
                var transcript = Assert.IsType<AgentInputTranscriptUpdate>(update);
                Assert.Equal(AgentInputTranscriptStage.Final, transcript.Stage);
                Assert.Equal("How are you doing today?", transcript.Text);
                Assert.Equal("audio-user-1", transcript.ItemId);
            });
    }

    [Fact]
    public async Task RunAsync_MapsRealtimeInputTranscriptionFailure()
    {
        var session = new FakeRealtimeSession();
        session.ServerMessages.Enqueue(new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionFailed)
        {
            ItemId = "item-user",
            Error = new ErrorContent("could not transcribe")
            {
                ErrorCode = "transcription.failed"
            }
        });
        var executor = new RealtimeModelTurnExecutor();
        var request = CreateRequest(session);

        var updates = await ReadUpdatesAsync(executor.RunAsync(request));

        var transcript = Assert.IsType<AgentInputTranscriptUpdate>(Assert.Single(updates));
        Assert.Equal(AgentInputTranscriptStage.Failed, transcript.Stage);
        Assert.Equal("item-user", transcript.ItemId);
        Assert.Contains("transcription.failed", transcript.Error?.Message);
    }

    [Fact]
    public async Task RunAsync_MapsRealtimeErrorUpdate()
    {
        var session = new FakeRealtimeSession();
        session.ServerMessages.Enqueue(new ErrorRealtimeServerMessage
        {
            OriginatingMessageId = "client-msg-1",
            Error = new ErrorContent("provider failed")
            {
                ErrorCode = "provider.error"
            }
        });
        var executor = new RealtimeModelTurnExecutor();
        var request = CreateRequest(session);

        var updates = await ReadUpdatesAsync(executor.RunAsync(request));

        var error = Assert.IsType<AgentResponseLifecycleUpdate>(Assert.Single(updates));
        Assert.Equal(AgentModelResponseState.Failed, error.State);
        Assert.Equal("client-msg-1", error.ResponseId);
        Assert.Contains("provider.error", error.Error?.Message);
    }

    [Fact]
    public async Task RunAsync_FinalToolCall_ReturnsControlWithoutWaitingForProviderCompletion()
    {
        var session = new HangingAfterToolRealtimeSession();
        var executor = new RealtimeModelTurnExecutor();
        var request = CreateRequest(session);

        var updates = await ReadUpdatesAsync(executor.RunAsync(request))
            .WaitAsync(TimeSpan.FromSeconds(2));

        var tool = Assert.IsType<AgentToolCallUpdate>(Assert.Single(updates));
        Assert.True(tool.IsFinal);
        Assert.Equal("call-add", tool.Call.CallId);
        Assert.False(session.ContinuationReached);
    }

    [Fact]
    public async Task RunAsync_TextDone_DoesNotDuplicatePriorTextDelta()
    {
        var session = new FakeRealtimeSession();
        session.ServerMessages.Enqueue(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDelta)
        {
            ResponseId = "resp-final",
            Text = "The final answer is 20."
        });
        session.ServerMessages.Enqueue(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDone)
        {
            ResponseId = "resp-final",
            Text = "The final answer is 20."
        });
        var executor = new RealtimeModelTurnExecutor();
        var request = CreateRequest(session);

        var updates = await ReadUpdatesAsync(executor.RunAsync(request));

        Assert.Collection(
            updates,
            update =>
            {
                var text = Assert.IsType<AgentTextDeltaUpdate>(update);
                Assert.False(text.IsFinal);
                Assert.Equal("The final answer is 20.", text.Text);
            },
            update =>
            {
                var text = Assert.IsType<AgentTextDeltaUpdate>(update);
                Assert.True(text.IsFinal);
                Assert.Empty(text.Text);
            });
    }

    [Fact]
    public async Task RunAsync_ExistingSession_SendsOnlyNewUserMessages()
    {
        var session = new FakeRealtimeSession();
        session.ServerMessages.Enqueue(new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
        {
            ResponseId = "resp-1",
            Status = RealtimeResponseStatus.Completed
        });
        session.ServerMessages.Enqueue(new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
        {
            ResponseId = "resp-2",
            Status = RealtimeResponseStatus.Completed
        });
        var executor = new RealtimeModelTurnExecutor();
        var firstUser = new ChatMessage(ChatRole.User, "Use math.")
        {
            MessageId = "user-1"
        };
        var request = CreateRequest(session, [firstUser]);

        _ = await ReadUpdatesAsync(executor.RunAsync(request));
        session.Sent.Clear();

        var priorAssistant = new ChatMessage(
            ChatRole.Assistant,
            [
                new FunctionCallContent(
                    "call-add",
                    "Add",
                    new Dictionary<string, object?> { ["left"] = 2, ["right"] = 3 })
            ])
        {
            MessageId = "assistant-1"
        };
        var priorTool = new ChatMessage(
            ChatRole.Tool,
            [
                new FunctionResultContent("call-add", 5)
            ])
        {
            MessageId = "tool-1"
        };
        var secondUser = new ChatMessage(ChatRole.User, "Now add 10 and 7.")
        {
            MessageId = "user-2"
        };
        var followUpRequest = CreateRequest(
            session,
            [firstUser, priorAssistant, priorTool, secondUser]);

        _ = await ReadUpdatesAsync(executor.RunAsync(followUpRequest));

        var sentResponseRequests = session.Sent
            .OfType<CreateResponseRealtimeClientMessage>()
            .ToList();

        var sentResponse = Assert.Single(sentResponseRequests);
        var sentItem = Assert.Single(sentResponse.Items!);
        Assert.Equal(ChatRole.User, sentItem.Role);
        Assert.Equal("Now add 10 and 7.", Assert.Single(sentItem.Contents.OfType<TextContent>()).Text);
    }

    [Fact]
    public async Task SubmitToolResultsAsync_SendsRealtimeFunctionResultAndCreatesNextResponse()
    {
        var session = new FakeRealtimeSession();
        var executor = new RealtimeModelTurnExecutor();
        var request = CreateRequest(session);
        _ = await ReadUpdatesAsync(executor.RunAsync(request));
        session.Sent.Clear();

        await executor.SubmitToolResultsAsync(
            [new FunctionResultContent("call-add", 5)],
            request);

        Assert.Collection(
            session.Sent,
            message =>
            {
                var createItem = Assert.IsType<CreateConversationItemRealtimeClientMessage>(message);
                var result = Assert.Single(createItem.Item.Contents.OfType<FunctionResultContent>());
                Assert.Equal("call-add", result.CallId);
                Assert.Equal(5, result.Result);
            },
            message => Assert.IsType<CreateResponseRealtimeClientMessage>(message));
    }

    [Fact]
    public async Task SubmitToolResultsAsync_TwoConsecutiveCycles_DoesNotDuplicateResponseRequests()
    {
        var session = new FakeRealtimeSession();
        session.ServerMessages.Enqueue(ToolCallDone("resp-add-1", "call-add-1"));
        session.ServerMessages.Enqueue(ToolCallDone("resp-add-2", "call-add-2"));
        var executor = new RealtimeModelTurnExecutor();
        var request = CreateRequest(session);

        var firstUpdates = await ReadUpdatesAsync(executor.RunAsync(request));
        var firstTool = Assert.IsType<AgentToolCallUpdate>(Assert.Single(firstUpdates));
        Assert.Equal("call-add-1", firstTool.Call.CallId);
        Assert.Equal(1, session.Sent.OfType<CreateResponseRealtimeClientMessage>().Count());

        await executor.SubmitToolResultsAsync(
            [new FunctionResultContent("call-add-1", 5)],
            request);
        Assert.Equal(2, session.Sent.OfType<CreateResponseRealtimeClientMessage>().Count());

        var sentCountBeforeSecondRun = session.Sent.Count;
        var secondUpdates = await ReadUpdatesAsync(executor.RunAsync(request));
        var secondTool = Assert.IsType<AgentToolCallUpdate>(Assert.Single(secondUpdates));
        Assert.Equal("call-add-2", secondTool.Call.CallId);
        Assert.Equal(sentCountBeforeSecondRun, session.Sent.Count);

        await executor.SubmitToolResultsAsync(
            [new FunctionResultContent("call-add-2", 7)],
            request);

        var resultItems = session.Sent
            .OfType<CreateConversationItemRealtimeClientMessage>()
            .SelectMany(message => message.Item.Contents)
            .OfType<FunctionResultContent>()
            .Select(result => result.CallId)
            .ToArray();

        Assert.Equal(["call-add-1", "call-add-2"], resultItems);
        Assert.Equal(3, session.Sent.OfType<CreateResponseRealtimeClientMessage>().Count());
    }

    private static AgentModelTurnRequest CreateRequest(
        IRealtimeClientSession session,
        IReadOnlyList<ChatMessage>? messages = null,
        AgentRunConfig? runConfig = null)
    {
        messages ??= new List<ChatMessage>
        {
            new(ChatRole.User, "Use math.")
        };

        return new AgentModelTurnRequest
        {
            Transport = AgentModelTransport.Realtime,
            RealtimeModel = new FakeRealtimeClient(session),
            Messages = messages,
            Options = new ChatOptions
            {
                Instructions = "Be precise."
            },
            RunConfig = runConfig,
            State = AgentLoopState.InitialSafe(
                messages,
                "run-realtime",
                "conversation-realtime",
                "RealtimeTestAgent"),
            Iteration = 0
        };
    }

    private static async Task<List<AgentModelUpdate>> ReadUpdatesAsync(
        IAsyncEnumerable<AgentModelUpdate> updates)
    {
        var result = new List<AgentModelUpdate>();
        await foreach (var update in updates)
        {
            result.Add(update);
        }

        return result;
    }

    private static ResponseOutputItemRealtimeServerMessage ToolCallDone(string responseId, string callId)
        => new(RealtimeServerMessageType.ResponseOutputItemDone)
        {
            ResponseId = responseId,
            Item = new RealtimeConversationItem(
                [
                    new FunctionCallContent(
                        callId,
                        "Add",
                        new Dictionary<string, object?> { ["left"] = 2, ["right"] = 3 })
                    ])
        };

    private static ResponseCreatedRealtimeServerMessage ResponseDone(string responseId)
        => new(RealtimeServerMessageType.ResponseDone)
        {
            ResponseId = responseId,
            Status = RealtimeResponseStatus.Completed
        };

    private sealed class FakeRealtimeClient(IRealtimeClientSession session) : IRealtimeClient
    {
        public Task<IRealtimeClientSession> CreateSessionAsync(
            RealtimeSessionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (session is FakeRealtimeSession fakeSession)
            {
                fakeSession.Options = options;
            }
            else if (session is HangingAfterToolRealtimeSession hangingSession)
            {
                hangingSession.Options = options;
            }

            return Task.FromResult<IRealtimeClientSession>(session);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class FakeRealtimeSession : IRealtimeClientSession
    {
        public RealtimeSessionOptions? Options { get; set; }

        public List<RealtimeClientMessage> Sent { get; } = [];

        public Queue<RealtimeServerMessage> ServerMessages { get; } = [];

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
            while (ServerMessages.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return ServerMessages.Dequeue();
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class HangingAfterToolRealtimeSession : IRealtimeClientSession
    {
        public RealtimeSessionOptions? Options { get; set; }

        public bool ContinuationReached { get; private set; }

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
            yield return new ResponseOutputItemRealtimeServerMessage(RealtimeServerMessageType.ResponseOutputItemDone)
            {
                ResponseId = "resp-tool",
                Item = new RealtimeConversationItem(
                    [
                        new FunctionCallContent(
                            "call-add",
                            "Add",
                            new Dictionary<string, object?> { ["left"] = 2, ["right"] = 3 })
                    ])
            };

            ContinuationReached = true;
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

}

#pragma warning restore MEAI001
