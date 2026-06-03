using HPD.Agent.Audio;
using HPD.Agent.Audio.Providers;

namespace HPD.Agent.Audio.Interaction;

public interface IAudioInteractionSession : IAsyncDisposable
{
    InteractionSessionId Id { get; }

    AudioInteractionSessionState State { get; }

    InteractionExecutionPlan Plan { get; }

    IAsyncEnumerable<AudioInteractionUpdate> Updates { get; }

    ValueTask OpenAsync(InteractionExecutionPlan plan, CancellationToken cancellationToken = default);

    ValueTask SendAsync(AudioInteractionInput input, CancellationToken cancellationToken = default);

    ValueTask<InteractionStateSnapshot> CaptureStateAsync(
        InteractionStateSnapshotRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<ProviderRepairResult> RepairAsync(
        ProviderRepairOperation operation,
        CancellationToken cancellationToken = default);

    ValueTask CloseAsync(AudioStopMode mode, CancellationToken cancellationToken = default);
}

public interface IAudioInteractionSessionFactory
{
    ValueTask<IAudioInteractionSession> CreateAsync(
        ProviderRouteDecision decision,
        CancellationToken cancellationToken = default);
}

public enum AudioInteractionSessionState
{
    Created = 0,
    Opening = 1,
    Active = 2,
    Draining = 3,
    Closed = 4,
    Faulted = 5
}

public sealed record InteractionExecutionPlan
{
    public required AudioInteractionTopology Topology { get; init; }

    public required ProviderRouteEpoch RouteEpoch { get; init; }

    public required ProviderCapabilityProfile Capabilities { get; init; }

    public ProviderResponseOwnership ResponseOwnership { get; init; } = ProviderResponseOwnership.HpdChatOwnsResponse;

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public enum AudioInteractionTopology
{
    SplitSpeechToTextChatTextToSpeech = 1,
    TextOnly = 2,
    ReplayScript = 3
}

public enum ProviderResponseOwnership
{
    HpdChatOwnsResponse = 0,
    NoAssistantResponse = 2
}

public enum AudioStopMode
{
    Drain = 0,
    CancelPending = 1,
    Abort = 2
}

public sealed record InteractionStateSnapshotRequest
{
    public bool IncludeProviderState { get; init; }
}

public sealed record InteractionStateSnapshot
{
    public required InteractionSessionId SessionId { get; init; }

    public required AudioInteractionSessionState State { get; init; }

    public required ProviderRouteEpochId RouteEpochId { get; init; }

    public DateTimeOffset CapturedAt { get; init; }

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public sealed record ProviderRepairOperation
{
    public required string Kind { get; init; }

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public sealed record ProviderRepairResult
{
    public required bool Succeeded { get; init; }

    public string? Reason { get; init; }

    public AudioErrorInfo? Error { get; init; }

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}
