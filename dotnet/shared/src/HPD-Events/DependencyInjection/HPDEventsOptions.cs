namespace HPD.Events.DependencyInjection;

/// <summary>
/// Options for registering HPD.Events services.
/// </summary>
public sealed class HPDEventsOptions
{
    /// <summary>Service lifetime used for the class-event coordinator and related surfaces.</summary>
    public HPDEventsServiceLifetime Lifetime { get; set; } = HPDEventsServiceLifetime.Singleton;

    /// <summary>Register process-local struct event hub services.</summary>
    public bool RegisterStructEvents { get; set; } = true;

    /// <summary>Register open generic live event stream sources.</summary>
    public bool RegisterEventStreams { get; set; } = true;
}

/// <summary>
/// Supported HPD.Events service lifetimes.
/// </summary>
public enum HPDEventsServiceLifetime
{
    Singleton,
    Scoped,
    Transient
}
