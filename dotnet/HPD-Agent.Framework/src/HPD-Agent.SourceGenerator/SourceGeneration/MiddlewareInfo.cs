using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace HPD.Agent.SourceGenerator;

/// <summary>
/// Information about a middleware class discovered during source generation.
/// Captures all metadata needed to generate MiddlewareFactory entries.
/// </summary>
public class MiddlewareInfo
{
    /// <summary>
    /// The class name (identifier text).
    /// </summary>
    public string ClassName { get; set; } = "";

    /// <summary>
    /// Custom name from [Middleware(Name = "...")], if provided.
    /// </summary>
    public string? CustomName { get; set; }

    /// <summary>
    /// Effective name for registry lookup.
    /// Uses CustomName if provided, otherwise ClassName.
    /// </summary>
    public string EffectiveName => CustomName ?? ClassName;

    /// <summary>
    /// The namespace containing this middleware class.
    /// </summary>
    public string Namespace { get; set; } = "";

    /// <summary>
    /// Fully qualified type name (Namespace.ClassName).
    /// </summary>
    public string FullTypeName => string.IsNullOrEmpty(Namespace) ? ClassName : $"{Namespace}.{ClassName}";

    /// <summary>
    /// Whether the class has a parameterless constructor.
    /// </summary>
    public bool HasParameterlessConstructor { get; set; }

    /// <summary>
    /// Whether the class is publicly accessible (public and not nested in non-public).
    /// </summary>
    public bool IsPubliclyAccessible { get; set; }

    /// <summary>
    /// Fully qualified type name of the config constructor parameter, if detected.
    /// Example: "MyApp.Config.RateLimitConfig" for RateLimitMiddleware(RateLimitConfig config).
    /// Null if middleware has no config constructor.
    /// </summary>
    public string? ConfigConstructorTypeName { get; set; }

    /// <summary>
    /// Whether this middleware requires DI (no parameterless or config constructor).
    /// </summary>
    public bool RequiresDI => !HasParameterlessConstructor && string.IsNullOrEmpty(ConfigConstructorTypeName);

    /// <summary>
    /// Diagnostics collected during analysis.
    /// </summary>
    public List<Diagnostic> Diagnostics { get; set; } = new();
}
