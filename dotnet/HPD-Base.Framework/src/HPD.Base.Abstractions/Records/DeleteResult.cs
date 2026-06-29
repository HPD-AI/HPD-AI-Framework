using HPD.Base;

namespace HPD.Base.Records;

public sealed record DeleteResult
{
    public required RecordId Id { get; init; }
    public bool Deleted { get; init; }
    public RecordEnvelope? Previous { get; init; }
}
