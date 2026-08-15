using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HPD.Payments.Analyzers;

/// <summary>Enforces statically decidable HPD.Payments ownership, boundedness, provenance, and closed-graph laws.</summary>
/// <remarks>Diagnostics reject suspicious construction. They never certify runtime behavior or capability evidence.</remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PaymentsSemanticAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "HPD.Payments.Safety";
    private static readonly string[] AmbientAuthorityNames = ["Tenant", "Principal", "Policy", "Clock", "Config", "Credential"];
    private static readonly ImmutableArray<DiagnosticDescriptor> Rules =
    [
        Rule("HPDPA001", "Forbidden authority dependency or mutation", "OWN-01: '{0}' crosses an authority/dependency mutation boundary"),
        Rule("HPDPA002", "Unstable or duplicate discriminator", "OWN-02: discriminator '{0}' is unstable or duplicated"),
        Rule("HPDPA003", "Ambient execution authority", "OWN-03: ambient {0} must be supplied explicitly"),
        Rule("HPDPA004", "Borrowed value escapes ownership", "OWN-04: borrowed or pooled value escapes through {0}"),
        Rule("HPDPA005", "Invalid pooled lifetime", "OWN-05: pooled value has use-after-return, double-return, or missing classified clear: {0}"),
        Rule("HPDPA006", "Unbounded resource", "OWN-06: resource '{0}' requires an explicit finite bound"),
        Rule("HPDPA007", "Runtime activation in static graph", "OWN-07: static graph cannot use reflection, runtime activation, scanning, or fallback: {0}"),
        Rule("HPDPA008", "Unknown value falls through", "OWN-08: unknown/default input must be preserved, quarantined, unsupported, or indeterminate"),
        Rule("HPDPA009", "Incomplete result", "OWN-09: result '{0}' omits an explicit complete disposition"),
        Rule("HPDPA010", "Claim lacks exact receipt", "OWN-10: claim '{0}' must bind an exact receipt key"),
        Rule("HPDPA011", "Grouped proof", "OWN-11: proof cannot be inherited or grouped across cells: {0}"),
        Rule("HPDPA012", "Unreviewed suppression", "OWN-12: suppression of '{0}' requires reviewed manifest evidence"),
    ];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Rules;

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
        context.RegisterSyntaxNodeAction(AnalyzeSwitch, SyntaxKind.SwitchStatement, SyntaxKind.SwitchExpression);
        context.RegisterSyntaxNodeAction(AnalyzeMember, SyntaxKind.MethodDeclaration, SyntaxKind.PropertyDeclaration, SyntaxKind.FieldDeclaration);
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static DiagnosticDescriptor Rule(string id, string title, string message) =>
        new(id, title, message, Category, DiagnosticSeverity.Error, true,
            description: "A compile-time guard. Runtime conformance remains independently required.",
            helpLinkUri: $"https://hpd.invalid/payments/analyzers/{id}", customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var node = (InvocationExpressionSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(node, context.CancellationToken).Symbol as IMethodSymbol;
        var name = symbol?.Name ?? node.Expression.ToString().Split('.').Last();
        var containing = symbol?.ContainingType.ToDisplayString() ?? node.Expression.ToString();
        if (name is "GetCurrentDirectory" or "GetEnvironmentVariable" || containing.Contains("Thread.CurrentPrincipal", StringComparison.Ordinal)) Report(context, node, 2, name);
        if (name is "GetType" or "CreateInstance" or "Load" or "LoadFrom" or "GetTypes" || containing.Contains("Reflection", StringComparison.Ordinal)) Report(context, node, 6, name);
        if (name.Contains("ClaimAll", StringComparison.Ordinal) || name.Contains("InheritProof", StringComparison.Ordinal)) Report(context, node, 10, name);
        if (name.Contains("Claim", StringComparison.Ordinal) && !node.ArgumentList.Arguments.Any(a => a.ToString().Contains("receipt", StringComparison.OrdinalIgnoreCase))) Report(context, node, 9, name);
        if (name.Contains("Enqueue", StringComparison.Ordinal) || name.Contains("Publish", StringComparison.Ordinal) || name.Contains("Store", StringComparison.Ordinal) || name.Contains("Send", StringComparison.Ordinal) || name.Contains("Trace", StringComparison.Ordinal))
            if (node.ArgumentList.Arguments.Any(a => LooksBorrowed(a.Expression))) Report(context, node, 3, name);
        if (name is "Return")
        {
            var statement = node.FirstAncestorOrSelf<StatementSyntax>();
            if (statement?.ToString().Contains("Clear", StringComparison.Ordinal) != true) Report(context, node, 4, "return without same-scope classified clear");
        }
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var node = (ObjectCreationExpressionSyntax)context.Node;
        var type = context.SemanticModel.GetTypeInfo(node, context.CancellationToken).Type?.ToDisplayString() ?? node.Type.ToString();
        if ((type.Contains("Channel", StringComparison.Ordinal) || type.Contains("ConcurrentDictionary", StringComparison.Ordinal) || type.Contains("MemoryCache", StringComparison.Ordinal)) && node.ArgumentList?.Arguments.Count == 0)
            Report(context, node, 5, type);
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        var node = (AttributeSyntax)context.Node;
        var name = node.Name.ToString();
        if (name.Contains("SuppressMessage", StringComparison.Ordinal) || name.Contains("UnconditionalSuppressMessage", StringComparison.Ordinal)) Report(context, node, 11, name);
        if (name.Contains("Discriminator", StringComparison.Ordinal))
        {
            var value = node.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
            if (value is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression)) Report(context, node, 1, value?.ToString() ?? "missing");
        }
    }

    private static void AnalyzeSwitch(SyntaxNodeAnalysisContext context)
    {
        var hasDefault = context.Node switch
        {
            SwitchStatementSyntax statement => statement.Sections.Any(s => s.Labels.Any(l => l.IsKind(SyntaxKind.DefaultSwitchLabel))),
            SwitchExpressionSyntax expression => expression.Arms.Any(a => a.Pattern.IsKind(SyntaxKind.DiscardPattern)),
            _ => false,
        };
        if (!hasDefault) Report(context, context.Node, 7, "switch");
    }

    private static void AnalyzeMember(SyntaxNodeAnalysisContext context)
    {
        var text = context.Node.ToString();
        if (text.Contains("static", StringComparison.Ordinal) && AmbientAuthorityNames.Any(text.Contains)) Report(context, context.Node, 2, "state");
        if (context.Node is MethodDeclarationSyntax method)
        {
            if ((method.Modifiers.Any(SyntaxKind.PublicKeyword) || method.Modifiers.Any(SyntaxKind.ProtectedKeyword)) && (LooksBorrowed(method.ReturnType) || method.DescendantNodes().OfType<ReturnStatementSyntax>().Any(r => r.Expression is not null && LooksBorrowed(r.Expression)))) Report(context, method, 3, "public return");
            var returnName = method.ReturnType.ToString();
            if (returnName.EndsWith("Result", StringComparison.Ordinal) && ((method.Body?.ToString().Contains("default", StringComparison.Ordinal) ?? false) || (method.ExpressionBody?.ToString().Contains("default", StringComparison.Ordinal) ?? false))) Report(context, method, 8, returnName);
            if (method.Identifier.ValueText.Contains("Mutate", StringComparison.Ordinal) && method.Modifiers.Any(SyntaxKind.PublicKeyword)) Report(context, method, 0, method.Identifier.ValueText);
        }
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var discriminators = new Dictionary<string, Location>(StringComparer.Ordinal);
        foreach (var tree in context.Compilation.SyntaxTrees)
        foreach (var attribute in tree.GetRoot(context.CancellationToken).DescendantNodes().OfType<AttributeSyntax>().Where(a => a.Name.ToString().Contains("Discriminator", StringComparison.Ordinal)))
        {
            if (attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression)) continue;
            var value = literal.Token.ValueText;
            if (!discriminators.TryAdd(value, literal.GetLocation())) context.ReportDiagnostic(Diagnostic.Create(Rules[1], literal.GetLocation(), value));
        }
    }

    private static bool LooksBorrowed(SyntaxNode node)
    {
        var text = node.ToString();
        return text.Contains("Span<", StringComparison.Ordinal) || text.Contains("ReadOnlySpan<", StringComparison.Ordinal) || text.Contains("MemoryPool", StringComparison.Ordinal) || text.Contains("rented", StringComparison.OrdinalIgnoreCase) || text.Contains("borrowed", StringComparison.OrdinalIgnoreCase);
    }

    private static void Report(SyntaxNodeAnalysisContext context, SyntaxNode node, int index, object argument) => context.ReportDiagnostic(Diagnostic.Create(Rules[index], node.GetLocation(), argument));
}

/// <summary>Lists the only bounded mechanical corrections offered for analyzer findings.</summary>
/// <remarks>These recipes do not weaken a diagnostic and never add suppressions or capability claims.</remarks>
public static class PaymentsCodeFixCatalog
{
    /// <summary>Gets diagnostic IDs for which a mechanical local correction is safe to suggest.</summary>
    public static ImmutableArray<string> FixableDiagnosticIds { get; } = ["HPDPA003", "HPDPA006", "HPDPA008"];

    /// <summary>Gets a deterministic correction description without changing semantic authority.</summary>
    /// <param name="diagnosticId">A stable HPD.Payments analyzer diagnostic identifier.</param>
    /// <returns>A bounded correction description, or <see langword="null"/> when human review is mandatory.</returns>
    public static string? GetRecipe(string diagnosticId) => diagnosticId switch
    {
        "HPDPA003" => "Inject the authority value as an explicit parameter.",
        "HPDPA006" => "Supply an explicit finite capacity owned by configuration.",
        "HPDPA008" => "Add an explicit Unknown/Unsupported/Indeterminate disposition.",
        _ => null,
    };
}
