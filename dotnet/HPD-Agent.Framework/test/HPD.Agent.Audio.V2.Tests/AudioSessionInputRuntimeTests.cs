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
        private readonly Channel<ManagedAudioTranscriptCandidateV1> _candidates = Channel.CreateUnbounded<ManagedAudioTranscriptCandidateV1>();
        internal bool CandidateRead { get; private set; }
        public string AudioSessionId => "managed-audio-1";
        internal RecordingOutputSink Output { get; } = new();
        public IAudioOutputSink? OutputSink => Output;

        internal ValueTask PublishAsync(ManagedAudioTranscriptCandidateV1 candidate) =>
            _candidates.Writer.WriteAsync(candidate);

        public async IAsyncEnumerable<ManagedAudioTranscriptCandidateV1> ReadTranscriptCandidatesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var candidate in _candidates.Reader.ReadAllAsync(cancellationToken))
            {
                CandidateRead = true;
                yield return candidate;
            }
        }

        public ValueTask SetInputEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
        public ValueTask SetOutputEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
        public ValueTask<ManagedAudioOutputInterruptionV1> InterruptOutputAsync(
            string operationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ManagedAudioOutputInterruptionV1.AlreadyIdle);
        public ValueTask StopAsync(AudioSessionStopReason reason, CancellationToken cancellationToken = default)
        {
            _candidates.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
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
