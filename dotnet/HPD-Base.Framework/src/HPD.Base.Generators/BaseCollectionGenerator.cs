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
    private const string IndexAttribute =
        "HPD.Base.BaseIndexAttribute";
    private const string RelationAttribute =
        "HPD.Base.BaseRelationAttribute";
    private const string JsonPropertyNameAttribute =
        "System.Text.Json.Serialization.JsonPropertyNameAttribute";
    private const string JsonOptionsAttribute =
        "System.Text.Json.Serialization.JsonSourceGenerationOptionsAttribute";

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
        var storedNames = new HashSet<string>(StringComparer.Ordinal);
        var propertyFields = new Dictionary<string, FieldModel>(StringComparer.Ordinal);
        var relationIds = new HashSet<string>(StringComparer.Ordinal);
        bool camelCaseJson = UsesCamelCase(jsonContext);

        foreach (IPropertySymbol property in symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property =>
                !property.IsStatic &&
                !property.IsIndexer &&
                property.DeclaredAccessibility == Accessibility.Public &&
                property.GetMethod != null)
            .OrderBy(property => property.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue))
        {
            AttributeData fieldAttribute = FindAttribute(property, FieldAttribute);
            if (fieldAttribute == null)
            {
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
                continue;
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
            string storedName =
                GetNamedString(fieldAttribute, "Name") ??
                GetJsonPropertyName(property) ??
                (camelCaseJson ? ToCamelCase(property.Name) : property.Name);
            if (string.IsNullOrWhiteSpace(storedName))
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

            if (!storedNames.Add(storedName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateField,
                    GetLocation(property),
                    collectionId,
                    storedName));
                return null;
            }

            var field = new FieldModel
            {
                Id = fieldId,
                PropertyName = property.Name,
                StoredName = storedName,
                TypeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                TypedRecordIdTarget = TypedRecordIdTarget(property.Type),
                SchemaType = GetSchemaType(property.Type),
                SchemaFormat = GetSchemaFormat(property.Type),
                Nullable = IsNullable(property),
                Required = property.IsRequired || !IsNullable(property),
                Operators = operators,
            };

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
        var indexIds = new HashSet<string>(StringComparer.Ordinal);
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

        string fullTypeName =
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string metadataName = symbol.ToDisplayString(
            new SymbolDisplayFormat(
                typeQualificationStyle:
                    SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));

        fields.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        indexes.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));

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
        source.Append("    private static global::HPD.Base.BaseCollection<")
            .Append(model.FullTypeName).AppendLine("> CreateHPDBaseCollection()");
        source.AppendLine("    {");
        source.Append("        var jsonTypeInfo = ")
            .Append(model.ContextTypeName)
            .Append(".Default.GetTypeInfo(typeof(").Append(model.FullTypeName)
            .Append(")) as global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<")
            .Append(model.FullTypeName).AppendLine(">");
        source.Append("            ?? throw new global::System.InvalidOperationException(")
            .Append(Literal("The configured JSON context does not expose the generated record type."))
            .AppendLine(");");
        source.AppendLine();
        source.Append("        return global::HPD.Base.BaseCollection<")
            .Append(model.FullTypeName).AppendLine(">.Create(");
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
        source.AppendLine("            },");
        source.AppendLine("            jsonTypeInfo,");
        source.AppendLine("            fields =>");
        source.AppendLine("            {");

        foreach (FieldModel field in model.Fields)
        {
            source.Append("                Fields.Set")
                .Append(EscapeIdentifier(field.PropertyName)).Append("(fields.Add<")
                .Append(field.TypeName).Append(">(")
                .Append(Literal(field.Id)).Append(", ")
                .Append(Literal(field.StoredName)).Append(", nullable: ")
                .Append(field.Nullable ? "true" : "false")
                .Append(", operators: (global::HPD.Base.BaseFieldOperator)")
                .Append(field.Operators.ToString(CultureInfo.InvariantCulture))
                .AppendLine("));");
        }

        source.AppendLine("            });");
        source.AppendLine("    }");
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
            source.Append("                        Name = ").Append(Literal(field.StoredName)).AppendLine(",");
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

    private static bool IsSupported(INamedTypeSymbol symbol) =>
        symbol.TypeKind == TypeKind.Class &&
        symbol.ContainingType == null &&
        symbol.TypeParameters.Length == 0;

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

    private static bool UsesCamelCase(INamedTypeSymbol jsonContext)
    {
        AttributeData options = FindAttribute(jsonContext, JsonOptionsAttribute);
        if (options == null)
        {
            return false;
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
            return member != null && member.Name == "CamelCase";
        }

        return false;
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

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToLowerInvariant();
        }

        return char.ToLowerInvariant(value[0]) + value.Substring(1);
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
        /// <summary>Provides the context type name value.</summary>
        public string ContextTypeName;
        /// <summary>Provides the hint name value.</summary>
        public string HintName;
    }

    private sealed class FieldModel
    {
        /// <summary>Provides the ID value.</summary>
        public string Id;
        /// <summary>Provides the property name value.</summary>
        public string PropertyName;
        /// <summary>Provides the stored name value.</summary>
        public string StoredName;
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
}
