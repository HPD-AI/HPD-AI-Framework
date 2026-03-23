using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

/// <summary>
/// Immutable request object for function/tool calls.
/// </summary>
/// <remarks>
/// <para><b>Design Philosophy:</b></para>
/// <para>
/// FunctionRequest is immutable to preserve the original request for debugging,
/// logging, and retry logic. Middleware can create modified copies using the
/// <see cref="Override"/> method without affecting the original request.
/// </para>
/// <para><b>Use Cases:</b></para>
/// <list type="bullet">
/// <item>Function RetryMiddleware  (preserve original args for retry)</item>
/// <item>Argument transformation middleware (sanitize PII)</item>
/// <item>Timeout middleware (wrap execution)</item>
/// <item>Permission middleware (inspect args before execution)</item>
/// </list>
/// <para><b>Example:</b></para>
/// <code>
/// public async Task&lt;object?&gt; WrapFunctionCallAsync(
///     FunctionRequest request,
///     Func&lt;FunctionRequest, Task&lt;object?&gt;&gt; handler,
///     CancellationToken ct)
/// {
///     // Retry logic with exponential backoff
///     for (int attempt = 0; attempt &lt; 3; attempt++)
///     {
///         try
///         {
///             return await handler(request);  // Original request preserved!
///         }
///         catch (Exception ex) when (ShouldRetry(ex) &amp;&amp; attempt &lt; 2)
///         {
///             await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
///         }
///     }
///
///     return await handler(request);
/// }
/// </code>
/// </remarks>
public sealed record FunctionRequest
{
    /// <summary>
    /// The function being invoked.
    ///   Always available (never NULL)
    /// </summary>
    public required AIFunction Function { get; init; }

    /// <summary>
    /// Unique call ID for this function invocation.
    ///   Always available (never NULL)
    /// </summary>
    public required string CallId { get; init; }

    /// <summary>
    /// Arguments to pass to the function.
    ///   Always available (never NULL, but may be empty)
    /// Immutable dictionary - use Override() to create modified copy
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Arguments { get; init; }

    /// <summary>
    /// Current agent state at time of request.
    ///   Always available (never NULL)
    /// </summary>
    public required AgentLoopState State { get; init; }

    /// <summary>
    /// Name of the Toolkit that contains this function, if any.
    /// May be NULL if function is not part of a Toolkit.
    /// </summary>
    public string? ToolkitName { get; init; }

    /// <summary>
    /// Name of the skill that referenced this function, if any.
    /// May be NULL if function is not part of a skill.
    /// </summary>
    public string? SkillName { get; init; }

    //
    // MIDDLEWARE CAPABILITIES
    //

    /// <summary>
    /// Event coordinator for emitting events during function execution.
    /// Middleware can emit retry events, timeout warnings, etc.
    ///   May be NULL in test scenarios
    /// </summary>
    /// <remarks>
    /// <para>
    /// Middleware can use this to emit observability events during function execution.
    /// For example, RetryMiddleware emits <c>FunctionRetryEvent</c> when retrying failed functions.
    /// </para>
    /// <para><b>Example (RetryMiddleware):</b></para>
    /// <code>
    /// request.EventCoordinator?.Emit(new FunctionRetryEvent(
    ///     FunctionName: request.FunctionName,
    ///     Attempt: attempt,
    ///     MaxRetries: maxRetries,
    ///     Delay: delay,
    ///     Exception: ex,
    ///     ExceptionType: ex.GetType().Name,
    ///     ErrorMessage: ex.Message
    /// ));
    /// </code>
    /// </remarks>
    public HPD.Events.IEventCoordinator? EventCoordinator { get; init; }

    /// <summary>
    /// Creates a modified copy of this request.
    /// </summary>
    /// <param name="function">Optional new function (null = keep original)</param>
    /// <param name="arguments">Optional new arguments (null = keep original)</param>
    /// <returns>A new FunctionRequest with specified properties overridden</returns>
    /// <remarks>
    /// <para>
    /// This method preserves the original request intact while creating a modified copy.
    /// Useful for argument transformation, PII sanitization, or function substitution.
    /// </para>
    /// <para><b>Example:</b></para>
    /// <code>
    /// // Sanitize PII from arguments
    /// var sanitizedArgs = request.Arguments
    ///     .Where(kvp => !IsPII(kvp.Key))
    ///     .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    ///
    /// var newRequest = request.Override(arguments: sanitizedArgs);
    ///
    /// // Or transform specific argument
    /// var newArgs = new Dictionary&lt;string, object?&gt;(request.Arguments)
    /// {
    ///     ["apiKey"] = "***REDACTED***"
    /// };
    /// var newRequest = request.Override(arguments: newArgs);
    /// </code>
    /// </remarks>
    public FunctionRequest Override(
        AIFunction? function = null,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        return this with
        {
            Function = function ?? Function,
            Arguments = arguments ?? Arguments
        };
    }

    //
    // HELPERS
    //

    /// <summary>
    /// Convenience property for function name.
    /// </summary>
    public string FunctionName => Function.Name;

    /// <summary>
    /// True if this function is part of a Toolkit.
    /// </summary>
    public bool IsToolkitFunction => ToolkitName != null;

    /// <summary>
    /// True if this function is part of a skill.
    /// </summary>
    public bool IsSkillFunction => SkillName != null;
}
