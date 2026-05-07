using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HPDAgent.Graph.SourceGenerator;

/// <summary>
/// Source generator that creates DI registration extension methods for handlers.
/// Scans all handlers marked with [GraphNodeHandler] and generates AddGeneratedXHandlers() methods.
/// </summary>
[Generator]
public class DIRegistrationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax c && c.AttributeLists.Count > 0,
                transform: static (ctx, _) => (ClassDeclarationSyntax)ctx.Node)
            .Collect();

        context.RegisterSourceOutput(
            classDeclarations.Combine(context.CompilationProvider),
            (spc, pair) => Execute(pair.Right, pair.Left, spc));
    }

    private void Execute(Compilation compilation, System.Collections.Immutable.ImmutableArray<ClassDeclarationSyntax> classes, SourceProductionContext context)
    {
        // Group handlers by context type
        var handlersByContext = new Dictionary<string, List<INamedTypeSymbol>>();

        // Collect routers (no context type needed - they implement IMapRouter)
        var routers = new List<INamedTypeSymbol>();

        foreach (var candidateClass in classes)
        {
            var model = compilation.GetSemanticModel(candidateClass.SyntaxTree);
            var symbol = model.GetDeclaredSymbol(candidateClass);

            if (symbol is not INamedTypeSymbol classSymbol)
                continue;

            // Check if class has [GraphNodeHandler] attribute
            var handlerAttribute = classSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "GraphNodeHandlerAttribute");

            if (handlerAttribute != null)
            {
                var executeMethod = FindSocketExecuteAsyncMethod(classSymbol);
                if (GetInterfaceContextType(classSymbol) == null && executeMethod == null)
                {
                    continue;
                }

                var contextTypeName = InferContextType(classSymbol, executeMethod, handlerAttribute);

                if (!handlersByContext.ContainsKey(contextTypeName))
                {
                    handlersByContext[contextTypeName] = new List<INamedTypeSymbol>();
                }

                handlersByContext[contextTypeName].Add(classSymbol);
            }

            // Check if class has [MapRouter] attribute
            var routerAttribute = classSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "MapRouterAttribute");

            if (routerAttribute != null)
            {
                // Verify it implements IMapRouter
                if (ImplementsIMapRouter(classSymbol))
                {
                    routers.Add(classSymbol);
                }
            }
        }

        // Generate a registration method for each context type
        foreach (var kvp in handlersByContext)
        {
            var contextType = kvp.Key;
            var handlers = kvp.Value;

            if (handlers.Count == 0)
                continue;

            var source = GenerateHandlerRegistrationCode(contextType, handlers);
            var contextTypeName = contextType.Split('.').Last().Replace("<", "_").Replace(">", "");
            var fileName = $"GraphHandlers.{contextTypeName}.g.cs";

            context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
        }

        var allHandlers = handlersByContext.Values.SelectMany(h => h).ToList();
        if (allHandlers.Count > 0)
        {
            var catalogSource = GenerateHandlerCatalogCode(allHandlers);
            context.AddSource("GeneratedHandlerCatalog.g.cs", SourceText.From(catalogSource, Encoding.UTF8));
        }

        // Generate router registration method if any routers found
        if (routers.Count > 0)
        {
            var source = GenerateRouterRegistrationCode(routers);
            context.AddSource("GraphRouters.g.cs", SourceText.From(source, Encoding.UTF8));
        }
    }

    private ITypeSymbol? GetInterfaceContextType(INamedTypeSymbol classSymbol)
    {
        // Check if class implements IGraphNodeHandler<TContext>
        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (iface.Name == "IGraphNodeHandler" && iface.TypeArguments.Length == 1)
            {
                return iface.TypeArguments[0];
            }
        }
        return null;
    }

    private string InferContextType(
        INamedTypeSymbol classSymbol,
        IMethodSymbol? executeMethod,
        AttributeData attribute)
    {
        var attributeContextType = GetAttributeType(attribute, "ContextType");
        if (attributeContextType != null)
        {
            return attributeContextType.ToDisplayString();
        }

        var contextParam = executeMethod?.Parameters.FirstOrDefault(p =>
            p.Name.Contains("context", System.StringComparison.OrdinalIgnoreCase));
        if (contextParam != null)
        {
            return contextParam.Type.ToDisplayString();
        }

        var interfaceContextType = GetInterfaceContextType(classSymbol);
        if (interfaceContextType != null)
        {
            return interfaceContextType.ToDisplayString();
        }

        return "HPDAgent.Graph.Core.Context.GraphContext";
    }

    private bool ImplementsIMapRouter(INamedTypeSymbol classSymbol)
    {
        // Check if class implements IMapRouter
        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (iface.Name == "IMapRouter")
            {
                return true;
            }
        }
        return false;
    }

    private string GenerateHandlerRegistrationCode(string contextType, List<INamedTypeSymbol> handlers)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        sb.AppendLine();

        // Use a common namespace for extension methods
        sb.AppendLine("namespace HPDAgent.Graph.Extensions;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Auto-generated DI registration extension methods for {contextType} handlers.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static partial class GeneratedHandlerRegistration");
        sb.AppendLine("{");

        // Generate extension method name based on context type
        var contextTypeName = contextType.Split('.').Last().Replace("<", "").Replace(">", "");
        var methodName = $"AddGenerated{contextTypeName}Handlers";

        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// Registers all generated handlers for {contextType}.");
        sb.AppendLine($"    /// Found {handlers.Count} handler(s) in the current assembly.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public static IServiceCollection {methodName}(this IServiceCollection services)");
        sb.AppendLine("    {");
        sb.AppendLine("        services.TryAddSingleton<HPDAgent.Graph.Abstractions.Discovery.GeneratedHandlerCatalog>();");
        sb.AppendLine("        services.TryAddSingleton<HPDAgent.Graph.Abstractions.Discovery.IGeneratedHandlerCatalog>(sp => sp.GetRequiredService<HPDAgent.Graph.Abstractions.Discovery.GeneratedHandlerCatalog>());");

        foreach (var handler in handlers)
        {
            var handlerFullName = handler.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var invokerTypeName = GetGeneratedInvokerTypeName(handler);

            sb.AppendLine($"        services.AddScoped<{handlerFullName}>();");
            sb.AppendLine($"        services.AddScoped<HPDAgent.Graph.Abstractions.Handlers.IGraphNodeHandler<{contextType}>>(sp => sp.GetRequiredService<{handlerFullName}>());");
            sb.AppendLine($"        services.AddScoped<HPDAgent.Graph.Abstractions.Invocation.IGraphNodeHandlerInvoker, {invokerTypeName}>();");
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        foreach (var handler in handlers)
        {
            AppendInvokerCode(sb, contextType, handler);
        }

        return sb.ToString();
    }

    private string GenerateHandlerCatalogCode(List<INamedTypeSymbol> handlers)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace HPDAgent.Graph.Abstractions.Discovery;");
        sb.AppendLine();
        sb.AppendLine("public sealed class GeneratedHandlerCatalog : IGeneratedHandlerCatalog");
        sb.AppendLine("{");
        sb.AppendLine("    public System.Collections.Generic.IReadOnlyDictionary<string, HandlerDescriptor> GetHandlers()");
        sb.AppendLine("    {");
        sb.AppendLine("        return new System.Collections.Generic.Dictionary<string, HandlerDescriptor>(System.StringComparer.Ordinal)");
        sb.AppendLine("        {");

        foreach (var handler in handlers.OrderBy(h => GetHandlerName(h), StringComparer.Ordinal))
        {
            AppendHandlerDescriptor(sb, handler);
        }

        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private void AppendHandlerDescriptor(StringBuilder sb, INamedTypeSymbol handler)
    {
        var handlerName = GetHandlerName(handler);
        var handlerAttribute = handler.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "GraphNodeHandlerAttribute");
        var executeMethod = FindSocketExecuteAsyncMethod(handler);
        var contextType = handlerAttribute is null
            ? GetInterfaceContextType(handler)?.ToDisplayString() ?? "unknown"
            : InferContextType(handler, executeMethod, handlerAttribute);
        var displayName = handler.Name.EndsWith("Handler", System.StringComparison.Ordinal)
            ? handler.Name.Substring(0, handler.Name.Length - "Handler".Length)
            : handler.Name;
        var domain = InferDomain(handler);
        var configClass = handler.GetTypeMembers().FirstOrDefault(t => t.Name == "Config");

        sb.AppendLine($"            [\"{Escape(handlerName)}\"] = new HandlerDescriptor");
        sb.AppendLine("            {");
        sb.AppendLine($"                HandlerName = \"{Escape(handlerName)}\",");
        sb.AppendLine($"                DisplayName = \"{Escape(displayName)}\",");
        sb.AppendLine($"                Domain = \"{Escape(domain)}\",");
        sb.AppendLine($"                HandlerType = \"{Escape(handler.ToDisplayString())}\",");
        sb.AppendLine($"                ContextType = \"{Escape(contextType)}\",");
        sb.AppendLine("                Inputs = new SocketDescriptor[]");
        sb.AppendLine("                {");

        if (executeMethod is not null)
        {
            foreach (var parameter in executeMethod.Parameters)
            {
                var inputAttribute = parameter.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "InputSocketAttribute");

                if (inputAttribute is null)
                    continue;

                var optional = GetAttributeProperty<bool>(inputAttribute, "Optional") || parameter.HasExplicitDefaultValue;
                var description = GetAttributeProperty<string>(inputAttribute, "Description");

                sb.AppendLine("                    new SocketDescriptor");
                sb.AppendLine("                    {");
                sb.AppendLine($"                        Name = \"{Escape(parameter.Name)}\",");
                sb.AppendLine($"                        TypeName = \"{Escape(parameter.Type.ToDisplayString())}\",");
                sb.AppendLine("                        Direction = SocketDirection.Input,");
                sb.AppendLine($"                        Required = {(!optional).ToString().ToLowerInvariant()},");
                if (!string.IsNullOrWhiteSpace(description))
                {
                    sb.AppendLine($"                        Description = \"{Escape(description!)}\",");
                }
                sb.AppendLine("                    },");
            }
        }

        sb.AppendLine("                },");
        sb.AppendLine("                Outputs = new SocketDescriptor[]");
        sb.AppendLine("                {");

        var returnType = executeMethod is null ? null : GetTaskInnerType(executeMethod.ReturnType) as INamedTypeSymbol;
        if (returnType is not null)
        {
            foreach (var property in returnType.GetMembers().OfType<IPropertySymbol>())
            {
                var outputAttribute = property.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "OutputSocketAttribute");

                if (outputAttribute is null)
                    continue;

                var description = GetAttributeProperty<string>(outputAttribute, "Description");

                sb.AppendLine("                    new SocketDescriptor");
                sb.AppendLine("                    {");
                sb.AppendLine($"                        Name = \"{Escape(property.Name)}\",");
                sb.AppendLine($"                        TypeName = \"{Escape(property.Type.ToDisplayString())}\",");
                sb.AppendLine("                        Direction = SocketDirection.Output,");
                sb.AppendLine("                        Required = true,");
                if (!string.IsNullOrWhiteSpace(description))
                {
                    sb.AppendLine($"                        Description = \"{Escape(description!)}\",");
                }
                sb.AppendLine("                    },");
            }
        }

        sb.AppendLine("                },");
        if (configClass is not null)
        {
            sb.AppendLine("                Config = new ConfigDescriptor");
            sb.AppendLine("                {");
            sb.AppendLine($"                    TypeName = \"{Escape(configClass.ToDisplayString())}\",");
            sb.AppendLine("                },");
        }
        sb.AppendLine("            },");
    }

    private static void AppendInvokerCode(StringBuilder sb, string contextType, INamedTypeSymbol handler)
    {
        var handlerFullName = handler.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var invokerTypeName = GetGeneratedInvokerTypeName(handler);

        sb.AppendLine();
        sb.AppendLine($"internal sealed class {invokerTypeName} : HPDAgent.Graph.Abstractions.Invocation.IGraphNodeHandlerInvoker");
        sb.AppendLine("{");
        sb.AppendLine($"    private readonly {handlerFullName} _handler;");
        sb.AppendLine();
        sb.AppendLine($"    public {invokerTypeName}({handlerFullName} handler)");
        sb.AppendLine("    {");
        sb.AppendLine("        _handler = handler;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public string HandlerName => ((HPDAgent.Graph.Abstractions.Handlers.IGraphNodeHandler<{contextType}>)_handler).HandlerName;");
        sb.AppendLine($"    public System.Type HandlerType => typeof({handlerFullName});");
        sb.AppendLine($"    public System.Type ContextType => typeof({contextType});");
        sb.AppendLine();
        sb.AppendLine("    public async System.Threading.Tasks.ValueTask<HPDAgent.Graph.Abstractions.Execution.NodeExecutionResult> ExecuteAsync(");
        sb.AppendLine("        HPDAgent.Graph.Abstractions.Context.IGraphContext context,");
        sb.AppendLine("        HPDAgent.Graph.Abstractions.Handlers.HandlerInputs inputs,");
        sb.AppendLine("        System.Threading.CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine($"        if (context is not {contextType} typedContext)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new System.InvalidOperationException(");
        sb.AppendLine($"                $\"Handler '{{HandlerName}}' requires context type '{{typeof({contextType}).FullName}}', but received '{{context.GetType().FullName}}'.\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        return await ((HPDAgent.Graph.Abstractions.Handlers.IGraphNodeHandler<{contextType}>)_handler)");
        sb.AppendLine("            .ExecuteAsync(typedContext, inputs, cancellationToken)");
        sb.AppendLine("            .ConfigureAwait(false);");
        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    private static string GetGeneratedInvokerTypeName(INamedTypeSymbol handler)
    {
        var fullName = handler.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);

        var sb = new StringBuilder(fullName.Length + "GraphInvoker".Length);
        foreach (var ch in fullName)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        sb.Append("GraphInvoker");
        return sb.ToString();
    }

    private string GetHandlerName(INamedTypeSymbol handler)
    {
        var attribute = handler.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "GraphNodeHandlerAttribute");

        var nodeName = attribute is null
            ? null
            : GetAttributeProperty<string>(attribute, "NodeName");

        if (!string.IsNullOrWhiteSpace(nodeName))
            return nodeName!;

        var name = handler.Name.EndsWith("Handler", System.StringComparison.Ordinal)
            ? handler.Name.Substring(0, handler.Name.Length - "Handler".Length)
            : handler.Name;

        return ToSnakeCase(name);
    }

    private string InferDomain(INamedTypeSymbol handler)
    {
        var ns = handler.ContainingNamespace.ToDisplayString();
        if (ns.Contains("RAG", System.StringComparison.OrdinalIgnoreCase) ||
            ns.Contains("Mrag", System.StringComparison.OrdinalIgnoreCase))
        {
            return "rag";
        }

        if (ns.Contains("MultiAgent", System.StringComparison.OrdinalIgnoreCase))
        {
            return "multiagent";
        }

        return "graph";
    }

    private IMethodSymbol? FindSocketExecuteAsyncMethod(INamedTypeSymbol handler)
    {
        return handler.GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Name == "ExecuteAsync" && HasSocketAttributes(m));
    }

    private bool HasSocketAttributes(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            if (parameter.GetAttributes().Any(a => a.AttributeClass?.Name == "InputSocketAttribute"))
                return true;
        }

        var returnType = GetTaskInnerType(method.ReturnType) as INamedTypeSymbol;
        return returnType?.GetMembers()
            .OfType<IPropertySymbol>()
            .Any(p => p.GetAttributes().Any(a => a.AttributeClass?.Name == "OutputSocketAttribute")) == true;
    }

    private ITypeSymbol? GetTaskInnerType(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol namedType &&
            namedType.Name == "Task" &&
            namedType.TypeArguments.Length == 1)
        {
            return namedType.TypeArguments[0];
        }

        return null;
    }

    private string ToSnakeCase(string text)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsUpper(c) && i > 0)
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    private T? GetAttributeProperty<T>(AttributeData attribute, string propertyName)
    {
        var namedArg = attribute.NamedArguments.FirstOrDefault(a => a.Key == propertyName);
        if (namedArg.Value.Value is T value)
            return value;

        return default;
    }

    private ITypeSymbol? GetAttributeType(AttributeData attribute, string propertyName)
    {
        var namedArg = attribute.NamedArguments.FirstOrDefault(a => a.Key == propertyName);
        return namedArg.Value.Value as ITypeSymbol;
    }

    private string GenerateRouterRegistrationCode(List<INamedTypeSymbol> routers)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();

        // Use a common namespace for extension methods
        sb.AppendLine("namespace HPDAgent.Graph.Extensions;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Auto-generated DI registration extension methods for Map routers.");
        sb.AppendLine("/// Pattern: Identical to handler registration (AddGeneratedGraphContextHandlers).");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static partial class GeneratedRouterRegistration");
        sb.AppendLine("{");

        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Registers all generated Map routers.");
        sb.AppendLine($"    /// Found {routers.Count} router(s) in the current assembly.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static IServiceCollection AddGeneratedMapRouters(this IServiceCollection services)");
        sb.AppendLine("    {");

        // Use Singleton lifetime for stateless routers (better performance)
        // Use fully qualified names to prevent namespace conflicts
        foreach (var router in routers)
        {
            var routerFullName = router.ToDisplayString();
            sb.AppendLine($"        services.AddSingleton<HPDAgent.Graph.Abstractions.Routing.IMapRouter, {routerFullName}>();");
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }


}
