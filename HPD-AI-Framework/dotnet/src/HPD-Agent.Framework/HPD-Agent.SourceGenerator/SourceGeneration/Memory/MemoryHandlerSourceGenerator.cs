using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace HPD.Agent.SourceGenerator.Memory;

/// <summary>
/// Source generator for HPD-Agent.Memory pipeline handlers.
/// Generates AOT-compatible handler registration code by discovering [PipelineHandler] attributes.
/// </summary>
[Generator]
public class MemoryHandlerSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all classes marked with [PipelineHandler]
        var handlerClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, ct) => IsHandlerClass(node),
                transform: static (ctx, ct) => GetHandlerInfo(ctx, ct))
            .Where(static handler => handler is not null)
            .Collect();

        context.RegisterSourceOutput(handlerClasses, GenerateHandlerRegistrations);
    }

    /// <summary>
    /// Fast syntactic check to see if this might be a handler class.
    /// </summary>
    private static bool IsHandlerClass(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax classDecl)
            return false;

        // Must be public and not abstract
        if (!classDecl.Modifiers.Any(SyntaxKind.PublicKeyword))
            return false;

        if (classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword))
            return false;

        // Must have [PipelineHandler] attribute
        return classDecl.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Any(attr => attr.Name.ToString().Contains("PipelineHandler"));
    }

    /// <summary>
    /// Extract full handler information using semantic model.
    /// </summary>
    private static HandlerInfo? GetHandlerInfo(GeneratorSyntaxContext context, System.Threading.CancellationToken cancellationToken)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, cancellationToken);
        if (classSymbol == null) return null;

        // Verify it has [PipelineHandler] attribute
        var handlerAttribute = classSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name == "PipelineHandlerAttribute");

        if (handlerAttribute == null) return null;

        // Extract StepName from attribute (or derive from class name)
        string? stepName = null;
        if (handlerAttribute.NamedArguments.Any(arg => arg.Key == "StepName"))
        {
            stepName = handlerAttribute.NamedArguments
                .First(arg => arg.Key == "StepName")
                .Value.Value?.ToString();
        }

        // If no explicit step name, derive from class name (remove "Handler" suffix)
        if (string.IsNullOrEmpty(stepName))
        {
            stepName = classSymbol.Name;
            if (stepName.EndsWith("Handler"))
            {
                stepName = stepName.Substring(0, stepName.Length - 7);
            }
            // Convert PascalCase to kebab-case
            stepName = ToKebabCase(stepName);
        }

        // Find the IPipelineHandler<TMetadata> interface to extract context type
        var pipelineHandlerInterface = classSymbol.AllInterfaces
            .FirstOrDefault(i => i.Name == "IPipelineHandler" && i.IsGenericType);

        if (pipelineHandlerInterface == null) return null;

        var contextType = pipelineHandlerInterface.TypeArguments.FirstOrDefault();
        if (contextType == null) return null;

        return new HandlerInfo
        {
            ClassName = classSymbol.Name,
            Namespace = classSymbol.ContainingNamespace.ToDisplayString(),
            StepName = stepName,
            ContextTypeName = contextType.Name,
            ContextTypeFullName = contextType.ToDisplayString(),
            IsPublic = classSymbol.DeclaredAccessibility == Accessibility.Public,
            IsAbstract = classSymbol.IsAbstract
        };
    }

    /// <summary>
    /// Generate registration extension methods for all discovered handlers.
    /// </summary>
    private static void GenerateHandlerRegistrations(
        SourceProductionContext context,
        ImmutableArray<HandlerInfo?> handlers)
    {
        // Debug output
        context.AddSource("_MemoryHandlerGeneratorTest.g.cs",
            $"// Memory Handler Source Generator is running! Found {handlers.Length} handlers.");

        var validHandlers = handlers
            .Where(h => h != null && h.IsPublic && !h.IsAbstract)
            .Select(h => h!)
            .ToList();

        if (!validHandlers.Any())
            return;

        // Group handlers by context type
        var handlerGroups = validHandlers
            .GroupBy(h => h.ContextTypeFullName)
            .Select(group => new HandlerGroup
            {
                ContextTypeName = group.First().ContextTypeName,
                ContextTypeFullName = group.Key,
                Handlers = group.ToList()
            })
            .ToList();

        // Generate one registration file per context type
        foreach (var group in handlerGroups)
        {
            var source = GenerateRegistrationExtensions(group);
            context.AddSource($"{group.ContextTypeName}HandlerRegistration.g.cs", source);
        }

        // Generate a master registration file that calls all context-specific methods
        var masterSource = GenerateMasterRegistration(handlerGroups);
        context.AddSource("AllHandlersRegistration.g.cs", masterSource);
    }

    /// <summary>
    /// Generate extension methods for a specific context type.
    /// </summary>
    private static string GenerateRegistrationExtensions(HandlerGroup group)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#pragma warning disable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using HPDAgent.Memory.Abstractions.Pipeline;");
        sb.AppendLine();

        // Import all handler namespaces
        var uniqueNamespaces = group.Handlers.Select(h => h.Namespace).Distinct();
        foreach (var ns in uniqueNamespaces)
        {
            sb.AppendLine($"using {ns};");
        }

        sb.AppendLine();
        sb.AppendLine("namespace HPDAgent.Memory.Extensions;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Generated registration extensions for {group.ContextTypeName} handlers.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static partial class MemoryServiceCollectionExtensions");
        sb.AppendLine("{");

        // Generate the extension method
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// Registers all discovered {group.ContextTypeName} handlers.");
        sb.AppendLine($"    /// Found {group.Handlers.Count} handler(s): {string.Join(", ", group.Handlers.Select(h => h.ClassName))}");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public static IServiceCollection {group.ExtensionMethodName}(");
        sb.AppendLine("        this IServiceCollection services)");
        sb.AppendLine("    {");

        foreach (var handler in group.Handlers)
        {
            sb.AppendLine($"        // Register {handler.ClassName} (step: {handler.StepName})");
            sb.AppendLine($"        services.AddPipelineHandler<{group.ContextTypeFullName}, {handler.Namespace}.{handler.ClassName}>(");
            sb.AppendLine($"            \"{handler.StepName}\");");
            sb.AppendLine();
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Generate a master registration method that calls all context-specific methods.
    /// </summary>
    private static string GenerateMasterRegistration(List<HandlerGroup> groups)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#pragma warning disable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace HPDAgent.Memory.Extensions;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Master registration for all discovered pipeline handlers.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static partial class MemoryServiceCollectionExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Registers ALL discovered pipeline handlers across all context types.");
        sb.AppendLine($"    /// Found {groups.Sum(g => g.Handlers.Count)} handler(s) across {groups.Count} context type(s).");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static IServiceCollection AddAllGeneratedHandlers(");
        sb.AppendLine("        this IServiceCollection services)");
        sb.AppendLine("    {");

        foreach (var group in groups)
        {
            sb.AppendLine($"        services.{group.ExtensionMethodName}();");
        }

        sb.AppendLine();
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Convert PascalCase to kebab-case (e.g., "TextExtraction" -> "text-extraction")
    /// </summary>
    private static string ToKebabCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new StringBuilder();
        sb.Append(char.ToLower(text[0]));

        for (int i = 1; i < text.Length; i++)
        {
            if (char.IsUpper(text[i]))
            {
                sb.Append('-');
                sb.Append(char.ToLower(text[i]));
            }
            else
            {
                sb.Append(text[i]);
            }
        }

        return sb.ToString();
    }
}
