using Microsoft.CodeAnalysis;

namespace HPD.Agent.SourceGenerator.Contracts;

internal static class AIContractDiagnosticDescriptors
{
    private const string Category = "HPD.Agent.SourceGenerator.AIContracts";

    public static readonly DiagnosticDescriptor UnsupportedModelType = new(
        "HPDAI001",
        "Unsupported AI-function input type",
        "AI-function contract path '{0}' uses unsupported model-facing type '{1}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OpenModelType = new(
        "HPDAI002",
        "Open AI-function input is not allowed",
        "AI-function contract path '{0}' uses open input type '{1}'; declare a closed typed contract instead",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RecursiveContract = new(
        "HPDAI003",
        "Recursive AI-function input is not supported",
        "AI-function contract path '{0}' recursively references type '{1}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateJsonName = new(
        "HPDAI004",
        "Duplicate AI-function JSON property name",
        "AI-function contract path '{0}' maps multiple members to exact JSON name '{1}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousConstruction = new(
        "HPDAI005",
        "AI-function input has no deterministic construction plan",
        "AI-function contract path '{0}' cannot select one deterministic constructor for type '{1}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonStringDictionaryKey = new(
        "HPDAI006",
        "AI-function dictionaries require string keys",
        "AI-function contract path '{0}' uses dictionary key type '{1}'; only string keys are supported",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FlagsEnum = new(
        "HPDAI007",
        "Flags enums are not supported as AI-function input",
        "AI-function contract path '{0}' uses flags enum '{1}'; expose an array of a non-flags enum or a typed object",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidUnion = new(
        "HPDAI008",
        "Invalid discriminated AI-function union",
        "AI-function contract path '{0}' has invalid union declaration: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidReusableContractDeclaration = new(
        "HPDAI009",
        "Invalid reusable AI input-contract declaration",
        "Reusable AI input contract type '{0}' must be a non-generic, top-level partial type",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
