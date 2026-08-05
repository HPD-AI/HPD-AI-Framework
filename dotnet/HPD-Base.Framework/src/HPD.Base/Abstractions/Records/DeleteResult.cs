
namespace HPD.Base;

/// <summary>Represents a delete result.</summary>
public sealed record DeleteResult
{
    /// <summary>Gets or sets the ID.</summary>
    public required RecordId Id { get; init; }
    /// <summary>Gets or sets the deleted.</summary>
    public bool Deleted { get; init; }
    /// <summary>Gets or sets the previous.</summary>
    public RecordEnvelope? Previous { get; init; }
}
