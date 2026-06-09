namespace HPD.Agent.Audio.Policies;

public sealed record InputMediaPolicy
{
    public InputMediaHandlingMode HandlingMode { get; init; } = InputMediaHandlingMode.RouteByProviderCapability;

    public bool AllowBatchTranscription { get; init; } = true;

    public bool RetainInputMediaArtifact { get; init; }

    public bool AllowDerivedTextPersistence { get; init; } = true;

    public bool AllowDigestCapture { get; init; } = true;
}

public enum InputMediaHandlingMode
{
    TranscribeOnly = 0,
    RouteByProviderCapability = 2,
    ReferenceOnly = 3,
    Reject = 4
}

public enum InputMediaDisposition
{
    Received = 0,
    StoredAsArtifact = 1,
    Transcribed = 2,
    ReferenceOnly = 4,
    RejectedByPolicy = 5,
    Failed = 6
}
