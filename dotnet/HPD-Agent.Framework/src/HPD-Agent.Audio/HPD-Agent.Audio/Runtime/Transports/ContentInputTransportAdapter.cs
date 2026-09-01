using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Policies;
using HPD.Agent.Audio.Transports;

namespace HPD.Agent.Audio.Runtime.Transports;

internal sealed class ContentInputTransportAdapter : IAsyncDisposable
{
    private readonly List<TransportEvent> _events = [];
    private readonly RuntimeClock _clock;
    private readonly AudioPolicySet _policySet;
    private readonly AudioSessionId _sessionId;
    private bool _mediaRead;

    public ContentInputTransportAdapter(
        TransportAdapterId id,
        AudioSessionId sessionId,
        InputContentRef inputContent,
        AudioPolicySet policySet,
        RuntimeClock? clock = null)
    {
        Id = id;
        _sessionId = sessionId;
        InputContent = inputContent;
        _policySet = policySet;
        _clock = clock ?? new RuntimeClock();
    }

    public TransportAdapterId Id { get; }

    public InputContentId InputContentId => InputContent.Id;

    public InputContentRef InputContent { get; }

    public TransportAdapterState State { get; private set; } = TransportAdapterState.Created;

    public async IAsyncEnumerable<TransportEvent> ReadEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var transportEvent in _events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return transportEvent;
            await Task.Yield();
        }
    }

    public async IAsyncEnumerable<CanonicalMediaEnvelope> ReadMediaAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_mediaRead || State is not TransportAdapterState.Active)
        {
            yield break;
        }

        _mediaRead = true;

        if (_policySet.InputMedia.HandlingMode is InputMediaHandlingMode.Reject)
        {
            yield break;
        }

        yield return CreateEnvelope();
        await Task.Yield();
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetState(TransportAdapterState.Starting);
        SetState(TransportAdapterState.Active);
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetState(TransportAdapterState.Stopped);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        State = TransportAdapterState.Stopped;
        return ValueTask.CompletedTask;
    }

    private CanonicalMediaEnvelope CreateEnvelope()
    {
        var mediaType = InputContent.MediaType ?? "application/octet-stream";
        return new CanonicalMediaEnvelope
        {
            SessionId = _sessionId,
            Kind = ToMediaKind(InputContent.Kind),
            Direction = MediaDirection.Inbound,
            Payload = new MediaPayloadRef.InputContent(InputContent),
            Format = new MediaFormatDescriptor
            {
                MediaType = mediaType,
                Codec = GuessCodec(mediaType)
            },
            CaptureDisposition = _policySet.InputMedia.RetainInputMediaArtifact
                ? MediaCaptureDisposition.ArtifactRef
                : InputContent.Sha256 is not null && _policySet.InputMedia.AllowDigestCapture
                    ? MediaCaptureDisposition.DigestOnly
                    : MediaCaptureDisposition.MetadataOnly,
            Correlation = new AudioCorrelation { SessionId = _sessionId },
            Metadata = new AudioExtensionData(new Dictionary<string, object?>
            {
                ["inputContentId"] = InputContent.Id.Value,
                ["sourceKind"] = InputContent.SourceKind.ToString(),
                ["inputMediaArtifactRetained"] = _policySet.InputMedia.RetainInputMediaArtifact
            })
        };
    }

    private void SetState(TransportAdapterState state)
    {
        State = state;
        _events.Add(new TransportStateChangedEvent
        {
            AdapterId = Id,
            ObservedAt = _clock.Tick(),
            State = state,
            Correlation = new AudioCorrelation { SessionId = _sessionId }
        });
    }

    private static string? GuessCodec(string mediaType) =>
        mediaType.ToLowerInvariant() switch
        {
            "audio/mpeg" => "mp3",
            "audio/mp3" => "mp3",
            "audio/wav" => "wav",
            "audio/x-wav" => "wav",
            "audio/ogg" => "ogg",
            "audio/webm" => "webm",
            "audio/mp4" => "m4a",
            "audio/flac" => "flac",
            _ => null
        };

    private static MediaKind ToMediaKind(InputContentKind kind) =>
        kind switch
        {
            InputContentKind.Audio => MediaKind.Audio,
            InputContentKind.Image => MediaKind.Image,
            InputContentKind.Video => MediaKind.Video,
            InputContentKind.Document => MediaKind.Document,
            InputContentKind.Text => MediaKind.Text,
            _ => MediaKind.Unknown
        };

}
