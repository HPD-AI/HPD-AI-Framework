using HPD.Agent.Audio;
using HPD.Agent.Audio.Preemptive;
using HPD.Agent.Audio.Recognition;
using HPD.Agent.Audio.Turn;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

#pragma warning disable MEAI001

public sealed class AudioPipelineMiddlewareRuntimeInputTests : AgentTestBase
{
    [Fact]
    public async Task StartedAgent_AudioInputFrameFinal_TranscribesAndCommitsUserTextInput()
    {
        var chatClient = new FakeChatClient();
        chatClient.EnqueueTextResponse("agent response");

        var stt = new FakeSpeechToTextClient("hello from live audio");
        var middleware = new AudioPipelineMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };
        var agent = CreateAgentWithMiddlewares(
            client: chatClient,
            middlewares: [middleware]);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transcriptionCompleted = new TaskCompletionSource<TranscriptionCompletedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var eotDetected = new TaskCompletionSource<EotDetectedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recognitionFinal = new TaskCompletionSource<SpeechRecognitionFinalEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var turnCommitted = new TaskCompletionSource<UserTurnCommittedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        agent.Subscribe<MessageTurnFinishedEvent>(_ => finished.TrySetResult());
        agent.Subscribe<TranscriptionCompletedEvent>(evt =>
        {
            transcriptionCompleted.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        agent.Subscribe<EotDetectedEvent>(evt =>
        {
            eotDetected.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        agent.Subscribe<SpeechRecognitionFinalEvent>(evt =>
        {
            recognitionFinal.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        agent.Subscribe<UserTurnCommittedEvent>(evt =>
        {
            turnCommitted.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync(new AudioInputFrame(
            SessionId: null,
            BranchId: "main",
            Audio: new byte[] { 1, 2 },
            MimeType: "audio/pcm",
            TimestampNs: 0,
            IsFinal: false), TestCancellationToken);
        await agent.RunAsync(new AudioInputFrame(
            SessionId: null,
            BranchId: "main",
            Audio: new byte[] { 3, 4 },
            MimeType: "audio/pcm",
            TimestampNs: 1_000_000,
            IsFinal: true), TestCancellationToken);

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        var completed = await transcriptionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        var eot = await eotDetected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        var final = await recognitionFinal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        var committed = await turnCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal("hello from live audio", completed.FinalText);
        Assert.Equal("hello from live audio", eot.TranscribedText);
        Assert.Equal("hello from live audio", final.Transcript.Text);
        Assert.Equal("hello from live audio", committed.Transcript.Text);
        Assert.Equal(1, stt.CallCount);
        Assert.Equal([1, 2, 3, 4], stt.LastReceivedBytes);
        Assert.Single(chatClient.CapturedRequests);
        Assert.Contains(chatClient.CapturedRequests[0], message => message.Text == "hello from live audio");
    }

    [Fact]
    public async Task StartedAgent_PreflightRecognition_StartsPreemptiveGenerationCandidate()
    {
        var chatClient = new FakeChatClient();
        chatClient.EnqueueTextResponse("agent response");

        var middleware = new AudioPipelineMiddleware
        {
            SpeechRecognizer = new ScriptedSpeechRecognizer("hello early"),
            IOMode = AudioIOMode.AudioToText,
            EnablePreemptiveGeneration = true,
            PreemptiveGenerationThreshold = 0.7f
        };
        var agent = CreateAgentWithMiddlewares(
            client: chatClient,
            middlewares: [middleware]);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preemptiveStarted = new TaskCompletionSource<PreemptiveGenerationStartedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var turnCommitted = new TaskCompletionSource<UserTurnCommittedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        agent.Subscribe<MessageTurnFinishedEvent>(_ => finished.TrySetResult());
        agent.Subscribe<PreemptiveGenerationStartedEvent>(evt =>
        {
            preemptiveStarted.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        agent.Subscribe<UserTurnCommittedEvent>(evt =>
        {
            turnCommitted.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync(new AudioInputFrame(
            SessionId: null,
            BranchId: "main",
            Audio: new byte[] { 1, 2 },
            MimeType: "audio/pcm",
            TimestampNs: 0,
            IsFinal: false), TestCancellationToken);
        await agent.RunAsync(new AudioInputFrame(
            SessionId: null,
            BranchId: "main",
            Audio: new byte[] { 3, 4 },
            MimeType: "audio/pcm",
            TimestampNs: 1_000_000,
            IsFinal: true), TestCancellationToken);

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        var started = await preemptiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        var committed = await turnCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal("hello early", started.Candidate.TranscriptText);
        Assert.Equal("hello early", committed.Transcript.Text);
        Assert.Equal(started.Candidate.TranscriptRevisionId, committed.Transcript.TranscriptRevisionId);
        Assert.Single(chatClient.CapturedRequests);
        Assert.Contains(chatClient.CapturedRequests[0], message => message.Text == "hello early");
    }

    [Fact]
    public async Task StartedAgent_VadEndOfSpeech_TranscribesRunsEotAndCommitsUserTextInput()
    {
        var chatClient = new FakeChatClient();
        chatClient.EnqueueTextResponse("agent response");

        var stt = new FakeSpeechToTextClient("hello from vad.");
        var middleware = new AudioPipelineMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            Vad = new ScriptedVad(
                new VadResult { State = VadState.Starting, SpeechProbability = 0.8f, IsSpeaking = true },
                new VadResult { State = VadState.Speaking, SpeechProbability = 0.9f, IsSpeaking = true },
                new VadResult { State = VadState.Stopping, SpeechProbability = 0.2f, IsSpeaking = false }),
            EotDetector = new HPD.Agent.Audio.Eot.HeuristicEotDetector(),
            IOMode = AudioIOMode.AudioToText
        };
        var agent = CreateAgentWithMiddlewares(
            client: chatClient,
            middlewares: [middleware]);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vadStart = new TaskCompletionSource<VadStartOfSpeechEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var vadEnd = new TaskCompletionSource<VadEndOfSpeechEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var eotDetected = new TaskCompletionSource<EotDetectedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        agent.Subscribe<MessageTurnFinishedEvent>(_ => finished.TrySetResult());
        agent.Subscribe<VadStartOfSpeechEvent>(evt =>
        {
            vadStart.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        agent.Subscribe<VadEndOfSpeechEvent>(evt =>
        {
            vadEnd.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        agent.Subscribe<EotDetectedEvent>(evt =>
        {
            eotDetected.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync(new AudioInputFrame(null, "main", new byte[] { 1 }, "audio/pcm", 0, IsFinal: false), TestCancellationToken);
        await agent.RunAsync(new AudioInputFrame(null, "main", new byte[] { 2 }, "audio/pcm", 1_000_000, IsFinal: false), TestCancellationToken);
        await agent.RunAsync(new AudioInputFrame(null, "main", new byte[] { 3 }, "audio/pcm", 2_000_000, IsFinal: false), TestCancellationToken);

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        var start = await vadStart.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        var end = await vadEnd.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        var eot = await eotDetected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal(TimeSpan.Zero, start.AudioTimestamp);
        Assert.True(end.SpeechDuration > TimeSpan.Zero);
        Assert.Equal("hello from vad.", eot.TranscribedText);
        Assert.Equal("vad-end-of-speech", eot.DetectionMethod);
        Assert.Equal(1, stt.CallCount);
        Assert.Equal([1, 2, 3], stt.LastReceivedBytes);
        Assert.Single(chatClient.CapturedRequests);
        Assert.Contains(chatClient.CapturedRequests[0], message => message.Text == "hello from vad.");
    }

    [Fact]
    public async Task StartedAgent_VadStartOfSpeech_InterruptsActiveRuntimeTurn()
    {
        var chatClient = new BlockingChatClient();
        var middleware = new AudioPipelineMiddleware
        {
            Vad = new ScriptedVad(
                new VadResult { State = VadState.Starting, SpeechProbability = 0.8f, IsSpeaking = true }),
            IOMode = AudioIOMode.AudioToText
        };
        var agent = CreateAgentWithMiddlewares(
            client: chatClient,
            middlewares: [middleware]);
        var interruption = new TaskCompletionSource<InterruptionHandledEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var userInterrupted = new TaskCompletionSource<UserInterruptedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var vadStart = new TaskCompletionSource<VadStartOfSpeechEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        agent.Subscribe<InterruptionHandledEvent>((Func<InterruptionHandledEvent, ValueTask>)(evt =>
        {
            interruption.TrySetResult(evt);
            return ValueTask.CompletedTask;
        }));
        agent.Subscribe<UserInterruptedEvent>((Func<UserInterruptedEvent, ValueTask>)(evt =>
        {
            userInterrupted.TrySetResult(evt);
            return ValueTask.CompletedTask;
        }));
        agent.Subscribe<VadStartOfSpeechEvent>((Func<VadStartOfSpeechEvent, ValueTask>)(evt =>
        {
            vadStart.TrySetResult(evt);
            return ValueTask.CompletedTask;
        }));

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync("block", cancellationToken: TestCancellationToken);
        await chatClient.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        await agent.RunAsync(new AudioInputFrame(
            SessionId: null,
            BranchId: "main",
            Audio: new byte[] { 9 },
            MimeType: "audio/pcm",
            TimestampNs: 0,
            IsFinal: false), TestCancellationToken);

        await chatClient.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        var interrupt = await interruption.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await userInterrupted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await vadStart.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Null(interrupt.EventFlowId);
        Assert.Equal("vad_start_of_speech", interrupt.Reason);
        Assert.Equal(InterruptionSource.User, interrupt.Source);
    }

    [Fact]
    public async Task StartedAgent_AudioRunConfigSttOverrideClient_TranscribesRuntimeAudioInput()
    {
        var chatClient = new FakeChatClient();
        chatClient.EnqueueTextResponse("agent response");

        var stt = new FakeSpeechToTextClient("hello from override client");
        var middleware = new AudioPipelineMiddleware
        {
            IOMode = AudioIOMode.AudioToText
        };
        var agent = CreateAgentWithMiddlewares(
            client: chatClient,
            middlewares: [middleware]);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transcriptionCompleted = new TaskCompletionSource<TranscriptionCompletedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        agent.Subscribe<MessageTurnFinishedEvent>(_ => finished.TrySetResult());
        agent.Subscribe<TranscriptionCompletedEvent>(evt =>
        {
            transcriptionCompleted.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(
            new AgentRunConfig
            {
                Audio = new AudioRunConfig
                {
                    Stt = new HPD.Agent.Audio.Stt.SttConfig
                    {
                        OverrideClient = stt
                    }
                }
            },
            cancellationToken: TestCancellationToken);
        await agent.RunAsync(new AudioInputFrame(
            SessionId: null,
            BranchId: "main",
            Audio: new byte[] { 5, 6 },
            MimeType: "audio/pcm",
            TimestampNs: 0,
            IsFinal: false), TestCancellationToken);
        await agent.RunAsync(new AudioInputFrame(
            SessionId: null,
            BranchId: "main",
            Audio: new byte[] { 7, 8 },
            MimeType: "audio/pcm",
            TimestampNs: 1_000_000,
            IsFinal: true), TestCancellationToken);

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        var completed = await transcriptionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal("hello from override client", completed.FinalText);
        Assert.Equal(1, stt.CallCount);
        Assert.Equal([5, 6, 7, 8], stt.LastReceivedBytes);
        Assert.Single(chatClient.CapturedRequests);
        Assert.Contains(chatClient.CapturedRequests[0], message => message.Text == "hello from override client");
    }

    private sealed class FakeSpeechToTextClient(string result) : ISpeechToTextClient
    {
        public int CallCount { get; private set; }
        public byte[]? LastReceivedBytes { get; private set; }

        public Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            using var stream = new MemoryStream();
            audioSpeechStream.CopyTo(stream);
            LastReceivedBytes = stream.ToArray();
            return Task.FromResult(new SpeechToTextResponse(result));
        }

        public IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ScriptedSpeechRecognizer(string text) : ISpeechRecognizer
    {
        public SpeechRecognitionCapabilities Capabilities { get; } = new()
        {
            StreamingInput = true,
            InterimResults = true,
            PreflightResults = true,
            FinalResults = true
        };

        public async IAsyncEnumerable<SpeechRecognitionEvent> RecognizeAsync(
            IAsyncEnumerable<AudioInputFrame> audio,
            SpeechRecognitionOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            AudioInputFrame? firstFrame = null;
            AudioInputFrame? lastFrame = null;
            await foreach (var frame in audio.WithCancellation(cancellationToken))
            {
                firstFrame ??= frame;
                lastFrame = frame;
            }

            if (firstFrame is null)
                yield break;

            var recognitionId = Guid.NewGuid().ToString("N");
            var utteranceId = Guid.NewGuid().ToString("N");
            var revisionId = Guid.NewGuid().ToString("N");
            var startContext = CreateContext(options, recognitionId, utteranceId, firstFrame.Value);
            var finalContext = CreateContext(options, recognitionId, utteranceId, lastFrame ?? firstFrame.Value);

            yield return new SpeechRecognitionStartedEvent { Context = startContext };
            yield return new SpeechRecognitionPreflightEvent
            {
                Context = finalContext,
                Transcript = new SpeechRecognitionTranscript(
                    text,
                    Confidence: 0.95f,
                    TranscriptRevisionId: revisionId)
            };
            yield return new SpeechRecognitionFinalEvent
            {
                Context = finalContext,
                Transcript = new SpeechRecognitionTranscript(
                    text,
                    Confidence: 0.99f,
                    TranscriptRevisionId: revisionId)
            };
            yield return new SpeechRecognitionEndedEvent
            {
                Context = finalContext,
                SpeechDuration = TimeSpan.FromMilliseconds(1)
            };
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static SpeechRecognitionContext CreateContext(
            SpeechRecognitionOptions options,
            string recognitionId,
            string utteranceId,
            AudioInputFrame frame) =>
            new(
                RuntimeId: options.RuntimeId,
                SessionId: options.SessionId ?? frame.SessionId,
                BranchId: options.BranchId ?? frame.BranchId,
                UtteranceId: utteranceId,
                RecognitionId: recognitionId,
                SegmentId: null,
                ProviderRequestId: null,
                Provider: "test",
                Model: "scripted",
                SequenceNumber: frame.SequenceNumber == 0 ? null : frame.SequenceNumber,
                TimestampNs: frame.TimestampNs == 0 ? null : frame.TimestampNs,
                ObservedAt: DateTimeOffset.UtcNow);
    }

    private sealed class ScriptedVad(params VadResult[] results) : IVoiceActivityDetector
    {
        private int _index;

        public VadResult Process(AudioFrame frame)
        {
            var index = Math.Min(_index++, results.Length - 1);
            return results[index];
        }

        public async IAsyncEnumerable<VadEvent> DetectAsync(
            IAsyncEnumerable<AudioFrame> audio,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var frame in audio.WithCancellation(cancellationToken))
            {
                var result = Process(frame);
                yield return new VadEvent
                {
                    Type = result.State switch
                    {
                        VadState.Starting => VadEventType.StartOfSpeech,
                        VadState.Stopping => VadEventType.EndOfSpeech,
                        _ => VadEventType.InferenceDone
                    },
                    Timestamp = frame.Timestamp,
                    SpeechProbability = result.SpeechProbability
                };
            }
        }

        public void Reset() => _index = 0;

        public void Dispose()
        {
        }
    }

    private sealed class BlockingChatClient : IChatClient
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }

            throw new InvalidOperationException("Blocking chat client should not complete.");
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }

            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
