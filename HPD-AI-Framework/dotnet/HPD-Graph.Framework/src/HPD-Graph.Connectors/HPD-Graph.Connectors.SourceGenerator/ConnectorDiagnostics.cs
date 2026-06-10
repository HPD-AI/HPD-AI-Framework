using Microsoft.CodeAnalysis;

namespace HPD.Graph.Connectors.SourceGenerator;

internal static class ConnectorDiagnostics
{
    public static readonly DiagnosticDescriptor MissingPartial = new(
        "HPDC001",
        "Connector generated type must be partial",
        "Type '{0}' decorated with '{1}' must be partial",
        "HPD.Graph.Connectors.SourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateId = new(
        "HPDC002",
        "Duplicate connector id",
        "Duplicate {0} id '{1}' on '{2}'",
        "HPD.Graph.Connectors.SourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidSignature = new(
        "HPDC003",
        "Invalid connector method signature",
        "{0} '{1}' has an unsupported signature",
        "HPD.Graph.Connectors.SourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnknownReference = new(
        "HPDC004",
        "Unknown connector reference",
        "{0} references unknown {1} '{2}'",
        "HPD.Graph.Connectors.SourceGenerator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingJsonContext = new(
        "HPDC005",
        "Connector JSON context is missing",
        "Connector '{0}' does not declare JsonContextType; generated code will use a reflection-based JSON fallback",
        "HPD.Graph.Connectors.SourceGenerator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
