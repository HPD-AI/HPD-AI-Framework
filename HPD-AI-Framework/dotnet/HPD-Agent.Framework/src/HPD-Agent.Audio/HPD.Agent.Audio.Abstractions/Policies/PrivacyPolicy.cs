namespace HPD.Agent.Audio.Policies;

public sealed record PrivacyPolicy
{
    public bool RedactRawAudioByDefault { get; init; } = true;

    public bool AllowMetadataOnlyReplay { get; init; } = true;

    public bool AllowTranscriptReplay { get; init; } = true;
}
