namespace HPD.Agent.Audio.Policies;

public sealed record AudioPolicySet
{
    public InputMediaPolicy InputMedia { get; init; } = new();

    public TraceCapturePolicy Trace { get; init; } = new();

    public PrivacyPolicy Privacy { get; init; } = new();

    public BranchProjectionPolicy BranchProjection { get; init; } = new();
}
