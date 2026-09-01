using HPD.Agent.Audio.AgentIntegration.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HPD.Agent.Audio.Output;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class AudioSessionInputRuntimeTests
{
    [Fact]
    public async Task AgentRunAsync_DelegatesAudioSessionCommandToConfiguredAuthority()
    {
        var authority = new RecordingSessionAuthority();
        await using var agent = await AgentBuilder.Create()
            .WithAudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
            {
                SessionControlAuthority = authority
            })
            .BuildAsync();

        await agent.StartAsync();
        var input = new AudioSessionInputEvent { Command = new AudioSessionCommand.Start() };

        var result = await agent.RunAsync(input);

        Assert.Equal(input.Command, authority.Seen?.Command);
        var audioResult = Assert.IsType<AgentInputResult.AudioSession>(result);
        Assert.Equal(new AudioSessionInputResult.Started("audio-1", 1), audioResult.Result);
    }

    [Fact]
    public async Task AgentStartAsync_ResolvesAudioSessionAuthorityFromServices()
    {
        var authority = new RecordingSessionAuthority();
        var services = new ServiceCollection()
            .AddSingleton<IAudioSessionControlAuthorityV1>(authority)
            .BuildServiceProvider();
        await using var agent = await AgentBuilder.Create()
            .WithServiceProvider(services)
            .WithAudioRuntimeAttachment()
            .BuildAsync();

        await agent.StartAsync();
        var input = new AudioSessionInputEvent
        {
            Command = new AudioSessionCommand.SetInputEnabled("audio-1", false)
        };

        var result = await agent.RunAsync(input);

        Assert.Equal(input.Command, authority.Seen?.Command);
        var audioResult = Assert.IsType<AgentInputResult.AudioSession>(result);
        Assert.Equal(new AudioSessionInputResult.Started("audio-1", 1), audioResult.Result);
    }

    [Fact]
    public async Task CommitInputTurn_AdmitsTranscriptAsNormalAgentMessage_AndAcknowledgesCandidate()
    {
        var backend = new RecordingManagedBackend();
        var authority = new ManagedAudioSessionAuthorityV1(backend);
        var chat = new CapturingChatClient();
        await using var agent = await AgentBuilder.Create()
            .WithChatClient(chat)
            .WithAudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
            {
                SessionControlAuthority = authority
            })
            .BuildAsync();
        await agent.StartAsync();
        await agent.CreateSessionAsync("session");
        var started = Assert.IsType<AgentInputResult.AudioSession>(await agent.RunAsync(
            new AudioSessionInputEvent
            {
                AgentId = "agent",
                SessionId = "session",
                ThreadId = "main",
                Command = new AudioSessionCommand.Start()
            }));
        var session = Assert.IsType<AudioSessionInputResult.Started>(started.Result);
        await backend.Session.PublishAsync(new ManagedAudioTranscriptCandidateV1
        {
            CandidateId = "candidate-1",
            Text = "hello from retained speech",
            CommitAutomatically = false
        });
        await WaitUntilAsync(() => backend.Session.CandidateRead);

        var committed = Assert.IsType<AgentInputResult.AudioSession>(await agent.RunAsync(
            new AudioSessionInputEvent
            {
                AgentId = "agent",
                SessionId = "session",
                ThreadId = "main",
                Command = new AudioSessionCommand.CommitInputTurn(
                    session.AudioSessionId, "candidate-1", session.Revision)
            }));

        Assert.IsType<AudioSessionInputResult.InputTurnCommitted>(committed.Result);
        Assert.Contains(chat.Seen, message =>
            message.Role == ChatRole.User && message.Text == "hello from retained speech");
    }

    [Fact]
    public async Task AutomaticCandidate_ReentersThroughAudioSessionInput_AndRunsAgentTurn()
    {
        var backend = new RecordingManagedBackend();
        var authority = new ManagedAudioSessionAuthorityV1(backend);
        var chat = new CapturingChatClient();
        await using var agent = await AgentBuilder.Create()
            .WithChatClient(chat)
            .WithAudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
            {
                SessionControlAuthority = authority
            })
            .BuildAsync();
        await agent.StartAsync();
        await agent.CreateSessionAsync("session");
        await agent.RunAsync(new AudioSessionInputEvent
        {
            AgentId = "agent",
            SessionId = "session",
            ThreadId = "main",
            Command = new AudioSessionCommand.Start()
        });

        await backend.Session.PublishAsync(new ManagedAudioTranscriptCandidateV1
        {
            CandidateId = "candidate-auto",
            Text = "automatic retained speech"
        });

        await WaitUntilAsync(() => chat.Seen.Any(message => message.Text == "automatic retained speech"));
    }

    [Fact]
    public async Task SpeechStarted_InterruptsOutputBeforeCommittedTranscriptIsSubmitted()
    {
        var backend = new RecordingManagedBackend();
        await using var authority = new ManagedAudioSessionAuthorityV1(backend);
        await authority.ExecuteAsync(new AudioSessionInputEvent
        {
            AgentId = "agent", SessionId = "session", ThreadId = "main",
            Command = new AudioSessionCommand.Start()
        }, null);

        await backend.Session.PublishAsync(new ManagedAudioSpeechStartedV1
        {
            ObservationId = "speech-1"
        });

        await WaitUntilAsync(() => backend.Session.Interruptions == 1);
        Assert.False(backend.Session.TranscriptCandidateRead);
    }

    [Fact]
    public async Task ManagedInterruption_IsRevisionedIdempotentAndPreservesIdleTruth()
    {
        var backend = new RecordingManagedBackend();
        await using var authority = new ManagedAudioSessionAuthorityV1(backend);
        var started = Assert.IsType<AudioSessionInputResult.Started>(await authority.ExecuteAsync(new AudioSessionInputEvent
        {
            AgentId = "agent", SessionId = "session", ThreadId = "main",
            Command = new AudioSessionCommand.Start()
        }, null));
        AudioSessionInputEvent Interrupt(long revision, string operation) => new()
        {
            AgentId = "agent", SessionId = "session", ThreadId = "main",
            Command = new AudioSessionCommand.InterruptOutput(started.AudioSessionId, revision, operation)
        };

        var conflict = await authority.ExecuteAsync(Interrupt(99, "interrupt-1"), null);
        Assert.Equal("audio-revision-conflict", Assert.IsType<AudioSessionInputResult.Rejected>(conflict).SafeCode);
        var first = Assert.IsType<AudioSessionInputResult.OutputInterrupted>(
            await authority.ExecuteAsync(Interrupt(started.Revision, "interrupt-1"), null));
        var retry = Assert.IsType<AudioSessionInputResult.OutputInterrupted>(
            await authority.ExecuteAsync(Interrupt(started.Revision, "interrupt-1"), null));
        Assert.Equal(first, retry);
        Assert.Equal(1, backend.Session.Interruptions);

        backend.Session.InterruptionResult = ManagedAudioOutputInterruptionV1.AlreadyIdle;
        var idle = Assert.IsType<AudioSessionInputResult.OutputAlreadyIdle>(
            await authority.ExecuteAsync(Interrupt(first.Revision, "interrupt-idle"), null));
        Assert.Equal(first.Revision, idle.Revision);
    }

    [Fact]
    public async Task UnknownStop_RetainsSessionAndExactRetryReconcilesBeforeDisposal()
    {
        var backend = new RecordingManagedBackend();
        backend.Session.StopUnknownOnce = true;
        await using var authority = new ManagedAudioSessionAuthorityV1(backend);
        var started = Assert.IsType<AudioSessionInputResult.Started>(await authority.ExecuteAsync(new AudioSessionInputEvent
        {
            AgentId = "agent", SessionId = "session", ThreadId = "main",
            Command = new AudioSessionCommand.Start()
        }, null));
        AudioSessionInputEvent Stop() => new()
        {
            AgentId = "agent", SessionId = "session", ThreadId = "main",
            Command = new AudioSessionCommand.Stop(started.AudioSessionId)
        };

        var unknown = Assert.IsType<AudioSessionInputResult.OutcomeUnknown>(
            await authority.ExecuteAsync(Stop(), null));
        Assert.Equal("stop-unknown", unknown.OperationId);
        Assert.False(backend.Session.Disposed);

        Assert.IsType<AudioSessionInputResult.Stopped>(await authority.ExecuteAsync(Stop(), null));
        Assert.Equal(2, backend.Session.StopCalls);
        Assert.True(backend.Session.Disposed);
    }

    [Fact]
    public async Task ManagedOutputRouter_SelectsLiveSessionByAgentSessionId()
    {
        var backend = new RecordingManagedBackend();
        await using var authority = new ManagedAudioSessionAuthorityV1(backend);
        await authority.ExecuteAsync(new AudioSessionInputEvent
        {
            AgentId = "agent",
            SessionId = "session",
            ThreadId = "main",
            Command = new AudioSessionCommand.Start()
        }, null);
        var stream = new OutputAudioStream
        {
            SessionId = "session",
            OutputFlowId = new OutputFlowId("flow-1"),
            ResponseId = new ResponseId("response-1"),
            SegmentId = new OutputSegmentId("segment-1"),
            MediaType = "audio/pcm"
        };

        var result = await authority.OutputSink.StartAsync(stream);

        Assert.Equal(OutputSinkStartDisposition.Accepted, result.Disposition);
        Assert.Same(stream, backend.Session.Output.Seen);
    }

    [Fact]
    public async Task ManagedSession_AutomaticTranscript_SynthesizesAssistantIntoItsOutputSink()
    {
        var backend = new RecordingManagedBackend();
        var authority = new ManagedAudioSessionAuthorityV1(backend);
        var textToSpeech = new RecordingTextToSpeechClient();
        await using var agent = await AgentBuilder.Create()
            .WithChatClient(new CapturingChatClient())
            .WithAudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
            {
                SessionControlAuthority = authority,
                AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.FinalText,
                AssistantOutputTextToSpeechClient = textToSpeech,
                AssistantOutputProviderKey = "recording",
                AssistantOutputModelId = "recording-tts",
                AssistantOutputFormat = "pcm_16000",
                AssistantOutputArtifactCapturePolicy = AssistantAudioArtifactCapturePolicy.Disabled,
                EnableAssistantOutputPlayback = true
            })
            .BuildAsync();
        await agent.StartAsync();
        await agent.CreateSessionAsync("session");
        await agent.RunAsync(new AudioSessionInputEvent
        {
            AgentId = "agent",
            SessionId = "session",
            ThreadId = "main",
            Command = new AudioSessionCommand.Start()
        });

        await backend.Session.PublishAsync(new ManagedAudioTranscriptCandidateV1
        {
            CandidateId = "candidate-with-output",
            Text = "speak and answer"
        });

        await WaitUntilAsync(() => textToSpeech.LastText == "heard");
        await WaitUntilAsync(() => backend.Session.Output.Seen is not null);
        Assert.Equal("session", backend.Session.Output.Seen!.SessionId);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.Fail("The expected asynchronous condition was not observed.");
    }

    private sealed class RecordingSessionAuthority : IAudioSessionControlAuthorityV1
    {
        public AudioSessionInputEvent? Seen { get; private set; }

        public ValueTask<AudioSessionInputResult> ExecuteAsync(
            AudioSessionInputEvent input,
            AgentClientSet? clientSet,
            CancellationToken cancellationToken = default)
        {
            Seen = input;
            return ValueTask.FromResult<AudioSessionInputResult>(
                new AudioSessionInputResult.Started("audio-1", 1));
        }
    }

    private sealed class RecordingManagedBackend : IManagedAudioSessionBackendV1
    {
        internal RecordingManagedSession Session { get; } = new();

        public ValueTask<IManagedAudioSessionV1> StartAsync(
            ManagedAudioSessionStartRequestV1 request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IManagedAudioSessionV1>(Session);
    }

    private sealed class RecordingManagedSession : IManagedAudioSessionV1
    {
        private readonly Channel<ManagedAudioInputObservationV1> _candidates = Channel.CreateUnbounded<ManagedAudioInputObservationV1>();
        internal bool CandidateRead { get; private set; }
        internal bool TranscriptCandidateRead { get; private set; }
        internal int Interruptions { get; private set; }
        internal ManagedAudioOutputInterruptionV1 InterruptionResult { get; set; } = ManagedAudioOutputInterruptionV1.Interrupted;
        internal bool StopUnknownOnce { get; set; }
        internal int StopCalls { get; private set; }
        internal bool Disposed { get; private set; }
        public string AudioSessionId => "managed-audio-1";
        internal RecordingOutputSink Output { get; } = new();
        public IAudioOutputSink? OutputSink => Output;

        internal ValueTask PublishAsync(ManagedAudioTranscriptCandidateV1 candidate) =>
            _candidates.Writer.WriteAsync(candidate);

        internal ValueTask PublishAsync(ManagedAudioSpeechStartedV1 observation) =>
            _candidates.Writer.WriteAsync(observation);

        public async IAsyncEnumerable<ManagedAudioInputObservationV1> ReadInputObservationsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var candidate in _candidates.Reader.ReadAllAsync(cancellationToken))
            {
                CandidateRead = true;
                if (candidate is ManagedAudioTranscriptCandidateV1)
                    TranscriptCandidateRead = true;
                yield return candidate;
            }
        }

        public ValueTask SetInputEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
        public ValueTask SetOutputEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
        public ValueTask<ManagedAudioOutputInterruptionV1> InterruptOutputAsync(
            string operationId, CancellationToken cancellationToken = default)
        {
            Interruptions++;
            return ValueTask.FromResult(InterruptionResult);
        }
        public ValueTask<ManagedAudioSessionStopResultV1> StopAsync(
            AudioSessionStopReason reason, CancellationToken cancellationToken = default)
        {
            StopCalls++;
            if (StopUnknownOnce)
            {
                StopUnknownOnce = false;
                return ValueTask.FromResult<ManagedAudioSessionStopResultV1>(
                    new ManagedAudioSessionStopResultV1.OutcomeUnknown("stop-unknown"));
            }
            _candidates.Writer.TryComplete();
            return ValueTask.FromResult<ManagedAudioSessionStopResultV1>(new ManagedAudioSessionStopResultV1.Stopped());
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _candidates.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingOutputSink : IAudioOutputSink
    {
        internal OutputAudioStream? Seen { get; private set; }

        public ValueTask<OutputSinkStartResult> StartAsync(
            OutputAudioStream stream, CancellationToken cancellationToken = default)
        {
            Seen = stream;
            return ValueTask.FromResult(new OutputSinkStartResult
            {
                OutputFlowId = stream.OutputFlowId,
                ResponseId = stream.ResponseId,
                SegmentId = stream.SegmentId,
                SegmentIndex = stream.SegmentIndex,
                Disposition = OutputSinkStartDisposition.Accepted
            });
        }
        public ValueTask WriteAsync(OutputAudioChunk chunk, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
        public ValueTask CompleteAsync(OutputAudioStreamCompletion completion, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
        public async IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(
            OutputFlowId outputFlowId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public ValueTask<OutputPlaybackBoundary> InterruptAsync(
            OutputFlowId outputFlowId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OutputPlaybackBoundary
            {
                OutputFlowId = outputFlowId,
                ResponseId = new ResponseId("response-1"),
                PlayedTextLength = 0
            });
        public ValueTask FlushAsync(OutputFlowId outputFlowId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingTextToSpeechClient : ITextToSpeechClient
    {
        internal string? LastText { get; private set; }

        public Task<TextToSpeechResponse> GetAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastText = text;
            return Task.FromResult(new TextToSpeechResponse([
                new DataContent(new byte[] { 1, 0, 2, 0 }, "audio/pcm")
            ]));
        }

        public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetAudioAsync(text, options, cancellationToken);
            yield return new TextToSpeechResponseUpdate(response.Contents)
            {
                Kind = TextToSpeechResponseUpdateKind.AudioUpdated
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(TextToSpeechClientMetadata)
                ? new TextToSpeechClientMetadata("recording", null, "recording-tts")
                : null;

        public void Dispose() { }
    }

    private sealed class CapturingChatClient : IChatClient
    {
        private readonly object _gate = new();
        private readonly List<ChatMessage> _seen = [];
        internal IReadOnlyList<ChatMessage> Seen { get { lock (_gate) return _seen.ToArray(); } }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            lock (_gate) _seen.AddRange(chatMessages);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "heard")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            lock (_gate) _seen.AddRange(chatMessages);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "heard");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
