using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HPD.Base.Generators;

/// <summary>Generates the complete collection and registered-read schema through one context authority.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class BaseSchemaGenerator : IIncrementalGenerator
{
    private const string CollectionAttribute = "HPD.Base.BaseCollectionAttribute";
    private const string ReadAttribute = "HPD.Base.BaseReadAttribute";
    private const string JsonOptionsAttribute = "System.Text.Json.Serialization.JsonSourceGenerationOptionsAttribute";

    private static readonly DiagnosticDescriptor StringEnumOption = new(
        "HPDBASE0450",
        "Context-wide string-enum conversion is unsupported",
        "Serializer context '{0}' enables UseStringEnumConverter, which is unsupported for authoritative BASE contracts ({1} dependent roots); use the closed enum representation or an explicitly versioned property converter",
        "HPD.Base.Generation", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor UnsupportedOption = new(
        "HPDBASE0451",
        "Authoritative serializer-context option is unsupported",
        "Serializer context '{0}' option '{1}' has unsupported value '{2}' for authoritative BASE contracts ({3} dependent roots)",
        "HPD.Base.Generation", DiagnosticSeverity.Error, true);

    /// <summary>Initializes the combined incremental schema pipeline.</summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<INamedTypeSymbol> collections =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                CollectionAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol);
        IncrementalValuesProvider<INamedTypeSymbol> reads =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                ReadAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol);

        context.RegisterSourceOutput(collections.Collect().Combine(reads.Collect()),
            static (productionContext, roots) => Generate(productionContext, roots.Left, roots.Right));
        BaseCollectionGenerator.RegisterForbiddenReferences(context);
    }

    private static void Generate(
        SourceProductionContext context,
        ImmutableArray<INamedTypeSymbol> collections,
        ImmutableArray<INamedTypeSymbol> reads)
    {
        var rootsByContext = new Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        AddRoots(rootsByContext, collections, CollectionAttribute);
        AddRoots(rootsByContext, reads, ReadAttribute);

        var results = ImmutableDictionary.CreateBuilder<INamedTypeSymbol, ContextValidationResult>(SymbolEqualityComparer.Default);
        foreach (KeyValuePair<INamedTypeSymbol, HashSet<INamedTypeSymbol>> pair in rootsByContext
            .OrderBy(static pair => ContextIdentity(pair.Key), StringComparer.Ordinal))
        {
            INamedTypeSymbol serializerContext = pair.Key;
            int rootCount = pair.Value.Count;
            var defects = ImmutableArray.CreateBuilder<ContextValidationDefect>();
            string contextIdentity = ContextIdentity(serializerContext);
            if (contextIdentity.Length == 0 || Encoding.UTF8.GetByteCount(contextIdentity) > 512 ||
                contextIdentity.Any(char.IsControl))
            {
                Diagnostic identityDiagnostic = Diagnostic.Create(
                    BaseCollectionGenerator.SerializerGraphLimit,
                    serializerContext.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None,
                    serializerContext.MetadataName);
                context.ReportDiagnostic(identityDiagnostic);
                defects.Add(new ContextValidationDefect(identityDiagnostic.Id, "contextIdentity", "outOfBounds"));
            }
            AttributeData options = serializerContext.GetAttributes().FirstOrDefault(static attribute =>
                attribute.AttributeClass?.ToDisplayString() == JsonOptionsAttribute);
            if (options is not null && defects.Count == 0)
            {
                foreach (KeyValuePair<string, TypedConstant> option in options.NamedArguments
                    .OrderBy(static option => option.Key, StringComparer.Ordinal))
                {
                    if (IsAcceptedOption(option.Key, option.Value)) continue;
                    Location location = OptionLocation(options, option.Key, serializerContext);
                    Diagnostic diagnostic;
                    if (option.Key == "UseStringEnumConverter" && IsTrue(option.Value))
                    {
                        diagnostic = Diagnostic.Create(
                            StringEnumOption, location, contextIdentity, rootCount);
                    }
                    else
                    {
                        diagnostic = Diagnostic.Create(
                            UnsupportedOption, location, contextIdentity, option.Key, Normalize(option.Value),
                            rootCount);
                    }
                    context.ReportDiagnostic(diagnostic);
                    defects.Add(new ContextValidationDefect(diagnostic.Id, option.Key, Normalize(option.Value)));
                }
            }
            results.Add(serializerContext, new ContextValidationResult(
                serializerContext,
                pair.Value.OrderBy(static root => root.ToDisplayString(), StringComparer.Ordinal).ToImmutableArray(),
                defects.ToImmutable()));
        }

        ImmutableDictionary<INamedTypeSymbol, ContextValidationResult> contextResults = results.ToImmutable();
        BaseCollectionGenerator.GenerateCombined(context, collections, contextResults);
        BaseReadGenerator.GenerateCombined(context, reads, contextResults);
    }

    private static void AddRoots(
        Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>> rootsByContext,
        ImmutableArray<INamedTypeSymbol> roots,
        string attributeName)
    {
        foreach (INamedTypeSymbol root in roots.Distinct(SymbolEqualityComparer.Default))
        {
            AttributeData declaration = root.GetAttributes().FirstOrDefault(attribute =>
                attribute.AttributeClass?.ToDisplayString() == attributeName);
            if (declaration?.ConstructorArguments.Length < 2 ||
                declaration.ConstructorArguments[1].Value is not INamedTypeSymbol serializerContext)
                continue;
            if (!rootsByContext.TryGetValue(serializerContext, out HashSet<INamedTypeSymbol> owners))
            {
                owners = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                rootsByContext.Add(serializerContext, owners);
            }
            owners.Add(root);
        }
    }

    internal static bool IsAcceptedOption(string name, TypedConstant value)
    {
        if (name == "Converters") return value.Kind == TypedConstantKind.Array &&
            (value.Values.IsDefaultOrEmpty || value.Values.Length == 0);
        if (!TryInt64(value, out long selected)) return false;
        if (name == "PropertyNamingPolicy") return selected is >= 0 and <= 5;
        if (name == "GenerationMode") return selected is 0 or 1;
        if (name is "UseStringEnumConverter" or "IncludeFields" or "IgnoreReadOnlyFields" or
            "IgnoreReadOnlyProperties" or "WriteIndented" or "AllowDuplicateProperties" or
            "AllowOutOfOrderMetadataProperties" or "AllowTrailingCommas" or
            "PropertyNameCaseInsensitive") return selected == 0;
        if (name is "NumberHandling" or "DefaultIgnoreCondition" or "DictionaryKeyPolicy" or
            "PreferredObjectCreationHandling" or "ReadCommentHandling" or "ReferenceHandler" or
            "UnknownTypeHandling" or "NewLine") return selected == 0;
        if (name == "UnmappedMemberHandling") return selected == 1;
        if (name is "RespectNullableAnnotations" or "RespectRequiredConstructorParameters") return selected == 1;
        if (name == "MaxDepth") return selected == 64;
        if (name == "DefaultBufferSize") return selected == 16384;
        if (name == "IndentCharacter") return selected == 32;
        if (name == "IndentSize") return selected == 2;
        return false;
    }

    private static bool IsTrue(TypedConstant value) => TryInt64(value, out long selected) && selected == 1;

    private static bool TryInt64(TypedConstant value, out long selected)
    {
        try
        {
            if (value.Value is null) { selected = 0; return true; }
            selected = Convert.ToInt64(value.Value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception)
        {
            selected = 0;
            return false;
        }
    }

    private static string Normalize(TypedConstant value)
    {
        if (value.Kind == TypedConstantKind.Array) return "array";
        if (value.Value is bool boolean) return boolean ? "true" : "false";
        if (TryInt64(value, out long selected)) return selected.ToString(CultureInfo.InvariantCulture);
        return value.Value is null ? "null" : "unsupported";
    }

    private static Location OptionLocation(AttributeData options, string optionName, INamedTypeSymbol serializerContext)
    {
        if (options.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax syntax)
        {
            AttributeArgumentSyntax argument = syntax.ArgumentList?.Arguments.FirstOrDefault(item =>
                item.NameEquals?.Name.Identifier.ValueText == optionName);
            if (argument is not null) return argument.Expression.GetLocation();
            return syntax.GetLocation();
        }
        return serializerContext.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None;
    }

    private static string ContextIdentity(INamedTypeSymbol context)
    {
        var segments = new Stack<string>();
        for (INamedTypeSymbol current = context; current is not null; current = current.ContainingType)
            segments.Push(current.MetadataName);
        string type = string.Join("+", segments);
        string ns = context.ContainingNamespace?.IsGlobalNamespace == false
            ? context.ContainingNamespace.ToDisplayString() + "."
            : string.Empty;
        return ns + type;
    }
}

internal sealed class ContextValidationResult
{
    internal ContextValidationResult(
        INamedTypeSymbol context,
        ImmutableArray<INamedTypeSymbol> roots,
        ImmutableArray<ContextValidationDefect> defects)
    {
        Context = context;
        Roots = roots;
        Defects = defects;
    }

    internal INamedTypeSymbol Context { get; }
    internal ImmutableArray<INamedTypeSymbol> Roots { get; }
    internal ImmutableArray<ContextValidationDefect> Defects { get; }
    internal bool IsValid => Defects.IsDefaultOrEmpty;
}

internal readonly struct ContextValidationDefect
{
    internal ContextValidationDefect(string diagnosticId, string rule, string normalizedValue)
    {
        DiagnosticId = diagnosticId;
        Rule = rule;
        NormalizedValue = normalizedValue;
    }

    internal string DiagnosticId { get; }
    internal string Rule { get; }
    internal string NormalizedValue { get; }
}
