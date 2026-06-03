namespace HPD.Agent.Audio.Output;

public enum AssistantOutputSynthesisMode
{
    Disabled = 0,
    FinalText = 1,
    Progressive = 2,
    ProgressiveWithFinalFallback = 3
}

public enum AssistantAudioArtifactCapturePolicy
{
    ContentStoreArtifact = 0,
    Disabled = 1,
    MetadataOnly = 2,
    DigestOnly = 3
}
