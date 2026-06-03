using HPD.Agent.Audio;
using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Audio.Meai;

#pragma warning disable EXTEXP0001

public sealed class MeaiBatchSpeechToTextInteractionSession : IAudioInteractionSession
{
    private readonly ISpeechToTextClient _client;
    private readonly IInputContentSourceResolver _sourceResolver;
    private readonly MeaiBatchSpeechToTextInteractionSessionOptions _options;
    private readonly List<AudioInteractionUpdate> _updates = [];
    private bool _disposed;

    public MeaiBatchSpeechToTextInteractionSession(
        InteractionSessionId id,
        ISpeechToTextClient client,
        IInputContentSourceResolver sourceResolver,
        MeaiBatchSpeechToTextInteractionSessionOptions? options = null)
    {
        Id = id;
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sourceResolver = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));
        _options = options ?? new MeaiBatchSpeechToTextInteractionSessionOptions();
    }

    public InteractionSessionId Id { get; }

    public AudioInteractionSessionState State { get; private set; } = AudioInteractionSessionState.Created;

    public InteractionExecutionPlan Plan { get; private set; } = new()
    {
        Topology = AudioInteractionTopology.SplitSpeechToTextChatTextToSpeech,
        RouteEpoch = new ProviderRouteEpoch
        {
            Id = new ProviderRouteEpochId("meai-stt-route-epoch"),
            ProviderKey = "meai-stt",
            StartedAt = DateTimeOffset.UnixEpoch
        },
        Capabilities = new ProviderCapabilityProfile
        {
            ProviderKey = "meai-stt",
            Declared = new ProviderDeclaredCapabilities
            {
                Flags = ProviderCapabilityFlag.SpeechToText
            }
        }
    };

    public IAsyncEnumerable<AudioInteractionUpdate> Updates => ReadUpdatesCoreAsync();

    public ValueTask OpenAsync(InteractionExecutionPlan plan, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        State = AudioInteractionSessionState.Opening;
        Plan = plan;
        State = AudioInteractionSessionState.Active;

        return ValueTask.CompletedTask;
    }

    public async ValueTask SendAsync(
        AudioInteractionInput input,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        if (State is not AudioInteractionSessionState.Active)
        {
            return;
        }

        if (Plan.Topology is not AudioInteractionTopology.SplitSpeechToTextChatTextToSpeech)
        {
            _updates.Add(CreateError(
                "meai-stt.unsupported-plan",
                $"MEAI batch STT cannot execute interaction topology '{Plan.Topology}'.",
                input.Correlation,
                isRetryable: false));
            return;
        }

        if (input is not InteractionInputMedia audioInput)
        {
            _updates.Add(CreateError(
                "meai-stt.unsupported-input",
                $"MEAI batch STT only accepts input content audio input, not '{input.GetType().Name}'.",
                input.Correlation,
                isRetryable: false));
            return;
        }

        if (audioInput.Envelope.Payload is not MediaPayloadRef.InputContent inputContent)
        {
            _updates.Add(CreateError(
                "meai-stt.unreadable-source",
                $"Input media payload is not readable by the MEAI batch STT adapter: {audioInput.Envelope.Payload.GetType().Name}.",
                input.Correlation,
                isRetryable: false));
            return;
        }

        await TranscribeAsync(
            inputContent.Content,
            audioInput.Envelope,
            input.Correlation,
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<InteractionStateSnapshot> CaptureStateAsync(
        InteractionStateSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new InteractionStateSnapshot
        {
            SessionId = Id,
            State = State,
            RouteEpochId = Plan.RouteEpoch.Id,
            CapturedAt = DateTimeOffset.UtcNow
        });
    }

    public ValueTask<ProviderRepairResult> RepairAsync(
        ProviderRepairOperation operation,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ProviderRepairResult
        {
            Succeeded = false,
            Reason = "meai-batch-stt-repair-unsupported"
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
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        State = AudioInteractionSessionState.Closed;

        if (_options.DisposeClient)
        {
            _client.Dispose();
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private async ValueTask TranscribeAsync(
        InputContentRef inputContent,
        CanonicalMediaEnvelope envelope,
        AudioCorrelation correlation,
        CancellationToken cancellationToken)
    {
        InputContentSourceOpenResult openResult;
        try
        {
            openResult = await _sourceResolver.OpenAsync(
                inputContent,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _updates.Add(CreateError(
                "meai-stt.resolver-exception",
                $"Input media source resolution failed: {exception.Message}",
                correlation,
                isRetryable: true));
            return;
        }

        if (openResult.Status is not InputContentSourceOpenStatus.Opened ||
            openResult.Source is null)
        {
            _updates.Add(CreateError(
                $"meai-stt.resolver-{openResult.Status.ToString().ToLowerInvariant()}",
                openResult.Reason ?? "Input media source could not be opened.",
                correlation,
                isRetryable: openResult.Status is InputContentSourceOpenStatus.Failed));
            return;
        }

        try
        {
            await using var stream = await openResult.Source.OpenStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var response = await _client.GetTextAsync(
                    stream,
                    CreateSpeechToTextOptions(envelope),
                    cancellationToken)
                .ConfigureAwait(false);

            var transcript = response.Text;
            if (string.IsNullOrWhiteSpace(transcript) && _options.TreatEmptyTranscriptAsError)
            {
                _updates.Add(CreateError(
                    "meai-stt.empty-transcript",
                    "MEAI speech-to-text returned an empty transcript.",
                    correlation,
                    isRetryable: false));
                return;
            }

            _updates.Add(new TranscriptUpdate
            {
                SessionId = Id,
                ObservedAt = DateTimeOffset.UtcNow,
                RouteEpochId = Plan.RouteEpoch.Id,
                Stage = TranscriptStage.Final,
                Text = transcript,
                InputContentId = inputContent.Id,
                Correlation = correlation
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _updates.Add(CreateError(
                "meai-stt.transcription-exception",
                "MEAI speech-to-text failed",
                correlation,
                isRetryable: true,
                exception));
        }
    }

    private SpeechToTextOptions CreateSpeechToTextOptions(CanonicalMediaEnvelope envelope)
    {
        var additionalProperties = _options.AdditionalProperties is null
            ? new AdditionalPropertiesDictionary()
            : new AdditionalPropertiesDictionary(_options.AdditionalProperties);

        AddIfNotNull(additionalProperties, "prompt", _options.Prompt);
        AddIfNotNull(additionalProperties, "temperature", _options.Temperature);
        AddIfNotNull(additionalProperties, "responseFormat", _options.ResponseFormat);
        AddIfNotNull(additionalProperties, "timestampGranularities", _options.TimestampGranularities);
        AddIfNotNull(additionalProperties, "includeLogprobs", _options.IncludeLogprobs);

        return new SpeechToTextOptions
        {
            ModelId = _options.ModelId,
            SpeechLanguage = _options.SpeechLanguage,
            TextLanguage = _options.TextLanguage,
            SpeechSampleRate = _options.SpeechSampleRate ?? envelope.Format.SampleRateHz,
            AdditionalProperties = additionalProperties.Count == 0 ? null : additionalProperties,
            RawRepresentationFactory = _options.RawRepresentationFactory
        };
    }

    private static void AddIfNotNull(
        AdditionalPropertiesDictionary properties,
        string key,
        object? value)
    {
        if (value is not null)
        {
            properties[key] = value;
        }
    }

    private ProviderErrorUpdate CreateError(
        string code,
        string message,
        AudioCorrelation correlation,
        bool isRetryable,
        Exception? exception = null)
        => new()
        {
            SessionId = Id,
            ObservedAt = DateTimeOffset.UtcNow,
            RouteEpochId = Plan.RouteEpoch.Id,
            Correlation = correlation,
            Error = exception is null
                ? new AudioErrorInfo
            {
                Code = code,
                Message = message,
                Category = "speech-to-text",
                IsRetryable = isRetryable
            }
                : MeaiAudioErrorMapper.FromException(
                    exception,
                    _options.ProviderErrorHandler,
                    code,
                    message,
                    "speech-to-text",
                    isRetryable)
        };

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
}

#pragma warning restore EXTEXP0001
