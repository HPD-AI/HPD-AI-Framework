using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HPD.Agent.SourceGenerator.SourceGeneration;

/// <summary>Generates one reflection-free application capability composition for the consuming application.</summary>
[Generator]
public sealed class HPDApplicationCompositionSourceGenerator : IIncrementalGenerator
{
    private const string ProviderManifestAttributeName = "HPD.Agent.Providers.HpdProviderManifestAttribute";
    private const string ApplicationCompositionHostName = "HPD.Agent.HPDApplicationCompositionHost";
    private static readonly DiagnosticDescriptor UnusedProviderExclusion = new(
        "HPDP016",
        "Unused provider exclusion",
        "Excluded provider key '{0}' has no discovered provider manifest",
        "HPD.Provider",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor UnsupportedProviderManifest = new(
        "HPDP017",
        "Unsupported provider manifest",
        "Provider manifest type '{0}' must be public and expose a public static Fragment property",
        "HPD.Provider",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var model = context.CompilationProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, _) => Analyze(pair.Left, pair.Right));

        context.RegisterSourceOutput(model, static (productionContext, model) =>
        {
            foreach (var diagnostic in model.Diagnostics)
                productionContext.ReportDiagnostic(diagnostic);
            if (model.Source is not null)
                productionContext.AddSource("HPDGeneratedApplicationComposition.g.cs", model.Source);
        });
    }

    private static GenerationResult Analyze(Compilation compilation, AnalyzerConfigOptionsProvider options)
    {
        if (!TryGetBoolean(options, "build_property.HpdAgentApplication", defaultValue: false) ||
            compilation.GetTypeByMetadataName(ApplicationCompositionHostName) is null)
            return GenerationResult.Empty;

        var excludedProviders = new HashSet<string>(
            SplitList(GetOption(options, "build_property.HpdAgentExcludeProviders")),
            StringComparer.OrdinalIgnoreCase);
        var providers = new HashSet<string>(StringComparer.Ordinal);
        var suppressedProviderAssemblies = new HashSet<string>(StringComparer.Ordinal);
        var discoveredProviderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        AddAssembly(compilation.Assembly);
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
            AddAssembly(assembly);

        foreach (var excluded in excludedProviders.Where(key => !discoveredProviderKeys.Contains(key)))
            diagnostics.Add(Diagnostic.Create(UnusedProviderExclusion, Location.None, excluded));

        return new GenerationResult(
            Emit(
                providers.OrderBy(static value => value, StringComparer.Ordinal),
                suppressedProviderAssemblies.OrderBy(static value => value, StringComparer.Ordinal),
                compilation.AssemblyName ?? "HPD.Agent.Application"),
            diagnostics.ToImmutable());

        void AddAssembly(IAssemblySymbol assembly)
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                var attributeName = attribute.AttributeClass?.ToDisplayString();
                if (attributeName != ProviderManifestAttributeName ||
                    attribute.ConstructorArguments.Length < 2 ||
                    attribute.ConstructorArguments[0].Value is not INamedTypeSymbol providerType ||
                    attribute.ConstructorArguments[1].Value is not string providerKey)
                    continue;

                discoveredProviderKeys.Add(providerKey);
                if (excludedProviders.Contains(providerKey))
                {
                    suppressedProviderAssemblies.Add(providerType.ContainingAssembly.Name);
                }
                else if (providerType.DeclaredAccessibility != Accessibility.Public ||
                         !providerType.GetMembers("Fragment").OfType<IPropertySymbol>().Any(static property =>
                             property.IsStatic &&
                             property.DeclaredAccessibility == Accessibility.Public &&
                             property.Type.Name == "ProviderManifestFragment"))
                {
                    diagnostics.Add(Diagnostic.Create(
                        UnsupportedProviderManifest,
                        Location.None,
                        providerType.ToDisplayString()));
                }
                else
                {
                    providers.Add(providerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
            }
        }
    }

    private static string Emit(
        IEnumerable<string> providers,
        IEnumerable<string> suppressedProviderAssemblies,
        string assemblyIdentity)
    {
        var normalizedAssembly = new string(assemblyIdentity
            .Select(static character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray());
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine("namespace HPD.Agent.Generated;");
        source.AppendLine();
        source.AppendLine("/// <summary>Provides the complete compile-time application capability composition.</summary>");
        source.AppendLine("internal static class GeneratedHPDApplicationComposition");
        source.AppendLine("{");
        source.AppendLine("    public static global::HPD.Agent.HPDApplicationComposition Application { get; } = new(");
        AppendProviderComposition(source, providers);
        source.AppendLine(",");
        source.AppendLine(
            $"        global::HPD.Agent.Serialization.GeneratedAgentEventComposition_{normalizedAssembly}.Composition,");
        source.AppendLine(
            $"        global::HPD.Agent.Serialization.GeneratedAgentInputComposition_{normalizedAssembly}.Composition");
        source.AppendLine(");");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("internal static class GeneratedHPDApplicationCompositionRegistration");
        source.AppendLine("{");
        source.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        source.AppendLine("    internal static void Register()");
        source.AppendLine("        => global::HPD.Agent.HPDApplicationCompositionHost.Register(");
        source.AppendLine("            GeneratedHPDApplicationComposition.Application,");
        source.AppendLine("            new string[]");
        source.AppendLine("            {");
        foreach (var assembly in suppressedProviderAssemblies)
            source.AppendLine($"                {Literal(assembly)},");
        source.AppendLine("            });");
        source.AppendLine("}");
        return source.ToString();
    }

    private static void AppendProviderComposition(StringBuilder source, IEnumerable<string> providers)
    {
        source.AppendLine("        global::HPD.Agent.Providers.ProviderComposition.Create(");
        source.AppendLine("            new global::HPD.Agent.Providers.ProviderManifestFragment[]");
        source.AppendLine("            {");
        foreach (var provider in providers)
            source.AppendLine($"                {provider}.Fragment,");
        source.AppendLine("            })");
    }

    private static string? GetOption(AnalyzerConfigOptionsProvider options, string key) =>
        options.GlobalOptions.TryGetValue(key, out var value) ? value : null;

    private static bool TryGetBoolean(
        AnalyzerConfigOptionsProvider options,
        string key,
        bool defaultValue) =>
        bool.TryParse(GetOption(options, key), out var value) ? value : defaultValue;

    private static IEnumerable<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value!.Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(static item => item.Trim())
                .Where(static item => item.Length > 0);

    private static string Literal(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private sealed record GenerationResult(
        string? Source,
        ImmutableArray<Diagnostic> Diagnostics)
    {
        public static GenerationResult Empty { get; } =
            new(null, ImmutableArray<Diagnostic>.Empty);
    }
}
