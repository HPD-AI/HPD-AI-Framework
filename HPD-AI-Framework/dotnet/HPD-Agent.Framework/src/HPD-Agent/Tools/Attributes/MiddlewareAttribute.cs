using System;

namespace HPD.Agent;

/// <summary>
/// Marks a middleware class for source-generated registry inclusion.
/// Middleware will be resolvable by name in AgentConfig.
/// </summary>
/// <remarks>
/// <para>
/// Use this attribute on classes that implement <see cref="IAgentMiddleware"/> to enable
/// config-based registration. The source generator will create a middleware registry entry
/// that allows the middleware to be referenced by name in JSON configuration.
/// </para>
/// <para>
/// <b>Example:</b>
/// <code>
/// [Middleware(Name = "RateLimit")]
/// public class RateLimitMiddleware : IAgentMiddleware
/// {
///     public RateLimitMiddleware() { }
///     public RateLimitMiddleware(RateLimitConfig config) { _config = config; }
///     // ...
/// }
/// </code>
/// </para>
/// <para>
/// <b>JSON Configuration:</b>
/// <code>
/// {
///   "middlewares": [
///     "RateLimit",
///     { "name": "RateLimit", "config": { "requestsPerMinute": 60 } }
///   ]
/// }
/// </code>
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MiddlewareAttribute : Attribute
{
    /// <summary>
    /// Optional custom name for registry lookup.
    /// If not provided, the class name is used (without "Middleware" suffix if present).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Examples:
    /// - Class "RateLimitMiddleware" without Name -> registered as "RateLimitMiddleware"
    /// - Class "RateLimitMiddleware" with Name = "RateLimit" -> registered as "RateLimit"
    /// </para>
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// Creates a new MiddlewareAttribute with default settings.
    /// </summary>
    public MiddlewareAttribute() { }

    /// <summary>
    /// Creates a new MiddlewareAttribute with a custom name.
    /// </summary>
    /// <param name="name">The custom name for registry lookup.</param>
    public MiddlewareAttribute(string name)
    {
        Name = name;
    }
}
