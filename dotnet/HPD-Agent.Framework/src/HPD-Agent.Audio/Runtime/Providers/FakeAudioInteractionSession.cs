using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Providers;

namespace HPD.Agent.Audio.Runtime.Providers;

public sealed class FakeAudioInteractionSession : IAudioInteractionSession
{
    private readonly List<AudioInteractionUpdate> _updates = [];
    private readonly RuntimeClock _clock;
    private readonly Func<InputContentRef, string> _transcriptFactory;
    private readonly string? _scriptedAssistantText;
    private readonly FakeRealtimeToolCall? _scriptedToolCall;
    private readonly List<InteractionInputToolResult> _receivedToolResults = [];
    private readonly List<AudioInteractionInput> _receivedInputs = [];

    public FakeAudioInteractionSession(
        InteractionSessionId id,
        RuntimeClock? clock = null,
        Func<InputContentRef, string>? transcriptFactory = null)
        : this(id, new FakeAudioInteractionSessionOptions
        {
            TranscriptFactory = transcriptFactory
        }, clock)
    {
    }

    public FakeAudioInteractionSession(
        InteractionSessionId id,
        FakeAudioInteractionSessionOptions? options,
        RuntimeClock? clock = null)
    {
        Id = id;
        _clock = clock ?? new RuntimeClock();
        var resolvedOptions = options ?? new FakeAudioInteractionSessionOptions();
        _transcriptFactory = resolvedOptions.TranscriptFactory
            ?? (!string.IsNullOrWhiteSpace(resolvedOptions.ScriptedTranscript)
                ? _ => resolvedOptions.ScriptedTranscript!
                : DefaultTranscriptFactory);
        _scriptedAssistantText = resolvedOptions.ScriptedAssistantText;
        _scriptedToolCall = resolvedOptions.ScriptedToolCall;
    }

    public InteractionSessionId Id { get; }

    public AudioInteractionSessionState State { get; private set; } = AudioInteractionSessionState.Created;

    public InteractionExecutionPlan Plan { get; private set; } = new()
    {
        Topology = AudioInteractionTopology.SplitSpeechToTextChatTextToSpeech,
        RouteEpoch = new ProviderRouteEpoch
        {
            Id = new ProviderRouteEpochId("fake-route-epoch"),
            ProviderKey = "fake-stt",
            StartedAt = DateTimeOffset.UnixEpoch
        },
        Capabilities = new ProviderCapabilityProfile
        {
            ProviderKey = "fake-stt",
            Declared = new ProviderDeclaredCapabilities
            {
                Flags = ProviderCapabilityFlag.SpeechToText
            }
        }
    };

    public IAsyncEnumerable<AudioInteractionUpdate> Updates => ReadUpdatesCoreAsync();

    public IReadOnlyList<InteractionInputToolResult> ReceivedToolResults => _receivedToolResults;

    public IReadOnlyList<AudioInteractionInput> ReceivedInputs => _receivedInputs;

    public ValueTask OpenAsync(InteractionExecutionPlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        State = AudioInteractionSessionState.Opening;
        Plan = plan;
        State = AudioInteractionSessionState.Active;
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync(AudioInteractionInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (State is not AudioInteractionSessionState.Active)
        {
            return ValueTask.CompletedTask;
        }

        _receivedInputs.Add(input);

        if (input is InteractionInputToolResult toolResult)
        {
            _receivedToolResults.Add(toolResult);
            return ValueTask.CompletedTask;
        }

        if (input is InteractionInputControl
            {
                Kind: RealtimeInteractionControlKinds.CreateResponse
            })
        {
            AddScriptedProviderOwnedResponse(input.Correlation);
            return ValueTask.CompletedTask;
        }

        if (input is InteractionInputMedia { Envelope.Payload: MediaPayloadRef.InputContent inputContent })
        {
            _updates.Add(new TranscriptUpdate
            {
                SessionId = Id,
                ObservedAt = _clock.Tick(),
                RouteEpochId = Plan.RouteEpoch.Id,
                Stage = TranscriptProjectionStageV1.Final,
                Text = _transcriptFactory(inputContent.Content),
                Confidence = 1.0f,
                InputContentId = inputContent.Content.Id,
                Correlation = input.Correlation
            });

            if (_scriptedToolCall is not null)
            {
                _updates.Add(new ToolCallUpdate
                {
                    SessionId = Id,
                    ObservedAt = _clock.Tick(),
                    RouteEpochId = Plan.RouteEpoch.Id,
                    ToolCallId = _scriptedToolCall.ToolCallId,
                    Name = _scriptedToolCall.Name,
                    ArgumentsDelta = _scriptedToolCall.ArgumentsJson,
                    IsFinal = true,
                    Correlation = input.Correlation
                });
            }
        }

        return ValueTask.CompletedTask;
    }

    private void AddScriptedProviderOwnedResponse(AudioCorrelation correlation)
    {
        if (string.IsNullOrWhiteSpace(_scriptedAssistantText))
        {
            return;
        }

        var responseId = new ResponseId($"{Id.Value}-response-0001");
        _updates.Add(new ResponseLifecycleUpdate
        {
            SessionId = Id,
            ObservedAt = _clock.Tick(),
            RouteEpochId = Plan.RouteEpoch.Id,
            ResponseId = responseId,
            State = ResponseLifecycleState.Created,
            Correlation = correlation
        });
        _updates.Add(new OutputTextUpdate
        {
            SessionId = Id,
            ObservedAt = _clock.Tick(),
            RouteEpochId = Plan.RouteEpoch.Id,
            ResponseId = responseId,
            Delta = _scriptedAssistantText,
            IsFinal = true,
            Correlation = correlation
        });
        _updates.Add(new ResponseLifecycleUpdate
        {
            SessionId = Id,
            ObservedAt = _clock.Tick(),
            RouteEpochId = Plan.RouteEpoch.Id,
            ResponseId = responseId,
            State = ResponseLifecycleState.Completed,
            Correlation = correlation
        });
    }

    public ValueTask<InteractionStateSnapshot> CaptureStateAsync(
        InteractionStateSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new InteractionStateSnapshot
        {
            SessionId = Id,
            State = State,
            RouteEpochId = Plan.RouteEpoch.Id,
            CapturedAt = _clock.Tick()
        });
    }

    public ValueTask<ProviderRepairResult> RepairAsync(
        ProviderRepairOperation operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ProviderRepairResult
        {
            Succeeded = false,
            Reason = "fake-repair-unsupported"
        });
    }

    public ValueTask CloseAsync(AudioStopMode mode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        State = AudioInteractionSessionState.Closed;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        State = AudioInteractionSessionState.Closed;
        return ValueTask.CompletedTask;
    }

    private async IAsyncEnumerable<AudioInteractionUpdate> ReadUpdatesCoreAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in _updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
            await Task.Yield();
        }
    }

    private static string DefaultTranscriptFactory(InputContentRef content)
    {
        if (!string.IsNullOrWhiteSpace(content.Name))
        {
            return $"transcript for {content.Name}";
        }

        return $"transcript for {content.Id.Value}";
    }
}

public sealed record FakeAudioInteractionSessionOptions
{
    public string? ScriptedTranscript { get; init; } = "hello from audio";

    public string? ScriptedAssistantText { get; init; } = "hello from realtime provider";

    public Func<InputContentRef, string>? TranscriptFactory { get; init; }

    public FakeRealtimeToolCall? ScriptedToolCall { get; init; }
}

public sealed record FakeRealtimeToolCall(
    string ToolCallId,
    string Name,
    string? ArgumentsJson);

public sealed class FakeAudioInteractionSessionFactory : IAudioInteractionSessionFactory
{
    private readonly RuntimeIdFactory _ids;
    private readonly RuntimeClock _clock;
    private readonly FakeAudioInteractionSessionOptions _options;

    public FakeAudioInteractionSessionFactory(
        RuntimeIdFactory? ids = null,
        RuntimeClock? clock = null,
        FakeAudioInteractionSessionOptions? options = null)
    {
        _ids = ids ?? new RuntimeIdFactory();
        _clock = clock ?? new RuntimeClock();
        _options = options ?? new FakeAudioInteractionSessionOptions();
    }

    public ValueTask<IAudioInteractionSession> CreateAsync(
        ProviderRouteDecision decision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IAudioInteractionSession>(
            new FakeAudioInteractionSession(_ids.NextInteractionSessionId(), _options, _clock));
    }
}
