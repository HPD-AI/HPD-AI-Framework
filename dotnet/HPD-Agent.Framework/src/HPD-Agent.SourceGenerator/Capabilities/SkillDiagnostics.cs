using Microsoft.CodeAnalysis;

namespace HPD.Agent.SourceGenerator.Capabilities;

internal static class SkillDiagnostics
{
    private const string Category = "HPD.Agent.Skills";

    public static readonly DiagnosticDescriptor DuplicateSkillName = Error(
        "HPDSKILL001", "Duplicate skill model name", "Skill model name '{0}' conflicts with another generated skill.");
    public static readonly DiagnosticDescriptor InvalidDescription = Error(
        "HPDSKILL002", "Invalid skill description", "Skill '{0}' must have a nonblank compile-time discovery description.");
    public static readonly DiagnosticDescriptor MissingInstructions = Error(
        "HPDSKILL003", "Missing skill instructions", "Skill '{0}' must supply an instruction provider.");
    public static readonly DiagnosticDescriptor InvalidInstructionProvider = Error(
        "HPDSKILL004", "Invalid instruction provider", "Skill '{0}' has an instruction provider with an unsupported signature.");
    public static readonly DiagnosticDescriptor MissingToolHarness = Error(
        "HPDSKILL005", "Missing referenced tool harness", "Skill '{0}' references unavailable tool harness '{1}'.");
    public static readonly DiagnosticDescriptor MemberNotFunction = Error(
        "HPDSKILL006", "Referenced member is not an AI function", "Skill '{0}' references '{1}', which is not marked [AIFunction].");
    public static readonly DiagnosticDescriptor AmbiguousMember = Error(
        "HPDSKILL007", "Ambiguous referenced member", "Skill '{0}' references ambiguous member '{1}'.");
    public static readonly DiagnosticDescriptor CircularRelationship = Error(
        "HPDSKILL008", "Circular skill relationship", "Skill '{0}' participates in a circular capability relationship.");
    public static readonly DiagnosticDescriptor DuplicateChildName = Error(
        "HPDSKILL009", "Duplicate child model name", "Skill '{0}' contains duplicate child model name '{1}'.");
    public static readonly DiagnosticDescriptor ResourceMissingDescription = Error(
        "HPDSKILL010", "Resource missing description", "Skill resource '{0}' must have a nonblank compile-time description.");
    public static readonly DiagnosticDescriptor InvalidResourceProvider = Error(
        "HPDSKILL011", "Invalid resource provider", "Skill resource '{0}' has an unsupported provider signature or result type.");
    public static readonly DiagnosticDescriptor ScriptMissingDescription = Error(
        "HPDSKILL012", "Script missing description", "Skill script '{0}' must have a nonblank compile-time description.");
    public static readonly DiagnosticDescriptor InvalidScriptReference = Error(
        "HPDSKILL013", "Invalid script reference", "Skill script '{0}' has an unsupported script-reference provider.");
    public static readonly DiagnosticDescriptor UnsupportedMetadata = Error(
        "HPDSKILL014", "Unsupported runtime skill metadata", "Skill method '{0}' uses identity metadata that cannot be analyzed at compile time.");
    public static readonly DiagnosticDescriptor RegistryNameCollision = Error(
        "HPDSKILL015", "Generated registry name collision", "Model-visible name '{0}' conflicts with another generated capability '{1}'.");
    public static readonly DiagnosticDescriptor UnsupportedLifetime = Error(
        "HPDSKILL016", "Unsupported activation lifetime", "Skill '{0}' uses an activation lifetime not supported by this runtime.");
    public static readonly DiagnosticDescriptor InaccessibleMember = Error(
        "HPDSKILL017", "Inaccessible generated member", "Skill member '{0}' must be public so generated registration can call it directly.");
    public static readonly DiagnosticDescriptor ReflectionDeclarationRejected = Error(
        "HPDSKILL018", "Reflection-only skill declaration rejected", "Skill method '{0}' must directly return Skill.Create(...); reflection fallback is not available.");

    private static DiagnosticDescriptor Error(string id, string title, string message)
        => new(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
