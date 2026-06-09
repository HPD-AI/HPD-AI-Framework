namespace HPD.Agent.Audio;

public sealed record AudioErrorInfo
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public string? Category { get; init; }

    public bool IsRetryable { get; init; }

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}
