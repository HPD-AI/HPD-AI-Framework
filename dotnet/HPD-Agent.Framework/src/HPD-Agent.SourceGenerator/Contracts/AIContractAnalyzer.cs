using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace HPD.Agent.SourceGenerator.Contracts;

/// <summary>
/// Builds the compile-time semantic contract for model-facing AI-function types.
/// </summary>
internal static class AIContractAnalyzer
{
    /// <summary>
    /// Analyzes a supported model-facing contract recursively.
    /// </summary>
    /// <param name="declaredType">The type as declared on the function or containing contract.</param>
    /// <param name="path">The stable model-facing path used in diagnostics.</param>
    /// <param name="description">The optional model-facing description.</param>
    /// <param name="location">The source location to associate with diagnostics.</param>
    /// <returns>The analyzed node, or a diagnostic when the type is not supported by this phase.</returns>
    public static AIContractAnalysisResult Analyze(
        ITypeSymbol declaredType,
        string path,
        string? description,
        Location? location = null)
    {
        var activeTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        return AnalyzeCore(declaredType, path, description, location ?? Location.None, activeTypes);
    }

    private static AIContractAnalysisResult AnalyzeCore(
        ITypeSymbol declaredType,
        string path,
        string? description,
        Location location,
        HashSet<ITypeSymbol> activeTypes)
    {
        var (type, allowsNull) = UnwrapNullable(declaredType);

        if (type.SpecialType is SpecialType.System_Object || type.TypeKind is TypeKind.Dynamic)
        {
            return Failure(
                AIContractDiagnosticDescriptors.OpenModelType,
                location,
                path,
                Display(type));
        }

        if (!activeTypes.Add(type))
        {
            return Failure(
                AIContractDiagnosticDescriptors.RecursiveContract,
                location,
                path,
                Display(type));
        }

        try
        {
            if (type.TypeKind is TypeKind.Enum && HasAttribute(type, "System.FlagsAttribute"))
            {
                return Failure(AIContractDiagnosticDescriptors.FlagsEnum, location, path, Display(type));
            }

            if (TryAnalyzeScalar(declaredType, type, allowsNull, description, out var scalar))
            {
                return AIContractAnalysisResult.Success(scalar);
            }

            if (type is IArrayTypeSymbol array)
            {
                var item = AnalyzeCore(array.ElementType, path + "[]", null, location, activeTypes);
                return item.Contract is null
                    ? item
                    : AIContractAnalysisResult.Success(
                        new ArrayContractNode(declaredType, allowsNull, description, item.Contract));
            }

            if (type is INamedTypeSymbol named && TryGetDictionaryValue(named, out var valueType))
            {
                var value = AnalyzeCore(valueType, path + "{}", null, location, activeTypes);
                return value.Contract is null
                    ? value
                    : AIContractAnalysisResult.Success(
                        new DictionaryContractNode(declaredType, allowsNull, description, value.Contract));
            }

            if (type is INamedTypeSymbol possibleDictionary && TryGetNonStringDictionaryKey(possibleDictionary, out var keyType))
            {
                return Failure(
                    AIContractDiagnosticDescriptors.NonStringDictionaryKey,
                    location,
                    path,
                    Display(keyType));
            }

            if (type is INamedTypeSymbol collection && TryGetCollectionItem(collection, out var itemType))
            {
                var item = AnalyzeCore(itemType, path + "[]", null, location, activeTypes);
                return item.Contract is null
                    ? item
                    : AIContractAnalysisResult.Success(
                        new ArrayContractNode(declaredType, allowsNull, description, item.Contract));
            }

            if (type is INamedTypeSymbol customCollection && ImplementsKnownCollection(customCollection))
            {
                return Failure(
                    AIContractDiagnosticDescriptors.UnsupportedModelType,
                    location,
                    path,
                    Display(type));
            }

            if (type is INamedTypeSymbol objectType)
            {
                var union = TryAnalyzeUnion(objectType, declaredType, allowsNull, path, description, location, activeTypes);
                if (union is not null)
                {
                    return union;
                }

                return AnalyzeObject(objectType, declaredType, allowsNull, path, description, location, activeTypes);
            }

            return Failure(
                AIContractDiagnosticDescriptors.UnsupportedModelType,
                location,
                path,
                Display(type));
        }
        finally
        {
            activeTypes.Remove(type);
        }
    }

    private static bool TryAnalyzeScalar(
        ITypeSymbol declaredType,
        ITypeSymbol effectiveType,
        bool allowsNull,
        string? description,
        out ScalarContractNode contract)
    {
        if (effectiveType.TypeKind is TypeKind.Enum)
        {
            var fields = effectiveType.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(static field => field.HasConstantValue && !field.IsImplicitlyDeclared)
                .ToImmutableArray();
            var values = fields.Select(static field => field.Name).ToImmutableArray();
            var describedValues = fields.Select(field => (field.Name, Description: GetDescription(field)))
                .Where(static field => !string.IsNullOrWhiteSpace(field.Description))
                .Select(static field => field.Name + ": " + field.Description);
            var valueDocumentation = string.Join(" ", describedValues);
            var effectiveDescription = valueDocumentation.Length == 0 ? description
                : string.IsNullOrWhiteSpace(description) ? valueDocumentation : description + " " + valueDocumentation;
            contract = new ScalarContractNode(declaredType, allowsNull, effectiveDescription, AIScalarKind.Enum, AllowedValues: values);
            return true;
        }

        var kind = effectiveType.SpecialType switch
        {
            SpecialType.System_String or SpecialType.System_Char => AIScalarKind.String,
            SpecialType.System_Boolean => AIScalarKind.Boolean,
            SpecialType.System_Byte or SpecialType.System_SByte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 => AIScalarKind.Integer,
            SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal => AIScalarKind.Number,
            _ => (AIScalarKind?)null
        };

        var format = GetWellKnownStringFormat(effectiveType);
        if (kind is null && format is not null)
        {
            kind = AIScalarKind.String;
        }

        if (kind is null)
        {
            contract = null!;
            return false;
        }

        contract = new ScalarContractNode(declaredType, allowsNull, description, kind.Value, format);
        return true;
    }

    private static string? GetWellKnownStringFormat(ITypeSymbol type) => Display(type) switch
    {
        "System.Guid" => "uuid",
        "System.DateTime" or "System.DateTimeOffset" => "date-time",
        "System.DateOnly" => "date",
        "System.TimeOnly" => "time",
        "System.TimeSpan" => "duration",
        _ => null
    };

    private static bool TryGetCollectionItem(INamedTypeSymbol type, out ITypeSymbol itemType)
    {
        var candidate = type;
        if (candidate.TypeArguments.Length == 1 && CollectionMetadataNames.Contains(MetadataName(candidate.OriginalDefinition)))
        {
            itemType = candidate.TypeArguments[0];
            return true;
        }

        itemType = null!;
        return false;
    }

    private static bool TryGetDictionaryValue(INamedTypeSymbol type, out ITypeSymbol valueType)
    {
        var candidate = type;
        if (candidate.TypeArguments.Length == 2 && DictionaryMetadataNames.Contains(MetadataName(candidate.OriginalDefinition)))
        {
            if (candidate.TypeArguments[0].SpecialType is SpecialType.System_String)
            {
                valueType = candidate.TypeArguments[1];
                return true;
            }

            valueType = null!;
            return false;
        }

        valueType = null!;
        return false;
    }

    private static bool TryGetNonStringDictionaryKey(INamedTypeSymbol type, out ITypeSymbol keyType)
    {
        foreach (var candidate in type.AllInterfaces.Concat(new[] { type }))
        {
            if (candidate.TypeArguments.Length == 2 && DictionaryMetadataNames.Contains(MetadataName(candidate.OriginalDefinition)))
            {
                keyType = candidate.TypeArguments[0];
                return keyType.SpecialType is not SpecialType.System_String;
            }
        }

        keyType = null!;
        return false;
    }

    private static bool ImplementsKnownCollection(INamedTypeSymbol type) =>
        type.AllInterfaces.Any(candidate =>
            CollectionMetadataNames.Contains(MetadataName(candidate.OriginalDefinition)) ||
            DictionaryMetadataNames.Contains(MetadataName(candidate.OriginalDefinition)));

    private static AIContractAnalysisResult? TryAnalyzeUnion(
        INamedTypeSymbol type,
        ITypeSymbol declaredType,
        bool allowsNull,
        string path,
        string? description,
        Location location,
        HashSet<ITypeSymbol> activeTypes)
    {
        var polymorphic = GetAttribute(type, "System.Text.Json.Serialization.JsonPolymorphicAttribute");
        if (polymorphic is null)
        {
            return null;
        }

        var discriminatorName = polymorphic.NamedArguments
            .FirstOrDefault(static pair => pair.Key == "TypeDiscriminatorPropertyName")
            .Value.Value as string;
        if (string.IsNullOrWhiteSpace(discriminatorName))
        {
            return Failure(AIContractDiagnosticDescriptors.InvalidUnion, location, path, "the discriminator property name must be a nonblank string");
        }

        var derivedAttributes = type.GetAttributes()
            .Where(static attribute => AttributeName(attribute) == "System.Text.Json.Serialization.JsonDerivedTypeAttribute")
            .ToArray();
        if (derivedAttributes.Length == 0)
        {
            return Failure(AIContractDiagnosticDescriptors.InvalidUnion, location, path, "at least one JsonDerivedType case is required");
        }

        var seenDiscriminators = new HashSet<string>(StringComparer.Ordinal);
        var cases = ImmutableArray.CreateBuilder<AIUnionCase>(derivedAttributes.Length);
        foreach (var attribute in derivedAttributes)
        {
            if (attribute.ConstructorArguments.Length < 2 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol concreteType ||
                attribute.ConstructorArguments[1].Value is not string discriminator ||
                string.IsNullOrWhiteSpace(discriminator))
            {
                return Failure(AIContractDiagnosticDescriptors.InvalidUnion, location, path, "every case must use a nonblank string discriminator");
            }

            if (!seenDiscriminators.Add(discriminator))
            {
                return Failure(AIContractDiagnosticDescriptors.InvalidUnion, location, path, $"duplicate discriminator '{discriminator}'");
            }

            if (!IsAssignableTo(concreteType, type) || concreteType.IsAbstract)
            {
                return Failure(AIContractDiagnosticDescriptors.InvalidUnion, location, path, $"case type '{Display(concreteType)}' must be a non-abstract subtype of '{Display(type)}'");
            }

            if (!activeTypes.Add(concreteType))
            {
                return Failure(AIContractDiagnosticDescriptors.RecursiveContract, location, path + "." + discriminator, Display(concreteType));
            }

            AIContractAnalysisResult caseResult;
            try
            {
                caseResult = AnalyzeObject(
                    concreteType,
                    concreteType,
                    allowsNull: false,
                    path + "." + discriminator,
                    description: null,
                    location,
                    activeTypes);
            }
            finally
            {
                activeTypes.Remove(concreteType);
            }
            if (caseResult.Contract is not ObjectContractNode objectContract)
            {
                return caseResult;
            }

            if (objectContract.Properties.Any(property => string.Equals(property.JsonName, discriminatorName, StringComparison.Ordinal)))
            {
                return Failure(AIContractDiagnosticDescriptors.InvalidUnion, location, path, $"case '{discriminator}' declares a property that collides with discriminator '{discriminatorName}'");
            }

            cases.Add(new AIUnionCase(discriminator, concreteType, objectContract));
        }

        return AIContractAnalysisResult.Success(
            new UnionContractNode(declaredType, allowsNull, description, discriminatorName!, cases.ToImmutable()));
    }

    private static AIContractAnalysisResult AnalyzeObject(
        INamedTypeSymbol type,
        ITypeSymbol declaredType,
        bool allowsNull,
        string path,
        string? description,
        Location location,
        HashSet<ITypeSymbol> activeTypes)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsRefLikeType || type.IsAbstract)
        {
            return Failure(AIContractDiagnosticDescriptors.UnsupportedModelType, location, path, Display(type));
        }

        var properties = GetSerializableProperties(type).ToArray();
        var byClrName = properties.ToDictionary(static property => property.Name, StringComparer.OrdinalIgnoreCase);
        var constructors = type.InstanceConstructors
            .Where(IsAccessibleConstructor)
            .Where(constructor => constructor.Parameters.All(parameter => byClrName.ContainsKey(parameter.Name)))
            .ToArray();
        var attributedConstructors = constructors.Where(constructor => HasAttribute(constructor, "System.Text.Json.Serialization.JsonConstructorAttribute")).ToArray();
        if (attributedConstructors.Length > 1)
        {
            return Failure(AIContractDiagnosticDescriptors.AmbiguousConstruction, location, path, Display(type));
        }

        IMethodSymbol? constructor = attributedConstructors.SingleOrDefault();
        if (constructor is null)
        {
            var parameterized = constructors.Where(static candidate => candidate.Parameters.Length > 0).ToArray();
            var parameterless = constructors.Where(static candidate => candidate.Parameters.Length == 0).ToArray();
            if (parameterized.Length == 1)
            {
                constructor = parameterized[0];
            }
            else if (parameterized.Length == 0 && parameterless.Length == 1)
            {
                constructor = parameterless[0];
            }
            else
            {
                return Failure(AIContractDiagnosticDescriptors.AmbiguousConstruction, location, path, Display(type));
            }
        }

        var constructorParameters = constructor.Parameters.ToDictionary(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
        var contractProperties = ImmutableArray.CreateBuilder<AIContractProperty>(properties.Length);
        var bindings = ImmutableArray.CreateBuilder<AIContractMemberBinding>(properties.Length);
        var jsonNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in properties)
        {
            var jsonName = GetJsonName(property);
            if (!jsonNames.Add(jsonName))
            {
                return Failure(AIContractDiagnosticDescriptors.DuplicateJsonName, property.Locations.FirstOrDefault() ?? location, path, jsonName);
            }

            constructorParameters.TryGetValue(property.Name, out var constructorParameter);
            if (constructorParameter is null &&
                (property.SetMethod is null || property.SetMethod.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)))
            {
                return Failure(AIContractDiagnosticDescriptors.AmbiguousConstruction, property.Locations.FirstOrDefault() ?? location, path + "." + jsonName, Display(type));
            }

            var nested = AnalyzeCore(property.Type, path + "." + jsonName, GetDescription(property), property.Locations.FirstOrDefault() ?? location, activeTypes);
            if (nested.Contract is null)
            {
                return nested;
            }

            var required = constructorParameter is not null
                ? !constructorParameter.HasExplicitDefaultValue
                : property.IsRequired ||
                    HasAttribute(property, "System.Text.Json.Serialization.JsonRequiredAttribute") ||
                    !AllowsNull(property.Type);
            contractProperties.Add(new AIContractProperty(property, jsonName, nested.Contract, required, GetDescription(property)));
            bindings.Add(new AIContractMemberBinding(property, constructorParameter));
        }

        var orderedProperties = contractProperties
            .OrderBy(property => ConstructorOrdinal(constructor, property.Symbol.Name))
            .ThenBy(static property => property.JsonName, StringComparer.Ordinal)
            .ToImmutableArray();
        return AIContractAnalysisResult.Success(
            new ObjectContractNode(
                declaredType,
                allowsNull,
                description,
                orderedProperties,
                new AIContractConstructionPlan(constructor, bindings.ToImmutable())));
    }

    private static IEnumerable<IPropertySymbol> GetSerializableProperties(INamedTypeSymbol type)
    {
        var hierarchy = new Stack<INamedTypeSymbol>();
        for (var current = type; current is not null && current.SpecialType is not SpecialType.System_Object; current = current.BaseType)
        {
            hierarchy.Push(current);
        }

        while (hierarchy.Count > 0)
        {
            foreach (var property in hierarchy.Pop().GetMembers().OfType<IPropertySymbol>())
            {
                if (!property.IsStatic && !property.IsIndexer && property.GetMethod is not null &&
                    property.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal &&
                    !IsAlwaysIgnored(property))
                {
                    yield return property;
                }
            }
        }
    }

    private static bool IsAlwaysIgnored(IPropertySymbol property)
    {
        var attribute = GetAttribute(property, "System.Text.Json.Serialization.JsonIgnoreAttribute");
        if (attribute is null)
        {
            return false;
        }

        var condition = attribute.NamedArguments.FirstOrDefault(static pair => pair.Key == "Condition").Value;
        return condition.IsNull || condition.Value is null || Convert.ToInt32(condition.Value) == 0;
    }

    private static string GetJsonName(IPropertySymbol property)
    {
        var attribute = GetAttribute(property, "System.Text.Json.Serialization.JsonPropertyNameAttribute");
        if (attribute?.ConstructorArguments.FirstOrDefault().Value is string explicitName)
        {
            return explicitName;
        }

        return property.Name.Length == 0 || char.IsLower(property.Name[0])
            ? property.Name
            : char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
    }

    private static string? GetDescription(ISymbol symbol) =>
        GetAttribute(symbol, "System.ComponentModel.DescriptionAttribute")?.ConstructorArguments.FirstOrDefault().Value as string;

    private static bool AllowsNull(ITypeSymbol type) =>
        type.NullableAnnotation is NullableAnnotation.Annotated ||
        type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T;

    private static bool IsAccessibleConstructor(IMethodSymbol constructor) =>
        !constructor.IsStatic && constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal;

    private static int ConstructorOrdinal(IMethodSymbol constructor, string propertyName)
    {
        for (var index = 0; index < constructor.Parameters.Length; index++)
        {
            if (string.Equals(constructor.Parameters[index].Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static bool IsAssignableTo(INamedTypeSymbol type, INamedTypeSymbol expectedBase)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, expectedBase))
            {
                return true;
            }
        }

        return type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, expectedBase));
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName) => GetAttribute(symbol, metadataName) is not null;

    private static AttributeData? GetAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().FirstOrDefault(attribute => AttributeName(attribute) == metadataName);

    private static string? AttributeName(AttributeData attribute) =>
        attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static (ITypeSymbol Type, bool AllowsNull) UnwrapNullable(ITypeSymbol type)
    {
        var allowsNull = type.NullableAnnotation is NullableAnnotation.Annotated;
        if (type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T)
        {
            return (named.TypeArguments[0], true);
        }

        return (type, allowsNull);
    }

    private static AIContractAnalysisResult Failure(
        DiagnosticDescriptor descriptor,
        Location location,
        params object[] arguments) =>
        new(null, ImmutableArray.Create(Diagnostic.Create(descriptor, location, arguments)));

    private static string MetadataName(INamedTypeSymbol type) =>
        type.ContainingNamespace.IsGlobalNamespace
            ? type.MetadataName
            : type.ContainingNamespace.ToDisplayString() + "." + type.MetadataName;

    private static string Display(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static readonly ImmutableHashSet<string> CollectionMetadataNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "System.Collections.Generic.IEnumerable`1",
            "System.Collections.Generic.IReadOnlyCollection`1",
            "System.Collections.Generic.IReadOnlyList`1",
            "System.Collections.Generic.ICollection`1",
            "System.Collections.Generic.IList`1",
            "System.Collections.Generic.List`1");

    private static readonly ImmutableHashSet<string> DictionaryMetadataNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "System.Collections.Generic.IReadOnlyDictionary`2",
            "System.Collections.Generic.IDictionary`2",
            "System.Collections.Generic.Dictionary`2");
}

/// <summary>
/// Contains either an analyzed contract or diagnostics explaining why analysis failed.
/// </summary>
internal sealed record AIContractAnalysisResult(
    AIContractNode? Contract,
    ImmutableArray<Diagnostic> Diagnostics)
{
    /// <summary>Creates a successful analysis result.</summary>
    public static AIContractAnalysisResult Success(AIContractNode contract) =>
        new(contract, ImmutableArray<Diagnostic>.Empty);
}
