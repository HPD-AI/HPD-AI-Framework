using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rhodium.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsafeAccessAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        id: "RHD001",
        title: "Direct unsafe access from safe code",
        messageFormat: "Type '{0}' from Rhodium.Unsafe should not be referenced directly from '{1}'",
        category: "Safety",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationContext =>
        {
            compilationContext.RegisterSyntaxNodeAction(static nodeContext =>
            {
                var containingAssembly = nodeContext.Compilation.AssemblyName ?? "";
                if (!containingAssembly.StartsWith("Rhodium.", StringComparison.Ordinal) ||
                    containingAssembly is "Rhodium.Unsafe" or "Rhodium.Tensor" or "Rhodium.Kernel")
                    return;

                var symbol = nodeContext.SemanticModel.GetSymbolInfo(nodeContext.Node, nodeContext.CancellationToken).Symbol;
                var type = symbol switch
                {
                    INamedTypeSymbol named => named,
                    IMethodSymbol method => method.ContainingType,
                    IPropertySymbol property => property.ContainingType,
                    IFieldSymbol field => field.ContainingType,
                    _ => null
                };

                if (type is null) return;

                var typeName = type.ToDisplayString();
                if (!typeName.StartsWith("Rhodium.Unsafe.", StringComparison.Ordinal)) return;

                nodeContext.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    nodeContext.Node.GetLocation(),
                    typeName,
                    containingAssembly));
            }, Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierName, Microsoft.CodeAnalysis.CSharp.SyntaxKind.QualifiedName);
        });
    }
}
