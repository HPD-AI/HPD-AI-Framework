using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace HPD.Base.Generators;

/// <summary>Represents a base collection generator.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class BaseCollectionGenerator : IIncrementalGenerator
{
    private const string CollectionAttribute =
        "HPD.Base.BaseCollectionAttribute";
    private const string FieldAttribute =
        "HPD.Base.BaseFieldAttribute";
    private const string ConfidentialityAttribute = "HPD.Base.BaseFieldConfidentialityAttribute";
    private const string DisclosureAttribute = "HPD.Base.BaseFieldDisclosureAttribute";
    private const string StorageProtectionAttribute = "HPD.Base.BaseCollectionStorageProtectionAttribute";
    private const string IndexAttribute =
        "HPD.Base.BaseIndexAttribute";
    private const string RelationAttribute =
        "HPD.Base.BaseRelationAttribute";
    private const string VectorIndexAttribute =
        "HPD.Base.BaseVectorIndexAttribute";
    private const string JsonPropertyNameAttribute =
        "System.Text.Json.Serialization.JsonPropertyNameAttribute";
    private const string JsonOptionsAttribute =
        "System.Text.Json.Serialization.JsonSourceGenerationOptionsAttribute";
    private const string JsonIgnoreAttribute = "System.Text.Json.Serialization.JsonIgnoreAttribute";
    private const string JsonConverterAttribute = "System.Text.Json.Serialization.JsonConverterAttribute";
    private const string BaseSerializerConverterAttribute = "HPD.Base.BaseSerializerConverterAttribute";

    private static readonly DiagnosticDescriptor TypeMustBePartial = new DiagnosticDescriptor(
        "HPDBASE001",
        "BASE collection type must be partial",
        "Collection type '{0}' must be declared partial",
        "HPD.Base.Generation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor DuplicateCollectionId = new DiagnosticDescriptor(
        "HPDBASE002",
        "Duplicate BASE collection identifier",
        "Collection identifier '{0}' is also used by '{1}'",
        "HPD.Base.Generation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor UnsupportedType = new DiagnosticDescriptor(
        "HPDBASE003",
        "Unsupported BASE collection declaration",
        "Collection type '{0}' must be a top-level, non-generic class or record",
        "HPD.Base.Generation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor DuplicateField = new DiagnosticDescriptor(
        "HPDBASE004",
        "Duplicate BASE stored field",
        "Collection '{0}' declares stored field '{1}' more than once",
        "HPD.Base.Generation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidIndexField = new DiagnosticDescriptor(
        "HPDBASE005",
        "Invalid BASE index field",
        "Index '{0}' on collection '{1}' references unknown property '{2}'",
        "HPD.Base.Generation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidCollectionId = new DiagnosticDescriptor(
        "HPDBASE006",
        "Invalid BASE collection identifier",
        "Collection type '{0}' must declare a non-empty collection identifier",
        "HPD.Base.Generation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor MissingJsonRegistration = new DiagnosticDescriptor(
        "HPDBASE007",
        "Missing source-generated JSON registration",
        "JSON context '{0}' must declare [JsonSerializable(typeof({1}))]",
        "HPD.Base.Generation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidField = new DiagnosticDescriptor(
        "HPDBASE008",
        "Invalid BASE field declaration",
        "Collection '{0}' field '{1}' is invalid: {2}",
        "HPD.Base.Generation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidIndex = new DiagnosticDescriptor(
        "HPDBASE009",
        "Invalid BASE index declaration",
        "Collection '{0}' index '{1}' is invalid: {2}",
        "HPD.Base.Generation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor MissingFieldIdentity = new DiagnosticDescriptor(
        "HPDBASE010",
        "Missing stable BASE field identifier",
        "Collection '{0}' property '{1}' must declare [BaseField(\"stable-id\")] or an explicitly ignored BaseField attribute",
        "HPD.Base.Generation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor DuplicateFieldIdentity = new DiagnosticDescriptor(
        "HPDBASE011",
        "Duplicate stable BASE field identifier",
        "Collection '{0}' declares stable field identifier '{1}' more than once",
        "HPD.Base.Generation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidRelation = new DiagnosticDescriptor(
        "HPDBASE012",
        "Invalid BASE relation declaration",
        "Collection '{0}' relation on property '{1}' is invalid: {2}",
        "HPD.Base.Generation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidMutationMode = new DiagnosticDescriptor(
        "HPDBASE013",
        "Invalid BASE collection mutation mode",
        "Collection '{0}' declares unsupported mutation mode value '{1}'",
        "HPD.Base.Generation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor UnsupportedSerializerContract = new DiagnosticDescriptor(
        "HPDBASE0447", "Unsupported serializer contract",
        "Collection '{0}' serializer contract is unsupported: {1}", "HPD.Base.Generation",
        DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor SerializerGraphLimit = new DiagnosticDescriptor(
        "HPDBASE0448", "Serializer graph limit exceeded",
        "Collection '{0}' serializer graph exceeds a closed L44 bound", "HPD.Base.Generation",
        DiagnosticSeverity.Error, true);

    /// <summary>Executes the initialize operation.</summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<INamedTypeSymbol> candidates =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                CollectionAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, _) =>
                    (INamedTypeSymbol)attributeContext.TargetSymbol);

        context.RegisterSourceOutput(
            candidates.Collect(),
            static (productionContext, symbols) =>
                Generate(productionContext, symbols));
    }

    private static void Generate(
        SourceProductionContext context,
        ImmutableArray<INamedTypeSymbol> symbols)
    {
        INamedTypeSymbol[] ordered = symbols
            .OrderBy(
                symbol => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                StringComparer.Ordinal)
            .ToArray();

        var collectionOwners = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

        foreach (INamedTypeSymbol symbol in ordered)
        {
            AttributeData collection = FindAttribute(symbol, CollectionAttribute);
            string collectionId = GetConstructorString(collection, 0);

            if (string.IsNullOrWhiteSpace(collectionId))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidCollectionId,
                    GetLocation(symbol),
                    symbol.Name));
                continue;
            }

            INamedTypeSymbol existing;
            if (collectionOwners.TryGetValue(collectionId, out existing))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateCollectionId,
                    GetLocation(symbol),
                    collectionId,
                    existing.ToDisplayString()));
                continue;
            }

            collectionOwners.Add(collectionId, symbol);

            if (!IsSupported(symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedType,
                    GetLocation(symbol),
                    symbol.ToDisplayString()));
                continue;
            }

            if (!IsPartial(symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    TypeMustBePartial,
                    GetLocation(symbol),
                    symbol.ToDisplayString()));
                continue;
            }

            CollectionModel model = CreateModel(context, symbol, collection, collectionId);
            if (model == null)
            {
                continue;
            }

            context.AddSource(
                model.HintName + ".g.cs",
                SourceText.From(Render(model), Encoding.UTF8));
        }
    }

    private static CollectionModel CreateModel(
        SourceProductionContext context,
        INamedTypeSymbol symbol,
        AttributeData collection,
        string collectionId)
    {
        string mutationMode;
        int mutationModeValue;
        if (!TryGetMutationMode(collection, out mutationMode, out mutationModeValue))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidMutationMode,
                GetLocation(collection, symbol),
                collectionId,
                mutationModeValue));
            return null;
        }

        INamedTypeSymbol jsonContext = GetConstructorType(collection, 1);
        if (jsonContext == null || !HasJsonRegistration(jsonContext, symbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MissingJsonRegistration,
                GetLocation(symbol),
                jsonContext == null ? "<missing>" : jsonContext.ToDisplayString(),
                symbol.ToDisplayString()));
            return null;
        }

        var fields = new List<FieldModel>();
        var fieldIds = new HashSet<string>(StringComparer.Ordinal);
        var wireNames = new HashSet<string>(StringComparer.Ordinal);
        var propertyFields = new Dictionary<string, FieldModel>(StringComparer.Ordinal);
        var relationIds = new HashSet<string>(StringComparer.Ordinal);
        string jsonNamingPolicy = GetJsonNamingPolicy(jsonContext);
        if (!ValidJsonOptions(jsonContext))
        {
            context.ReportDiagnostic(Diagnostic.Create(UnsupportedSerializerContract, GetLocation(symbol), collectionId, "the JsonSourceGenerationOptions declaration conflicts with the closed BASE option receipt"));
            return null;
        }

        foreach (IPropertySymbol property in SerializableProperties(symbol)
            .Where(property =>
                !property.IsStatic &&
                !property.IsIndexer &&
                property.DeclaredAccessibility == Accessibility.Public &&
                property.GetMethod != null)
            .OrderBy(property => property.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue))
        {
            AttributeData fieldAttribute = FindAttribute(property, FieldAttribute);
            bool serializerIgnored = IsAlwaysIgnored(property);
            if (fieldAttribute == null)
            {
                if (serializerIgnored) continue;
                context.ReportDiagnostic(Diagnostic.Create(
                    MissingFieldIdentity,
                    GetLocation(property),
                    collectionId,
                    property.Name));
                return null;
            }

            string fieldId = GetConstructorString(fieldAttribute, 0);
            if (!IsValidId(fieldId))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidField,
                    GetLocation(property),
                    collectionId,
                    property.Name,
                    "the stable identifier must be 1-128 ASCII letters, digits, '.', '-', or '_' and start with a letter or digit"));
                return null;
            }

            if (GetNamedBoolean(fieldAttribute, "Ignore", false))
            {
                if (!serializerIgnored)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidField, GetLocation(property), collectionId, property.Name, "ignored BASE fields must be absent from serializer metadata"));
                    return null;
                }
                continue;
            }
            if (serializerIgnored || FindAttribute(property, JsonIgnoreAttribute) is not null || property.SetMethod is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidField, GetLocation(property), collectionId, property.Name, "active BASE fields require unconditional readable and writable serializer membership"));
                return null;
            }

            if (!fieldIds.Add(fieldId))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateFieldIdentity,
                    GetLocation(property),
                    collectionId,
                    fieldId));
                return null;
            }
            if (!ValidApplicationName(property.Name))
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidField, GetLocation(property), collectionId, property.Name, "the application name must use the closed cross-language identifier grammar"));
                return null;
            }
            string wireName = GetJsonPropertyName(property) ?? property.Name;
            if (string.IsNullOrWhiteSpace(wireName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidField,
                    GetLocation(property),
                    collectionId,
                    property.Name,
                    "the stored name must not be empty"));
                return null;
            }

            if (!IsSupportedFieldType(property.Type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidField,
                    GetLocation(property),
                    collectionId,
                    property.Name,
                    "the payload type is not supported"));
                return null;
            }

            long operators = GetNamedInt64(fieldAttribute, "Operators", 1);
            if ((operators & ~15L) != 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidField,
                    GetLocation(property),
                    collectionId,
                    property.Name,
                    "the query operator flags are not recognized"));
                return null;
            }

            if (!wireNames.Add(wireName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateField,
                    GetLocation(property),
                    collectionId,
                    wireName));
                return null;
            }

            var field = new FieldModel
            {
                Id = fieldId,
                PropertyName = property.Name,
                ApplicationName = property.Name,
                WireName = wireName,
                ExplicitWireName = GetJsonPropertyName(property) is not null,
                TypeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                TypedRecordIdTarget = TypedRecordIdTarget(property.Type),
                SchemaType = GetSchemaType(property.Type),
                SchemaFormat = GetSchemaFormat(property.Type),
                Nullable = IsNullable(property),
                Required = property.IsRequired || !IsNullable(property),
                Operators = operators,
            };
            AttributeData confidentialityAttribute = FindAttribute(property, ConfidentialityAttribute);
            field.Confidentiality = confidentialityAttribute is null ? 0 : (int)(confidentialityAttribute.ConstructorArguments[0].Value ?? -1);
            if (field.Confidentiality is < 0 or > 3)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidField, GetLocation(property), collectionId, property.Name, "the confidentiality class is invalid"));
                return null;
            }
            AttributeData disclosureAttribute = FindAttribute(property, DisclosureAttribute);
            if (disclosureAttribute is not null)
            {
                string[] requiredDisclosureNames = new[] { "RecordRead", "Event", "Realtime", "Diagnostic", "AdministrativeDataExport", "OrdinaryDataExport", "Indexing" };
                if (requiredDisclosureNames.Any(name => disclosureAttribute.NamedArguments.All(pair => pair.Key != name)))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidField, GetLocation(property), collectionId, property.Name, "all disclosure channels must be assigned"));
                    return null;
                }
                field.Disclosure = requiredDisclosureNames.Select(name => (int)disclosureAttribute.NamedArguments.Single(pair => pair.Key == name).Value.Value!).ToArray();
            }
            field.MaximumBytes = (int)GetNamedInt64(fieldAttribute, "MaximumBytes", 0);
            bool binary = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::HPD.Base.BaseBinary";
            if (binary != (field.MaximumBytes > 0) || binary && field.MaximumBytes > 1_048_576)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidField, GetLocation(property), collectionId, property.Name, "binary fields require MaximumBytes from 1 through 1048576 and other fields forbid it"));
                return null;
            }

            AttributeData relationAttribute = FindAttribute(property, RelationAttribute);
            if (relationAttribute != null)
            {
                string relationId = GetConstructorString(relationAttribute, 0);
                INamedTypeSymbol targetType = GetConstructorType(relationAttribute, 1);
                AttributeData targetCollection = targetType == null ? null : FindAttribute(targetType, CollectionAttribute);
                string targetCollectionId = targetCollection == null ? null : GetConstructorString(targetCollection, 0);
                if (!IsValidId(relationId) || targetType == null || !IsValidId(targetCollectionId))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidRelation, GetLocation(relationAttribute, property), collectionId, property.Name,
                        "the relation id and target generated collection must be valid"));
                    return null;
                }

                if (!relationIds.Add(relationId))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidRelation, GetLocation(relationAttribute, property), collectionId, property.Name,
                        "the relation id is already declared"));
                    return null;
                }

                bool manyShape = IsManyRecordIdShape(property.Type);
                if (field.TypedRecordIdTarget == null ||
                    !string.Equals(field.TypedRecordIdTarget, targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparison.Ordinal))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidRelation, GetLocation(relationAttribute, property), collectionId, property.Name,
                        "the property must be BaseRecordId<TTarget> for the declared target type"));
                    return null;
                }

                string targetFieldId = GetNamedString(relationAttribute, "TargetFieldId") ?? "base.recordId";
                string inverseNavigationId = GetNamedString(relationAttribute, "InverseNavigationId");
                long localMultiplicity = GetNamedInt64(relationAttribute, "LocalMultiplicity", 0);
                long inverseMultiplicity = GetNamedInt64(relationAttribute, "InverseMultiplicity", 2);
                long deleteBehavior = GetNamedInt64(relationAttribute, "DeleteBehavior", 0);
                long minimumCount = GetNamedInt64(relationAttribute, "MinimumCount", -1);
                long maximumCount = GetNamedInt64(relationAttribute, "MaximumCount", -1);
                long includeMaximumDepth = GetNamedInt64(relationAttribute, "IncludeMaximumDepth", -1);
                if (!IsValidId(targetFieldId) ||
                    (inverseNavigationId != null && !IsValidId(inverseNavigationId)) ||
                    localMultiplicity < 0 || localMultiplicity > 2 ||
                    inverseMultiplicity < 0 || inverseMultiplicity > 2 ||
                    deleteBehavior != 0 ||
                    (manyShape != (localMultiplicity == 2)) ||
                    (localMultiplicity == 1 && !field.Required) ||
                    (localMultiplicity == 0 && field.Required) ||
                    (localMultiplicity != 2 && (minimumCount >= 0 || maximumCount >= 0)) ||
                    minimumCount < -1 || maximumCount < -1 ||
                    (minimumCount >= 0 && maximumCount >= 0 && minimumCount > maximumCount) ||
                    maximumCount > 10_000 || includeMaximumDepth is < -1 or > 32)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidRelation, GetLocation(relationAttribute, property), collectionId, property.Name,
                        "identifiers, CLR nullability, and multiplicities must agree and only Restrict delete behavior is executable"));
                    return null;
                }

                field.Relation = new RelationModel
                {
                    Id = relationId,
                    TargetCollectionId = targetCollectionId,
                    TargetFieldId = targetFieldId,
                    LocalMultiplicity = localMultiplicity,
                    InverseMultiplicity = inverseMultiplicity,
                    InverseNavigationId = inverseNavigationId,
                    DeleteBehavior = deleteBehavior,
                    IncludeAllowed = GetNamedBoolean(relationAttribute, "IncludeAllowed", false),
                    MinimumCount = minimumCount < 0 ? null : (int?)minimumCount,
                    MaximumCount = maximumCount < 0 ? null : (int?)maximumCount,
                    IncludeFilterAllowed = GetNamedBoolean(relationAttribute, "IncludeFilterAllowed", false),
                    IncludeSortAllowed = GetNamedBoolean(relationAttribute, "IncludeSortAllowed", false),
                    IncludeMaximumDepth = includeMaximumDepth < 0 ? null : (int?)includeMaximumDepth,
                };
            }

            fields.Add(field);
            propertyFields.Add(property.Name, field);
        }

        var indexes = new List<IndexModel>();
        var vectorIndexes = new List<VectorIndexModel>();
        var indexIds = new HashSet<string>(StringComparer.Ordinal);
        var vectorHandleNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (AttributeData indexAttribute in symbol.GetAttributes()
            .Where(attribute =>
                attribute.AttributeClass != null &&
                attribute.AttributeClass.ToDisplayString() == IndexAttribute))
        {
            string indexId = GetConstructorString(indexAttribute, 0);
            Location indexLocation = GetLocation(indexAttribute, symbol);
            if (string.IsNullOrWhiteSpace(indexId))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidIndex,
                    indexLocation,
                    collectionId,
                    indexId ?? string.Empty,
                    "the identifier must not be empty"));
                return null;
            }

            if (!indexIds.Add(indexId))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidIndex,
                    indexLocation,
                    collectionId,
                    indexId,
                    "the identifier is duplicated"));
                return null;
            }

            ImmutableArray<TypedConstant> fieldConstants =
                indexAttribute.ConstructorArguments.Length > 1 &&
                indexAttribute.ConstructorArguments[1].Kind == TypedConstantKind.Array
                    ? indexAttribute.ConstructorArguments[1].Values
                    : ImmutableArray<TypedConstant>.Empty;
            if (fieldConstants.IsDefaultOrEmpty)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidIndex,
                    indexLocation,
                    collectionId,
                    indexId,
                    "at least one field is required"));
                return null;
            }

            var indexFields = new List<FieldModel>();
            var indexedProperties = new HashSet<string>(StringComparer.Ordinal);
            foreach (TypedConstant fieldConstant in fieldConstants)
            {
                string propertyName = fieldConstant.Value as string;
                FieldModel field;
                if (propertyName == null || !propertyFields.TryGetValue(propertyName, out field))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidIndexField,
                        indexLocation,
                        indexId ?? string.Empty,
                        collectionId,
                        propertyName ?? string.Empty));
                    return null;
                }

                if (!indexedProperties.Add(propertyName))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidIndex,
                        indexLocation,
                        collectionId,
                        indexId,
                        "a field is included more than once"));
                    return null;
                }

                if (field.TypeName == "global::HPD.Base.BaseVector")
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidIndex,
                        indexLocation,
                        collectionId,
                        indexId,
                        "vector fields may only participate in a BaseVectorIndex"));
                    return null;
                }

                indexFields.Add(field);
            }

            indexes.Add(new IndexModel
            {
                Id = indexId,
                Unique = GetNamedBoolean(indexAttribute, "Unique", false),
                Required = GetNamedBoolean(indexAttribute, "Required", true),
                Fields = indexFields,
            });
        }

        foreach (AttributeData vectorAttribute in symbol.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() == VectorIndexAttribute))
        {
            string vectorIndexId = GetConstructorString(vectorAttribute, 0);
            string vectorPropertyName = GetConstructorString(vectorAttribute, 1);
            Location location = GetLocation(vectorAttribute, symbol);
            string vectorSpace = GetNamedString(vectorAttribute, "VectorSpace");
            int dimensions = (int)GetNamedInt64(vectorAttribute, "Dimensions", 0);
            int function = (int)GetNamedInt64(vectorAttribute, "Function", 0);
            if (!IsValidId(vectorIndexId) || !indexIds.Add(vectorIndexId))
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, location, collectionId, vectorIndexId ?? string.Empty, "the stable vector-index identifier is invalid or duplicated"));
                return null;
            }
            if (!IsValidId(vectorSpace) || dimensions is < 1 or > 32768 || function is < 0 or > 2)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, location, collectionId, vectorIndexId, "the vector space, dimensions, or function is invalid"));
                return null;
            }
            if (!propertyFields.TryGetValue(vectorPropertyName, out FieldModel vectorField)
                || vectorField.TypeName is not ("global::HPD.Base.BaseVector" or "global::HPD.Base.BaseVector?")
                || vectorField.Operators != 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, location, collectionId, vectorIndexId, "the vector property must be a stored BaseVector or nullable BaseVector field with BaseFieldOperator.None"));
                return null;
            }

            var filterFields = new List<FieldModel>();
            var filterProperties = new HashSet<string>(StringComparer.Ordinal);
            foreach (TypedConstant constant in GetNamedArray(vectorAttribute, "FilterFields"))
            {
                string propertyName = constant.Value as string;
                if (propertyName == null
                    || !propertyFields.TryGetValue(propertyName, out FieldModel filterField)
                    || filterField == vectorField
                    || (filterField.Operators & 1) == 0
                    || !filterProperties.Add(propertyName))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, location, collectionId, vectorIndexId, "filter fields must be unique stored equality-capable non-vector properties"));
                    return null;
                }
                filterFields.Add(filterField);
            }
            if (filterFields.Count > 16)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, location, collectionId, vectorIndexId, "at most 16 filter fields are permitted"));
                return null;
            }
            string handleName = VectorHandleName(vectorIndexId);
            if (!vectorHandleNames.Add(handleName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidIndex,
                    location,
                    collectionId,
                    vectorIndexId,
                    "the generated vector-index member name collides with another vector index"));
                return null;
            }
            vectorIndexes.Add(new VectorIndexModel
            {
                Id = vectorIndexId,
                PropertyName = handleName,
                VectorField = vectorField,
                VectorSpaceId = vectorSpace,
                Dimensions = dimensions,
                Function = function,
                FilterFields = filterFields,
            });
        }

        string fullTypeName =
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string metadataName = symbol.ToDisplayString(
            new SymbolDisplayFormat(
                typeQualificationStyle:
                    SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));

        fields.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        indexes.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        vectorIndexes.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));

        var storageRequirements = new List<string>();
        foreach (AttributeData requirementAttribute in symbol.GetAttributes().Where(static attribute => attribute.AttributeClass?.ToDisplayString() == StorageProtectionAttribute))
        {
            INamedTypeSymbol declaringType = requirementAttribute.ConstructorArguments[0].Value as INamedTypeSymbol;
            string propertyName = requirementAttribute.ConstructorArguments[1].Value as string;
            IPropertySymbol property = declaringType?.GetMembers(propertyName ?? string.Empty).OfType<IPropertySymbol>().SingleOrDefault();
            if (property is null || !property.IsStatic || property.DeclaredAccessibility != Accessibility.Public ||
                property.Type.ToDisplayString() != "HPD.Base.BaseStorageProtectionRequirement")
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidField, GetLocation(symbol), collectionId, "<collection>",
                    "storage-protection declarations must name a public static BaseStorageProtectionRequirement property"));
                return null;
            }
            storageRequirements.Add(declaringType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + EscapeIdentifier(property.Name));
        }
        List<SerializerPropertyModel> serializerProperties = CollectSerializerProperties(context, symbol, collectionId);
        if (serializerProperties is null) return null;

        return new CollectionModel
        {
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : symbol.ContainingNamespace.ToDisplayString(),
            TypeName = EscapeIdentifier(symbol.Name),
            FullTypeName = fullTypeName,
            IsRecord = symbol.IsRecord,
            CollectionId = collectionId,
            CollectionName = GetNamedString(collection, "Name") ?? collectionId,
            CollectionKind = GetNamedString(collection, "Kind") ?? "record",
            Strict = GetNamedBoolean(collection, "Strict", true),
            MutationMode = mutationMode,
            Fields = fields,
            Indexes = indexes,
            VectorIndexes = vectorIndexes,
            StorageRequirements = storageRequirements,
            JsonNamingPolicy = jsonNamingPolicy,
            SerializerProperties = serializerProperties,
            ContextTypeName =
                jsonContext.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            HintName = "HPDBaseCollection_" + Sanitize(metadataName),
        };
    }

    private static string Render(CollectionModel model)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        source.AppendLine();

        if (model.Namespace != null)
        {
            source.Append("namespace ").Append(model.Namespace).AppendLine(";");
            source.AppendLine();
        }

        source.Append("partial ")
            .Append(model.IsRecord ? "record class " : "class ")
            .Append(model.TypeName)
            .AppendLine();
        source.AppendLine("{");
        foreach (string target in model.Fields
            .Select(static field => field.TypedRecordIdTarget)
            .Where(static target => target != null)
            .Distinct(StringComparer.Ordinal))
        {
            source.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
            source.Append("    internal static void RegisterHPDBaseRecordIdJsonConverter_")
                .Append(Sanitize(target!)).Append("() => global::HPD.Base.BaseRecordIdJsonConverterFactory.Register<")
                .Append(target).AppendLine(">();");
            source.AppendLine();
        }
        source.AppendLine("    /// <summary>Gets the generated collection contract with stable logical identities and source-generated serialization metadata.</summary>");
        source.Append("    public static global::HPD.Base.BaseCollection<")
            .Append(model.FullTypeName)
            .AppendLine("> Collection { get; } = CreateHPDBaseCollection();");
        source.AppendLine();
        source.AppendLine("    /// <summary>Provides typed handles for the collection's declared fields.</summary>");
        source.AppendLine("    public static class Fields");
        source.AppendLine("    {");

        foreach (FieldModel field in model.Fields)
        {
            source.Append("        private static global::HPD.Base.BaseField<")
                .Append(model.FullTypeName).Append(", ").Append(field.TypeName)
                .Append("> __").Append(EscapeIdentifier(field.PropertyName))
                .AppendLine(" = null!;");
            source.Append("        /// <summary>Gets the typed field handle for stable field <c>").Append(field.Id).AppendLine("</c>.</summary>");
            source.Append("        public static global::HPD.Base.BaseField<")
                .Append(model.FullTypeName).Append(", ").Append(field.TypeName)
                .Append("> ").Append(EscapeIdentifier(field.PropertyName)).AppendLine();
            source.AppendLine("        {");
            source.AppendLine("            get");
            source.AppendLine("            {");
            source.Append("                _ = ").Append(model.FullTypeName)
                .AppendLine(".Collection;");
            source.Append("                return __")
                .Append(EscapeIdentifier(field.PropertyName)).AppendLine(";");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.Append("        internal static void Set")
                .Append(EscapeIdentifier(field.PropertyName))
                .Append("(global::HPD.Base.BaseField<")
                .Append(model.FullTypeName).Append(", ").Append(field.TypeName)
                .Append("> value) => __")
                .Append(EscapeIdentifier(field.PropertyName)).AppendLine(" = value;");
            source.AppendLine();
        }

        source.AppendLine("    }");
        source.AppendLine();
        if (model.VectorIndexes.Count != 0)
        {
            source.AppendLine("    /// <summary>Provides typed handles for the collection's declared vector indexes.</summary>");
            source.AppendLine("    public static class VectorIndexes");
            source.AppendLine("    {");
            foreach (VectorIndexModel index in model.VectorIndexes)
            {
                source.Append("        /// <summary>Gets vector index <c>").Append(index.Id).AppendLine("</c>.</summary>");
                source.Append("        public static global::HPD.Base.BaseVectorIndex<").Append(model.FullTypeName).Append("> ")
                    .Append(index.PropertyName).AppendLine(" { get; } = new()");
                source.AppendLine("        {");
                source.Append("            CollectionId = ").Append(Literal(model.CollectionId)).AppendLine(",");
                source.Append("            Id = ").Append(Literal(index.Id)).AppendLine(",");
                source.Append("            VectorFieldId = ").Append(Literal(index.VectorField.Id)).AppendLine(",");
                source.Append("            VectorSpaceId = ").Append(Literal(index.VectorSpaceId)).AppendLine(",");
                source.Append("            Dimensions = ").Append(index.Dimensions).AppendLine(",");
                source.Append("            Function = (global::HPD.Base.BaseVectorFunction)").Append(index.Function).AppendLine(",");
                source.Append("            FilterFieldIds = global::System.Collections.Immutable.ImmutableArray.Create<string>(");
                source.Append(string.Join(", ", index.FilterFields.Select(static field => Literal(field.Id))));
                source.AppendLine("),");
                source.AppendLine("        };");
            }
            source.AppendLine("    }");
            source.AppendLine();
        }
        source.Append("    private static global::HPD.Base.BaseCollection<")
            .Append(model.FullTypeName).AppendLine("> CreateHPDBaseCollection()");
        source.AppendLine("    {");
        source.AppendLine("        var jsonRegistration = global::HPD.Base.BaseSerializerGeneratedContract.RegisterContext(__CreateHPDBaseJsonContext);");
        foreach (FieldModel field in model.Fields)
        {
            source.Append("        string __wire_").Append(EscapeIdentifier(field.PropertyName)).Append(" = ");
            source.Append("global::HPD.Base.BaseSerializerGeneratedContract.ProvisionalWireName(")
                .Append(model.JsonNamingPolicy == null ? "null" : "global::System.Text.Json.JsonNamingPolicy." + model.JsonNamingPolicy)
                .Append(", ").Append(Literal(field.ApplicationName)).Append(", ")
                .Append(field.ExplicitWireName ? Literal(field.WireName) : "null").Append(')');
            source.AppendLine(";");
        }
        source.AppendLine();
        source.Append("        return global::HPD.Base.BaseCollection<")
            .Append(model.FullTypeName).AppendLine(">.CreateGenerated(");
        source.AppendLine("            new global::HPD.Base.CollectionDefinition");
        source.AppendLine("            {");
        source.Append("                Id = ").Append(Literal(model.CollectionId)).AppendLine(",");
        source.Append("                Name = ").Append(Literal(model.CollectionName)).AppendLine(",");
        source.Append("                Kind = ").Append(Literal(model.CollectionKind)).AppendLine(",");
        source.Append("                SchemaMode = global::HPD.Base.SchemaMode.")
            .Append(model.Strict ? "Strict" : "Loose").AppendLine(",");
        source.Append("                UnknownFields = global::HPD.Base.UnknownFieldPolicy.")
            .Append(model.Strict ? "Reject" : "Preserve").AppendLine(",");
        source.Append("                MutationMode = global::HPD.Base.BaseCollectionMutationMode.")
            .Append(model.MutationMode).AppendLine(",");
        source.AppendLine("                Source = new global::HPD.Base.SchemaSourceDescriptor");
        source.AppendLine("                {");
        source.AppendLine("                    Id = \"hpd.base.application.generated\",");
        source.AppendLine("                    Kind = global::HPD.Base.SchemaSourceKind.Generated,");
        source.AppendLine("                },");
        RenderFieldDefinitions(source, model);
        RenderIndexes(source, model);
        RenderVectorIndexes(source, model);
        if (model.StorageRequirements.Count != 0)
        {
            source.Append("                StorageProtectionRequirements = new global::HPD.Base.BaseStorageProtectionRequirement[] { ")
                .Append(string.Join(", ", model.StorageRequirements)).AppendLine(" },");
        }
        source.AppendLine("            },");
        source.AppendLine("            jsonRegistration,");
        source.AppendLine("            fields =>");
        source.AppendLine("            {");

        foreach (FieldModel field in model.Fields)
        {
            source.Append("                Fields.Set")
                .Append(EscapeIdentifier(field.PropertyName)).Append("(fields.Add<")
                .Append(field.TypeName).Append(">(")
                .Append(Literal(field.Id)).Append(", ")
                .Append(Literal(field.ApplicationName)).Append(", ")
                .Append("__wire_").Append(EscapeIdentifier(field.PropertyName)).Append(", nullable: ")
                .Append(field.Nullable ? "true" : "false")
                .Append(", operators: (global::HPD.Base.BaseFieldOperator)")
                .Append(field.Operators.ToString(CultureInfo.InvariantCulture))
                .AppendLine("));");
        }

        source.AppendLine("            },");
        source.AppendLine("            new global::HPD.Base.BaseSerializerPropertyDeclaration[]");
        source.AppendLine("            {");
        foreach (SerializerPropertyModel property in model.SerializerProperties)
        {
            source.Append("                global::HPD.Base.BaseSerializerPropertyDeclaration.Create(typeof(").Append(property.DeclaringType)
                .Append("), ").Append(Literal(property.ApplicationName))
                .Append(", typeof(").Append(property.PropertyType).Append("), ")
                .Append(property.ExplicitWireName is null ? "null" : Literal(property.ExplicitWireName))
                .Append(", ").Append(property.Required ? "true" : "false")
                .Append(", ").Append(property.Nullable ? "true" : "false")
                .Append(", ").Append(Literal(property.ConverterIdentity))
                .Append(", ").Append(property.ConverterType is null ? "null" : "typeof(" + property.ConverterType + ")")
                .AppendLine("),");
        }
        source.AppendLine("            });");
        source.AppendLine("    }");
        source.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"HPD.Base.Generators\", \"44\")]");
        source.Append("    private static ").Append(model.ContextTypeName).Append(" __CreateHPDBaseJsonContext() => new(")
            .Append("global::HPD.Base.BaseSerializerGeneratedContract.CreateOptions(")
            .Append(model.JsonNamingPolicy == null ? "null" : "global::System.Text.Json.JsonNamingPolicy." + model.JsonNamingPolicy).AppendLine("));");
        source.AppendLine("}");
        return source.ToString();
    }

    private static void RenderFieldDefinitions(
        StringBuilder source,
        CollectionModel model)
    {
        IReadOnlyList<FieldModel> fields = model.Fields;
        source.AppendLine("                Fields =");
        source.AppendLine("                [");
        foreach (FieldModel field in fields)
        {
            source.AppendLine("                    new global::HPD.Base.FieldDefinition");
            source.AppendLine("                    {");
            source.Append("                        Id = ").Append(Literal(field.Id)).AppendLine(",");
            source.Append("                        ApplicationName = ").Append(Literal(field.ApplicationName)).AppendLine(",");
            source.Append("                        WireName = __wire_").Append(EscapeIdentifier(field.PropertyName)).AppendLine(",");
            source.Append("                        Type = ").Append(Literal(field.SchemaType)).AppendLine(",");
            if (field.SchemaFormat != null)
            {
                source.Append("                        Format = ")
                    .Append(Literal(field.SchemaFormat)).AppendLine(",");
            }
            source.Append("                        Required = ")
                .Append(field.Required ? "true" : "false").AppendLine(",");
            source.Append("                        Nullable = ")
                .Append(field.Nullable ? "true" : "false").AppendLine(",");
            source.Append("                        Confidentiality = (global::HPD.Base.BaseFieldConfidentiality)").Append(field.Confidentiality).AppendLine(",");
            if (field.Disclosure is not null)
            {
                source.AppendLine("                        Disclosure = new global::HPD.Base.BaseFieldDisclosurePolicy");
                source.AppendLine("                        {");
                source.Append("                            RecordRead = (global::HPD.Base.BaseRecordDisclosure)").Append(field.Disclosure[0]).AppendLine(",");
                source.AppendLine("                            AuthoritativeHistory = global::HPD.Base.BaseHistoryProtection.AuthoritativeRequired,");
                source.Append("                            Event = (global::HPD.Base.BaseProjectionDisclosure)").Append(field.Disclosure[1]).AppendLine(",");
                source.Append("                            Realtime = (global::HPD.Base.BaseProjectionDisclosure)").Append(field.Disclosure[2]).AppendLine(",");
                source.Append("                            Diagnostic = (global::HPD.Base.BaseProjectionDisclosure)").Append(field.Disclosure[3]).AppendLine(",");
                source.AppendLine("                            AuthoritativeBackup = global::HPD.Base.BaseAuthoritativeBackupProtection.PreserveAuthoritativeValue,");
                source.Append("                            AdministrativeDataExport = (global::HPD.Base.BaseProjectionDisclosure)").Append(field.Disclosure[4]).AppendLine(",");
                source.Append("                            OrdinaryDataExport = (global::HPD.Base.BaseProjectionDisclosure)").Append(field.Disclosure[5]).AppendLine(",");
                source.Append("                            Indexing = (global::HPD.Base.BaseIndexDisclosure)").Append(field.Disclosure[6]).AppendLine(",");
                source.AppendLine("                        },");
            }
            else
                source.Append("                        Disclosure = global::HPD.Base.BaseFieldDisclosurePolicies.For((global::HPD.Base.BaseFieldConfidentiality)").Append(field.Confidentiality).AppendLine("),");
            if (field.MaximumBytes > 0) source.Append("                        MaximumBytes = ").Append(field.MaximumBytes).AppendLine(",");
            if (field.Relation != null)
            {
                source.AppendLine("                        Relation = new global::HPD.Base.RelationDefinition");
                source.AppendLine("                        {");
                source.Append("                            Id = ").Append(Literal(field.Relation.Id)).AppendLine(",");
                source.Append("                            SourceCollectionId = ").Append(Literal(model.CollectionId)).AppendLine(",");
                source.Append("                            SourceFieldId = ").Append(Literal(field.Id)).AppendLine(",");
                source.Append("                            TargetCollectionId = ").Append(Literal(field.Relation.TargetCollectionId)).AppendLine(",");
                source.Append("                            TargetFieldId = ").Append(Literal(field.Relation.TargetFieldId)).AppendLine(",");
                source.Append("                            LocalMultiplicity = (global::HPD.Base.BaseRelationMultiplicity)").Append(field.Relation.LocalMultiplicity).AppendLine(",");
                source.Append("                            InverseMultiplicity = (global::HPD.Base.BaseRelationMultiplicity)").Append(field.Relation.InverseMultiplicity).AppendLine(",");
                source.Append("                            Required = ").Append(field.Required ? "true" : "false").AppendLine(",");
                if (field.Relation.MinimumCount is int minimumCount)
                    source.Append("                            MinimumCount = ").Append(minimumCount).AppendLine(",");
                if (field.Relation.MaximumCount is int maximumCount)
                    source.Append("                            MaximumCount = ").Append(maximumCount).AppendLine(",");
                if (field.Relation.InverseNavigationId != null)
                    source.Append("                            InverseNavigationId = ").Append(Literal(field.Relation.InverseNavigationId)).AppendLine(",");
                source.Append("                            DeleteBehavior = (global::HPD.Base.BaseRelationDeleteBehavior)").Append(field.Relation.DeleteBehavior).AppendLine(",");
                source.Append("                            Include = new global::HPD.Base.RelationIncludeDefinition { Allowed = ").Append(field.Relation.IncludeAllowed ? "true" : "false")
                    .Append(", FilterAllowed = ").Append(field.Relation.IncludeFilterAllowed ? "true" : "false")
                    .Append(", SortAllowed = ").Append(field.Relation.IncludeSortAllowed ? "true" : "false");
                if (field.Relation.IncludeMaximumDepth is int includeMaximumDepth)
                    source.Append(", MaxDepth = ").Append(includeMaximumDepth);
                source.AppendLine(" },");
                source.AppendLine("                        },");
            }
            source.AppendLine("                    },");
        }
        source.AppendLine("                ],");
    }

    private static void RenderIndexes(StringBuilder source, CollectionModel model)
    {
        if (model.Indexes.Count == 0)
        {
            source.AppendLine("                Indexes = null,");
            return;
        }

        source.AppendLine("                Indexes =");
        source.AppendLine("                [");
        foreach (IndexModel index in model.Indexes)
        {
            source.AppendLine("                    new global::HPD.Base.IndexDefinition");
            source.AppendLine("                    {");
            source.Append("                        Id = ").Append(Literal(index.Id)).AppendLine(",");
            source.Append("                        Name = ").Append(Literal(index.Id)).AppendLine(",");
            source.Append("                        CollectionId = ")
                .Append(Literal(model.CollectionId)).AppendLine(",");
            source.AppendLine("                        Kind = global::HPD.Base.IndexKind.Key,");
            source.Append("                        Unique = ")
                .Append(index.Unique ? "true" : "false").AppendLine(",");
            source.Append("                        Enforcement = global::HPD.Base.EnforcementOwner.")
                .Append(index.Required ? "Store" : "Advisory").AppendLine(",");
            source.AppendLine("                        Parts =");
            source.AppendLine("                        [");
            foreach (FieldModel field in index.Fields)
            {
                source.AppendLine("                            new global::HPD.Base.IndexPart");
                source.AppendLine("                            {");
                source.AppendLine("                                Kind = global::HPD.Base.IndexPartKind.Field,");
                source.Append("                                FieldId = ")
                    .Append(Literal(field.Id)).AppendLine(",");
                source.AppendLine("                            },");
            }
            source.AppendLine("                        ],");
            source.AppendLine("                    },");
        }
        source.AppendLine("                ],");
    }

    private static void RenderVectorIndexes(StringBuilder source, CollectionModel model)
    {
        if (model.VectorIndexes.Count == 0)
        {
            source.AppendLine("                VectorIndexes = null,");
            return;
        }
        source.AppendLine("                VectorIndexes =");
        source.AppendLine("                [");
        foreach (VectorIndexModel index in model.VectorIndexes)
        {
            source.AppendLine("                    new global::HPD.Base.VectorIndexDefinition");
            source.AppendLine("                    {");
            source.Append("                        Id = ").Append(Literal(index.Id)).AppendLine(",");
            source.Append("                        CollectionId = ").Append(Literal(model.CollectionId)).AppendLine(",");
            source.Append("                        VectorFieldId = ").Append(Literal(index.VectorField.Id)).AppendLine(",");
            source.Append("                        VectorSpaceId = ").Append(Literal(index.VectorSpaceId)).AppendLine(",");
            source.Append("                        Dimensions = ").Append(index.Dimensions).AppendLine(",");
            source.Append("                        Function = (global::HPD.Base.BaseVectorFunction)").Append(index.Function).AppendLine(",");
            source.Append("                        FilterFieldIds = [").Append(string.Join(", ", index.FilterFields.Select(static field => Literal(field.Id)))).AppendLine("],");
            source.AppendLine("                    },");
        }
        source.AppendLine("                ],");
    }

    private static bool IsSupported(INamedTypeSymbol symbol) =>
        symbol.TypeKind == TypeKind.Class &&
        symbol.ContainingType == null &&
        symbol.TypeParameters.Length == 0;

    private static List<SerializerPropertyModel> CollectSerializerProperties(
        SourceProductionContext context, INamedTypeSymbol root, string collectionId)
    {
        var result = new List<SerializerPropertyModel>();
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var converterTypes = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        bool unsupported = false;
        bool limitExceeded = false;
        bool Visit(ITypeSymbol input, int depth, int wrappers)
        {
            ITypeSymbol type = input;
            if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                if (wrappers >= 16) { limitExceeded = true; return false; }
                return Visit(nullable.TypeArguments[0], depth, wrappers + 1);
            }
            if (type is IArrayTypeSymbol array)
            {
                if (array.Rank != 1) { unsupported = true; return false; }
                if (wrappers >= 16) { limitExceeded = true; return false; }
                return Visit(array.ElementType, depth, wrappers + 1);
            }
            if (type is INamedTypeSymbol sequence && sequence.IsGenericType &&
                sequence.ConstructedFrom.ToDisplayString() is "System.Collections.Generic.IReadOnlyList<T>" or "System.Collections.Immutable.ImmutableArray<T>")
            {
                if (wrappers >= 16) { limitExceeded = true; return false; }
                return Visit(sequence.TypeArguments[0], depth, wrappers + 1);
            }
            if (SerializerScalar(type)) return true;
            if (depth > 32) { limitExceeded = true; return false; }
            if (type is not INamedTypeSymbol named || named.TypeParameters.Length != 0 ||
                !named.Locations.Any(static location => location.IsInSource))
            {
                unsupported = true;
                return false;
            }
            if (!visited.Add(named)) return true;
            if (visited.Count > 256) { limitExceeded = true; return false; }
            foreach (IPropertySymbol property in SerializableProperties(named)
                         .Where(static property => !property.IsStatic && !property.IsIndexer && property.DeclaredAccessibility == Accessibility.Public && property.GetMethod is not null)
                         .OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                AttributeData ignore = FindAttribute(property, JsonIgnoreAttribute);
                if (ignore is not null && (ignore.NamedArguments.Length == 0 || GetNamedInt64(ignore, "Condition", 1) == 1)) continue;
                AttributeData converterAttribute = FindAttribute(property, JsonConverterAttribute);
                if (property.SetMethod is null ||
                    FindAttribute(property, "System.Text.Json.Serialization.JsonExtensionDataAttribute") is not null ||
                    FindAttribute(property, "System.Text.Json.Serialization.JsonIncludeAttribute") is not null)
                { unsupported = true; return false; }
                string converterIdentity = "stj-built-in";
                string converterType = null;
                if (converterAttribute is not null)
                {
                    INamedTypeSymbol converter = GetConstructorType(converterAttribute, 0);
                    AttributeData contract = converter is null ? null : FindAttribute(converter, BaseSerializerConverterAttribute);
                    string contractId = contract is null ? null : GetConstructorString(contract, 0);
                    int version = contract?.ConstructorArguments.Length > 1 && contract.ConstructorArguments[1].Value is int value ? value : 0;
                    if (!ValidConverter(converter, contractId, version)) { unsupported = true; return false; }
                    converterIdentity = "explicit:" + contractId + ":" + version.ToString(CultureInfo.InvariantCulture);
                    if (converterTypes.TryGetValue(converterIdentity, out INamedTypeSymbol existing) &&
                        !SymbolEqualityComparer.Default.Equals(existing, converter)) { unsupported = true; return false; }
                    converterTypes[converterIdentity] = converter;
                    converterType = converter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
                result.Add(new SerializerPropertyModel
                {
                    DeclaringType = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ApplicationName = property.Name,
                    PropertyType = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ExplicitWireName = GetJsonPropertyName(property),
                    Required = property.IsRequired,
                    Nullable = IsNullable(property),
                    ConverterIdentity = converterIdentity,
                    ConverterType = converterType,
                });
                if (result.Count > 4096) { limitExceeded = true; return false; }
                if (!Visit(property.Type, depth + 1, 0)) return false;
            }
            return true;
        }
        if (Visit(root, 0, 0)) return result;
        context.ReportDiagnostic(limitExceeded
            ? Diagnostic.Create(SerializerGraphLimit, GetLocation(root), collectionId)
            : Diagnostic.Create(UnsupportedSerializerContract, GetLocation(root), collectionId,
                unsupported ? "the reachable serializer graph is not a closed supported graph" : "the serializer graph declaration is invalid"));
        return null;
    }

    private static bool IsAlwaysIgnored(IPropertySymbol property)
    {
        AttributeData ignore = FindAttribute(property, JsonIgnoreAttribute);
        return ignore is not null && (ignore.NamedArguments.Length == 0 || GetNamedInt64(ignore, "Condition", 1) == 1);
    }

    private static bool ValidJsonOptions(INamedTypeSymbol context)
    {
        AttributeData options = FindAttribute(context, JsonOptionsAttribute);
        if (options is null) return true;
        foreach (KeyValuePair<string, TypedConstant> pair in options.NamedArguments)
        {
            long value = pair.Value.Value is null ? 0 : Convert.ToInt64(pair.Value.Value, CultureInfo.InvariantCulture);
            if (pair.Key == "PropertyNamingPolicy" && value is >= 0 and <= 5) continue;
            if (pair.Key == "GenerationMode" && value is 0 or 1) continue;
            if (pair.Key is "UseStringEnumConverter" or "IncludeFields" or "IgnoreReadOnlyFields" or "IgnoreReadOnlyProperties" or "WriteIndented" && value == 0) continue;
            if (pair.Key is "NumberHandling" or "DefaultIgnoreCondition" && value == 0) continue;
            if (pair.Key == "UnmappedMemberHandling" && value == 1) continue;
            return false;
        }
        return true;
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
        {
            foreach (IPropertySymbol property in chain.Pop().GetMembers().OfType<IPropertySymbol>())
                properties[property.Name] = property;
        }
        return properties.Values;
    }

    private static bool ValidConverter(INamedTypeSymbol converter, string contractId, int version)
    {
        if (converter is null || !converter.IsSealed || converter.IsGenericType || !converter.Locations.Any(static location => location.IsInSource) ||
            !IsValidId(contractId) || version < 1 || converter.AllInterfaces.Any(static item => item.TypeKind == TypeKind.Error)) return false;
        string baseType = converter.BaseType?.OriginalDefinition.ToDisplayString() ?? string.Empty;
        if (!string.Equals(baseType, "System.Text.Json.Serialization.JsonConverter<T>", StringComparison.Ordinal)) return false;
        IMethodSymbol[] constructors = converter.InstanceConstructors.ToArray();
        if (constructors.Length != 1 || constructors[0].DeclaredAccessibility != Accessibility.Public || constructors[0].Parameters.Length != 0) return false;
        foreach (ISymbol member in converter.GetMembers())
        {
            if (member is IFieldSymbol field && (!field.IsConst || !ConverterConstant(field.Type))) return false;
            if (member is IPropertySymbol property && property.IsStatic) return false;
            if (member is IEventSymbol) return false;
            if (member is IFieldSymbol delegateField && delegateField.Type.TypeKind == TypeKind.Delegate) return false;
        }
        return true;
    }

    private static bool ConverterConstant(ITypeSymbol type) => type.TypeKind == TypeKind.Enum || type.SpecialType is
        SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte or
        SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or
        SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Char or SpecialType.System_String or
        SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;

    private static bool IsSupportedFieldType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return array.Rank == 1 && IsSupportedFieldType(array.ElementType);
        }

        if (type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return IsSupportedFieldType(named.TypeArguments[0]);
        }

        return type.TypeKind is not (
            TypeKind.Delegate or
            TypeKind.Error or
            TypeKind.FunctionPointer or
            TypeKind.Pointer or
            TypeKind.TypeParameter) &&
            !type.IsRefLikeType;
    }

    private static bool IsPartial(INamedTypeSymbol symbol) =>
        symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(declaration =>
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static AttributeData FindAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass != null &&
            attribute.AttributeClass.ToDisplayString() == metadataName);

    private static string GetConstructorString(AttributeData attribute, int index) =>
        attribute != null &&
        attribute.ConstructorArguments.Length > index
            ? attribute.ConstructorArguments[index].Value as string
            : null;

    private static INamedTypeSymbol GetConstructorType(AttributeData attribute, int index) =>
        attribute != null &&
        attribute.ConstructorArguments.Length > index
            ? attribute.ConstructorArguments[index].Value as INamedTypeSymbol
            : null;

    private static bool HasJsonRegistration(
        INamedTypeSymbol jsonContext,
        INamedTypeSymbol recordType)
    {
        foreach (AttributeData attribute in jsonContext.GetAttributes())
        {
            if (attribute.AttributeClass == null ||
                attribute.AttributeClass.ToDisplayString() !=
                "System.Text.Json.Serialization.JsonSerializableAttribute" ||
                attribute.ConstructorArguments.Length == 0)
            {
                continue;
            }

            ITypeSymbol registeredType =
                attribute.ConstructorArguments[0].Value as ITypeSymbol;
            if (SymbolEqualityComparer.Default.Equals(registeredType, recordType))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetNamedString(AttributeData attribute, string name)
    {
        if (attribute == null)
        {
            return null;
        }

        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == name)
            {
                return argument.Value.Value as string;
            }
        }

        return null;
    }

    private static ImmutableArray<TypedConstant> GetNamedArray(AttributeData attribute, string name)
    {
        if (attribute != null)
        {
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                if (argument.Key == name && argument.Value.Kind == TypedConstantKind.Array)
                    return argument.Value.Values;
            }
        }
        return ImmutableArray<TypedConstant>.Empty;
    }

    private static bool GetNamedBoolean(
        AttributeData attribute,
        string name,
        bool fallback)
    {
        if (attribute == null)
        {
            return fallback;
        }

        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is bool)
            {
                return (bool)argument.Value.Value;
            }
        }

        return fallback;
    }

    private static bool TryGetMutationMode(
        AttributeData attribute,
        out string mutationMode,
        out int rawValue)
    {
        rawValue = 0;
        if (attribute != null)
        {
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                if (argument.Key == "MutationMode" && argument.Value.Value is int value)
                {
                    rawValue = value;
                    mutationMode = value switch
                    {
                        0 => "Mutable",
                        1 => "AppendOnly",
                        2 => "AppendOnlyWithAdministrativePurge",
                        3 => "ReadOnly",
                        _ => string.Empty,
                    };
                    return mutationMode.Length != 0;
                }
            }
        }

        mutationMode = "Mutable";
        return true;
    }

    private static long GetNamedInt64(
        AttributeData attribute,
        string name,
        long fallback)
    {
        if (attribute == null)
        {
            return fallback;
        }

        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value != null)
            {
                return Convert.ToInt64(argument.Value.Value, CultureInfo.InvariantCulture);
            }
        }

        return fallback;
    }

    private static string GetJsonPropertyName(IPropertySymbol property)
    {
        AttributeData attribute = FindAttribute(property, JsonPropertyNameAttribute);
        return GetConstructorString(attribute, 0);
    }

    private static string GetJsonNamingPolicy(INamedTypeSymbol jsonContext)
    {
        AttributeData options = FindAttribute(jsonContext, JsonOptionsAttribute);
        if (options == null)
        {
            return null;
        }

        foreach (KeyValuePair<string, TypedConstant> argument in options.NamedArguments)
        {
            if (argument.Key != "PropertyNamingPolicy" ||
                argument.Value.Type == null ||
                argument.Value.Value == null)
            {
                continue;
            }

            long selected = Convert.ToInt64(
                argument.Value.Value,
                CultureInfo.InvariantCulture);
            IFieldSymbol member = argument.Value.Type.GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(field =>
                    field.HasConstantValue &&
                    Convert.ToInt64(
                        field.ConstantValue,
                        CultureInfo.InvariantCulture) == selected);
            return member?.Name switch
            {
                "CamelCase" => "CamelCase",
                "SnakeCaseLower" => "SnakeCaseLower",
                "SnakeCaseUpper" => "SnakeCaseUpper",
                "KebabCaseLower" => "KebabCaseLower",
                "KebabCaseUpper" => "KebabCaseUpper",
                _ => null,
            };
        }

        return null;
    }

    private static bool IsNullable(IPropertySymbol property)
    {
        INamedTypeSymbol named = property.Type as INamedTypeSymbol;
        if (named != null &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return true;
        }

        return property.Type.IsReferenceType &&
               property.NullableAnnotation == NullableAnnotation.Annotated;
    }

    private static string TypedRecordIdTarget(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array && array.Rank == 1)
            return TypedRecordIdTarget(array.ElementType);

        if (type is INamedTypeSymbol collection && IsApprovedRecordIdCollection(collection))
            return TypedRecordIdTarget(collection.TypeArguments[0]);

        if (type is INamedTypeSymbol named &&
            named.IsGenericType &&
            named.ConstructedFrom.ToDisplayString() == "HPD.Base.BaseRecordId<TRecord>")
        {
            return named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        return null;
    }

    private static bool IsManyRecordIdShape(ITypeSymbol type) =>
        type is IArrayTypeSymbol { Rank: 1 } ||
        type is INamedTypeSymbol named && IsApprovedRecordIdCollection(named);

    private static bool IsApprovedRecordIdCollection(INamedTypeSymbol type)
    {
        if (!type.IsGenericType || type.TypeArguments.Length != 1) return false;
        string definition = type.ConstructedFrom.ToDisplayString();
        return definition is "System.Collections.Generic.IReadOnlyList<T>" or
            "System.Collections.Immutable.ImmutableArray<T>";
    }

    private static string GetSchemaType(ITypeSymbol type)
    {
        if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::HPD.Base.BaseBinary") return "string";
        if (IsBaseVector(type))
        {
            return "vector";
        }

        IArrayTypeSymbol array = type as IArrayTypeSymbol;
        if (array != null)
        {
            return "array";
        }

        INamedTypeSymbol named = type as INamedTypeSymbol;
        if (named != null &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return GetSchemaType(named.TypeArguments[0]);
        }

        if (named != null && IsApprovedRecordIdCollection(named)) return "array";

        if (type.TypeKind == TypeKind.Enum)
        {
            return "string";
        }

        if (TypedRecordIdTarget(type) != null)
        {
            return "id";
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_String:
            case SpecialType.System_Char:
                return "string";
            case SpecialType.System_Boolean:
                return "boolean";
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                return "integer";
            case SpecialType.System_Decimal:
                return "decimal";
            case SpecialType.System_Single:
            case SpecialType.System_Double:
                return "number";
            default:
                return IsKnownStringShape(type) ? "string" : "object";
        }
    }

    private static string GetSchemaFormat(ITypeSymbol type)
    {
        string name = type.ToDisplayString();
        if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::HPD.Base.BaseBinary") return "base64";
        if (IsBaseVector(type))
        {
            return "float32";
        }
        if (name == "System.DateTime" || name == "System.DateTimeOffset")
        {
            return "date-time";
        }

        if (name == "System.Guid")
        {
            return "uuid";
        }

        return null;
    }

    private static bool IsKnownStringShape(ITypeSymbol type)
    {
        string name = type.ToDisplayString();
        return name == "System.DateTime" ||
               name == "System.DateTimeOffset" ||
               name == "System.Guid";
    }

    private static bool IsBaseVector(ITypeSymbol type)
    {
        string name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return name is "global::HPD.Base.BaseVector" or "BaseVector";
    }

    private static string EscapeIdentifier(string value) =>
        SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None ? "@" + value : value;

    private static bool IsValidId(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || !IsAsciiLetterOrDigit(value[0]))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!IsAsciiLetterOrDigit(character) &&
                character != '.' &&
                character != '-' &&
                character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static bool ValidApplicationName(string value) => value.Length is >= 1 and <= 128 &&
        (value[0] is >= 'a' and <= 'z' or >= 'A' and <= 'Z' || value[0] == '_') &&
        value.All(static character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' || character == '_');

    private static string Sanitize(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            result.Append(char.IsLetterOrDigit(character) || character == '_'
                ? character
                : '_');
        }

        return result.ToString();
    }

    private static string VectorHandleName(string id)
    {
        string tail = id.Split('.').Last();
        var result = new StringBuilder(tail.Length);
        bool upper = true;
        foreach (char character in tail)
        {
            if (!char.IsLetterOrDigit(character)) { upper = true; continue; }
            result.Append(upper ? char.ToUpperInvariant(character) : character);
            upper = false;
        }
        return result.Length == 0 ? "Index" : EscapeIdentifier(result.ToString());
    }

    private static string Literal(string value) =>
        SymbolDisplay.FormatLiteral(value ?? string.Empty, true);

    private static Location GetLocation(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault(location => location.IsInSource) ?? Location.None;

    private static Location GetLocation(AttributeData attribute, ISymbol fallback) =>
        attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ??
        GetLocation(fallback);

    private sealed class CollectionModel
    {
        /// <summary>Provides the namespace value.</summary>
        public string Namespace;
        /// <summary>Provides the type name value.</summary>
        public string TypeName;
        /// <summary>Provides the full type name value.</summary>
        public string FullTypeName;
        /// <summary>Provides the is record value.</summary>
        public bool IsRecord;
        /// <summary>Provides the collection ID value.</summary>
        public string CollectionId;
        /// <summary>Provides the collection name value.</summary>
        public string CollectionName;
        /// <summary>Provides the collection kind value.</summary>
        public string CollectionKind;
        /// <summary>Provides the strict value.</summary>
        public bool Strict;
        /// <summary>Provides the collection mutation mode value.</summary>
        public string MutationMode;
        /// <summary>Provides the fields value.</summary>
        public List<FieldModel> Fields;
        /// <summary>Provides the indexes value.</summary>
        public List<IndexModel> Indexes;
        /// <summary>Provides the vector indexes value.</summary>
        public List<VectorIndexModel> VectorIndexes;
        /// <summary>Provides the closed generated storage requirement references.</summary>
        public List<string> StorageRequirements;
        /// <summary>Provides the context type name value.</summary>
        public string ContextTypeName;
        /// <summary>Gets the serializer-owned built-in naming-policy property.</summary>
        public string JsonNamingPolicy;
        public List<SerializerPropertyModel> SerializerProperties;
        /// <summary>Provides the hint name value.</summary>
        public string HintName;
    }

    private sealed class SerializerPropertyModel
    {
        public string DeclaringType;
        public string ApplicationName;
        public string PropertyType;
        public string ExplicitWireName;
        public bool Required;
        public bool Nullable;
        public string ConverterIdentity;
        public string ConverterType;
    }

    private sealed class FieldModel
    {
        /// <summary>Provides the ID value.</summary>
        public string Id;
        /// <summary>Provides the property name value.</summary>
        public string PropertyName;
        /// <summary>Provides the stored name value.</summary>
        public string ApplicationName;
        public string WireName;
        public bool ExplicitWireName;
        /// <summary>Provides the type name value.</summary>
        public string TypeName;
        /// <summary>Provides the typed record ID target value.</summary>
        public string TypedRecordIdTarget;
        /// <summary>Provides the schema type value.</summary>
        public string SchemaType;
        /// <summary>Provides the schema format value.</summary>
        public string SchemaFormat;
        /// <summary>Provides the nullable value.</summary>
        public bool Nullable;
        /// <summary>Provides the required value.</summary>
        public bool Required;
        /// <summary>Provides the operators value.</summary>
        public long Operators;
        /// <summary>Provides confidentiality.</summary>
        public int Confidentiality;
        /// <summary>Provides the seven optional disclosure values.</summary>
        public int[] Disclosure;
        /// <summary>Provides the binary maximum.</summary>
        public int MaximumBytes;
        /// <summary>Provides the relation value.</summary>
        public RelationModel Relation;
    }

    private sealed class RelationModel
    {
        /// <summary>Provides the ID value.</summary>
        public string Id;
        /// <summary>Provides the target collection ID value.</summary>
        public string TargetCollectionId;
        /// <summary>Provides the target field ID value.</summary>
        public string TargetFieldId;
        /// <summary>Provides the local multiplicity value.</summary>
        public long LocalMultiplicity;
        /// <summary>Provides the inverse multiplicity value.</summary>
        public long InverseMultiplicity;
        /// <summary>Provides the inverse navigation ID value.</summary>
        public string InverseNavigationId;
        /// <summary>Provides the delete behavior value.</summary>
        public long DeleteBehavior;
        /// <summary>Provides the include allowed value.</summary>
        public bool IncludeAllowed;
        /// <summary>Provides the minimum count value.</summary>
        public int? MinimumCount;
        /// <summary>Provides the maximum count value.</summary>
        public int? MaximumCount;
        /// <summary>Provides the include filter allowed value.</summary>
        public bool IncludeFilterAllowed;
        /// <summary>Provides the include sort allowed value.</summary>
        public bool IncludeSortAllowed;
        /// <summary>Provides the include maximum depth value.</summary>
        public int? IncludeMaximumDepth;
    }

    private sealed class IndexModel
    {
        /// <summary>Provides the ID value.</summary>
        public string Id;
        /// <summary>Provides the unique value.</summary>
        public bool Unique;
        /// <summary>Provides the required value.</summary>
        public bool Required;
        /// <summary>Provides the fields value.</summary>
        public List<FieldModel> Fields;
    }

    private sealed class VectorIndexModel
    {
        public string Id;
        public string PropertyName;
        public FieldModel VectorField;
        public string VectorSpaceId;
        public int Dimensions;
        public int Function;
        public List<FieldModel> FilterFields;
    }
}
