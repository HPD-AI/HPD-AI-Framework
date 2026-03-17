namespace HPD.Agent.Adapters;

/// <summary>
/// Declares the streaming strategy for an <see cref="HpdAdapterAttribute"/> adapter.
/// Controls how agent response tokens are delivered to the platform.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HpdStreamingAttribute(StreamingStrategy strategy) : Attribute
{
    /// <summary>How streamed agent output is sent to the platform.</summary>
    public StreamingStrategy Strategy => strategy;

    /// <summary>
    /// For <see cref="StreamingStrategy.PostAndEdit"/>: minimum milliseconds between
    /// consecutive <c>chat.update</c> (or equivalent) API calls during streaming.
    /// Prevents hitting platform rate limits. Default: 500ms.
    /// </summary>
    public int DebounceMs { get; init; } = 500;
}
