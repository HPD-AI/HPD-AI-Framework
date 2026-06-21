namespace HPD.Agent.Audio;

public sealed record AudioCorrelation
{
    public static AudioCorrelation Empty { get; } = new();

    public string? ConversationId { get; init; }

    public string? RequestId { get; init; }

    public string? OperationId { get; init; }

    public string? ParentId { get; init; }

    public AudioSessionId? SessionId { get; init; }

    public AudioTurnId? TurnId { get; init; }

    public OutputFlowId? OutputFlowId { get; init; }
}
