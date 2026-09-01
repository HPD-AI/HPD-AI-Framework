namespace HPD.Events;

/// <summary>
/// Supplies an immutable canonical event-content digest for trusted finalized
/// replay-frame capabilities.
/// </summary>
public interface IReplayContentDigest
{
    /// <summary>Gets the precomputed canonical content digest without materializing event payload.</summary>
    string ReplayContentDigest { get; }
}
