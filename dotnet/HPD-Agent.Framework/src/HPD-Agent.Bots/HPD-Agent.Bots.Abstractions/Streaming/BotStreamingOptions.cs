namespace HPD.Agent.Bots.Streaming;

/// <summary>
/// Shared streaming defaults generated from <see cref="HpdStreamingAttribute"/>
/// and consumed by platform adapters that use <see cref="BotStreamingRunner"/>.
/// </summary>
public sealed class BotStreamingOptions
{
    /// <summary>How streamed agent output should be delivered to the platform.</summary>
    public StreamingStrategy Strategy { get; set; } = StreamingStrategy.PostAndEdit;

    /// <summary>Minimum milliseconds between editable message updates.</summary>
    public int DebounceMs { get; set; } = 500;
}
