using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace HPDAgent.Graph.SourceGenerator;

/// <summary>
/// Analyzer that validates handler configurations at build time.
/// Checks for duplicate handler names and invalid socket configurations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class HandlerValidationAnalyzer : DiagnosticAnalyzer
{
    // Diagnostic IDs
    private const string DuplicateHandlerNameId = "HPDG001";
    private const string InvalidSocketTypeId = "HPDG002";

    // Diagnostic descriptors
    private static readonly DiagnosticDescriptor DuplicateHandlerNameRule = new DiagnosticDescriptor(
        id: DuplicateHandlerNameId,
        title: "Duplicate handler name",
        messageFormat: "Handler name '{0}' is already used by another handler. Handler names must be unique within the assembly.",
        category: "HPD-Graph",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each handler must have a unique name for proper routing.");

    private static readonly DiagnosticDescriptor InvalidSocketTypeRule = new DiagnosticDescriptor(
        id: InvalidSocketTypeId,
        title: "Invalid socket type",
        messageFormat: "Socket parameter '{0}' has unsupported type '{1}'. Sockets must use JSON-serializable types.",
        category: "HPD-Graph",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Socket parameters must use types that can be serialized to/from JSON.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuplicateHandlerNameRule, InvalidSocketTypeRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Register for compilation analysis to check duplicate names across the assembly
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var handlerNames = new System.Collections.Generic.Dictionary<string, INamedTypeSymbol>();

        // Find all classes with [GraphNodeHandler] attribute
        foreach (var syntaxTree in context.Compilation.SyntaxTrees)
        {
            var semanticModel = context.Compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot(context.CancellationToken);

            var classDeclarations = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(c => c.AttributeLists.Count > 0);

            foreach (var classDecl in classDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(classDecl, context.CancellationToken);
                if (symbol is not INamedTypeSymbol classSymbol)
                    continue;

                // Check if class has [GraphNodeHandler] attribute
                var attribute = classSymbol.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "GraphNodeHandlerAttribute");

                if (attribute == null)
                    continue;

                // Get the handler name
                var handlerName = GetHandlerName(classSymbol, attribute);

                // Check for duplicates
                if (handlerNames.TryGetValue(handlerName, out var existingHandler))
                {
                    // Report duplicate handler name error
                    var diagnostic = Diagnostic.Create(
                        DuplicateHandlerNameRule,
                        classDecl.Identifier.GetLocation(),
                        handlerName);

                    context.ReportDiagnostic(diagnostic);
                }
                else
                {
                    handlerNames[handlerName] = classSymbol;
                }

                // Validate socket types
                ValidateSocketTypes(context, classSymbol, semanticModel, classDecl);
            }
        }
    }

    private string GetHandlerName(INamedTypeSymbol classSymbol, AttributeData attribute)
    {
        // Check if NodeName is specified in attribute
        var nodeName = attribute.NamedArguments
            .FirstOrDefault(a => a.Key == "NodeName")
            .Value.Value as string;

        if (!string.IsNullOrEmpty(nodeName))
            return nodeName;

        // Check if class has HandlerName property
        var handlerNameProp = classSymbol.GetMembers("HandlerName")
            .OfType<IPropertySymbol>()
            .FirstOrDefault();

        if (handlerNameProp != null)
        {
            // Try to get constant value (if it's a simple property)
            // For generated properties, we convert class name to snake_case
            return ToSnakeCase(classSymbol.Name.Replace("Handler", ""));
        }

        // Default: Convert class name to snake_case
        return ToSnakeCase(classSymbol.Name.Replace("Handler", ""));
    }

    private string ToSnakeCase(string text)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsUpper(c) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLower(c));
        }
        return sb.ToString();
    }

    private void ValidateSocketTypes(
        CompilationAnalysisContext context,
        INamedTypeSymbol classSymbol,
        SemanticModel semanticModel,
        ClassDeclarationSyntax classDecl)
    {
        // Find ExecuteAsync method
        var executeMethod = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Name == "ExecuteAsync");

        if (executeMethod == null)
            return;

        // Check input socket parameters
        foreach (var param in executeMethod.Parameters)
        {
            var inputAttr = param.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "InputSocketAttribute");

            if (inputAttr == null)
                continue;

            // Validate that type is JSON-serializable
            if (!IsJsonSerializable(param.Type))
            {
                var paramSyntax = classDecl.DescendantNodes()
                    .OfType<ParameterSyntax>()
                    .FirstOrDefault(p => p.Identifier.Text == param.Name);

                if (paramSyntax != null)
                {
                    var diagnostic = Diagnostic.Create(
                        InvalidSocketTypeRule,
                        paramSyntax.GetLocation(),
                        param.Name,
                        param.Type.ToDisplayString());

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private bool IsJsonSerializable(ITypeSymbol type)
    {
        // Allow primitives
        if (type.SpecialType != SpecialType.None)
            return true;

        // Allow common types
        var typeName = type.ToDisplayString();
        if (typeName.StartsWith("System.String") ||
            typeName.StartsWith("System.DateTime") ||
            typeName.StartsWith("System.DateTimeOffset") ||
            typeName.StartsWith("System.Guid") ||
            typeName.StartsWith("System.Collections.Generic.List<") ||
            typeName.StartsWith("System.Collections.Generic.Dictionary<"))
            return true;

        // Allow arrays
        if (type.TypeKind == TypeKind.Array)
            return true;

        // Allow classes/records (assume they're serializable)
        if (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct)
            return true;

        return false;
    }
}
