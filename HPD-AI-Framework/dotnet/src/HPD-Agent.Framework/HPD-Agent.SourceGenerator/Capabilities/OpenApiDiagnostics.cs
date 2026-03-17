using Microsoft.CodeAnalysis;

namespace HPD.Agent.SourceGenerator.Capabilities;

/// <summary>
/// Diagnostic descriptors for [OpenApi] capability analysis.
/// </summary>
internal static class OpenApiDiagnostics
{
    /// <summary>
    /// HPDAG0401: [OpenApi] method has invalid return type.
    /// Must return OpenApiConfig (from HPD-Agent.OpenApi).
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidReturnType = new(
        id: "HPDAG0401",
        title: "Invalid OpenApi return type",
        messageFormat: "[OpenApi] method '{0}' must return OpenApiConfig, but returns '{1}'",
        category: "HPDAgent.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Methods marked with [OpenApi] must return OpenApiConfig (from HPD-Agent.OpenApi). " +
                    "The source generator uses the return value as an OpenAPI spec configuration at build time.");

    /// <summary>
    /// HPDAG0402: [OpenApi] method has conflicting capability attributes.
    /// Cannot combine with [AIFunction], [Skill], [SubAgent], [MultiAgent], or [MCPServer].
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingAttributes = new(
        id: "HPDAG0402",
        title: "Conflicting capability attributes on OpenApi",
        messageFormat: "[OpenApi] method '{0}' cannot be combined with [{1}]. Use only one capability attribute per method.",
        category: "HPDAgent.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A method can only have one capability attribute. [OpenApi] cannot be combined with [AIFunction], [Skill], [SubAgent], [MultiAgent], or [MCPServer]. " +
                    "Each capability type has different execution semantics and return type requirements.");

    /// <summary>
    /// HPDAG0403: [OpenApi] method must be parameterless.
    /// ISecretResolver and other dependencies must be injected via the toolkit constructor.
    /// </summary>
    public static readonly DiagnosticDescriptor MethodMustBeParameterless = new(
        id: "HPDAG0403",
        title: "OpenApi method must be parameterless",
        messageFormat: "[OpenApi] method '{0}' must be parameterless — use constructor injection for ISecretResolver and other dependencies",
        category: "HPDAgent.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Methods marked with [OpenApi] must be parameterless. " +
                    "Dependencies like ISecretResolver should be declared as constructor parameters on the toolkit class. " +
                    "AgentBuilder wires them automatically through dependency injection. " +
                    "Secrets are resolved inside the AuthCallback closure at request time, enabling vault rotation without rebuilding.");
}
