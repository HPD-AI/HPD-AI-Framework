using HPD.Events;

namespace HPD.Base.Realtime.Configuration;

/// <summary>Configures the live, best-effort HPD.BASE realtime subsystem.</summary>
public sealed class BaseRealtimeOptions
{
    /// <summary>Gets or sets whether realtime capability is available.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the enforced realtime resource and protocol limits.</summary>
    public BaseRealtimeLimits Limits { get; set; } = new();

    /// <summary>
    /// Gets or sets the non-blocking HPD.Events inbox overflow behavior.
    /// Supported values are <see cref="AsyncStreamBackpressureMode.DropOldest"/>,
    /// <see cref="AsyncStreamBackpressureMode.DropNewest"/>, and
    /// <see cref="AsyncStreamBackpressureMode.DropWrite"/>.
    /// </summary>
    public AsyncStreamBackpressureMode Backpressure { get; set; } = AsyncStreamBackpressureMode.DropOldest;

    /// <summary>Gets or sets whether private channels require an authenticated principal.</summary>
    public bool RequireAuthenticatedPrivateChannels { get; set; } = true;
}
