namespace HPD.Agent.Bots.Contracts;

/// <summary>Base class for all HPD adapter exceptions.</summary>
public abstract class BotException : Exception
{
    /// <inheritdoc />
    protected BotException(string message) : base(message) { }

    /// <inheritdoc />
    protected BotException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when bot input or output violates platform validation rules.
/// The generated dispatch maps this to HTTP 400.
/// </summary>
public sealed class BotValidationException : BotException
{
    /// <inheritdoc />
    public BotValidationException(string message) : base(message) { }

    /// <inheritdoc />
    public BotValidationException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a platform request fails authentication.
/// The generated dispatch maps this to HTTP 401.
/// </summary>
public sealed class BotAuthenticationException : BotException
{
    /// <inheritdoc />
    public BotAuthenticationException(string message) : base(message) { }

    /// <inheritdoc />
    public BotAuthenticationException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a platform API call is rate-limited.
/// The generated dispatch maps this to HTTP 429.
/// </summary>
public sealed class BotRateLimitException : BotException
{
    /// <inheritdoc />
    public BotRateLimitException(string message) : base(message) { }

    /// <inheritdoc />
    public BotRateLimitException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when the adapter lacks permission to perform an action.
/// The generated dispatch maps this to HTTP 403.
/// </summary>
public sealed class BotPermissionException : BotException
{
    /// <inheritdoc />
    public BotPermissionException(string message) : base(message) { }

    /// <inheritdoc />
    public BotPermissionException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a referenced platform resource does not exist.
/// The generated dispatch maps this to HTTP 404.
/// </summary>
public sealed class BotNotFoundException : BotException
{
    /// <inheritdoc />
    public BotNotFoundException(string message) : base(message) { }

    /// <inheritdoc />
    public BotNotFoundException(string message, Exception inner) : base(message, inner) { }
}
