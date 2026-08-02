using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace HPD.Base.Generators;

/// <summary>Represents a base read generator.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class BaseReadGenerator : IIncrementalGenerator
{
    private const string ReadAttribute = "HPD.Base.BaseReadAttribute";
    private const string ParameterAttribute = "HPD.Base.BaseReadParameterAttribute";
    private const string FieldAttribute = "HPD.Base.BaseReadFieldAttribute";
    private const string JsonSerializableAttribute = "System.Text.Json.Serialization.JsonSerializableAttribute";

    private static readonly DiagnosticDescriptor InvalidRead = new(
        "HPDBASE020", "Invalid BASE registered read", "Registered read '{0}' is invalid: {1}",
        "HPD.Base.Generation", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor MissingReadIdentity = new(
        "HPDBASE021", "Missing stable BASE read member identity",
        "Registered read '{0}' member '{1}' must declare {2}",
        "HPD.Base.Generation", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor DuplicateReadIdentity = new(
        "HPDBASE022", "Duplicate stable BASE read member identity",
        "Registered read '{0}' declares stable identifier '{1}' more than once in {2}",
        "HPD.Base.Generation", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor UnsupportedReadType = new(
        "HPDBASE023", "Unsupported BASE registered read member type",
        "Registered read '{0}' member '{1}' uses unsupported type '{2}'",
        "HPD.Base.Generation", DiagnosticSeverity.Error, true);

    /// <summary>Executes the initialize operation.</summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider.ForAttributeWithMetadataName(
            ReadAttribute,
            static (node, _) => node is TypeDeclarationSyntax,
            static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol);
        context.RegisterSourceOutput(candidates.Collect(), Generate);
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<INamedTypeSymbol> candidates)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (INamedTypeSymbol symbol in candidates
            .Distinct(SymbolEqualityComparer.Default)
            .OrderBy(static symbol => symbol.ToDisplayString(), StringComparer.Ordinal))
        {
            AttributeData attribute = Find(symbol, ReadAttribute)!;
            string id = ConstructorString(attribute, 0);
            INamedTypeSymbol jsonContext = ConstructorType(attribute, 1);
            if (!ValidId(id) || !ids.Add(id) || !IsTopLevelPartialRecord(symbol))
            {
                Report(context, symbol, id ?? symbol.Name,
                    "the id must be unique and valid and the declaration must be a top-level partial record class");
                continue;
            }

            INamedTypeSymbol row = symbol.GetTypeMembers("Row").SingleOrDefault();
            if (row == null || !IsNestedPartialRecord(row) ||
                !HasJsonRegistration(jsonContext, symbol) || !HasJsonRegistration(jsonContext, row))
            {
                Report(context, symbol, id,
                    "a public nested partial Row record and source-generated JSON registrations for request and Row are required");
                continue;
            }

            List<MemberModel> parameters = Members(context, id, symbol, ParameterAttribute, "[BaseReadParameter(\"stable-id\")]", out bool parameterError);
            List<MemberModel> fields = Members(context, id, row, FieldAttribute, "[BaseReadField(\"stable-id\")]", out bool fieldError);
            if (parameterError || fieldError || fields.Count == 0 || !HasConfigure(symbol, row))
            {
                if (!parameterError && !fieldError && (fields.Count == 0 || !HasConfigure(symbol, row)))
                    Report(context, symbol, id, "at least one Row field and the exact public static Configure method are required");
                continue;
            }

            var model = new ReadModel
            {
                Namespace = symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
                TypeName = symbol.Name,
                FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                RowFullTypeName = row.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                JsonContext = jsonContext.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Id = id,
                Parameters = parameters,
                Fields = fields,
            };
            context.AddSource(Sanitize(model.FullTypeName) + ".HPDBaseRead.g.cs", SourceText.From(Render(model), Encoding.UTF8));
        }
    }

    private static List<MemberModel> Members(
        SourceProductionContext context,
        string readId,
        INamedTypeSymbol owner,
        string attributeName,
        string requiredAttribute,
        out bool failed)
    {
        failed = false;
        var result = new List<MemberModel>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (IPropertySymbol property in owner.GetMembers().OfType<IPropertySymbol>()
            .Where(static property => !property.IsStatic && !property.IsIndexer && property.DeclaredAccessibility == Accessibility.Public && property.GetMethod != null && property.SetMethod != null)
            .OrderBy(static property => property.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue))
        {
            AttributeData attribute = Find(property, attributeName);
            if (attribute == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(MissingReadIdentity, Location(property), readId, property.Name, requiredAttribute));
                failed = true;
                continue;
            }

            string id = ConstructorString(attribute, 0);
            if (!ValidId(id) || !ids.Add(id))
            {
                context.ReportDiagnostic(Diagnostic.Create(DuplicateReadIdentity, Location(property), readId, id ?? string.Empty, owner.Name));
                failed = true;
                continue;
            }

            bool parameterMember = attributeName == ParameterAttribute;
            if (!SupportedMemberType(property.Type, parameterMember))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedReadType, Location(property), readId, property.Name,
                    property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                failed = true;
                continue;
            }

            ITypeSymbol valueType = property.Type is IArrayTypeSymbol array ? array.ElementType : property.Type;
            ITypeSymbol unwrapped = UnwrapNullable(valueType);

            result.Add(new MemberModel
            {
                Name = property.Name,
                Id = id,
                Type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                TypedRecordIdTarget = TypedRecordIdTarget(unwrapped),
                IsArray = property.Type is IArrayTypeSymbol,
                IsNullable = !SymbolEqualityComparer.Default.Equals(valueType, unwrapped),
                ValueType = unwrapped.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Kind = ValueKind(property.Type is IArrayTypeSymbol ? unwrapped : UnwrapNullable(property.Type)),
                ContainerNullable = property.NullableAnnotation == NullableAnnotation.Annotated ||
                    property.Type is INamedTypeSymbol nullable && nullable.IsGenericType && nullable.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T,
            });
        }
        return result;
    }

    private static bool HasConfigure(INamedTypeSymbol type, INamedTypeSymbol row) =>
        type.GetMembers("Configure").OfType<IMethodSymbol>().Any(method =>
            method.IsStatic && method.DeclaredAccessibility == Accessibility.Public &&
            method.ReturnsVoid && method.TypeParameters.Length == 0 && method.Parameters.Length == 1 &&
            method.Parameters[0].Type is INamedTypeSymbol parameter && parameter.IsGenericType &&
            parameter.ConstructedFrom.ToDisplayString() == "HPD.Base.BaseReadDefinitionBuilder<TParameters, TRow>" &&
            SymbolEqualityComparer.Default.Equals(parameter.TypeArguments[0], type) &&
            SymbolEqualityComparer.Default.Equals(parameter.TypeArguments[1], row));

    private static string Render(ReadModel model)
    {
        var source = new StringBuilder("// <auto-generated />\n#nullable enable\n");
        if (model.Namespace != null) source.Append("namespace ").Append(model.Namespace).AppendLine(";\n");
        source.Append("partial record class ").Append(model.TypeName).AppendLine("\n{");
        foreach (string target in model.Parameters.Concat(model.Fields).Select(static member => member.TypedRecordIdTarget)
            .Where(static target => target != null).Distinct(StringComparer.Ordinal))
        {
            source.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
            source.Append("    internal static void RegisterHPDBaseReadRecordId_").Append(Sanitize(target!))
                .Append("() => global::HPD.Base.BaseRecordIdJsonConverterFactory.Register<").Append(target).AppendLine(">();\n");
        }
        source.AppendLine("    /// <summary>Provides typed handles for the registered read's bounded parameters.</summary>");
        source.AppendLine("    public static class Parameters\n    {");
        foreach (MemberModel member in model.Parameters)
        {
            source.Append("        /// <summary>Gets the typed parameter handle for stable parameter <c>").Append(member.Id).AppendLine("</c>.</summary>");
            source.Append("        public static global::HPD.Base.BaseReadParameter<").Append(model.FullTypeName).Append(", ").Append(member.Type).Append("> ")
                .Append(Escape(member.Name)).Append(" { get; } = global::HPD.Base.BaseReadGeneratedContract.Parameter<")
                .Append(model.FullTypeName).Append(", ").Append(member.Type).Append(">(").Append(Literal(member.Id)).AppendLine(");");
        }
        source.AppendLine("    }\n");
        source.AppendLine("    public sealed partial record class Row\n    {");
        source.AppendLine("        /// <summary>Provides typed handles for complete replacement-row fields.</summary>");
        source.AppendLine("        public static class Fields\n        {");
        foreach (MemberModel member in model.Fields)
        {
            source.Append("            /// <summary>Gets the typed row-field handle for stable field <c>").Append(member.Id).AppendLine("</c>.</summary>");
            source.Append("            public static global::HPD.Base.BaseReadField<").Append(model.RowFullTypeName).Append(", ").Append(member.Type).Append("> ")
                .Append(Escape(member.Name)).Append(" { get; } = global::HPD.Base.BaseReadGeneratedContract.Field<")
                .Append(model.RowFullTypeName).Append(", ").Append(member.Type).Append(">(").Append(Literal(member.Id)).AppendLine(");");
        }
        source.AppendLine("        }\n    }\n");
        source.Append("    private sealed class HPDBaseParameterCodec : global::HPD.Base.IBaseReadParameterCodec<").Append(model.FullTypeName).AppendLine(">\n    {");
        source.Append("        public global::HPD.Base.BaseRelationalParameterValue[] Encode(").Append(model.FullTypeName).AppendLine(" parameters) =>\n        [");
        foreach (MemberModel member in model.Parameters)
            source.Append("            new global::HPD.Base.BaseRelationalParameterValue { ParameterId = ").Append(Literal(member.Id))
                .Append(", Value = ").Append(Encode(member, "parameters." + Escape(member.Name))).AppendLine(" },");
        source.AppendLine("        ];\n    }\n");
        source.Append("    private sealed class HPDBaseRowCodec : global::HPD.Base.IBaseReadRowCodec<").Append(model.RowFullTypeName).AppendLine(">\n    {");
        source.Append("        public ").Append(model.RowFullTypeName).Append(" Decode(global::HPD.Base.BaseRelationalRow row) => new ").Append(model.RowFullTypeName).AppendLine("\n        {");
        foreach (MemberModel member in model.Fields)
            source.Append("            ").Append(Escape(member.Name)).Append(" = ").Append(Decode(member))
                .AppendLine(",");
        source.AppendLine("        };\n    }\n");
        source.AppendLine("    /// <summary>Gets the closed, validated registered-read definition used during host registration.</summary>");
        source.Append("    public static global::HPD.Base.BaseReadDefinition<").Append(model.FullTypeName).Append(", ").Append(model.RowFullTypeName)
            .AppendLine("> Definition { get; } = CreateHPDBaseReadDefinition();");
        source.AppendLine("    /// <summary>Gets the typed application handle for bounded snapshot and live execution.</summary>");
        source.Append("    public static global::HPD.Base.BaseReadHandle<").Append(model.FullTypeName).Append(", ").Append(model.RowFullTypeName)
            .AppendLine("> Handle => Definition.Handle;\n");
        source.Append("    private static global::HPD.Base.BaseReadDefinition<").Append(model.FullTypeName).Append(", ").Append(model.RowFullTypeName)
            .AppendLine("> CreateHPDBaseReadDefinition()\n    {");
        source.Append("        var parameterJson = ").Append(model.JsonContext).Append(".Default.GetTypeInfo(typeof(").Append(model.FullTypeName)
            .Append(")) as global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<").Append(model.FullTypeName).AppendLine("> ?? throw new global::System.InvalidOperationException(\"Missing generated read parameter JSON metadata.\");");
        source.Append("        var rowJson = ").Append(model.JsonContext).Append(".Default.GetTypeInfo(typeof(").Append(model.RowFullTypeName)
            .Append(")) as global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<").Append(model.RowFullTypeName).AppendLine("> ?? throw new global::System.InvalidOperationException(\"Missing generated read row JSON metadata.\");");
        source.Append("        return global::HPD.Base.BaseReadGeneratedContract.Create(").Append(Literal(model.Id)).AppendLine(", parameterJson, rowJson,");
        source.AppendLine("            new global::HPD.Base.BaseRelationalReadParameter[]");
        source.AppendLine("            {");
        foreach (MemberModel member in model.Parameters)
        {
            source.Append("                new global::HPD.Base.BaseRelationalReadParameter { Id = ").Append(Literal(member.Id))
                .Append(", Kind = global::HPD.Base.QueryValueKind.").Append(member.IsArray ? "Array" : member.Kind);
            if (member.IsArray) source.Append(", ElementKind = global::HPD.Base.QueryValueKind.").Append(member.Kind).Append(", MaxItems = 256");
            if (member.Kind is "String" or "Id") source.Append(", MaxLength = 4096");
            source.Append(", Nullable = ").Append(member.ContainerNullable ? "true" : "false").AppendLine(" },");
        }
        source.AppendLine("            }, new HPDBaseParameterCodec(), new HPDBaseRowCodec(), Configure);\n    }");
        source.AppendLine("}");
        return source.ToString();
    }

    private static bool IsTopLevelPartialRecord(INamedTypeSymbol symbol) =>
        symbol.ContainingType == null && symbol.TypeKind == TypeKind.Class && symbol.IsRecord &&
        symbol.TypeParameters.Length == 0 && Partial(symbol);
    private static bool IsNestedPartialRecord(INamedTypeSymbol symbol) =>
        symbol.TypeKind == TypeKind.Class && symbol.IsRecord && symbol.TypeParameters.Length == 0 &&
        symbol.DeclaredAccessibility == Accessibility.Public && Partial(symbol);
    private static bool Partial(INamedTypeSymbol symbol) => symbol.DeclaringSyntaxReferences.Any(reference =>
        reference.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
    private static bool HasJsonRegistration(INamedTypeSymbol context, INamedTypeSymbol target) => context != null && context.GetAttributes().Any(attribute =>
        attribute.AttributeClass?.ToDisplayString() == JsonSerializableAttribute &&
        SymbolEqualityComparer.Default.Equals(ConstructorType(attribute, 0), target));
    private static AttributeData Find(ISymbol symbol, string name) => symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == name);
    private static string ConstructorString(AttributeData attribute, int index) => attribute?.ConstructorArguments.Length > index ? attribute.ConstructorArguments[index].Value as string : null;
    private static INamedTypeSymbol ConstructorType(AttributeData attribute, int index) => attribute?.ConstructorArguments.Length > index ? attribute.ConstructorArguments[index].Value as INamedTypeSymbol : null;
    private static Location Location(ISymbol symbol) => symbol.Locations.FirstOrDefault(static location => location.IsInSource) ?? Microsoft.CodeAnalysis.Location.None;
    private static void Report(SourceProductionContext context, ISymbol symbol, string id, string reason) => context.ReportDiagnostic(Diagnostic.Create(InvalidRead, Location(symbol), id, reason));
    private static bool ValidId(string value) => !string.IsNullOrEmpty(value) && value.Length <= 128 && IsAscii(value[0]) && value.All(static character => IsAscii(character) || character is '.' or '-' or '_');
    private static bool IsAscii(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
    private static string TypedRecordIdTarget(ITypeSymbol type) => type is INamedTypeSymbol named && named.IsGenericType && named.ConstructedFrom.ToDisplayString() == "HPD.Base.BaseRecordId<TRecord>" ? named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : null;
    private static string Encode(MemberModel member, string expression)
    {
        if (member.IsArray)
        {
            string item = member.TypedRecordIdTarget == null
                ? "global::HPD.Base.BaseReadGeneratedContract.Value(item)"
                : member.IsNullable
                    ? "item is { } value ? global::HPD.Base.BaseReadGeneratedContract.Value(value.Value) : global::HPD.Base.BaseReadGeneratedContract.Value<global::HPD.Base.RecordId?>(null)"
                    : "global::HPD.Base.BaseReadGeneratedContract.Value(item.Value)";
            return expression + " is null ? new global::HPD.Base.QueryValue { Kind = global::HPD.Base.QueryValueKind.Null } : " +
                "new global::HPD.Base.QueryValue { Kind = global::HPD.Base.QueryValueKind.Array, Array = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(" +
                expression + ", static item => " + item + ")) }";
        }
        if (member.TypedRecordIdTarget != null && member.IsNullable)
            return expression + " is { } value ? global::HPD.Base.BaseReadGeneratedContract.Value(value.Value) : global::HPD.Base.BaseReadGeneratedContract.Value<global::HPD.Base.RecordId?>(null)";
        return member.TypedRecordIdTarget == null
            ? "global::HPD.Base.BaseReadGeneratedContract.Value(" + expression + ")"
            : "global::HPD.Base.BaseReadGeneratedContract.Value(" + expression + ".Value)";
    }

    private static string Decode(MemberModel member)
    {
        if (member.TypedRecordIdTarget != null)
        {
            string scalar = "new global::HPD.Base.BaseRecordId<" + member.TypedRecordIdTarget + ">(global::HPD.Base.BaseReadGeneratedContract.Read<global::HPD.Base.RecordId>(row, " + Literal(member.Id) + "))";
            return member.IsNullable
                ? "global::HPD.Base.BaseReadGeneratedContract.IsNull(row, " + Literal(member.Id) + ") ? null : " + scalar
                : scalar;
        }
        return member.IsNullable
            ? "global::HPD.Base.BaseReadGeneratedContract.ReadNullable<" + member.ValueType + ">(row, " + Literal(member.Id) + ")"
            : "global::HPD.Base.BaseReadGeneratedContract.Read<" + member.Type + ">(row, " + Literal(member.Id) + ")";
    }

    private static bool SupportedMemberType(ITypeSymbol type, bool allowArray)
    {
        if (type is IArrayTypeSymbol array)
            return allowArray && array.Rank == 1 && SupportedScalar(UnwrapNullable(array.ElementType));
        return SupportedScalar(UnwrapNullable(type));
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol named && named.IsGenericType &&
        named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T
            ? named.TypeArguments[0]
            : type;

    private static bool SupportedScalar(ITypeSymbol type)
    {
        if (type.SpecialType is SpecialType.System_String or SpecialType.System_Boolean or
            SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Double or
            SpecialType.System_Decimal) return true;
        string name = type is INamedTypeSymbol named && named.IsGenericType
            ? named.ConstructedFrom.ToDisplayString()
            : type.ToDisplayString();
        return name is "System.DateTime" or "System.DateTimeOffset" or "System.Guid" or "HPD.Base.RecordId" or "HPD.Base.BaseRecordId<TRecord>";
    }
    private static string ValueKind(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String) return "String";
        if (type.SpecialType == SpecialType.System_Boolean) return "Boolean";
        if (type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64) return "Integer";
        if (type.SpecialType == SpecialType.System_Double) return "Number";
        if (type.SpecialType == SpecialType.System_Decimal) return "Decimal";
        string name = type is INamedTypeSymbol named && named.IsGenericType
            ? named.ConstructedFrom.ToDisplayString()
            : type.ToDisplayString();
        return name is "System.DateTime" or "System.DateTimeOffset" ? "DateTime" : "Id";
    }
    private static string Escape(string value) => SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None ? "@" + value : value;
    private static string Sanitize(string value) => new(value.Select(static character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value ?? string.Empty, true);

    private sealed class ReadModel { /// <summary>Provides the namespace value.</summary>
        public string Namespace; /// <summary>Provides the type name value.</summary>
        public string TypeName; /// <summary>Provides the full type name value.</summary>
        public string FullTypeName; /// <summary>Provides the row full type name value.</summary>
        public string RowFullTypeName; /// <summary>Provides the JSON context value.</summary>
        public string JsonContext; /// <summary>Provides the ID value.</summary>
        public string Id; /// <summary>Provides the parameters value.</summary>
        public List<MemberModel> Parameters; /// <summary>Provides the fields value.</summary>
        public List<MemberModel> Fields; }
    private sealed class MemberModel { /// <summary>Provides the name value.</summary>
        public string Name; /// <summary>Provides the ID value.</summary>
        public string Id; /// <summary>Provides the type value.</summary>
        public string Type; /// <summary>Provides the typed record ID target value.</summary>
        public string TypedRecordIdTarget; /// <summary>Provides the is array value.</summary>
        public bool IsArray; /// <summary>Provides the is nullable value.</summary>
        public bool IsNullable; /// <summary>Provides the value type value.</summary>
        public string ValueType; /// <summary>Provides the kind value.</summary>
        public string Kind; /// <summary>Provides the container nullable value.</summary>
        public bool ContainerNullable; }
}
