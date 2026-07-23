using Microsoft.CodeAnalysis;

namespace HPD.Agent.ToolHarness.Coding.SourceGenerator.Diagnostics;

internal static class DebugAdapterGeneratorDiagnostics
{
    private const string Category = "HPD.DebugAdapter";

    public static readonly DiagnosticDescriptor DeclarationNotPublic = Create("HPDDBG001", "Debug adapter declaration must be public", "Debug adapter declaration '{0}' must be public");
    public static readonly DiagnosticDescriptor InvalidId = Create("HPDDBG002", "Invalid debug adapter id", "Debug adapter declaration '{0}' must specify a non-empty adapter id");
    public static readonly DiagnosticDescriptor DuplicateId = Create("HPDDBG003", "Duplicate debug adapter id", "Debug adapter id '{0}' is used by both '{1}' and '{2}' in the same assembly");
    public static readonly DiagnosticDescriptor MissingLanguages = Create("HPDDBG004", "Missing debug adapter languages", "Debug adapter declaration '{0}' must declare at least one language");
    public static readonly DiagnosticDescriptor InvalidExtension = Create("HPDDBG005", "Invalid debug adapter extension", "Debug adapter declaration '{0}' has invalid extension '{1}'; extensions must start with '.'");
    public static readonly DiagnosticDescriptor DuplicateValue = Create("HPDDBG006", "Duplicate debug adapter metadata", "Debug adapter declaration '{0}' repeats {1} value '{2}'");
    public static readonly DiagnosticDescriptor MissingTargetKinds = Create("HPDDBG007", "Missing debug target kinds", "Debug adapter declaration '{0}' must declare at least one target kind");
    public static readonly DiagnosticDescriptor MissingCommandHint = Create("HPDDBG008", "Static debug adapter needs a command hint", "Debug adapter declaration '{0}' must declare a command hint when it has no behavioral factory");
    public static readonly DiagnosticDescriptor InvalidFactory = Create("HPDDBG009", "Invalid debug adapter factory", "Factory '{1}' declared by '{0}' must be an accessible concrete implementation of IDebugAdapterFactory");
    public static readonly DiagnosticDescriptor InvalidLanguage = Create("HPDDBG010", "Invalid debug adapter language", "Debug adapter declaration '{0}' has a blank language value");
    public static readonly DiagnosticDescriptor InvalidRootMarker = Create("HPDDBG011", "Invalid debug adapter root marker", "Debug adapter declaration '{0}' has unsafe or blank root marker '{1}'");
    public static readonly DiagnosticDescriptor InvalidCommandHint = Create("HPDDBG012", "Invalid debug adapter command hint", "Debug adapter declaration '{0}' has a blank, path-containing, or NUL-containing command hint");
    public static readonly DiagnosticDescriptor InvalidArgumentHint = Create("HPDDBG013", "Invalid debug adapter argument hint", "Debug adapter declaration '{0}' has a blank or NUL-containing argument hint");
    public static readonly DiagnosticDescriptor InvalidInstallGuidance = Create("HPDDBG014", "Invalid install guidance id", "Debug adapter declaration '{0}' has an invalid install guidance id '{1}'");
    public static readonly DiagnosticDescriptor UnsupportedTargetKinds = Create("HPDDBG015", "Unsupported debug target flags", "Debug adapter declaration '{0}' contains unsupported target-kind bits '{1}'");
    public static readonly DiagnosticDescriptor InvalidPriority = Create("HPDDBG016", "Invalid debug adapter priority", "Debug adapter declaration '{0}' has priority '{1}' outside the supported range -10000..10000");
    public static readonly DiagnosticDescriptor ExplicitStandardFactory = Create("HPDDBG017", "Shared standard factory must be implicit", "Debug adapter declaration '{0}' must omit DebugAdapterFactory when using StandardDebugAdapterFactory");
    public static readonly DiagnosticDescriptor MissingProgramKinds = Create("HPDDBG018", "Missing debug adapter program kinds", "Debug adapter declaration '{0}' must declare at least one concrete program kind");
    public static readonly DiagnosticDescriptor UnsupportedProgramKinds = Create("HPDDBG019", "Unsupported debug adapter program flags", "Debug adapter declaration '{0}' contains unsupported program-kind bits '{1}'");

    private static DiagnosticDescriptor Create(string id, string title, string message) => new(
        id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
