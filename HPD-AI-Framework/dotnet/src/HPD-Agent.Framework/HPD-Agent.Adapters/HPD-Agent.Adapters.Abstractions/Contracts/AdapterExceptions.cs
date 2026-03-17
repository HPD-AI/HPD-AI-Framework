namespace HPD.Agent.Adapters.Contracts;

/// <summary>Base class for all HPD adapter exceptions.</summary>
public abstract class AdapterException : Exception
{
    /// <inheritdoc />
    protected AdapterException(string message) : base(message) { }

    /// <inheritdoc />
    protected AdapterException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a platform request fails authentication.
/// The generated dispatch maps this to HTTP 401.
/// </summary>
public sealed class AdapterAuthenticationException : AdapterException
{
    /// <inheritdoc />
    public AdapterAuthenticationException(string message) : base(message) { }

    /// <inheritdoc />
    public AdapterAuthenticationException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a platform API call is rate-limited.
/// The generated dispatch maps this to HTTP 429.
/// </summary>
public sealed class AdapterRateLimitException : AdapterException
{
    /// <inheritdoc />
    public AdapterRateLimitException(string message) : base(message) { }

    /// <inheritdoc />
    public AdapterRateLimitException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when the adapter lacks permission to perform an action.
/// The generated dispatch maps this to HTTP 403.
/// </summary>
public sealed class AdapterPermissionException : AdapterException
{
    /// <inheritdoc />
    public AdapterPermissionException(string message) : base(message) { }

    /// <inheritdoc />
    public AdapterPermissionException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a referenced platform resource does not exist.
/// The generated dispatch maps this to HTTP 404.
/// </summary>
public sealed class AdapterNotFoundException : AdapterException
{
    /// <inheritdoc />
    public AdapterNotFoundException(string message) : base(message) { }

    /// <inheritdoc />
    public AdapterNotFoundException(string message, Exception inner) : base(message, inner) { }
}
