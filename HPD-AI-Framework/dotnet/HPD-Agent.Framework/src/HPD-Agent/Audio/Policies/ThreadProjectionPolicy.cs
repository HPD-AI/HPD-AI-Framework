namespace HPD.Agent.Audio.Policies;

public sealed record ThreadProjectionPolicy
{
    public bool ProjectCommittedUserTurns { get; init; } = true;

    public bool ProjectCommittedAssistantOutputs { get; init; } = true;

    public bool ProjectInputContentMetadata { get; init; } = true;

    public bool ProjectRawInputMedia { get; init; }
}
