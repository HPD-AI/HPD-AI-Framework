using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using HPD.Agent.ToolHarness.Coding.SourceGenerator.Diagnostics;
using HPD.Agent.ToolHarness.Coding.SourceGenerator.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HPD.Agent.ToolHarness.Coding.SourceGenerator;

internal sealed record LanguageServerProviderInfo(
    string Id,
    string ClassName,
    string FullyQualifiedTypeName,
    IReadOnlyList<string> Extensions,
    IReadOnlyDictionary<string, string> LanguageIds,
    IReadOnlyList<string> RootMarkers,
    IReadOnlyList<string> ExcludeRootMarkers,
    string? Executable,
    IReadOnlyList<string> Arguments,
    bool Experimental,
    bool DisabledByDefault,
    bool ImplementsProvider);

[Generator]
public sealed class LanguageServerSourceGenerator : IIncrementalGenerator
{
    private const string HpdLanguageServerAttribute = "HPDOS.ToolHarnesses.Middleware.HpdLanguageServerAttribute";
    private const string ExtensionsAttribute = "HPDOS.ToolHarnesses.Middleware.LanguageServerExtensionsAttribute";
    private const string LanguageIdsAttribute = "HPDOS.ToolHarnesses.Middleware.LanguageServerLanguageIdsAttribute";
    private const string RootMarkersAttribute = "HPDOS.ToolHarnesses.Middleware.LanguageServerRootMarkersAttribute";
    private const string ExcludeRootMarkersAttribute = "HPDOS.ToolHarnesses.Middleware.LanguageServerExcludeRootMarkersAttribute";
    private const string ExecutableAttribute = "HPDOS.ToolHarnesses.Middleware.LanguageServerExecutableAttribute";
    private const string ArgumentsAttribute = "HPDOS.ToolHarnesses.Middleware.LanguageServerArgumentsAttribute";
    private const string ExperimentalAttribute = "HPDOS.ToolHarnesses.Middleware.LanguageServerExperimentalAttribute";
    private const string DisabledByDefaultAttribute = "HPDOS.ToolHarnesses.Middleware.LanguageServerDisabledByDefaultAttribute";
    private const string ProviderInterface = "HPDOS.ToolHarnesses.Middleware.ILanguageServerProvider";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var providerClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HpdLanguageServerAttribute,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => ctx.TargetSymbol as INamedTypeSymbol)
            .Where(static symbol => symbol is not null)
            .Collect();

        context.RegisterSourceOutput(providerClasses, static (ctx, symbols) =>
        {
            Execute(ctx, symbols!);
        });
    }

    private static void Execute(
        SourceProductionContext context,
        ImmutableArray<INamedTypeSymbol?> symbols)
    {
        var providers = new List<LanguageServerProviderInfo>();
        var seenIds = new Dictionary<string, string>();

        foreach (var symbol in symbols)
        {
            if (symbol is null)
                continue;

            var provider = ResolveProvider(context, symbol);
            if (provider is null)
                continue;

            if (seenIds.TryGetValue(provider.Id, out var existingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    LanguageServerGeneratorDiagnostics.DuplicateServerId,
                    symbol.Locations.FirstOrDefault(),
                    provider.Id,
                    existingType,
                    symbol.Name));
                continue;
            }

            seenIds[provider.Id] = symbol.Name;
            providers.Add(provider);
        }

        RegistryGenerator.Generate(context, providers);
    }

    private static LanguageServerProviderInfo? ResolveProvider(
        SourceProductionContext context,
        INamedTypeSymbol symbol)
    {
        if (symbol.DeclaredAccessibility != Accessibility.Public)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                LanguageServerGeneratorDiagnostics.ProviderNotPublic,
                symbol.Locations.FirstOrDefault(),
                symbol.Name));
            return null;
        }

        var id = GetSingleStringConstructorArgument(symbol, HpdLanguageServerAttribute);
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var extensions = GetStringArrayConstructorArguments(symbol, ExtensionsAttribute);
        if (extensions.Count == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                LanguageServerGeneratorDiagnostics.MissingExtensions,
                symbol.Locations.FirstOrDefault(),
                symbol.Name));
            return null;
        }

        var hasInvalid = false;
        foreach (var extension in extensions)
        {
            if (string.IsNullOrWhiteSpace(extension) || !extension.StartsWith(".", System.StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    LanguageServerGeneratorDiagnostics.InvalidExtension,
                    symbol.Locations.FirstOrDefault(),
                    symbol.Name,
                    extension));
                hasInvalid = true;
            }
        }

        var languageIdPairs = GetStringArrayConstructorArguments(symbol, LanguageIdsAttribute);
        if (languageIdPairs.Count % 2 != 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                LanguageServerGeneratorDiagnostics.OddLanguageIdMapping,
                symbol.Locations.FirstOrDefault(),
                symbol.Name));
            hasInvalid = true;
        }

        var languageIds = new Dictionary<string, string>();
        for (var index = 0; index + 1 < languageIdPairs.Count; index += 2)
        {
            var extension = languageIdPairs[index];
            if (!extensions.Contains(extension, System.StringComparer.OrdinalIgnoreCase))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    LanguageServerGeneratorDiagnostics.LanguageIdExtensionNotDeclared,
                    symbol.Locations.FirstOrDefault(),
                    symbol.Name,
                    extension));
                hasInvalid = true;
            }

            languageIds[extension] = languageIdPairs[index + 1];
        }

        var rootMarkers = GetStringArrayConstructorArguments(symbol, RootMarkersAttribute);
        var seenMarkers = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var marker in rootMarkers)
        {
            if (!seenMarkers.Add(marker))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    LanguageServerGeneratorDiagnostics.DuplicateRootMarker,
                    symbol.Locations.FirstOrDefault(),
                    symbol.Name,
                    marker));
                hasInvalid = true;
            }
        }

        var implementsProvider = Implements(symbol, ProviderInterface);
        var executable = GetSingleStringConstructorArgument(symbol, ExecutableAttribute);

        if (!implementsProvider && string.IsNullOrWhiteSpace(executable))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                LanguageServerGeneratorDiagnostics.MissingExecutable,
                symbol.Locations.FirstOrDefault(),
                symbol.Name));
            hasInvalid = true;
        }

        if (hasInvalid)
            return null;

        return new LanguageServerProviderInfo(
            Id: id!,
            ClassName: symbol.Name,
            FullyQualifiedTypeName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Extensions: extensions,
            LanguageIds: languageIds,
            RootMarkers: rootMarkers,
            ExcludeRootMarkers: GetStringArrayConstructorArguments(symbol, ExcludeRootMarkersAttribute),
            Executable: executable,
            Arguments: GetStringArrayConstructorArguments(symbol, ArgumentsAttribute),
            Experimental: HasAttribute(symbol, ExperimentalAttribute),
            DisabledByDefault: HasAttribute(symbol, DisabledByDefaultAttribute),
            ImplementsProvider: implementsProvider);
    }

    private static bool Implements(INamedTypeSymbol symbol, string interfaceName)
        => symbol.AllInterfaces.Any(candidate => candidate.ToDisplayString() == interfaceName);

    private static bool HasAttribute(INamedTypeSymbol symbol, string attributeName)
        => symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == attributeName);

    private static string? GetSingleStringConstructorArgument(INamedTypeSymbol symbol, string attributeName)
    {
        var attribute = symbol.GetAttributes()
            .FirstOrDefault(candidate => candidate.AttributeClass?.ToDisplayString() == attributeName);
        return attribute?.ConstructorArguments.FirstOrDefault().Value as string;
    }

    private static IReadOnlyList<string> GetStringArrayConstructorArguments(INamedTypeSymbol symbol, string attributeName)
    {
        var attribute = symbol.GetAttributes()
            .FirstOrDefault(candidate => candidate.AttributeClass?.ToDisplayString() == attributeName);
        if (attribute is null || attribute.ConstructorArguments.Length == 0)
            return [];

        var argument = attribute.ConstructorArguments[0];
        if (argument.Kind == TypedConstantKind.Array)
            return argument.Values.Select(value => value.Value as string ?? string.Empty).ToArray();

        return argument.Value is string value ? [value] : [];
    }
}
