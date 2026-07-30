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

namespace HPD.Base.Application.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class BaseCollectionGenerator : IIncrementalGenerator
{
    private const string CollectionAttribute =
        "HPD.Base.Application.Generation.BaseCollectionAttribute";
    private const string FieldAttribute =
        "HPD.Base.Application.Generation.BaseFieldAttribute";
    private const string IndexAttribute =
        "HPD.Base.Application.Generation.BaseIndexAttribute";
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
        var storedNames = new HashSet<string>(StringComparer.Ordinal);
        var propertyFields = new Dictionary<string, FieldModel>(StringComparer.Ordinal);
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
            if (GetNamedBoolean(fieldAttribute, "Ignore", false))
            {
                continue;
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
                PropertyName = property.Name,
                StoredName = storedName,
                TypeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                SchemaType = GetSchemaType(property.Type),
                SchemaFormat = GetSchemaFormat(property.Type),
                Nullable = IsNullable(property),
                Required = property.IsRequired || !IsNullable(property),
                Operators = operators,
            };

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
        source.Append("    public static global::HPD.Base.Application.Collections.BaseCollection<")
            .Append(model.FullTypeName)
            .AppendLine("> Collection { get; } = CreateHPDBaseCollection();");
        source.AppendLine();
        source.AppendLine("    public static class Fields");
        source.AppendLine("    {");

        foreach (FieldModel field in model.Fields)
        {
            source.Append("        private static global::HPD.Base.Application.Collections.BaseField<")
                .Append(model.FullTypeName).Append(", ").Append(field.TypeName)
                .Append("> __").Append(EscapeIdentifier(field.PropertyName))
                .AppendLine(" = null!;");
            source.Append("        public static global::HPD.Base.Application.Collections.BaseField<")
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
                .Append("(global::HPD.Base.Application.Collections.BaseField<")
                .Append(model.FullTypeName).Append(", ").Append(field.TypeName)
                .Append("> value) => __")
                .Append(EscapeIdentifier(field.PropertyName)).AppendLine(" = value;");
            source.AppendLine();
        }

        source.AppendLine("    }");
        source.AppendLine();
        source.Append("    private static global::HPD.Base.Application.Collections.BaseCollection<")
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
        source.Append("        return global::HPD.Base.Application.Collections.BaseCollection<")
            .Append(model.FullTypeName).AppendLine(">.Create(");
        source.AppendLine("            new global::HPD.Base.Schema.CollectionDefinition");
        source.AppendLine("            {");
        source.Append("                Id = ").Append(Literal(model.CollectionId)).AppendLine(",");
        source.Append("                Name = ").Append(Literal(model.CollectionName)).AppendLine(",");
        source.Append("                Kind = ").Append(Literal(model.CollectionKind)).AppendLine(",");
        source.Append("                SchemaMode = global::HPD.Base.Schema.SchemaMode.")
            .Append(model.Strict ? "Strict" : "Loose").AppendLine(",");
        source.Append("                UnknownFields = global::HPD.Base.Schema.UnknownFieldPolicy.")
            .Append(model.Strict ? "Reject" : "Preserve").AppendLine(",");
        source.AppendLine("                Operations = new global::HPD.Base.Schema.CollectionOperationMatrix");
        source.AppendLine("                {");
        source.AppendLine("                    List = true,");
        source.AppendLine("                    Get = true,");
        source.AppendLine("                    Create = true,");
        source.AppendLine("                    Patch = true,");
        source.AppendLine("                    Replace = true,");
        source.AppendLine("                    Upsert = true,");
        source.AppendLine("                    Delete = true,");
        source.AppendLine("                },");
        source.AppendLine("                Source = new global::HPD.Base.Schema.SchemaSourceDescriptor");
        source.AppendLine("                {");
        source.AppendLine("                    Id = \"hpd.base.application.generated\",");
        source.AppendLine("                    Kind = global::HPD.Base.Schema.SchemaSourceKind.Generated,");
        source.AppendLine("                },");
        RenderFieldDefinitions(source, model.Fields);
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
                .Append(Literal(field.StoredName)).Append(", nullable: ")
                .Append(field.Nullable ? "true" : "false")
                .Append(", operators: (global::HPD.Base.Application.Collections.BaseFieldOperator)")
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
        IReadOnlyList<FieldModel> fields)
    {
        source.AppendLine("                Fields =");
        source.AppendLine("                [");
        foreach (FieldModel field in fields)
        {
            source.AppendLine("                    new global::HPD.Base.Schema.FieldDefinition");
            source.AppendLine("                    {");
            source.Append("                        Id = ").Append(Literal(field.StoredName)).AppendLine(",");
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
            source.AppendLine("                    new global::HPD.Base.Schema.IndexDefinition");
            source.AppendLine("                    {");
            source.Append("                        Id = ").Append(Literal(index.Id)).AppendLine(",");
            source.Append("                        Name = ").Append(Literal(index.Id)).AppendLine(",");
            source.Append("                        CollectionId = ")
                .Append(Literal(model.CollectionId)).AppendLine(",");
            source.AppendLine("                        Kind = global::HPD.Base.Schema.IndexKind.Key,");
            source.Append("                        Unique = ")
                .Append(index.Unique ? "true" : "false").AppendLine(",");
            source.Append("                        Enforcement = global::HPD.Base.Schema.EnforcementOwner.")
                .Append(index.Required ? "Store" : "Advisory").AppendLine(",");
            source.AppendLine("                        Parts =");
            source.AppendLine("                        [");
            foreach (FieldModel field in index.Fields)
            {
                source.AppendLine("                            new global::HPD.Base.Schema.IndexPart");
                source.AppendLine("                            {");
                source.AppendLine("                                Kind = global::HPD.Base.Schema.IndexPartKind.Field,");
                source.Append("                                FieldPath = ")
                    .Append(Literal(field.StoredName)).AppendLine(",");
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

        if (type.TypeKind == TypeKind.Enum)
        {
            return "string";
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
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
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
        public string Namespace;
        public string TypeName;
        public string FullTypeName;
        public bool IsRecord;
        public string CollectionId;
        public string CollectionName;
        public string CollectionKind;
        public bool Strict;
        public List<FieldModel> Fields;
        public List<IndexModel> Indexes;
        public string ContextTypeName;
        public string HintName;
    }

    private sealed class FieldModel
    {
        public string PropertyName;
        public string StoredName;
        public string TypeName;
        public string SchemaType;
        public string SchemaFormat;
        public bool Nullable;
        public bool Required;
        public long Operators;
    }

    private sealed class IndexModel
    {
        public string Id;
        public bool Unique;
        public bool Required;
        public List<FieldModel> Fields;
    }
}
