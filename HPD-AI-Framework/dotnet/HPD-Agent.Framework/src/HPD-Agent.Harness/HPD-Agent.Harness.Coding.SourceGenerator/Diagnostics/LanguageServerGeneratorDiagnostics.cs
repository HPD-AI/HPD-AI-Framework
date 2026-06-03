using Microsoft.CodeAnalysis;

namespace HPD.Agent.ToolHarness.Coding.SourceGenerator.Diagnostics;

internal static class LanguageServerGeneratorDiagnostics
{
    private const string Category = "HPD.LanguageServer";

    public static readonly DiagnosticDescriptor ProviderNotPublic = new(
        id: "HPDLS001",
        title: "[HpdLanguageServer] class must be public",
        messageFormat: "Language server provider class '{0}' must be public",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateServerId = new(
        id: "HPDLS002",
        title: "Duplicate language server id",
        messageFormat: "Language server id '{0}' is used by both '{1}' and '{2}' in the same assembly",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingExtensions = new(
        id: "HPDLS003",
        title: "Language server declaration is missing extensions",
        messageFormat: "Language server provider '{0}' must declare at least one extension",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidExtension = new(
        id: "HPDLS004",
        title: "Invalid language server extension",
        messageFormat: "Language server provider '{0}' declares invalid extension '{1}'. Extensions must start with '.'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OddLanguageIdMapping = new(
        id: "HPDLS005",
        title: "Language id mappings must be extension/id pairs",
        messageFormat: "Language server provider '{0}' has an odd number of language-id mapping arguments",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor LanguageIdExtensionNotDeclared = new(
        id: "HPDLS006",
        title: "Language id mapping extension is not declared",
        messageFormat: "Language server provider '{0}' maps language id for extension '{1}', but that extension is not declared",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingExecutable = new(
        id: "HPDLS007",
        title: "Language server declaration is missing executable",
        messageFormat: "Language server provider '{0}' must declare [LanguageServerExecutable] when it does not implement ILanguageServerProvider",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateRootMarker = new(
        id: "HPDLS008",
        title: "Duplicate language server root marker",
        messageFormat: "Language server provider '{0}' declares duplicate root marker '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
