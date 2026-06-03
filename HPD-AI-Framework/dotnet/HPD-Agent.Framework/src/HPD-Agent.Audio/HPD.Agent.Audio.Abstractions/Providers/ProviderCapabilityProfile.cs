namespace HPD.Agent.Audio.Providers;

public sealed record ProviderCapabilityProfile
{
    public required string ProviderKey { get; init; }

    public required ProviderDeclaredCapabilities Declared { get; init; }

    public ProviderNegotiatedCapabilities? Negotiated { get; init; }

    public ProviderObservedCapabilities? Observed { get; init; }

    public ProviderDegradedCapabilities? Degraded { get; init; }

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public sealed record ProviderDeclaredCapabilities
{
    public ProviderCapabilityFlag Flags { get; init; } = ProviderCapabilityFlag.None;

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public sealed record ProviderNegotiatedCapabilities
{
    public ProviderCapabilityFlag Flags { get; init; } = ProviderCapabilityFlag.None;

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public sealed record ProviderObservedCapabilities
{
    public ProviderCapabilityFlag Flags { get; init; } = ProviderCapabilityFlag.None;

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public sealed record ProviderDegradedCapabilities
{
    public ProviderCapabilityFlag UnavailableFlags { get; init; } = ProviderCapabilityFlag.None;

    public string? Reason { get; init; }

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

[Flags]
public enum ProviderCapabilityFlag
{
    None = 0,
    SpeechToText = 1 << 1,
    TextToSpeech = 1 << 2,
    Chat = 1 << 3,
    StreamingAudioOutput = 1 << 4,
    ServerVad = 1 << 5,
    ProviderItemTruncation = 1 << 6,
    ToolCalls = 1 << 7,
    SessionUpdate = 1 << 8
}

public enum ProviderRouteState
{
    Created = 0,
    Ready = 1,
    Active = 2,
    Degraded = 3,
    Faulted = 4,
    Stopped = 5
}
