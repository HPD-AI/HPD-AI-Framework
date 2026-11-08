using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HPD.Agent.SourceGenerator;

/// <summary>
/// Generates immutable input modules, descriptors, discriminators, and application
/// compositions for concrete <c>AgentInputEvent</c> contracts.
/// </summary>
[Generator]
public sealed class CustomInputSourceGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor DuplicateDiscriminator = new(
        "HPDINP001",
        "Invalid or duplicate agent input discriminator",
        "Agent input discriminator '{0}' is invalid or duplicated: {1}",
        "HPD.Agent.Serialization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingJsonMetadata = new(
        "HPDINP002",
        "Agent input is missing generated JSON metadata",
        "Input '{0}' is not declared by [JsonSerializable] on context '{1}'",
        "HPD.Agent.Serialization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenericInputSkipped = new(
        "HPDINP003",
        "Generic agent inputs are not supported",
        "Input type '{0}' is generic and cannot be registered",
        "HPD.Agent.Serialization",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AbstractInputSkipped = new(
        "HPDINP004",
        "Abstract agent input skipped",
        "Abstract input type '{0}' is a base contract and will not be registered",
        "HPD.Agent.Serialization",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidAgentInputTarget = new(
        "HPDINP005",
        "Invalid [AgentInput] target",
        "Type '{0}' cannot be marked [AgentInput]: {1}",
        "HPD.Agent.Serialization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor IncompleteManifest = new(
        "HPDINP006",
        "Application input manifest closure is incomplete",
        "Application input composition cannot close manifest '{0}': {1}",
        "HPD.Agent.Serialization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var inputs = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is RecordDeclarationSyntax { BaseList.Types.Count: > 0 },
                GetInputInfo)
            .Where(static input => input is not null);

        var publicTargets = context.SyntaxProvider.ForAttributeWithMetadataName(
            "HPD.Agent.Serialization.AgentInputAttribute",
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, _) => ValidatePublicInputTarget(
                (INamedTypeSymbol)ctx.TargetSymbol,
                ctx.TargetNode));
        context.RegisterSourceOutput(publicTargets, static (spc, diagnostic) =>
        {
            if (diagnostic is not null)
                spc.ReportDiagnostic(diagnostic);
        });

        context.RegisterSourceOutput(
            context.CompilationProvider
                .Combine(inputs.Collect())
                .Combine(context.AnalyzerConfigOptionsProvider),
            static (spc, value) => Generate(
                spc,
                value.Left.Left,
                value.Left.Right,
                value.Right));
    }

    private static InputInfo? GetInputInfo(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var declaration = (RecordDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken)
            is not INamedTypeSymbol type ||
            type.Name == "AgentInputEvent" ||
            !IsModuleVisible(type) ||
            HasAttribute(type, "EventRegistrationExcludedAttribute", "EventRegistrationExcluded") ||
            !InheritsFromHpdInput(type))
        {
            return null;
        }

        var diagnostics = new List<Diagnostic>();
        if (type.IsGenericType)
        {
            diagnostics.Add(Diagnostic.Create(
                GenericInputSkipped,
                declaration.Identifier.GetLocation(),
                type.Name));
            return new InputInfo(type, "", false, diagnostics, declaration.Identifier.GetLocation());
        }
        if (type.IsAbstract)
        {
            diagnostics.Add(Diagnostic.Create(
                AbstractInputSkipped,
                declaration.Identifier.GetLocation(),
                type.Name));
            return new InputInfo(type, "", false, diagnostics, declaration.Identifier.GetLocation());
        }

        var discriminator = GetEventTypeDiscriminator(type)
            ?? ToScreamingSnakeCase(type.Name);
        if (!IsCanonicalDiscriminator(discriminator))
        {
            diagnostics.Add(Diagnostic.Create(
                DuplicateDiscriminator,
                declaration.Identifier.GetLocation(),
                discriminator,
                "the inferred discriminator is not canonical SCREAMING_SNAKE_CASE"));
            return new InputInfo(type, discriminator, false, diagnostics, declaration.Identifier.GetLocation());
        }

        return new InputInfo(
            type,
            discriminator,
            HasAttribute(type, "AgentInputAttribute", "AgentInput"),
            diagnostics,
            declaration.Identifier.GetLocation());
    }

    private static Diagnostic? ValidatePublicInputTarget(INamedTypeSymbol type, SyntaxNode targetNode)
    {
        string? reason = null;
        if (!InheritsFromHpdInput(type))
            reason = "the type does not derive from AgentInputEvent";
        else if (type.IsAbstract || type.IsGenericType || type.TypeKind != TypeKind.Class)
            reason = "the input must be a concrete, closed class-based AgentInputEvent";

        return reason is null
            ? null
            : Diagnostic.Create(InvalidAgentInputTarget, targetNode.GetLocation(), type.ToDisplayString(), reason);
    }

    private static void Generate(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<InputInfo?> discovered,
        AnalyzerConfigOptionsProvider options)
    {
        var valid = new List<InputInfo>();
        foreach (var input in discovered)
        {
            if (input is null)
                continue;
            foreach (var diagnostic in input.Diagnostics)
                context.ReportDiagnostic(diagnostic);
            if (input.IsValid)
                valid.Add(input);
        }

        var duplicates = valid
            .GroupBy(static input => input.Discriminator, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .ToArray();
        if (duplicates.Length > 0)
        {
            foreach (var duplicate in duplicates)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateDiscriminator,
                    duplicate.First().Location,
                    duplicate.Key,
                    "multiple inputs use it: " +
                    string.Join(", ", duplicate.Select(static input => input.Symbol.ToDisplayString()))));
            }
            return;
        }

        options.GlobalOptions.TryGetValue("build_property.HpdAgentApplication", out var applicationOption);
        options.GlobalOptions.TryGetValue("build_property.NativeLib", out var nativeLib);
        var isApplication = bool.TryParse(applicationOption, out var explicitApplication)
            ? explicitApplication
            : compilation.Options.OutputKind is OutputKind.ConsoleApplication or OutputKind.WindowsApplication ||
              !string.IsNullOrWhiteSpace(nativeLib);
        if (valid.Count == 0 && !isApplication)
            return;

        options.GlobalOptions.TryGetValue("build_property.HpdAgentEventModuleId", out var configuredModuleId);
        var moduleId = string.Equals(compilation.AssemblyName, "HPD-Agent", StringComparison.Ordinal)
            ? "hpd.agent.core"
            : !string.IsNullOrWhiteSpace(configuredModuleId)
                ? configuredModuleId!
                : compilation.AssemblyName ?? "HPD.Agent.Module";
        var hasHandwrittenManifest = compilation.Assembly.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.Name == "HpdAgentInputModuleManifestAttribute");

        INamedTypeSymbol? jsonContext = null;
        if (valid.Count > 0)
        {
            jsonContext = FindJsonContext(compilation, valid);
            if (jsonContext is null)
            {
                foreach (var input in valid)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingJsonMetadata,
                        Location.None,
                        input.Symbol.ToDisplayString(),
                        "an assembly-local JsonSerializerContext"));
                }
                return;
            }
        }

        if (valid.Count > 0 && !hasHandwrittenManifest)
        {
            context.AddSource("CustomInputTypes.g.cs", GenerateConstants(valid));
            context.AddSource(
                "GeneratedAgentInputModule.g.cs",
                GenerateModule(
                    valid,
                    moduleId,
                    jsonContext!,
                    compilation.AssemblyName ?? "HPD.Agent.Module"));
        }

        if (!isApplication)
            return;

        var providers = new List<string>();
        var closureValid = AddManifestProviders(
            compilation.Assembly,
            providers,
            context,
            requirePublic: false);
        foreach (var referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            closureValid &= AddManifestProviders(
                referenced,
                providers,
                context,
                requirePublic: true);
        }
        if (!closureValid)
            return;
        if (!hasHandwrittenManifest && valid.Count > 0)
            providers.Add($"global::HPD.Agent.Serialization.{GetProviderName(moduleId)}");

        context.AddSource(
            "GeneratedAgentInputComposition.g.cs",
            GenerateApplicationComposition(
                compilation.AssemblyName ?? "HPD.Agent.Application",
                providers));
    }

    private static string GenerateConstants(IReadOnlyList<InputInfo> inputs)
    {
        var builder = new StringBuilder()
            .AppendLine("// <auto-generated/>")
            .AppendLine("#nullable enable")
            .AppendLine("namespace HPD.Agent.Serialization;")
            .AppendLine("internal static class CustomAgentInputTypes")
            .AppendLine("{");
        foreach (var input in inputs.OrderBy(static input => input.Discriminator, StringComparer.Ordinal))
        {
            builder.Append("    public const string ")
                .Append(input.Discriminator)
                .Append(" = \"")
                .Append(input.Discriminator)
                .AppendLine("\";");
        }
        return builder.AppendLine("}").ToString();
    }

    private static string GenerateModule(
        IReadOnlyList<InputInfo> inputs,
        string moduleId,
        INamedTypeSymbol jsonContext,
        string assemblyIdentity)
    {
        var providerName = GetProviderName(moduleId);
        var contextName = jsonContext.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var dependency = string.Equals(moduleId, "hpd.agent.core", StringComparison.Ordinal)
            ? string.Empty
            : ", typeof(global::HPD.Agent.Serialization.CoreAgentInputModule)";
        var builder = new StringBuilder()
            .AppendLine("// <auto-generated/>")
            .AppendLine("#nullable enable")
            .Append("[assembly: global::HPD.Agent.Serialization.HpdAgentInputModuleManifestAttribute(\"")
            .Append(moduleId)
            .Append("\", typeof(global::HPD.Agent.Serialization.")
            .Append(providerName)
            .Append(')')
            .Append(dependency)
            .AppendLine(")]")
            .AppendLine("namespace HPD.Agent.Serialization;")
            .Append("public static class ").AppendLine(providerName)
            .AppendLine("{")
            .AppendLine("    public static AgentInputModuleFragment Fragment { get; } = new()")
            .AppendLine("    {")
            .Append("        ModuleId = \"").Append(moduleId).AppendLine("\",")
            .AppendLine("        Inputs = global::System.Array.AsReadOnly<AgentInputDescriptor>([");
        foreach (var input in inputs.OrderBy(static input => input.Discriminator, StringComparer.Ordinal))
        {
            var fullName = input.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            builder.AppendLine("            new AgentInputDescriptor")
                .AppendLine("            {")
                .Append("                InputType = typeof(").Append(fullName).AppendLine("),")
                .Append("                Discriminator = \"").Append(input.Discriminator).AppendLine("\",")
                .Append("                JsonTypeInfo = ").Append(contextName)
                .Append(".Default.GetTypeInfo(typeof(").Append(fullName).AppendLine("))")
                .Append("                    ?? throw new global::System.InvalidOperationException(\"Missing generated JSON metadata for ")
                .Append(EscapeString(input.Symbol.ToDisplayString())).AppendLine(".\"),")
                .Append("                ModuleId = \"").Append(moduleId).AppendLine("\",")
                .Append("                IsPublic = ").Append(input.IsPublic ? "true" : "false").AppendLine()
                .AppendLine("            },");
        }
        builder.AppendLine("        ])")
            .AppendLine("    };")
            .AppendLine("}")
            .AppendLine("internal static class GeneratedAgentInputModuleRegistration")
            .AppendLine("{")
            .AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]")
            .AppendLine("    internal static void Register() => AgentInputCompositionHost.RegisterModule(")
            .Append("        ").Append(providerName).Append(".Fragment, \"")
            .Append(EscapeString(assemblyIdentity)).AppendLine("\");")
            .AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateApplicationComposition(
        string assemblyIdentity,
        IReadOnlyList<string> providers)
    {
        var compositionName = GetApplicationCompositionName(assemblyIdentity);
        var ordered = providers.Distinct(StringComparer.Ordinal)
            .OrderBy(static provider => provider, StringComparer.Ordinal)
            .ToArray();
        var builder = new StringBuilder()
            .AppendLine("// <auto-generated/>")
            .AppendLine("#nullable enable")
            .AppendLine("namespace HPD.Agent.Serialization")
            .AppendLine("{")
            .Append("internal static class ").AppendLine(compositionName)
            .AppendLine("{")
            .AppendLine("    public static AgentInputComposition Composition { get; } = AgentInputComposition.Create([");
        foreach (var provider in ordered)
            builder.Append("        ").Append(provider).AppendLine(".Fragment,");
        builder.AppendLine("    ]);")
            .AppendLine("}")
            .Append("internal static class ").Append(compositionName).AppendLine("Registration")
            .AppendLine("{")
            .AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]")
            .AppendLine("    internal static void Register() => AgentInputCompositionHost.RegisterApplication(")
            .Append("        ").Append(compositionName).Append(".Composition, \"")
            .Append(EscapeString(assemblyIdentity)).AppendLine("\");")
            .AppendLine("}")
            .AppendLine("}")
            .AppendLine("namespace Microsoft.Extensions.DependencyInjection")
            .AppendLine("{")
            .Append("public static class ").Append(compositionName).AppendLine("ServiceCollectionExtensions")
            .AppendLine("{")
            .AppendLine("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddHpdGeneratedAgentInputs(")
            .AppendLine("        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)")
            .AppendLine("    {")
            .AppendLine("        global::System.ArgumentNullException.ThrowIfNull(services);")
            .AppendLine("        global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(")
            .Append("            services, global::HPD.Agent.Serialization.").Append(compositionName).AppendLine(".Composition);")
            .AppendLine("        return services;")
            .AppendLine("    }")
            .AppendLine("}")
            .AppendLine("}");
        return builder.ToString();
    }

    private static bool AddManifestProviders(
        IAssemblySymbol assembly,
        List<string> providers,
        SourceProductionContext context,
        bool requirePublic)
    {
        var valid = true;
        foreach (var attribute in assembly.GetAttributes().Where(static attribute =>
                     attribute.AttributeClass?.Name == "HpdAgentInputModuleManifestAttribute"))
        {
            if (attribute.ConstructorArguments.Length < 2 ||
                attribute.ConstructorArguments[0].Value is not string moduleId ||
                string.IsNullOrWhiteSpace(moduleId) ||
                attribute.ConstructorArguments[1].Value is not INamedTypeSymbol providerType ||
                providerType.TypeKind == TypeKind.Error ||
                requirePublic && !IsPubliclyVisible(providerType))
            {
                valid = false;
                context.ReportDiagnostic(Diagnostic.Create(
                    IncompleteManifest,
                    attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None,
                    assembly.Identity.ToString(),
                    "the manifest has no stable ID, resolvable provider, or required public visibility"));
                continue;
            }

            providers.Add(providerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            if (attribute.ConstructorArguments.Length < 3)
                continue;
            foreach (var dependency in attribute.ConstructorArguments[2].Values)
            {
                if (dependency.Value is not INamedTypeSymbol dependencyType)
                    continue;
                if (dependencyType.TypeKind == TypeKind.Error ||
                    requirePublic && !IsPubliclyVisible(dependencyType))
                {
                    valid = false;
                    context.ReportDiagnostic(Diagnostic.Create(
                        IncompleteManifest,
                        attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None,
                        moduleId,
                        "a dependency fragment provider is unresolved or not public"));
                    continue;
                }
                providers.Add(dependencyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
        }
        return valid;
    }

    private static INamedTypeSymbol? FindJsonContext(
        Compilation compilation,
        IReadOnlyList<InputInfo> inputs)
    {
        var required = inputs.Select(static input => input.Symbol.ToDisplayString())
            .ToArray();
        var requiredSet = new HashSet<string>(required, StringComparer.Ordinal);
        foreach (var type in AllTypes(compilation.Assembly.GlobalNamespace))
        {
            if (!InheritsFrom(type, "JsonSerializerContext"))
                continue;
            var declared = type.GetAttributes()
                .Where(static attribute => attribute.AttributeClass?.Name == "JsonSerializableAttribute")
                .Select(static attribute => attribute.ConstructorArguments.FirstOrDefault().Value as INamedTypeSymbol)
                .Where(static symbol => symbol is not null)
                .Select(static symbol => symbol!.ToDisplayString());
            var declaredSet = new HashSet<string>(declared, StringComparer.Ordinal);
            if (requiredSet.All(declaredSet.Contains))
                return type;
        }
        return null;
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol symbol)
    {
        foreach (var type in symbol.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in NestedTypes(type))
                yield return nested;
        }
        foreach (var child in symbol.GetNamespaceMembers())
        foreach (var type in AllTypes(child))
            yield return type;
    }

    private static IEnumerable<INamedTypeSymbol> NestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var descendant in NestedTypes(nested))
                yield return descendant;
        }
    }

    private static bool InheritsFrom(INamedTypeSymbol type, string name)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            if (current.Name == name)
                return true;
        return false;
    }

    private static bool InheritsFromHpdInput(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "AgentInputEvent" &&
                current.ContainingNamespace.ToDisplayString() == "HPD.Agent")
                return true;
        }
        return false;
    }

    private static string? GetEventTypeDiscriminator(INamedTypeSymbol type) =>
        type.GetAttributes()
            .FirstOrDefault(static attribute =>
                attribute.AttributeClass?.Name is "EventTypeAttribute" or "EventType")
            ?.ConstructorArguments.FirstOrDefault().Value as string;

    private static bool HasAttribute(INamedTypeSymbol type, params string[] names) =>
        type.GetAttributes().Any(attribute =>
            attribute.AttributeClass is { } attributeClass &&
            names.Contains(attributeClass.Name, StringComparer.Ordinal));

    private static bool IsModuleVisible(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
            if (current.DeclaredAccessibility is not Accessibility.Public and not Accessibility.Internal)
                return false;
        return true;
    }

    private static bool IsPubliclyVisible(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
            if (current.DeclaredAccessibility != Accessibility.Public)
                return false;
        return true;
    }

    private static bool IsCanonicalDiscriminator(string value) =>
        value is { Length: > 0 } &&
        (value[0] is >= 'A' and <= 'Z' or '_') &&
        value.All(static character =>
            character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    private static string ToScreamingSnakeCase(string value)
    {
        if (value.EndsWith("Event", StringComparison.Ordinal))
            value = value.Substring(0, value.Length - 5);
        var builder = new StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index > 0 && char.IsUpper(character) && char.IsLower(value[index - 1]))
                builder.Append('_');
            builder.Append(char.ToUpperInvariant(character));
        }
        return builder.ToString();
    }

    private static string GetProviderName(string moduleId)
    {
        if (string.Equals(moduleId, "hpd.agent.core", StringComparison.Ordinal))
            return "CoreAgentInputModule";
        return $"GeneratedAgentInputModule_{Sanitize(moduleId)}_{Hash(moduleId):x8}";
    }

    private static string GetApplicationCompositionName(string assemblyIdentity) =>
        $"GeneratedAgentInputComposition_{Sanitize(assemblyIdentity)}";

    private static string Sanitize(string value) =>
        Regex.Replace(value, "[^A-Za-z0-9_]", "_");

    private static uint Hash(string value)
    {
        var hash = 2166136261;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= 16777619;
        }
        return hash;
    }

    private static string EscapeString(string value) =>
        value.Replace("\\", "\\\\")
            .Replace("\"", "\\\"");

    private sealed record InputInfo(
        INamedTypeSymbol Symbol,
        string Discriminator,
        bool IsPublic,
        IReadOnlyList<Diagnostic> Diagnostics,
        Location Location)
    {
        public bool IsValid => Symbol.TypeParameters.Length == 0 &&
            !Symbol.IsAbstract &&
            !string.IsNullOrWhiteSpace(Discriminator);
    }
}
