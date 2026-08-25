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

/// <summary>Renders collection roots beneath the combined schema generator.</summary>
internal static class BaseCollectionGenerator
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
    private const string IndexPartAttribute = "HPD.Base.BaseIndexPartAttribute";
    private const string IndexPredicateAttribute = "HPD.Base.BaseIndexPredicateAttribute";
    private const string RelationAttribute =
        "HPD.Base.BaseRelationAttribute";
    private const string SubjectReferenceAttribute =
        "HPD.Base.BaseSubjectReferenceAttribute";
    private const string ExportedSubjectAttribute =
        "HPD.Base.BaseExportedSubjectAttribute";
    private const string VectorIndexAttribute =
        "HPD.Base.BaseVectorIndexAttribute";
    private const string TextIndexAttribute =
        "HPD.Base.BaseTextIndexAttribute";
    private const string JsonPropertyNameAttribute =
        "System.Text.Json.Serialization.JsonPropertyNameAttribute";
    private const string JsonOptionsAttribute =
        "System.Text.Json.Serialization.JsonSourceGenerationOptionsAttribute";
    private const string JsonIgnoreAttribute = "System.Text.Json.Serialization.JsonIgnoreAttribute";
    private const string JsonConverterAttribute = "System.Text.Json.Serialization.JsonConverterAttribute";
    private const string JsonStringEnumMemberNameAttribute = "System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute";

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

    private static readonly DiagnosticDescriptor IncompatibleScalarConstraint = new(
        "HPDBASE5401", "Incompatible BASE scalar constraint", "Collection '{0}' field '{1}' applies a constraint incompatible with its exact scalar codec", "HPD.Base.Generation", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor InvalidScalarBound = new(
        "HPDBASE5402", "Invalid BASE scalar bound", "Collection '{0}' field '{1}' declares an invalid scalar bound", "HPD.Base.Generation", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor ContradictoryScalarRange = new(
        "HPDBASE5403", "Contradictory BASE scalar range", "Collection '{0}' field '{1}' declares a minimum greater than its maximum", "HPD.Base.Generation", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor AmbiguousPresenceNullability = new(
        "HPDBASE5404", "Ambiguous BASE presence or nullability", "Collection '{0}' field '{1}' contradicts its frozen serializer contract", "HPD.Base.Generation", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor MissingMandatoryScalarCeiling = new(
        "HPDBASE5405", "Missing mandatory BASE scalar ceiling", "Collection '{0}' field '{1}' omits a mandatory canonical JSON or collection ceiling", "HPD.Base.Generation", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor IncompatibleIndexLiteral = new(
        "HPDBASE5411", "Incompatible BASE index literal", "Collection '{0}' index '{1}' declares a noncanonical or incompatible equality literal", "HPD.Base.Generation", DiagnosticSeverity.Error, true);

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

    internal static readonly DiagnosticDescriptor UnsupportedSerializerContract = new DiagnosticDescriptor(
        "HPDBASE0447", "Unsupported serializer contract",
        "Serializer contract '{0}' is unsupported: {1}", "HPD.Base.Generation",
        DiagnosticSeverity.Error, true);

    internal static readonly DiagnosticDescriptor SerializerGraphLimit = new DiagnosticDescriptor(
        "HPDBASE0448", "Serializer graph limit exceeded",
        "Serializer contract '{0}' graph exceeds a closed L44 bound", "HPD.Base.Generation",
        DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor GeneratedInfrastructureInvocation = new DiagnosticDescriptor(
        "HPDBASE0449", "Generated serializer infrastructure is not an application API",
        "'{0}' may only be emitted by HPD Base generated source; the compiled application/build pipeline is trusted",
        "HPD.Base.Generation", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor GeneratedSubjectInfrastructureInvocation = new DiagnosticDescriptor(
        "HPDBASE0461", "Generated subject infrastructure is not an application API",
        "'{0}' may only be emitted by HPD Base generated source; the compiled application/build pipeline is trusted",
        "HPD.Base.Generation", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor GeneratedSemanticActivationInfrastructureInvocation = new DiagnosticDescriptor(
        "HPDBASE0530", "Generated semantic activation infrastructure is not an application API",
        "'{0}' may only be emitted by HPD Base generated source; the compiled application/build pipeline is trusted",
        "HPD.Base.Generation", DiagnosticSeverity.Error, true);

    internal static void RegisterForbiddenReferences(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<(Location Location, string Method, int Kind)?> forbiddenReferences =
            context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is SimpleNameSyntax,
                static (syntaxContext, _) => ForbiddenGeneratedReference(syntaxContext));
        context.RegisterSourceOutput(forbiddenReferences.Where(static item => item.HasValue),
            static (productionContext, item) => productionContext.ReportDiagnostic(Diagnostic.Create(
                item!.Value.Kind switch
                {
                    1 => GeneratedSubjectInfrastructureInvocation,
                    2 => GeneratedSemanticActivationInfrastructureInvocation,
                    _ => GeneratedInfrastructureInvocation,
                }, item.Value.Location, item.Value.Method)));
    }

    private static (Location Location, string Method, int Kind)? ForbiddenGeneratedReference(GeneratorSyntaxContext context)
    {
        var name = (SimpleNameSyntax)context.Node;
        SymbolInfo symbolInfo = context.SemanticModel.GetSymbolInfo(name);
        IMethodSymbol method = symbolInfo.Symbol as IMethodSymbol;
        if (method is null || !IsGeneratedInfrastructure(method))
            method = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>()
                .FirstOrDefault(IsGeneratedInfrastructure);
        if (method is null) return null;
        string owner = method.ContainingType.OriginalDefinition.ToDisplayString();
        return (name.GetLocation(), owner + "." + method.Name,
            owner == "HPD.Base.BaseGeneratedSemanticActivations" ? 2
                : owner is "HPD.Base.BaseGeneratedSubjects" or "HPD.Base.BaseSubjectReferenceJsonConverterFactory" ? 1 : 0);
    }

    private static bool IsGeneratedInfrastructure(IMethodSymbol method)
    {
        string owner = method.ContainingType.OriginalDefinition.ToDisplayString();
        return owner == "HPD.Base.BaseSerializerGeneratedContract" && method.Name == "RegisterContext" ||
            owner == "HPD.Base.BaseCollection<T>" && method.Name == "CreateGenerated" ||
            owner == "HPD.Base.BaseReadGeneratedContract" && method.Name == "CreateGenerated" ||
            owner == "HPD.Base.BaseGeneratedSubjects" && method.Name == "Register" ||
            owner == "HPD.Base.BaseSubjectReferenceJsonConverterFactory" && method.Name == "Register" ||
            owner == "HPD.Base.BaseGeneratedSemanticActivations" && method.Name == "Register";
    }

    internal static void GenerateCombined(
        SourceProductionContext context,
        ImmutableArray<INamedTypeSymbol> symbols,
        ImmutableDictionary<INamedTypeSymbol, ContextValidationResult> contextResults)
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

            INamedTypeSymbol declaredContext = GetConstructorType(collection, 1);
            if (declaredContext is not null && contextResults.TryGetValue(declaredContext, out ContextValidationResult contextResult) && !contextResult.IsValid)
            {
                context.AddSource(
                    "HPDBaseCollectionRecovery_" + Sanitize(symbol.ToDisplayString()) + ".g.cs",
                    SourceText.From(RenderRecovery(symbol), Encoding.UTF8));
                continue;
            }

            contextResults.TryGetValue(declaredContext, out ContextValidationResult sharedContextResult);
            CollectionModel model = CreateModel(context, symbol, collection, collectionId, sharedContextResult);
            if (model == null)
            {
                context.AddSource(
                    "HPDBaseCollectionRecovery_" + Sanitize(symbol.ToDisplayString()) + ".g.cs",
                    SourceText.From(RenderRecovery(symbol), Encoding.UTF8));
                continue;
            }

            context.AddSource(
                model.HintName + ".g.cs",
                SourceText.From(Render(model), Encoding.UTF8));
        }
    }

    private static string RenderRecovery(INamedTypeSymbol symbol)
    {
        var source = new StringBuilder("// <auto-generated />\n#nullable enable\n\n");
        if (!symbol.ContainingNamespace.IsGlobalNamespace)
            source.Append("namespace ").Append(symbol.ContainingNamespace.ToDisplayString()).AppendLine(";\n");
        source.Append("partial ").Append(symbol.IsRecord ? "record class " : "class ")
            .Append(symbol.Name).AppendLine("\n{");
        string record = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        source.Append("    public static global::HPD.Base.BaseCollection<").Append(record)
            .AppendLine("> Collection => null!;\n");
        source.AppendLine("    public static class Fields\n    {");
        foreach (IPropertySymbol property in symbol.GetMembers().OfType<IPropertySymbol>()
            .Where(static property => !property.IsStatic && !property.IsIndexer &&
                FindAttribute(property, FieldAttribute) is not null)
            .OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            source.Append("        public static global::HPD.Base.BaseField<").Append(record).Append(", ")
                .Append(property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append("> ")
                .Append(EscapeIdentifier(property.Name)).AppendLine(" => null!;");
        }
        source.AppendLine("    }\n}");
        string[] vectorHandles = symbol.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() == VectorIndexAttribute)
            .Select(attribute => GetConstructorString(attribute, 0))
            .Where(IsValidId)
            .Select(VectorHandleName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (vectorHandles.Length != 0)
        {
            int closing = source.ToString().LastIndexOf("}\n", StringComparison.Ordinal);
            source.Remove(closing, source.Length - closing);
            source.AppendLine("    public static class VectorIndexes\n    {");
            foreach (string handle in vectorHandles)
                source.Append("        public static global::HPD.Base.BaseVectorIndex<").Append(record).Append("> ")
                    .Append(handle).AppendLine(" => null!;");
            source.AppendLine("    }\n}");
        }
        return source.ToString();
    }

    private static CollectionModel CreateModel(
        SourceProductionContext context,
        INamedTypeSymbol symbol,
        AttributeData collection,
        string collectionId,
        ContextValidationResult sharedContextResult)
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

        string systemOwnerModuleId = GetNamedString(collection, "SystemOwnerModuleId");
        if (systemOwnerModuleId != null && !IsValidId(systemOwnerModuleId))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidField,
                GetLocation(collection, symbol),
                collectionId,
                "<collection>",
                "the system owner module identifier must use the stable BASE identifier grammar"));
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
            if (serializerIgnored || property.SetMethod is null)
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
            if (HasNamed(fieldAttribute, "Presence"))
            {
                long presence = GetNamedInt64(fieldAttribute, "Presence", -1);
                if (presence is < 0 or > 1) { context.ReportDiagnostic(Diagnostic.Create(InvalidField, GetLocation(property), collectionId, property.Name, "presence is invalid")); return null; }
                field.Required = presence == 0;
            }
            if (HasNamed(fieldAttribute, "Nullability"))
            {
                long nullability = GetNamedInt64(fieldAttribute, "Nullability", -1);
                if (nullability is < 0 or > 1) { context.ReportDiagnostic(Diagnostic.Create(InvalidField, GetLocation(property), collectionId, property.Name, "nullability is invalid")); return null; }
                field.Nullable = nullability == 1;
            }
            if (field.Nullable && property.Type.IsValueType && property.Type.NullableAnnotation != NullableAnnotation.Annotated)
            {
                context.ReportDiagnostic(Diagnostic.Create(AmbiguousPresenceNullability, GetLocation(property), collectionId, property.Name)); return null;
            }
            field.MinimumUtf8Bytes = NamedInt32(fieldAttribute, "MinimumUtf8Bytes");
            field.MaximumUtf8Bytes = NamedInt32(fieldAttribute, "MaximumUtf8Bytes");
            field.StringNormalization = HasNamed(fieldAttribute, "StringNormalization") ? (int?)GetNamedInt64(fieldAttribute, "StringNormalization", -1) : null;
            string[] numericBounds = { "MinimumInt64", "MaximumInt64", "MinimumInt32", "MaximumInt32", "MinimumUInt32", "MaximumUInt32", "MinimumUInt64", "MaximumUInt64" };
            foreach (string bound in numericBounds)
            {
                string presence = "Has" + bound;
                bool hasValue = HasNamed(fieldAttribute, bound), hasPresence = HasNamed(fieldAttribute, presence), admitted = GetNamedBoolean(fieldAttribute, presence, false);
                if (hasValue != hasPresence || hasPresence && !admitted)
                { context.ReportDiagnostic(Diagnostic.Create(InvalidScalarBound, GetLocation(property), collectionId, property.Name)); return null; }
            }
            field.MinimumInt64 = GetNamedBoolean(fieldAttribute, "HasMinimumInt64", false) ? (long?)GetNamedInt64(fieldAttribute, "MinimumInt64", long.MinValue) : null;
            field.MaximumInt64 = GetNamedBoolean(fieldAttribute, "HasMaximumInt64", false) ? (long?)GetNamedInt64(fieldAttribute, "MaximumInt64", long.MinValue) : null;
            field.MinimumInt32 = GetNamedBoolean(fieldAttribute, "HasMinimumInt32", false) ? NamedInt32(fieldAttribute, "MinimumInt32") : null;
            field.MaximumInt32 = GetNamedBoolean(fieldAttribute, "HasMaximumInt32", false) ? NamedInt32(fieldAttribute, "MaximumInt32") : null;
            field.MinimumUInt32 = GetNamedBoolean(fieldAttribute, "HasMinimumUInt32", false) ? NamedUInt32(fieldAttribute, "MinimumUInt32") : null;
            field.MaximumUInt32 = GetNamedBoolean(fieldAttribute, "HasMaximumUInt32", false) ? NamedUInt32(fieldAttribute, "MaximumUInt32") : null;
            field.MinimumUInt64 = GetNamedBoolean(fieldAttribute, "HasMinimumUInt64", false) ? NamedUInt64(fieldAttribute, "MinimumUInt64") : null;
            field.MaximumUInt64 = GetNamedBoolean(fieldAttribute, "HasMaximumUInt64", false) ? NamedUInt64(fieldAttribute, "MaximumUInt64") : null;
            field.MinimumDecimal = GetNamedString(fieldAttribute, "MinimumDecimal"); field.MaximumDecimal = GetNamedString(fieldAttribute, "MaximumDecimal");
            field.AllowedEnumLiterals = GetNamedStrings(fieldAttribute, "AllowedEnumLiterals");
            field.MinimumCollectionItems = NamedInt32(fieldAttribute, "MinimumCollectionItems");
            field.MaximumCollectionItems = NamedInt32(fieldAttribute, "MaximumCollectionItems");
            field.MaximumCanonicalJsonBytes = NamedInt32(fieldAttribute, "MaximumCanonicalJsonBytes");
            field.JsonShape = HasNamed(fieldAttribute, "JsonShape") ? (int?)GetNamedInt64(fieldAttribute, "JsonShape", -1) : null;
            field.MaximumJsonDepth = NamedInt32(fieldAttribute, "MaximumJsonDepth");
            field.MaximumJsonArrayItems = NamedInt32(fieldAttribute, "MaximumJsonArrayItems");
            field.MaximumJsonObjectProperties = NamedInt32(fieldAttribute, "MaximumJsonObjectProperties");
            field.MaximumJsonTotalNodes = NamedInt32(fieldAttribute, "MaximumJsonTotalNodes");
            field.MaximumJsonTotalStringUtf8Bytes = NamedInt32(fieldAttribute, "MaximumJsonTotalStringUtf8Bytes");
            field.MaximumJsonTotalNameUtf8Bytes = NamedInt32(fieldAttribute, "MaximumJsonTotalNameUtf8Bytes");
            string inferredKind = ScalarKind(field);
            if (inferredKind == "ClosedEnum")
            {
                ITypeSymbol enumType = property.Type is INamedTypeSymbol nullableEnum && nullableEnum.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? nullableEnum.TypeArguments[0] : property.Type;
                AttributeData converterAttribute = FindAttribute(property, JsonConverterAttribute);
                INamedTypeSymbol converter = converterAttribute?.ConstructorArguments.Length > 0 ? converterAttribute.ConstructorArguments[0].Value as INamedTypeSymbol : null;
                bool exactConverter = converter is { IsGenericType: true } && converter.ConstructedFrom.ToDisplayString() == "HPD.Base.BaseClosedEnumJsonConverter<TEnum>" && SymbolEqualityComparer.Default.Equals(converter.TypeArguments[0], enumType);
                bool renamed = enumType.GetMembers().OfType<IFieldSymbol>().Any(member => !member.IsImplicitlyDeclared && FindAttribute(member, JsonStringEnumMemberNameAttribute) is not null);
                if (!exactConverter || renamed)
                { context.ReportDiagnostic(Diagnostic.Create(IncompatibleScalarConstraint, GetLocation(property), collectionId, property.Name)); return null; }
                field.EnumCodecLiterals = EnumLiterals(property.Type);
            }
            if (inferredKind == "UtcDateTime")
            {
                AttributeData converterAttribute = FindAttribute(property, JsonConverterAttribute);
                INamedTypeSymbol converter = converterAttribute?.ConstructorArguments.Length > 0 ? converterAttribute.ConstructorArguments[0].Value as INamedTypeSymbol : null;
                bool exactType = field.TypeName is "global::System.DateTimeOffset" or "global::System.DateTimeOffset?";
                if (!exactType || converter?.ToDisplayString() != "HPD.Base.BaseUtcDateTimeJsonConverter")
                { context.ReportDiagnostic(Diagnostic.Create(IncompatibleScalarConstraint, GetLocation(property), collectionId, property.Name)); return null; }
            }
            bool stringConstraints = field.MinimumUtf8Bytes is not null || field.MaximumUtf8Bytes is not null || field.StringNormalization is not null;
            bool integerConstraints = field.MinimumInt64 is not null || field.MaximumInt64 is not null;
            bool int32Constraints = field.MinimumInt32 is not null || field.MaximumInt32 is not null;
            bool uint32Constraints = field.MinimumUInt32 is not null || field.MaximumUInt32 is not null;
            bool uint64Constraints = field.MinimumUInt64 is not null || field.MaximumUInt64 is not null;
            bool decimalConstraints = field.MinimumDecimal is not null || field.MaximumDecimal is not null;
            bool enumConstraints = field.AllowedEnumLiterals.Length != 0;
            bool collectionConstraints = field.MinimumCollectionItems is not null || field.MaximumCollectionItems is not null;
            bool jsonConstraints = field.MaximumCanonicalJsonBytes is not null || field.JsonShape is not null || field.MaximumJsonDepth is not null || field.MaximumJsonArrayItems is not null || field.MaximumJsonObjectProperties is not null || field.MaximumJsonTotalNodes is not null || field.MaximumJsonTotalStringUtf8Bytes is not null || field.MaximumJsonTotalNameUtf8Bytes is not null;
            if (stringConstraints && inferredKind != "String" || integerConstraints && inferredKind != "Int64" || int32Constraints && inferredKind != "Int32" || uint32Constraints && inferredKind != "UInt32" || uint64Constraints && inferredKind != "UInt64" || decimalConstraints && inferredKind != "Decimal" || enumConstraints && inferredKind != "ClosedEnum" || collectionConstraints && inferredKind != "FrozenArray" || jsonConstraints && inferredKind != "CanonicalJson")
            { context.ReportDiagnostic(Diagnostic.Create(IncompatibleScalarConstraint, GetLocation(property), collectionId, property.Name)); return null; }
            if (AnyNegative(field.MinimumUtf8Bytes, field.MaximumUtf8Bytes, field.MinimumCollectionItems, field.MaximumCollectionItems, field.MaximumCanonicalJsonBytes, field.MaximumJsonDepth, field.MaximumJsonArrayItems, field.MaximumJsonObjectProperties, field.MaximumJsonTotalNodes, field.MaximumJsonTotalStringUtf8Bytes, field.MaximumJsonTotalNameUtf8Bytes) || field.StringNormalization is < 0 or > 0 || field.JsonShape is < 0 or > 2)
            { context.ReportDiagnostic(Diagnostic.Create(InvalidScalarBound, GetLocation(property), collectionId, property.Name)); return null; }
            if (field.MinimumDecimal is not null && !ValidCanonicalDecimal(field.MinimumDecimal) || field.MaximumDecimal is not null && !ValidCanonicalDecimal(field.MaximumDecimal))
            { context.ReportDiagnostic(Diagnostic.Create(InvalidScalarBound, GetLocation(property), collectionId, property.Name)); return null; }
            if (InvalidRange(field.MinimumUtf8Bytes, field.MaximumUtf8Bytes) || InvalidRange(field.MinimumInt32, field.MaximumInt32) || InvalidRange(field.MinimumInt64, field.MaximumInt64) || InvalidRange(field.MinimumUInt32, field.MaximumUInt32) || InvalidRange(field.MinimumUInt64, field.MaximumUInt64) || InvalidRange(field.MinimumCollectionItems, field.MaximumCollectionItems) || field.AllowedEnumLiterals.Distinct(StringComparer.Ordinal).Count() != field.AllowedEnumLiterals.Length || !field.AllowedEnumLiterals.SequenceEqual(field.AllowedEnumLiterals.OrderBy(static value => value, StringComparer.Ordinal)))
            { context.ReportDiagnostic(Diagnostic.Create(ContradictoryScalarRange, GetLocation(property), collectionId, property.Name)); return null; }
            if (field.MinimumDecimal is not null && field.MaximumDecimal is not null && CompareCanonicalDecimal(field.MinimumDecimal, field.MaximumDecimal) > 0)
            { context.ReportDiagnostic(Diagnostic.Create(ContradictoryScalarRange, GetLocation(property), collectionId, property.Name)); return null; }
            if (enumConstraints && !field.AllowedEnumLiterals.SequenceEqual(EnumLiterals(property.Type), StringComparer.Ordinal))
            { context.ReportDiagnostic(Diagnostic.Create(IncompatibleScalarConstraint, GetLocation(property), collectionId, property.Name)); return null; }
            if (inferredKind == "CanonicalJson" && (field.MaximumCanonicalJsonBytes is not > 0 || field.MaximumJsonDepth is not > 0 || field.MaximumJsonArrayItems is not > 0 || field.MaximumJsonObjectProperties is not > 0 || field.MaximumJsonTotalNodes is not > 0 || field.MaximumJsonTotalStringUtf8Bytes is not > 0 || field.MaximumJsonTotalNameUtf8Bytes is not > 0) || inferredKind == "FrozenArray" && field.MaximumCollectionItems is not > 0)
            { context.ReportDiagnostic(Diagnostic.Create(MissingMandatoryScalarCeiling, GetLocation(property), collectionId, property.Name)); return null; }
            AttributeData subjectReferenceAttribute = FindAttribute(property, SubjectReferenceAttribute);
            if (subjectReferenceAttribute is not null)
            {
                INamedTypeSymbol marker = SubjectReferenceMarker(property.Type);
                INamedTypeSymbol declaredMarker = GetConstructorType(subjectReferenceAttribute, 0);
                AttributeData exported = marker is null ? null : FindAttribute(marker, ExportedSubjectAttribute);
                string contractId = exported is null ? null : GetConstructorString(exported, 0);
                int contractVersion = exported is null ? 0 : (int)GetNamedInt64(exported, "Version", 1);
                int idKind = exported is null ? -1 : (int)GetNamedInt64(exported, "SubjectIdKind", 0);
                int maximumIdBytes = exported is null ? 0 : (int)GetNamedInt64(exported, "MaximumSubjectIdUtf8Bytes", 256);
                int requirement = (int)GetNamedInt64(subjectReferenceAttribute, "Requirement", 0);
                int guarantee = (int)GetNamedInt64(subjectReferenceAttribute, "Guarantee", 0);
                if (marker is null || declaredMarker is null || !SymbolEqualityComparer.Default.Equals(marker, declaredMarker) ||
                    exported is null || !IsValidId(contractId) || contractVersion < 1 || idKind is < 0 or > 2 ||
                    maximumIdBytes is < 1 or > 256 || requirement is < 0 or > 1 || guarantee != 0 ||
                    property.Type.NullableAnnotation == NullableAnnotation.Annotated && property.IsRequired)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidField, GetLocation(property), collectionId, property.Name,
                        "subject references require an exact exported marker, closed contract, and scalar BaseSubjectReference<TSubject> shape"));
                    return null;
                }
                field.SchemaType = "subject-reference";
                field.SchemaFormat = null;
                field.SubjectReference = new SubjectReferenceModel
                {
                    MarkerType = marker.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ContractId = contractId,
                    ContractVersion = contractVersion,
                    SubjectIdKind = idKind,
                    MaximumSubjectIdBytes = maximumIdBytes,
                    Requirement = requirement,
                    Guarantee = guarantee,
                };
            }
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
            if (relationAttribute is not null && field.SubjectReference is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidField, GetLocation(property), collectionId, property.Name,
                    "a subject-reference field cannot also be a relation"));
                return null;
            }
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
        var textIndexes = new List<TextIndexModel>();
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

            long version = GetNamedInt64(indexAttribute, "Version", 1);
            AttributeData[] partAttributes = symbol.GetAttributes().Where(attribute => attribute.AttributeClass?.ToDisplayString() == IndexPartAttribute && GetConstructorString(attribute, 0) == indexId).ToArray();
            if (version < 1 || partAttributes.Length == 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidIndex,
                    indexLocation,
                    collectionId,
                    indexId,
                    version < 1 ? "the version must be positive" : "at least one BaseIndexPart is required"));
                return null;
            }

            var indexParts = new List<IndexPartModel>();
            var indexedProperties = new HashSet<string>(StringComparer.Ordinal);
            foreach (AttributeData partAttribute in partAttributes.OrderBy(static value => GetConstructorInt32(value, 1)))
            {
                int ordinal = GetConstructorInt32(partAttribute, 1);
                string propertyName = GetConstructorString(partAttribute, 2);
                FieldModel field;
                if (ordinal != indexParts.Count || propertyName == null || !propertyFields.TryGetValue(propertyName, out field))
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

                int direction = (int)GetNamedInt64(partAttribute, "Direction", 0), collation = (int)GetNamedInt64(partAttribute, "Collation", 0), nullOrder = (int)GetNamedInt64(partAttribute, "NullOrder", 0);
                if (direction is < 0 or > 1 || collation != 0 || nullOrder is < 0 or > 1)
                { context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, GetLocation(partAttribute, symbol), collectionId, indexId, "a part has invalid direction, collation, or null ordering")); return null; }
                indexParts.Add(new IndexPartModel { Field = field, Direction = direction, Collation = collation, NullOrder = nullOrder });
            }

            var predicateNodes = new List<IndexPredicateModel>();
            foreach (AttributeData predicateAttribute in symbol.GetAttributes().Where(attribute => attribute.AttributeClass?.ToDisplayString() == IndexPredicateAttribute && GetConstructorString(attribute, 0) == indexId))
            {
                string nodeId = GetConstructorString(predicateAttribute, 1); int kind = GetConstructorInt32(predicateAttribute, 2);
                string predicateFieldName = GetNamedString(predicateAttribute, "Field"); string[] children = GetNamedStrings(predicateAttribute, "Children");
                string literal = GetNamedString(predicateAttribute, "Literal");
                FieldModel predicateField = null;
                if (predicateFieldName is not null && !propertyFields.TryGetValue(predicateFieldName, out predicateField))
                { context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, GetLocation(predicateAttribute, symbol), collectionId, indexId, "a predicate references an unknown field")); return null; }
                bool fieldNode = kind is >= 2 and <= 6; bool equalNode = kind == 6; bool booleanNode = kind is 7 or 8; bool notNode = kind == 9;
                if (!IsValidId(nodeId) || kind is < 0 or > 9 || fieldNode != (predicateField is not null) || equalNode != (literal is not null) || (!equalNode && literal is not null) || (booleanNode && children.Length < 2) || (notNode && children.Length != 1) || (!booleanNode && !notNode && children.Length != 0))
                { context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, GetLocation(predicateAttribute, symbol), collectionId, indexId, "a predicate node has incompatible members")); return null; }
                if (equalNode && !ValidPredicateLiteral(predicateField, literal))
                { context.ReportDiagnostic(Diagnostic.Create(IncompatibleIndexLiteral, GetLocation(predicateAttribute, symbol), collectionId, indexId)); return null; }
                predicateNodes.Add(new IndexPredicateModel { Id = nodeId, Kind = kind, Field = predicateField, Children = children, Literal = literal });
            }
            string predicateRoot = "root";
            if (predicateNodes.Count != 0)
            {
                if (predicateNodes.Select(static node => node.Id).Distinct(StringComparer.Ordinal).Count() != predicateNodes.Count)
                { context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, indexLocation, collectionId, indexId, "predicate node identities are duplicated")); return null; }
                var referenced = new HashSet<string>(predicateNodes.SelectMany(static node => node.Children), StringComparer.Ordinal);
                string[] roots = predicateNodes.Where(node => !referenced.Contains(node.Id)).Select(static node => node.Id).ToArray();
                if (roots.Length != 1 || predicateNodes.SelectMany(static node => node.Children).Any(child => predicateNodes.All(node => node.Id != child)))
                { context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, indexLocation, collectionId, indexId, "the predicate must be one closed connected tree")); return null; }
                predicateRoot = roots[0];
                var nodesById = predicateNodes.ToDictionary(static node => node.Id, StringComparer.Ordinal);
                var parentCounts = predicateNodes.SelectMany(static node => node.Children)
                    .GroupBy(static child => child, StringComparer.Ordinal)
                    .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
                if (predicateNodes.Any(node => node.Id != predicateRoot && (!parentCounts.TryGetValue(node.Id, out int count) || count != 1)))
                { context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, indexLocation, collectionId, indexId, "the predicate must be one closed connected tree")); return null; }
                var visiting = new HashSet<string>(StringComparer.Ordinal);
                var visited = new HashSet<string>(StringComparer.Ordinal);
                bool Visit(string nodeId)
                {
                    if (!visiting.Add(nodeId)) return false;
                    if (visited.Contains(nodeId)) { visiting.Remove(nodeId); return true; }
                    foreach (string child in nodesById[nodeId].Children)
                        if (!Visit(child)) return false;
                    visiting.Remove(nodeId);
                    visited.Add(nodeId);
                    return true;
                }
                if (!Visit(predicateRoot) || visited.Count != predicateNodes.Count)
                { context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, indexLocation, collectionId, indexId, "the predicate must be one closed connected tree")); return null; }
            }

            indexes.Add(new IndexModel
            {
                Id = indexId,
                Version = version,
                Unique = GetNamedBoolean(indexAttribute, "Unique", false),
                Required = GetNamedBoolean(indexAttribute, "StoreRequired", true),
                Parts = indexParts,
                PredicateRoot = predicateRoot,
                PredicateNodes = predicateNodes,
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

        foreach (AttributeData textAttribute in symbol.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() == TextIndexAttribute))
        {
            string textIndexId = GetConstructorString(textAttribute, 0);
            Location location = GetLocation(textAttribute, symbol);
            int version = (int)GetNamedInt64(textAttribute, "Version", 1);
            int audience = (int)GetNamedInt64(textAttribute, "Audience", 1);
            if (!IsValidId(textIndexId) || !indexIds.Add(textIndexId) || version <= 0 || audience is < 0 or > 2)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, location, collectionId, textIndexId ?? string.Empty, "the text-index identity, version, or audience is invalid"));
                return null;
            }
            ImmutableArray<TypedConstant> fieldValues = GetNamedArray(textAttribute, "Fields");
            ImmutableArray<TypedConstant> weightValues = GetNamedArray(textAttribute, "Weights");
            if (fieldValues.Length is < 1 or > 8 || weightValues.Length != fieldValues.Length)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, location, collectionId, textIndexId, "text indexes require one through eight fields and one matching weight per field"));
                return null;
            }
            var searchFields = new List<TextIndexFieldModel>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < fieldValues.Length; index++)
            {
                string name = fieldValues[index].Value as string;
                int weight = weightValues[index].Value is int parsed ? parsed : 0;
                if (name is null || !propertyFields.TryGetValue(name, out FieldModel field)
                    || field.TypeName is not ("string" or "global::System.String" or "string?" or "global::System.String?")
                    || field.Confidentiality is 2 or 3 || weight is < 1 or > 16 || !used.Add(name))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, location, collectionId, textIndexId, "search fields must be unique serializer-bound public/internal strings with weights from one through sixteen"));
                    return null;
                }
                searchFields.Add(new TextIndexFieldModel { Field = field, Weight = weight });
            }
            var filterFields = new List<FieldModel>();
            foreach (TypedConstant value in GetNamedArray(textAttribute, "FilterFields"))
            {
                string name = value.Value as string;
                if (name is null || !propertyFields.TryGetValue(name, out FieldModel field) || !used.Add(name)
                    || (field.Operators & 1) == 0 || TextFilterKind(field.TypeName) < 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, location, collectionId, textIndexId, "filter fields must be unique equality-capable string, ID, Boolean, or signed integer fields"));
                    return null;
                }
                filterFields.Add(field);
            }
            if (filterFields.Count > 16)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidIndex, location, collectionId, textIndexId, "at most sixteen text filter fields are permitted"));
                return null;
            }
            filterFields.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
            textIndexes.Add(new TextIndexModel { Id = textIndexId, Version = version, Audience = audience, Fields = searchFields, FilterFields = filterFields });
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
        textIndexes.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));

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
        if (sharedContextResult is null) return null;
        List<SerializerPropertyModel> serializerProperties =
            sharedContextResult.UnionGraph.PropertiesForRoot(symbol).Select(static property => new SerializerPropertyModel
            {
                DeclaringType = property.DeclaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ApplicationName = property.ApplicationName,
                PropertyType = property.PropertyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ExplicitWireName = property.ExplicitWireName,
                Required = property.Required,
                Nullable = property.Nullable,
                Ignored = property.Ignored,
                ExplicitNever = property.ExplicitNever,
                ConverterIdentity = property.ConverterIdentity,
                ConverterType = property.ConverterType,
            }).ToList();

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
            SystemOwnerModuleId = systemOwnerModuleId,
            Fields = fields,
            Indexes = indexes,
            VectorIndexes = vectorIndexes,
            TextIndexes = textIndexes,
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
        foreach (SubjectReferenceModel reference in model.Fields.Select(static field => field.SubjectReference)
            .Where(static value => value is not null).Distinct(SubjectReferenceModelComparer.Instance))
        {
            source.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
            source.Append("    internal static void RegisterHPDBaseSubjectReferenceJsonConverter_")
                .Append(Sanitize(reference.MarkerType)).Append("() => global::HPD.Base.BaseSubjectReferenceJsonConverterFactory.Register<")
                .Append(reference.MarkerType).Append(">((global::HPD.Base.BaseSubjectIdKind)").Append(reference.SubjectIdKind)
                .Append(", ").Append(reference.MaximumSubjectIdBytes).AppendLine(");");
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
        if (model.TextIndexes.Count != 0)
        {
            source.AppendLine("    /// <summary>Provides typed handles for the collection's declared lexical indexes.</summary>");
            source.AppendLine("    public static class TextIndexes");
            source.AppendLine("    {");
            foreach (TextIndexModel index in model.TextIndexes)
            {
                source.Append("        /// <summary>Gets lexical index <c>").Append(index.Id).AppendLine("</c>.</summary>");
                source.Append("        public static global::HPD.Base.BaseTextIndex<").Append(model.FullTypeName).Append("> ")
                    .Append(index.PropertyName).AppendLine(" => new()");
                source.AppendLine("        {");
                source.Append("            Definition = global::System.Linq.Enumerable.Single(")
                    .Append(model.FullTypeName).Append(".Collection.Definition.TextIndexes!, static value => value.Id == ")
                    .Append(Literal(index.Id)).AppendLine("),");
                source.AppendLine("        };");
            }
            source.AppendLine("    }");
            source.AppendLine();
        }
        source.Append("    private static global::HPD.Base.BaseCollection<")
            .Append(model.FullTypeName).AppendLine("> CreateHPDBaseCollection()");
        source.AppendLine("    {");
        source.AppendLine("        var jsonRegistration = global::HPD.Base.BaseSerializerGeneratedContract.RegisterContext(__HPDBaseSerializerFactory.Create);");
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
        if (model.SystemOwnerModuleId != null)
        {
            source.AppendLine("                System = true,");
            source.AppendLine("                Exposed = false,");
            source.Append("                SystemOwnerModuleId = ").Append(Literal(model.SystemOwnerModuleId)).AppendLine(",");
        }
        source.AppendLine("                Source = new global::HPD.Base.SchemaSourceDescriptor");
        source.AppendLine("                {");
        source.AppendLine("                    Id = \"hpd.base.application.generated\",");
        source.AppendLine("                    Kind = global::HPD.Base.SchemaSourceKind.Generated,");
        source.AppendLine("                },");
        RenderFieldDefinitions(source, model);
        RenderIndexes(source, model);
        RenderVectorIndexes(source, model);
        RenderTextIndexes(source, model);
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
                .Append(", ").Append(property.Ignored ? "true" : "false")
                .Append(", ").Append(property.ExplicitNever ? "true" : "false")
                .Append(", ").Append(Literal(property.ConverterIdentity))
                .Append(", ").Append(property.ConverterType is null ? "null" : "typeof(" + property.ConverterType + ")")
                .AppendLine("),");
        }
        source.AppendLine("            });");
        source.AppendLine("    }");
        source.AppendLine("    private static class __HPDBaseSerializerFactory");
        source.AppendLine("    {");
        source.AppendLine("        [global::System.CodeDom.Compiler.GeneratedCode(\"HPD.Base.Generators\", \"44\")]");
        source.Append("        internal static ").Append(model.ContextTypeName).Append(" Create() => new(")
            .Append("global::HPD.Base.BaseSerializerGeneratedContract.CreateOptions(")
            .Append(model.JsonNamingPolicy == null ? "null" : "global::System.Text.Json.JsonNamingPolicy." + model.JsonNamingPolicy).AppendLine("));");
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
        for (int fieldOrdinal = 0; fieldOrdinal < fields.Count; fieldOrdinal++)
        {
            FieldModel field = fields[fieldOrdinal];
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
            source.Append("                        Presence = global::HPD.Base.BaseFieldPresence.").Append(field.Required ? "Required" : "Optional").AppendLine(",");
            source.Append("                        Nullability = global::HPD.Base.BaseFieldNullability.").Append(field.Nullable ? "Nullable" : "NonNullable").AppendLine(",");
            string scalarKind = ScalarKind(field);
            if (scalarKind is not null)
            {
                source.Append("                        ScalarKind = global::HPD.Base.BaseScalarKind.").Append(scalarKind).AppendLine(",");
                source.Append("                        ScalarCodec = ").Append(CodecExpression(field, scalarKind)).AppendLine(",");
                source.AppendLine("                        ScalarConstraints = new global::HPD.Base.BaseScalarConstraintSet");
                source.AppendLine("                        {");
                RenderScalarConstraints(source, field, "                            ");
                source.AppendLine("                        },");
                source.Append("                        ScalarConstraintChecksum = global::HPD.Base.BaseGeneratedSchemaRegistration.ScalarConstraintChecksum(")
                    .Append(Literal(model.CollectionId)).Append(", ").Append(Literal(field.Id)).Append(", global::HPD.Base.BaseFieldPresence.").Append(field.Required ? "Required" : "Optional")
                    .Append(", global::HPD.Base.BaseFieldNullability.").Append(field.Nullable ? "Nullable" : "NonNullable")
                    .Append(", ").Append(CodecExpression(field, scalarKind)).AppendLine(", new global::HPD.Base.BaseScalarConstraintSet")
                    .AppendLine("                        {");
                RenderScalarConstraints(source, field, "                            ");
                source.AppendLine("                        }),");
            }
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
            if (field.SubjectReference is not null)
            {
                source.AppendLine("                        SubjectReference = new global::HPD.Base.BaseSubjectReferenceDefinition");
                source.AppendLine("                        {");
                source.Append("                            ContractId = ").Append(Literal(field.SubjectReference.ContractId)).AppendLine(",");
                source.Append("                            ContractVersion = ").Append(field.SubjectReference.ContractVersion).AppendLine(",");
                source.AppendLine("                            ContractChecksum = \"\",");
                source.Append("                            Requirement = (global::HPD.Base.BaseSubjectReferenceRequirement)").Append(field.SubjectReference.Requirement).AppendLine(",");
                source.Append("                            Guarantee = (global::HPD.Base.BaseSubjectValidationGuarantee)").Append(field.SubjectReference.Guarantee).AppendLine(",");
                source.AppendLine("                        },");
            }
            source.AppendLine("                    },");
        }
        source.AppendLine("                ],");
    }

    private static void RenderScalarConstraints(StringBuilder source, FieldModel field, string indent)
    {
        void Optional(string name, object value) { if (value is not null) source.Append(indent).Append(name).Append(" = ").Append(Convert.ToString(value, CultureInfo.InvariantCulture)).AppendLine(","); }
        if (field.MaximumBytes > 0) Optional("MaximumBinaryBytes", field.MaximumBytes);
        Optional("MinimumUtf8Bytes", field.MinimumUtf8Bytes); Optional("MaximumUtf8Bytes", field.MaximumUtf8Bytes);
        if (field.StringNormalization is not null) source.Append(indent).Append("StringNormalization = (global::HPD.Base.BaseStringNormalizationRequirement)").Append(field.StringNormalization.Value).AppendLine(",");
        Optional("MinimumInt64", field.MinimumInt64); Optional("MaximumInt64", field.MaximumInt64);
        Optional("MinimumInt32", field.MinimumInt32); Optional("MaximumInt32", field.MaximumInt32);
        Optional("MinimumUInt32", field.MinimumUInt32); Optional("MaximumUInt32", field.MaximumUInt32);
        Optional("MinimumUInt64", field.MinimumUInt64); Optional("MaximumUInt64", field.MaximumUInt64);
        if (field.MinimumDecimal is not null) source.Append(indent).Append("MinimumDecimal = global::HPD.Base.BaseGeneratedSchemaRegistration.Decimal(").Append(Literal(field.MinimumDecimal)).AppendLine("),");
        if (field.MaximumDecimal is not null) source.Append(indent).Append("MaximumDecimal = global::HPD.Base.BaseGeneratedSchemaRegistration.Decimal(").Append(Literal(field.MaximumDecimal)).AppendLine("),");
        if (field.AllowedEnumLiterals.Length != 0) source.Append(indent).Append("AllowedEnumLiterals = [").Append(string.Join(", ", field.AllowedEnumLiterals.Select(Literal))).AppendLine("],");
        Optional("MinimumCollectionItems", field.MinimumCollectionItems); Optional("MaximumCollectionItems", field.MaximumCollectionItems);
        Optional("MaximumCanonicalJsonBytes", field.MaximumCanonicalJsonBytes);
        if (field.JsonShape is not null) source.Append(indent).Append("JsonShape = (global::HPD.Base.BaseJsonShape)").Append(field.JsonShape.Value).AppendLine(",");
        Optional("MaximumJsonDepth", field.MaximumJsonDepth); Optional("MaximumJsonArrayItems", field.MaximumJsonArrayItems);
        Optional("MaximumJsonObjectProperties", field.MaximumJsonObjectProperties); Optional("MaximumJsonTotalNodes", field.MaximumJsonTotalNodes);
        Optional("MaximumJsonTotalStringUtf8Bytes", field.MaximumJsonTotalStringUtf8Bytes); Optional("MaximumJsonTotalNameUtf8Bytes", field.MaximumJsonTotalNameUtf8Bytes);
    }

    private static string CodecExpression(FieldModel field, string scalarKind)
    {
        string prefix = "global::HPD.Base.BaseGeneratedSchemaRegistration.ScalarCodec(global::HPD.Base.BaseScalarKind." + scalarKind;
        return scalarKind == "ClosedEnum"
            ? prefix + ", global::HPD.Base.BaseGeneratedSchemaRegistration.EnumQualifier(" + string.Join(", ", field.EnumCodecLiterals.Select(Literal)) + "))"
            : prefix + ")";
    }

    private static string ScalarKind(FieldModel field)
    {
        if (field.TypeName == "global::HPD.Base.BaseCanonicalJson") return "CanonicalJson";
        if (field.SchemaType == "string")
        {
            if (field.SchemaFormat == "date-time") return "UtcDateTime";
            if (field.SchemaFormat == "base64") return "Binary";
            if (field.SchemaFormat == "enum") return "ClosedEnum";
            if (field.SchemaFormat == "uuid") return "Guid";
            return "String";
        }
        if (field.SchemaType == "boolean") return "Boolean";
        if (field.SchemaType == "integer") return field.TypeName switch { "int" or "global::System.Int32" => "Int32", "uint" or "global::System.UInt32" => "UInt32", "ulong" or "global::System.UInt64" => "UInt64", _ => "Int64" };
        if (field.SchemaType == "decimal" || field.SchemaType == "number" && field.SchemaFormat == "decimal") return "Decimal";
        if (field.SchemaType == "array") return "FrozenArray";
        return null;
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
            source.AppendLine("                    new global::HPD.Base.BaseLogicalIndexDefinition");
            source.AppendLine("                    {");
            source.Append("                        Id = global::HPD.Base.BaseLogicalIndexId.Create(").Append(Literal(index.Id)).AppendLine("),");
            source.Append("                        Version = ").Append(index.Version).AppendLine("L,");
            source.Append("                        CollectionId = ")
                .Append(Literal(model.CollectionId)).AppendLine(",");
            source.Append("                        Unique = ")
                .Append(index.Unique ? "true" : "false").AppendLine(",");
            source.Append("                        StoreRequired = ").Append(index.Required || index.Unique ? "true" : "false").AppendLine(",");
            source.AppendLine("                        Parts =");
            source.AppendLine("                        [");
            foreach (IndexPartModel part in index.Parts)
            {
                source.AppendLine("                            new global::HPD.Base.BaseLogicalIndexPart");
                source.AppendLine("                            {");
                source.Append("                                FieldOrdinal = ").Append(model.Fields.IndexOf(part.Field)).AppendLine(",");
                source.Append("                                Direction = (global::HPD.Base.BaseIndexSortDirection)").Append(part.Direction).AppendLine(",");
                source.Append("                                Collation = (global::HPD.Base.BaseIndexCollation)").Append(part.Collation).AppendLine(",");
                source.Append("                                NullOrder = (global::HPD.Base.BaseIndexNullOrder)").Append(part.NullOrder).AppendLine(",");
                source.AppendLine("                            },");
            }
            source.AppendLine("                        ],");
            source.AppendLine("                        MembershipPredicate = new global::HPD.Base.BaseIndexPredicateRegistry");
            source.AppendLine("                        {");
            source.Append("                            Root = global::HPD.Base.BaseIndexPredicateId.Create(").Append(Literal(index.PredicateRoot)).AppendLine("),");
            source.AppendLine("                            Nodes =");
            source.AppendLine("                            [");
            if (index.PredicateNodes.Count == 0)
                source.AppendLine("                                new global::HPD.Base.BaseIndexPredicateNode { Id = global::HPD.Base.BaseIndexPredicateId.Create(\"root\"), Kind = global::HPD.Base.BaseIndexPredicateNodeKind.True },");
            foreach (IndexPredicateModel node in index.PredicateNodes)
            {
                source.AppendLine("                                new global::HPD.Base.BaseIndexPredicateNode"); source.AppendLine("                                {");
                source.Append("                                    Id = global::HPD.Base.BaseIndexPredicateId.Create(").Append(Literal(node.Id)).AppendLine("),");
                source.Append("                                    Kind = (global::HPD.Base.BaseIndexPredicateNodeKind)").Append(node.Kind).AppendLine(",");
                if (node.Field is not null) source.Append("                                    FieldOrdinal = ").Append(model.Fields.IndexOf(node.Field)).AppendLine(",");
                if (node.Literal is not null)
                {
                    string scalarKind = ScalarKind(node.Field);
                    source.Append("                                    Literal = global::HPD.Base.BaseGeneratedSchemaRegistration.ScalarLiteral(global::HPD.Base.BaseScalarKind.").Append(scalarKind).Append(", ")
                        .Append(CodecExpression(node.Field, scalarKind)).Append(", ").Append(Literal(node.Literal)).AppendLine("),");
                }
                source.Append("                                    Children = [").Append(string.Join(", ", node.Children.Select(static child => "global::HPD.Base.BaseIndexPredicateId.Create(" + Literal(child) + ")"))).AppendLine("],");
                source.AppendLine("                                },");
            }
            source.AppendLine("                            ],");
            source.AppendLine("                            Checksum = default,");
            source.AppendLine("                        },");
            source.AppendLine("                        Checksum = default,");
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

    private static void RenderTextIndexes(StringBuilder source, CollectionModel model)
    {
        if (model.TextIndexes.Count == 0)
        {
            source.AppendLine("                TextIndexes = null,");
            return;
        }
        source.AppendLine("                TextIndexes =");
        source.AppendLine("                [");
        foreach (TextIndexModel index in model.TextIndexes)
        {
            source.AppendLine("                    new global::HPD.Base.BaseTextIndexDefinition");
            source.AppendLine("                    {");
            source.Append("                        Id = ").Append(Literal(index.Id)).AppendLine(",");
            source.Append("                        Version = ").Append(index.Version).AppendLine(",");
            source.Append("                        CollectionId = ").Append(Literal(model.CollectionId)).AppendLine(",");
            source.Append("                        Audience = (global::HPD.Base.HPDBaseEndpointAudience)").Append(index.Audience).AppendLine(",");
            source.AppendLine("                        Fields =");
            source.AppendLine("                        [");
            foreach (TextIndexFieldModel field in index.Fields)
            {
                source.AppendLine("                            new global::HPD.Base.BaseTextIndexFieldDefinition");
                source.AppendLine("                            {");
                source.Append("                                StableFieldId = ").Append(Literal(field.Field.Id)).AppendLine(",");
                source.Append("                                ApplicationName = ").Append(Literal(field.Field.ApplicationName)).AppendLine(",");
                source.Append("                                WireName = __wire_").Append(EscapeIdentifier(field.Field.PropertyName)).AppendLine(",");
                source.Append("                                Weight = ").Append(field.Weight).AppendLine(",");
                source.Append("                                Confidentiality = (global::HPD.Base.BaseFieldConfidentiality)").Append(field.Field.Confidentiality).AppendLine(",");
                source.Append("                                StaticInfluenceAudiences = global::System.Collections.Immutable.ImmutableArray.Create((global::HPD.Base.HPDBaseEndpointAudience)")
                    .Append(index.Audience).AppendLine("),");
                source.Append("                                RequiresDynamicInfluenceConstraint = ").Append(field.Field.Confidentiality == 1 ? "true" : "false").AppendLine(",");
                source.AppendLine("                            },");
            }
            source.AppendLine("                        ],");
            source.AppendLine("                        FilterFields =");
            source.AppendLine("                        [");
            foreach (FieldModel field in index.FilterFields)
            {
                source.AppendLine("                            new global::HPD.Base.BaseTextIndexFilterFieldDefinition");
                source.AppendLine("                            {");
                source.Append("                                StableFieldId = ").Append(Literal(field.Id)).AppendLine(",");
                source.Append("                                ApplicationName = ").Append(Literal(field.ApplicationName)).AppendLine(",");
                source.Append("                                WireName = __wire_").Append(EscapeIdentifier(field.PropertyName)).AppendLine(",");
                source.Append("                                ValueKind = (global::HPD.Base.BaseTextFilterValueKind)").Append(TextFilterKind(field.TypeName)).AppendLine(",");
                source.AppendLine("                            },");
            }
            source.AppendLine("                        ],");
            source.AppendLine("                        AnalyzerContractId = global::HPD.Base.BaseTextAnalyzers.UnicodeCaseFoldedV1,");
            source.AppendLine("                        AnalyzerReceipt = global::HPD.Base.BaseTextContractReceipts.AnalyzerReceipt,");
            source.AppendLine("                        ScoringContractId = global::HPD.Base.BaseTextScoring.ContractId,");
            source.AppendLine("                        ScoringReceipt = global::HPD.Base.BaseTextContractReceipts.ScoringReceipt,");
            source.AppendLine("                        Limits = global::HPD.Base.BaseTextPlatform.DefaultLimits,");
            source.AppendLine("                        SerializerGraphChecksum = global::System.Collections.Immutable.ImmutableArray.Create(new byte[32]),");
            source.AppendLine("                        DefinitionChecksum = global::System.Collections.Immutable.ImmutableArray<byte>.Empty,");
            source.AppendLine("                    },");
        }
        source.AppendLine("                ],");
    }

    private static int TextFilterKind(string typeName)
    {
        string value = typeName.EndsWith("?", StringComparison.Ordinal) ? typeName.Substring(0, typeName.Length - 1) : typeName;
        if (value is "string" or "global::System.String") return 0;
        if (value is "bool" or "global::System.Boolean") return 1;
        if (value is "sbyte" or "global::System.SByte" or "short" or "global::System.Int16" or "int" or "global::System.Int32" or "long" or "global::System.Int64") return 2;
        if (value.StartsWith("global::HPD.Base.BaseRecordId<", StringComparison.Ordinal) || value is "global::HPD.Base.BaseRecordId") return 3;
        return -1;
    }

    private static string TextHandleName(string id)
    {
        string[] parts = id.Split('.');
        string last = parts[parts.Length - 1];
        string tail = parts.Length > 1 && last.Length > 1 && last[0] is 'v' or 'V' && last.Skip(1).All(char.IsDigit)
            ? parts[parts.Length - 2]
            : last;
        return VectorHandleName(tail);
    }

    private static bool IsSupported(INamedTypeSymbol symbol) =>
        symbol.TypeKind == TypeKind.Class &&
        symbol.ContainingType == null &&
        symbol.TypeParameters.Length == 0;

    private static bool IsAlwaysIgnored(IPropertySymbol property)
    {
        AttributeData ignore = FindAttribute(property, JsonIgnoreAttribute);
        return ignore is not null && (ignore.NamedArguments.Length == 0 || GetNamedInt64(ignore, "Condition", 1) == 1);
    }

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
    private static int GetConstructorInt32(AttributeData attribute, int index) =>
        attribute != null && attribute.ConstructorArguments.Length > index && attribute.ConstructorArguments[index].Value is object value
            ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : -1;

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

    private static bool HasNamed(AttributeData attribute, string name) => attribute.NamedArguments.Any(argument => argument.Key == name);
    private static bool ValidPredicateLiteral(FieldModel field, string literal)
    {
        string kind = ScalarKind(field);
        if (kind is "CanonicalJson" or "FrozenArray") return false;
        if (kind is "String" or "Binary" or "Guid" or "UtcDateTime" or "ClosedEnum")
        {
            if (!TryCanonicalJsonString(literal, out string value)) return false;
            if (kind == "Guid") return Guid.TryParseExact(value, "D", out Guid guid) && value == guid.ToString("D");
            if (kind == "UtcDateTime") return DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset instant) && instant.Offset == TimeSpan.Zero;
            if (kind == "ClosedEnum") return field.EnumCodecLiterals.Contains(value, StringComparer.Ordinal);
            if (kind == "Binary") { try { return Convert.ToBase64String(Convert.FromBase64String(value)) == value; } catch (FormatException) { return false; } }
            return true;
        }
        if (kind == "Boolean") return literal is "true" or "false";
        if (kind == "Int32") return int.TryParse(literal, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int signed32) && signed32.ToString(CultureInfo.InvariantCulture) == literal;
        if (kind == "Int64") return long.TryParse(literal, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long signed64) && signed64.ToString(CultureInfo.InvariantCulture) == literal;
        if (kind == "UInt32") return uint.TryParse(literal, NumberStyles.None, CultureInfo.InvariantCulture, out uint unsigned32) && unsigned32.ToString(CultureInfo.InvariantCulture) == literal;
        if (kind == "UInt64") return ulong.TryParse(literal, NumberStyles.None, CultureInfo.InvariantCulture, out ulong unsigned64) && unsigned64.ToString(CultureInfo.InvariantCulture) == literal;
        return kind == "Decimal" && ValidCanonicalDecimal(literal);
    }

    private static bool TryCanonicalJsonString(string token, out string value)
    {
        value = null;
        if (token == null || token.Length < 2 || token[0] != '"' || token[token.Length - 1] != '"') return false;
        var result = new StringBuilder(token.Length - 2);
        for (int index = 1; index < token.Length - 1; index++)
        {
            char current = token[index];
            if (current == '"' || current < ' ') return false;
            if (current != '\\')
            {
                if (char.IsHighSurrogate(current)) { if (++index >= token.Length - 1 || !char.IsLowSurrogate(token[index])) return false; result.Append(current).Append(token[index]); }
                else { if (char.IsLowSurrogate(current)) return false; result.Append(current); }
                continue;
            }
            if (++index >= token.Length - 1) return false;
            char escape = token[index];
            if (escape == '"' || escape == '\\') result.Append(escape);
            else if (escape == 'b') result.Append('\b'); else if (escape == 'f') result.Append('\f'); else if (escape == 'n') result.Append('\n'); else if (escape == 'r') result.Append('\r'); else if (escape == 't') result.Append('\t');
            else if (escape == 'u' && index + 4 < token.Length - 1)
            {
                string hex = token.Substring(index + 1, 4);
                if (hex.Length != 4 || hex[0] != '0' || hex[1] != '0' || !int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int scalar) || scalar > 0x1f || scalar is 8 or 9 or 10 or 12 or 13 || hex.Any(static character => character is >= 'A' and <= 'F')) return false;
                result.Append((char)scalar); index += 4;
            }
            else return false;
        }
        value = result.ToString(); return true;
    }

    private static bool ValidCanonicalDecimal(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('e') >= 0 || text.IndexOf('E') >= 0 || text[0] == '+') return false;
        bool negative = text[0] == '-'; int start = negative ? 1 : 0, dot = text.IndexOf('.', start);
        string whole = dot < 0 ? text.Substring(start) : text.Substring(start, dot - start);
        string fraction = dot < 0 ? string.Empty : text.Substring(dot + 1);
        if (whole.Length == 0 || whole.Length > 1 && whole[0] == '0' || fraction.Length > 28 || dot >= 0 && (fraction.Length == 0 || fraction[fraction.Length - 1] == '0') || whole.Any(static value => value < '0' || value > '9') || fraction.Any(static value => value < '0' || value > '9')) return false;
        string digits = (whole + fraction).TrimStart('0');
        if (digits.Length == 0) return !negative && text == "0";
        string limit = negative ? "170141183460469231731687303715884105728" : "170141183460469231731687303715884105727";
        return digits.Length < limit.Length || digits.Length == limit.Length && string.CompareOrdinal(digits, limit) <= 0;
    }

    private static int CompareCanonicalDecimal(string left, string right)
    {
        bool leftNegative = left[0] == '-', rightNegative = right[0] == '-';
        if (leftNegative != rightNegative) return leftNegative ? -1 : 1;
        string leftMagnitude = left.TrimStart('-'), rightMagnitude = right.TrimStart('-');
        int leftDot = leftMagnitude.IndexOf('.'), rightDot = rightMagnitude.IndexOf('.');
        int leftWhole = leftDot < 0 ? leftMagnitude.Length : leftDot, rightWhole = rightDot < 0 ? rightMagnitude.Length : rightDot;
        int comparison = leftWhole.CompareTo(rightWhole);
        if (comparison == 0)
        {
            string leftDigits = leftMagnitude.Replace(".", string.Empty), rightDigits = rightMagnitude.Replace(".", string.Empty);
            int length = Math.Max(leftDigits.Length, rightDigits.Length);
            comparison = string.CompareOrdinal(leftDigits.PadRight(length, '0'), rightDigits.PadRight(length, '0'));
        }
        return leftNegative ? -comparison : comparison;
    }
    private static string[] EnumLiterals(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) type = nullable.TypeArguments[0];
        return type.GetMembers().OfType<IFieldSymbol>().Where(static field => field.HasConstantValue).Select(static field => field.Name).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
    }
    private static int? NamedInt32(AttributeData attribute, string name) => HasNamed(attribute, name) ? checked((int)GetNamedInt64(attribute, name, 0)) : (int?)null;
    private static uint? NamedUInt32(AttributeData attribute, string name) => HasNamed(attribute, name) ? Convert.ToUInt32(attribute.NamedArguments.Single(argument => argument.Key == name).Value.Value, CultureInfo.InvariantCulture) : (uint?)null;
    private static ulong? NamedUInt64(AttributeData attribute, string name) => HasNamed(attribute, name) ? Convert.ToUInt64(attribute.NamedArguments.Single(argument => argument.Key == name).Value.Value, CultureInfo.InvariantCulture) : (ulong?)null;
    private static bool AnyNegative(params int?[] values) => values.Any(static value => value < 0);
    private static bool InvalidRange<T>(T? minimum, T? maximum) where T : struct, IComparable<T> => minimum is { } min && maximum is { } max && min.CompareTo(max) > 0;

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

    private static string[] GetNamedStrings(AttributeData attribute, string name)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            if (argument.Key == name && argument.Value.Kind == TypedConstantKind.Array)
                return argument.Value.Values.Select(static value => value.Value as string).Where(static value => value is not null).ToArray();
        return Array.Empty<string>();
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

    private static INamedTypeSymbol SubjectReferenceMarker(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            type = nullable.TypeArguments[0];
        return type is INamedTypeSymbol named && named.IsGenericType &&
            named.ConstructedFrom.ToDisplayString() == "HPD.Base.BaseSubjectReference<TSubject>"
            ? named.TypeArguments[0] as INamedTypeSymbol
            : null;
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
        if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::HPD.Base.BaseCanonicalJson") return "object";
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
        if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            type = nullable.TypeArguments[0];
        }
        if (type.TypeKind == TypeKind.Enum) return "enum";
        string name = type.ToDisplayString();
        if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::HPD.Base.BaseBinary") return "base64";
        if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::HPD.Base.BaseCanonicalJson") return "base-json-v1";
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
        /// <summary>Provides the owning installed module for a generated system collection.</summary>
        public string SystemOwnerModuleId;
        /// <summary>Provides the fields value.</summary>
        public List<FieldModel> Fields;
        /// <summary>Provides the indexes value.</summary>
        public List<IndexModel> Indexes;
        /// <summary>Provides the vector indexes value.</summary>
        public List<VectorIndexModel> VectorIndexes;
        /// <summary>Provides the text indexes value.</summary>
        public List<TextIndexModel> TextIndexes;
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
        public bool Ignored;
        public bool ExplicitNever;
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
        public int? MinimumUtf8Bytes;
        public int? MaximumUtf8Bytes;
        public int? StringNormalization;
        public long? MinimumInt64;
        public long? MaximumInt64;
        public int? MinimumInt32;
        public int? MaximumInt32;
        public uint? MinimumUInt32;
        public uint? MaximumUInt32;
        public ulong? MinimumUInt64;
        public ulong? MaximumUInt64;
        public string MinimumDecimal;
        public string MaximumDecimal;
        public string[] AllowedEnumLiterals = Array.Empty<string>();
        public string[] EnumCodecLiterals = Array.Empty<string>();
        public int? MinimumCollectionItems;
        public int? MaximumCollectionItems;
        public int? MaximumCanonicalJsonBytes;
        public int? JsonShape;
        public int? MaximumJsonDepth;
        public int? MaximumJsonArrayItems;
        public int? MaximumJsonObjectProperties;
        public int? MaximumJsonTotalNodes;
        public int? MaximumJsonTotalStringUtf8Bytes;
        public int? MaximumJsonTotalNameUtf8Bytes;
        /// <summary>Provides the relation value.</summary>
        public RelationModel Relation;
        public SubjectReferenceModel SubjectReference;
    }

    private sealed class SubjectReferenceModel
    {
        public string MarkerType;
        public string ContractId;
        public int ContractVersion;
        public int SubjectIdKind;
        public int MaximumSubjectIdBytes;
        public int Requirement;
        public int Guarantee;
    }

    private sealed class SubjectReferenceModelComparer : IEqualityComparer<SubjectReferenceModel>
    {
        internal static SubjectReferenceModelComparer Instance { get; } = new();
        public bool Equals(SubjectReferenceModel x, SubjectReferenceModel y) =>
            x is not null && y is not null && string.Equals(x.MarkerType, y.MarkerType, StringComparison.Ordinal) &&
            x.SubjectIdKind == y.SubjectIdKind && x.MaximumSubjectIdBytes == y.MaximumSubjectIdBytes;
        public int GetHashCode(SubjectReferenceModel value) =>
            StringComparer.Ordinal.GetHashCode(value.MarkerType) ^ value.SubjectIdKind ^ value.MaximumSubjectIdBytes;
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
        public long Version;
        /// <summary>Provides the unique value.</summary>
        public bool Unique;
        /// <summary>Provides the required value.</summary>
        public bool Required;
        /// <summary>Provides the fields value.</summary>
        public List<IndexPartModel> Parts;
        public string PredicateRoot;
        public List<IndexPredicateModel> PredicateNodes;
    }

    private sealed class IndexPredicateModel
    {
        public string Id;
        public int Kind;
        public FieldModel Field;
        public string[] Children;
        public string Literal;
    }

    private sealed class IndexPartModel
    {
        public FieldModel Field;
        public int Direction;
        public int Collation;
        public int NullOrder;
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

    private sealed class TextIndexModel
    {
        public string Id;
        public int Version;
        public int Audience;
        public List<TextIndexFieldModel> Fields;
        public List<FieldModel> FilterFields;
        public string PropertyName => TextHandleName(Id);
    }

    private sealed class TextIndexFieldModel
    {
        public FieldModel Field;
        public int Weight;
    }
}
