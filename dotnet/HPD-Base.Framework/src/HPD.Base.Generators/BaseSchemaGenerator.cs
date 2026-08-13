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
    private const string JsonIgnoreAttribute = "System.Text.Json.Serialization.JsonIgnoreAttribute";
    private const string JsonConverterAttribute = "System.Text.Json.Serialization.JsonConverterAttribute";
    private const string JsonPropertyNameAttribute = "System.Text.Json.Serialization.JsonPropertyNameAttribute";
    private const string BaseSerializerConverterAttribute = "HPD.Base.BaseSerializerConverterAttribute";

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
            ContextUnionGraphResult unionGraph = ValidateUnionGraph(
                context, serializerContext, pair.Value, contextIdentity, defects);
            results.Add(serializerContext, new ContextValidationResult(
                serializerContext,
                pair.Value.OrderBy(static root => root.ToDisplayString(), StringComparer.Ordinal).ToImmutableArray(),
                BuildOptionReceipt(options),
                unionGraph,
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

    private static ImmutableArray<string> BuildOptionReceipt(AttributeData options)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["AllowDuplicateProperties"] = "false", ["AllowOutOfOrderMetadataProperties"] = "false",
            ["AllowTrailingCommas"] = "false", ["Converters"] = "empty", ["DefaultBufferSize"] = "16384",
            ["DefaultIgnoreCondition"] = "0", ["DictionaryKeyPolicy"] = "0", ["GenerationMode"] = "0",
            ["IgnoreReadOnlyFields"] = "false", ["IgnoreReadOnlyProperties"] = "false", ["IncludeFields"] = "false",
            ["IndentCharacter"] = "32", ["IndentSize"] = "2", ["MaxDepth"] = "64", ["NewLine"] = "0",
            ["NumberHandling"] = "0", ["PreferredObjectCreationHandling"] = "0",
            ["PropertyNameCaseInsensitive"] = "false", ["PropertyNamingPolicy"] = "0",
            ["ReadCommentHandling"] = "0", ["ReferenceHandler"] = "0", ["RespectNullableAnnotations"] = "true",
            ["RespectRequiredConstructorParameters"] = "true", ["UnknownTypeHandling"] = "0",
            ["UnmappedMemberHandling"] = "1", ["UseStringEnumConverter"] = "false", ["WriteIndented"] = "false",
        };
        if (options is not null)
            foreach (KeyValuePair<string, TypedConstant> option in options.NamedArguments)
                values[option.Key] = Normalize(option.Value);
        return values.Select(static pair => pair.Key + "=" + pair.Value).ToImmutableArray();
    }

    private static ContextUnionGraphResult ValidateUnionGraph(
        SourceProductionContext context,
        INamedTypeSymbol serializerContext,
        HashSet<INamedTypeSymbol> owners,
        string contextIdentity,
        ImmutableArray<ContextValidationDefect>.Builder defects)
    {
        var graphRoots = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (INamedTypeSymbol owner in owners)
        {
            graphRoots.Add(owner);
            if (owner.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() == ReadAttribute))
            {
                INamedTypeSymbol row = owner.GetTypeMembers("Row").SingleOrDefault();
                if (row is not null) graphRoots.Add(row);
            }
        }

        var nodes = new Dictionary<INamedTypeSymbol, ContextGraphNode>(SymbolEqualityComparer.Default);
        var converterTypes = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        int totalProperties = 0;
        bool limitReported = false;

        bool Visit(ITypeSymbol input, int depth, int wrappers, ISymbol origin)
        {
            ITypeSymbol type = input;
            if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                if (wrappers >= 16) return Limit(origin);
                return Visit(nullable.TypeArguments[0], depth, wrappers + 1, origin);
            }
            if (type is IArrayTypeSymbol array)
            {
                if (array.Rank != 1) return Unsupported(origin, "multidimensional arrays are forbidden");
                if (wrappers >= 16) return Limit(origin);
                return Visit(array.ElementType, depth, wrappers + 1, origin);
            }
            if (type is INamedTypeSymbol sequence && sequence.IsGenericType &&
                sequence.ConstructedFrom.ToDisplayString() is "System.Collections.Generic.IReadOnlyList<T>" or
                    "System.Collections.Immutable.ImmutableArray<T>")
            {
                if (wrappers >= 16) return Limit(origin);
                return Visit(sequence.TypeArguments[0], depth, wrappers + 1, origin);
            }
            if (SerializerScalar(type)) return true;
            if (depth > 32) return Limit(origin);
            if (type is not INamedTypeSymbol named || named.TypeParameters.Length != 0 ||
                !named.Locations.Any(static location => location.IsInSource))
                return Unsupported(origin, "the reachable serializer graph contains an open, external, or unsupported object node");
            if (nodes.ContainsKey(named)) return true;
            if (nodes.Count >= 256) return Limit(named);

            var node = new ContextGraphNode(named);
            nodes.Add(named, node);
            IPropertySymbol[] properties = SerializableProperties(named)
                .Where(static property => !property.IsStatic && !property.IsIndexer &&
                    property.DeclaredAccessibility == Accessibility.Public && property.GetMethod is not null)
                .OrderBy(static property => property.Name, StringComparer.Ordinal).ToArray();
            if (properties.Length > 256) return Limit(named);
            foreach (IPropertySymbol property in properties)
            {
                AttributeData ignore = Find(property, JsonIgnoreAttribute);
                if (ignore is not null && (ignore.NamedArguments.Length == 0 || NamedInt64(ignore, "Condition", 1) == 1))
                    continue;
                if (ignore is not null)
                {
                    Unsupported(property, "conditional serializer omission is forbidden for authoritative members");
                    continue;
                }
                if (property.SetMethod is null || Find(property, "System.Text.Json.Serialization.JsonExtensionDataAttribute") is not null ||
                    Find(property, "System.Text.Json.Serialization.JsonIncludeAttribute") is not null)
                {
                    Unsupported(property, "active properties require public read/write membership without extension data or JsonInclude");
                    continue;
                }
                string converterIdentity = "stj-built-in";
                string converterType = null;
                AttributeData converterAttribute = Find(property, JsonConverterAttribute);
                if (converterAttribute is not null)
                {
                    INamedTypeSymbol converter = ConstructorType(converterAttribute, 0);
                    AttributeData contract = converter is null ? null : Find(converter, BaseSerializerConverterAttribute);
                    string contractId = ConstructorString(contract, 0);
                    int version = contract?.ConstructorArguments.Length > 1 && contract.ConstructorArguments[1].Value is int selected ? selected : 0;
                    if (!ValidConverter(converter, contractId, version))
                    {
                        Unsupported(property, "the explicit converter does not satisfy the closed converter contract");
                        continue;
                    }
                    converterIdentity = "explicit:" + contractId + ":" + version.ToString(CultureInfo.InvariantCulture);
                    if (converterTypes.TryGetValue(converterIdentity, out INamedTypeSymbol existing) &&
                        !SymbolEqualityComparer.Default.Equals(existing, converter))
                    {
                        Unsupported(property, "the converter contract identity is ambiguous");
                        continue;
                    }
                    converterTypes[converterIdentity] = converter;
                    converterType = converter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
                totalProperties++;
                if (totalProperties > 4096) return Limit(property);
                node.Properties.Add(new ContextGraphProperty(
                    named, property.Name, property.Type, JsonPropertyName(property), property.IsRequired,
                    IsNullable(property), converterIdentity, converterType));
                Visit(property.Type, depth + 1, 0, property);
            }
            return true;
        }

        bool Unsupported(ISymbol symbol, string reason)
        {
            Diagnostic diagnostic = Diagnostic.Create(
                BaseCollectionGenerator.UnsupportedSerializerContract,
                symbol.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None,
                contextIdentity, reason);
            context.ReportDiagnostic(diagnostic);
            defects.Add(new ContextValidationDefect(diagnostic.Id, reason,
                symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            return false;
        }

        bool Limit(ISymbol symbol)
        {
            if (!limitReported)
            {
                Diagnostic diagnostic = Diagnostic.Create(
                    BaseCollectionGenerator.SerializerGraphLimit,
                    symbol.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None,
                    contextIdentity);
                context.ReportDiagnostic(diagnostic);
                defects.Add(new ContextValidationDefect(diagnostic.Id, "unionGraphLimit", "exceeded"));
                limitReported = true;
            }
            return false;
        }

        foreach (INamedTypeSymbol root in graphRoots.OrderBy(static value => value.ToDisplayString(), StringComparer.Ordinal))
            Visit(root, 0, 0, root);

        var byRoot = ImmutableDictionary.CreateBuilder<INamedTypeSymbol, ImmutableArray<ContextGraphProperty>>(SymbolEqualityComparer.Default);
        foreach (INamedTypeSymbol root in graphRoots)
        {
            var closure = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var properties = ImmutableArray.CreateBuilder<ContextGraphProperty>();
            Collect(root, closure, properties);
            byRoot[root] = properties.OrderBy(static property => property.CanonicalKey, StringComparer.Ordinal).ToImmutableArray();
        }
        ImmutableArray<ContextGraphProperty> union = nodes.Values.SelectMany(static node => node.Properties)
            .OrderBy(static property => property.CanonicalKey, StringComparer.Ordinal).ToImmutableArray();
        return new ContextUnionGraphResult(union, byRoot.ToImmutable());

        void Collect(ITypeSymbol input, HashSet<INamedTypeSymbol> closure, ImmutableArray<ContextGraphProperty>.Builder properties)
        {
            ITypeSymbol type = Unwrap(input);
            if (type is not INamedTypeSymbol named || !nodes.TryGetValue(named, out ContextGraphNode node) || !closure.Add(named)) return;
            properties.AddRange(node.Properties);
            foreach (ContextGraphProperty property in node.Properties) Collect(property.PropertyType, closure, properties);
        }
    }

    private static ITypeSymbol Unwrap(ITypeSymbol input)
    {
        ITypeSymbol current = input;
        while (true)
        {
            if (current is IArrayTypeSymbol array) { current = array.ElementType; continue; }
            if (current is INamedTypeSymbol named && named.IsGenericType &&
                (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ||
                 named.ConstructedFrom.ToDisplayString() is "System.Collections.Generic.IReadOnlyList<T>" or
                     "System.Collections.Immutable.ImmutableArray<T>"))
            { current = named.TypeArguments[0]; continue; }
            return current;
        }
    }

    private static bool SerializerScalar(ITypeSymbol type) => type.TypeKind == TypeKind.Enum || type.SpecialType is
        SpecialType.System_String or SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte or
        SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or
        SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double or
        SpecialType.System_Decimal || type.ToDisplayString() is "System.Guid" or "System.DateTime" or "System.DateTimeOffset" or
        "HPD.Base.BaseBinary" or "HPD.Base.BaseVector" or "HPD.Base.RecordId" ||
        type is INamedTypeSymbol named && named.IsGenericType && named.ConstructedFrom.ToDisplayString() == "HPD.Base.BaseRecordId<TRecord>";

    private static IEnumerable<IPropertySymbol> SerializableProperties(INamedTypeSymbol type)
    {
        var chain = new Stack<INamedTypeSymbol>();
        for (INamedTypeSymbol current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            chain.Push(current);
        var properties = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        while (chain.Count != 0)
            foreach (IPropertySymbol property in chain.Pop().GetMembers().OfType<IPropertySymbol>()) properties[property.Name] = property;
        return properties.Values;
    }

    private static AttributeData Find(ISymbol symbol, string name) => symbol?.GetAttributes()
        .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == name);
    private static INamedTypeSymbol ConstructorType(AttributeData attribute, int index) =>
        attribute?.ConstructorArguments.Length > index ? attribute.ConstructorArguments[index].Value as INamedTypeSymbol : null;
    private static string ConstructorString(AttributeData attribute, int index) =>
        attribute?.ConstructorArguments.Length > index ? attribute.ConstructorArguments[index].Value as string : null;
    private static long NamedInt64(AttributeData attribute, string name, long fallback) =>
        attribute?.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value is object value
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : fallback;
    private static string JsonPropertyName(IPropertySymbol property) =>
        ConstructorString(Find(property, JsonPropertyNameAttribute), 0);
    private static bool IsNullable(IPropertySymbol property) => property.NullableAnnotation == NullableAnnotation.Annotated ||
        property.Type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static bool ValidConverter(INamedTypeSymbol converter, string contractId, int version)
    {
        if (converter is null || !converter.IsSealed || converter.IsGenericType || !converter.Locations.Any(static location => location.IsInSource) ||
            string.IsNullOrWhiteSpace(contractId) || version < 1) return false;
        if (converter.BaseType?.OriginalDefinition.ToDisplayString() != "System.Text.Json.Serialization.JsonConverter<T>") return false;
        IMethodSymbol[] constructors = converter.InstanceConstructors.ToArray();
        if (constructors.Length != 1 || constructors[0].DeclaredAccessibility != Accessibility.Public || constructors[0].Parameters.Length != 0) return false;
        foreach (ISymbol member in converter.GetMembers())
        {
            if (member is IFieldSymbol field && (!field.IsConst || !ConverterConstant(field.Type))) return false;
            if (member is IPropertySymbol property && property.IsStatic || member is IEventSymbol) return false;
        }
        return true;
    }

    private static bool ConverterConstant(ITypeSymbol type) => type.TypeKind == TypeKind.Enum || type.SpecialType is
        SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or
        SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or
        SpecialType.System_UInt64 or SpecialType.System_Char or SpecialType.System_String or SpecialType.System_Single or
        SpecialType.System_Double or SpecialType.System_Decimal;
}

internal sealed class ContextValidationResult
{
    internal ContextValidationResult(
        INamedTypeSymbol context,
        ImmutableArray<INamedTypeSymbol> roots,
        ImmutableArray<string> optionReceipt,
        ContextUnionGraphResult unionGraph,
        ImmutableArray<ContextValidationDefect> defects)
    {
        Context = context;
        Roots = roots;
        OptionReceipt = optionReceipt;
        UnionGraph = unionGraph;
        Defects = defects;
    }

    internal INamedTypeSymbol Context { get; }
    internal ImmutableArray<INamedTypeSymbol> Roots { get; }
    internal ImmutableArray<string> OptionReceipt { get; }
    internal ContextUnionGraphResult UnionGraph { get; }
    internal ImmutableArray<ContextValidationDefect> Defects { get; }
    internal bool IsValid => Defects.IsDefaultOrEmpty;
}

internal sealed class ContextUnionGraphResult
{
    private readonly ImmutableDictionary<INamedTypeSymbol, ImmutableArray<ContextGraphProperty>> _propertiesByRoot;

    internal ContextUnionGraphResult(
        ImmutableArray<ContextGraphProperty> properties,
        ImmutableDictionary<INamedTypeSymbol, ImmutableArray<ContextGraphProperty>> propertiesByRoot)
    {
        Properties = properties;
        _propertiesByRoot = propertiesByRoot;
    }

    internal ImmutableArray<ContextGraphProperty> Properties { get; }

    internal ImmutableArray<ContextGraphProperty> PropertiesForRoot(INamedTypeSymbol root) =>
        _propertiesByRoot.TryGetValue(root, out ImmutableArray<ContextGraphProperty> properties)
            ? properties
            : ImmutableArray<ContextGraphProperty>.Empty;
}

internal sealed class ContextGraphNode
{
    internal ContextGraphNode(INamedTypeSymbol type) => Type = type;
    internal INamedTypeSymbol Type { get; }
    internal List<ContextGraphProperty> Properties { get; } = new();
}

internal sealed class ContextGraphProperty
{
    internal ContextGraphProperty(
        INamedTypeSymbol declaringType,
        string applicationName,
        ITypeSymbol propertyType,
        string explicitWireName,
        bool required,
        bool nullable,
        string converterIdentity,
        string converterType)
    {
        DeclaringType = declaringType;
        ApplicationName = applicationName;
        PropertyType = propertyType;
        ExplicitWireName = explicitWireName;
        Required = required;
        Nullable = nullable;
        ConverterIdentity = converterIdentity;
        ConverterType = converterType;
    }

    internal INamedTypeSymbol DeclaringType { get; }
    internal string ApplicationName { get; }
    internal ITypeSymbol PropertyType { get; }
    internal string ExplicitWireName { get; }
    internal bool Required { get; }
    internal bool Nullable { get; }
    internal string ConverterIdentity { get; }
    internal string ConverterType { get; }
    internal string CanonicalKey => DeclaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "\0" +
        ApplicationName + "\0" + PropertyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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
