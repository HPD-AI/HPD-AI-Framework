namespace HPD.Auth.Core.Events;

/// <summary>
/// Tenant and request context carried on every auth event.
/// </summary>
public sealed record AuthExecutionContext
{
    /// <summary>
    /// Multi-tenancy discriminator. Defaults to Guid.Empty for single-tenant apps.
    /// </summary>
    public Guid InstanceId { get; init; } = Guid.Empty;

    /// <summary>
    /// Client IP address at the time of the action. May be null outside request context.
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// User-Agent header value. May be null outside request context.
    /// </summary>
    public string? UserAgent { get; init; }
}

/// <summary>
/// Base record for all HPD.Auth domain events.
/// Extends <see cref="HPD.Events.Event"/> to integrate with the shared HPD-Events
/// coordinator (priority routing, stream grouping, hierarchical bubbling).
/// </summary>
public abstract record AuthEvent : HPD.Events.Event
{
    /// <summary>
    /// Auth-specific execution context (tenant, IP, User-Agent).
    /// </summary>
    public AuthExecutionContext? AuthContext { get; init; }
}
