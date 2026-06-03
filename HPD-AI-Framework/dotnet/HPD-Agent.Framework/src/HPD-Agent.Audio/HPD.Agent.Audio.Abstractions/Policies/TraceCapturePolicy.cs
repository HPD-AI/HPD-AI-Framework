namespace HPD.Agent.Audio.Policies;

public sealed record TraceCapturePolicy
{
    public bool CaptureTraceRecords { get; init; } = true;

    public bool CaptureStructEventSamples { get; init; }

    public bool CaptureRawMedia { get; init; }

    public bool CaptureProviderPayloads { get; init; }
}
