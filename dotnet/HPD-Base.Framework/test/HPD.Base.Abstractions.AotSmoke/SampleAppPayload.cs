namespace HPD.Base.Abstractions.AotSmoke;

public sealed record SampleAppPayload
{
    public required string Title { get; init; }
    public int Priority { get; init; }
}
