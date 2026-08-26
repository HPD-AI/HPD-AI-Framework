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

#nullable enable

namespace HPD.Base.Generators;

internal static class BaseModuleMutationGenerator
{
    private const string AttributeName = "HPD.Base.BaseRegisteredModuleMutationAttribute";
    private const string FieldAttribute = "HPD.Base.BaseFieldAttribute";
    private const string ConfidentialityAttribute = "HPD.Base.BaseFieldConfidentialityAttribute";
    private const string DisclosureAttribute = "HPD.Base.BaseFieldDisclosureAttribute";
    private static readonly DiagnosticDescriptor Invalid = new(
        "HPDBASE0500", "Invalid registered module mutation",
        "Registered module mutation '{0}' is invalid: {1}",
        "HPD.Base.Generation", DiagnosticSeverity.Error, true);

    internal static void GenerateCombined(
        SourceProductionContext context,
        ImmutableArray<INamedTypeSymbol> candidates,
        ImmutableDictionary<INamedTypeSymbol, ContextValidationResult> contexts)
    {
        var identities = new HashSet<(string, int)>();
        foreach (INamedTypeSymbol symbol in candidates.Distinct(SymbolEqualityComparer.Default).OfType<INamedTypeSymbol>()
            .OrderBy(static value => value.ToDisplayString(), StringComparer.Ordinal))
        {
            AttributeData attribute = symbol.GetAttributes().First(value => value.AttributeClass?.ToDisplayString() == AttributeName)!;
            string id = attribute.ConstructorArguments.ElementAtOrDefault(0).Value as string ?? string.Empty;
            INamedTypeSymbol? serializerContext = attribute.ConstructorArguments.ElementAtOrDefault(1).Value as INamedTypeSymbol;
            INamedTypeSymbol? request = attribute.ConstructorArguments.ElementAtOrDefault(2).Value as INamedTypeSymbol;
            INamedTypeSymbol? result = attribute.ConstructorArguments.ElementAtOrDefault(3).Value as INamedTypeSymbol;
            int version = NamedInt(attribute, "Version", 1);
            string owner = NamedString(attribute, "OwningModuleId");
            string grant = NamedString(attribute, "GrantId");
            bool declarationValid = symbol.TypeKind == TypeKind.Class && symbol.IsStatic && symbol.Arity == 0
                && symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal && Partial(symbol);
            if (!declarationValid || !ValidId(id) || version < 1 || !ValidId(owner) || !ValidId(grant)
                || serializerContext is null || request is null || result is null || !identities.Add((id, version))
                || !contexts.TryGetValue(serializerContext, out ContextValidationResult? validation)
                || !validation.IsValid || !validation.Roots.Contains(request, SymbolEqualityComparer.Default)
                || !validation.Roots.Contains(result, SymbolEqualityComparer.Default))
            {
                context.ReportDiagnostic(Diagnostic.Create(Invalid, symbol.Locations.FirstOrDefault(), id.Length == 0 ? symbol.Name : id,
                    "the declaration, identity, DTO roots, and serializer context must be graph-owned and valid"));
                context.AddSource(Sanitize(symbol) + ".HPDBaseModuleMutationRecovery.g.cs", SourceText.From(RenderRecovery(symbol, request, result), Encoding.UTF8));
                continue;
            }

            ImmutableArray<ContextGraphProperty> properties = validation.UnionGraph.PropertiesForRoot(request)
                .AddRange(validation.UnionGraph.PropertiesForRoot(result))
                .GroupBy(static property => property.CanonicalKey, StringComparer.Ordinal).Select(static group => group.First())
                .OrderBy(static property => property.CanonicalKey, StringComparer.Ordinal).ToImmutableArray();
            int namingPolicy = int.Parse(validation.OptionReceipt.Single(static value => value.StartsWith("PropertyNamingPolicy=", StringComparison.Ordinal)).Split('=')[1]);
            List<PropertyBinding> requestBindings = Bindings(request, validation.UnionGraph.PropertiesForRoot(request), includeNested: true, namingPolicy);
            List<PropertyBinding> resultBindings = Bindings(result, validation.UnionGraph.PropertiesForRoot(result), includeNested: false, namingPolicy);
            if (requestBindings.Count == 0 || resultBindings.Count == 0
                || !CanonicalGuidConverters(validation.UnionGraph.PropertiesForRoot(request))
                || !CanonicalGuidConverters(validation.UnionGraph.PropertiesForRoot(result)))
            {
                context.ReportDiagnostic(Diagnostic.Create(Invalid, symbol.Locations.FirstOrDefault(), id,
                    "request and result DTO properties must carry stable BaseField identities and exact canonical GUID converters"));
                context.AddSource(Sanitize(symbol) + ".HPDBaseModuleMutationRecovery.g.cs", SourceText.From(RenderRecovery(symbol, request, result), Encoding.UTF8));
                continue;
            }
            context.AddSource(Sanitize(symbol) + ".HPDBaseModuleMutation.g.cs",
                SourceText.From(Render(symbol, serializerContext, request, result, properties, requestBindings, resultBindings), Encoding.UTF8));
        }
    }

    private static bool CanonicalGuidConverters(ImmutableArray<ContextGraphProperty> properties)
    {
        foreach (ContextGraphProperty property in properties)
        {
            ITypeSymbol type = property.PropertyType;
            bool nullable = type is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Guid";
            bool guid = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Guid";
            if (!guid && !nullable) continue;
            string expected = nullable
                ? "global::HPD.Base.BaseCanonicalNullableGuidJsonConverter"
                : "global::HPD.Base.BaseCanonicalGuidJsonConverter";
            if (!string.Equals(property.ConverterType, expected, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static string Render(INamedTypeSymbol symbol, INamedTypeSymbol context, INamedTypeSymbol request, INamedTypeSymbol result,
        ImmutableArray<ContextGraphProperty> properties, List<PropertyBinding> requestBindings, List<PropertyBinding> resultBindings)
    {
        var source = Header(symbol);
        AppendClosedEnumAuthorities(source, requestBindings.Concat(resultBindings));
        AppendPropertyHandles(source, request, result, requestBindings, resultBindings);
        source.AppendLine("    /// <summary>Gets inert generated registration evidence for this operation.</summary>");
        source.Append("    public static global::HPD.Base.BaseGeneratedModuleMutationIdentity<").Append(Type(request)).Append(", ").Append(Type(result))
            .AppendLine("> Identity { get; } = CreateHPDBaseIdentity();");
        source.Append("    private static global::HPD.Base.BaseGeneratedModuleMutationIdentity<").Append(Type(request)).Append(", ").Append(Type(result))
            .AppendLine("> CreateHPDBaseIdentity()\n    {");
        source.AppendLine("        var registration = global::HPD.Base.BaseSerializerGeneratedContract.RegisterContext(__HPDBaseSerializerFactory.Create);");
        source.Append("        return global::HPD.Base.BaseGeneratedModuleMutations.Register<").Append(Type(request)).Append(", ").Append(Type(result))
            .AppendLine(">(Definition.Id, Definition.Version, Definition.Checksum.ToArray(), registration,");
        AppendDeclarations(source, properties);
        AppendBindings(source, requestBindings, trailingComma: true);
        AppendBindings(source, resultBindings, trailingComma: false);
        source.AppendLine("        );\n    }");
        source.AppendLine("    /// <summary>Creates graph-installation evidence for a semantic activation whose key is derived from this operation request.</summary>");
        source.Append("    public static global::HPD.Base.BaseSemanticActivationKeyIdentity<").Append(Type(request)).Append(", TDefinition> CreateSemanticActivationKeyIdentity<TDefinition>(global::HPD.Base.BaseSemanticActivationKeyDefinition definition, global::HPD.Base.BaseSemanticActivationKeyExpression expression)\n    {\n");
        source.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(definition);");
        source.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(expression);");
        source.Append("        return global::HPD.Base.BaseGeneratedSemanticActivations.Register<").Append(Type(request)).Append(", ").Append(Type(result)).Append(", TDefinition>(definition.Id, definition.Version, definition.OwningApplicationId, definition.OwningModuleId, definition.Checksum.AsSpan(), definition.Limits.MaximumCanonicalKeyBytes, Identity, expression);\n    }\n");
        source.AppendLine("    private static class __HPDBaseSerializerFactory\n    {");
        source.AppendLine("        [global::System.CodeDom.Compiler.GeneratedCode(\"HPD.Base.Generators\", \"50\")]");
        source.Append("        internal static ").Append(Type(context)).AppendLine(" Create() => new(global::HPD.Base.BaseSerializerGeneratedContract.CreateOptions(null));");
        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString();
    }

    private static void AppendClosedEnumAuthorities(StringBuilder source, IEnumerable<PropertyBinding> bindings)
    {
        foreach (INamedTypeSymbol enumType in bindings.Select(static binding =>
            binding.PropertyType is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? nullable.TypeArguments[0] : binding.PropertyType)
            .OfType<INamedTypeSymbol>().Where(static type => type.TypeKind == TypeKind.Enum)
            .GroupBy(static type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .Select(static group => group.First()))
        {
            (string Member, string Wire)[] cases = enumType.GetMembers().OfType<IFieldSymbol>()
                .Where(static field => field.HasConstantValue)
                .Select(field => (field.Name, field.GetAttributes().FirstOrDefault(attribute =>
                    attribute.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute")?.ConstructorArguments[0].Value as string ?? field.Name))
                .ToArray();
            string type = Type(enumType);
            source.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
            source.Append("    internal static void RegisterHPDBaseModuleClosedEnum_").Append(Sanitize(enumType))
                .Append("() => global::HPD.Base.BaseClosedEnumGeneratedContract.Register<").Append(type).Append(">(")
                .Append("new ").Append(type).Append("[] { ")
                .Append(string.Join(", ", cases.Select(item => type + "." + item.Member)))
                .Append(" }, new string[] { ").Append(string.Join(", ", cases.Select(item => Literal(item.Wire)))).AppendLine(" });");
        }
    }

    private static void AppendPropertyHandles(
        StringBuilder source,
        INamedTypeSymbol request,
        INamedTypeSymbol result,
        List<PropertyBinding> requestBindings,
        List<PropertyBinding> resultBindings)
    {
        Append("RequestProperties", request, requestBindings, request: true);
        Append("ResultProperties", result, resultBindings, request: false);

        void Append(string className, INamedTypeSymbol owner, List<PropertyBinding> bindings, bool request)
        {
            source.Append("    /// <summary>Provides exact generated ").Append(request ? "request" : "result").AppendLine(" property handles.</summary>");
            source.Append("    public static class ").Append(className).AppendLine("\n    {");
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (PropertyBinding binding in bindings)
            {
                string name = HandleName(binding, used);
                source.Append("        /// <summary>Gets the exact scalar handle for <c>").Append(string.Join("/", binding.Path)).AppendLine("</c>.</summary>");
                source.Append("        public static global::HPD.Base.BaseModule").Append(request ? "RequestProperty" : "ResultProperty")
                    .Append('<').Append(Type(owner)).Append(", ").Append(Type(binding.PropertyType)).Append("> ").Append(name)
                    .Append(" { get; } = ").Append(Manifest(binding)).Append('.').Append(request ? "RequestProperty" : "ResultProperty")
                    .Append('<').Append(Type(owner)).Append(", ").Append(Type(binding.PropertyType)).Append(">(");
                source.Append(string.Join(", ", binding.Path.Select(Literal))).AppendLine(");");
            }
            source.AppendLine("    }");
        }
    }

    private static string HandleName(PropertyBinding binding, HashSet<string> used)
    {
        string candidate = new string(binding.Name.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
        if (candidate.Length == 0 || !(char.IsLetter(candidate[0]) || candidate[0] == '_')) candidate = "_" + candidate;
        if (SyntaxFacts.GetKeywordKind(candidate) != SyntaxKind.None) candidate = "@" + candidate;
        if (used.Add(candidate)) return candidate;
        int suffix = 2;
        while (!used.Add(candidate + "_" + suffix.ToString(CultureInfo.InvariantCulture))) suffix++;
        candidate += "_" + suffix.ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    private static void AppendDeclarations(StringBuilder source, ImmutableArray<ContextGraphProperty> properties)
    {
        source.AppendLine("            new global::HPD.Base.BaseSerializerPropertyDeclaration[]\n            {");
        foreach (ContextGraphProperty property in properties)
            source.Append("                global::HPD.Base.BaseSerializerPropertyDeclaration.Create(typeof(").Append(Type(property.DeclaringType))
                .Append("), ").Append(Literal(property.ApplicationName)).Append(", typeof(").Append(Type(property.PropertyType)).Append("), ")
                .Append(property.ExplicitWireName is null ? "null" : Literal(property.ExplicitWireName)).Append(", ")
                .Append(property.Required ? "true" : "false").Append(", ").Append(property.Nullable ? "true" : "false").Append(", ")
                .Append(property.Ignored ? "true" : "false").Append(", ").Append(property.ExplicitNever ? "true" : "false").Append(", ")
                .Append(Literal(property.ConverterIdentity)).Append(", ")
                .Append(property.ConverterType is null ? "null" : "typeof(" + property.ConverterType + ")").AppendLine("),");
        source.AppendLine("            },");
    }

    private static void AppendBindings(StringBuilder source, List<PropertyBinding> bindings, bool trailingComma)
    {
        source.AppendLine("            new global::HPD.Base.BaseModuleDtoPropertyBinding[]\n            {");
        foreach (PropertyBinding binding in bindings)
        {
            source.Append("                global::HPD.Base.BaseModuleDtoPropertyBinding.")
                .Append(binding.Path.Length == 1 ? "CreateWire" : "CreatePathWire").Append('<').Append(Type(binding.DeclaringType)).Append(", ").Append(Type(binding.PropertyType)).Append(">(");
            if (binding.Path.Length == 1) source.Append(Literal(binding.Path[0]));
            else
            {
                source.Append("new string[] { ");
                foreach (string edge in binding.Path) source.Append(Literal(edge)).Append(", ");
                source.Append('}');
            }
            source.Append(", ").Append(Literal(binding.Name)).Append(", ");
            if (binding.Path.Length == 1) source.Append(Literal(binding.WirePath[0]));
            else
            {
                source.Append("new string[] { ");
                foreach (string edge in binding.WirePath) source.Append(Literal(edge)).Append(", ");
                source.Append('}');
            }
            source.Append(", ").Append(Manifest(binding)).Append(", (global::HPD.Base.BaseFieldConfidentiality)")
                .Append(binding.Confidentiality).Append(", (global::HPD.Base.BaseRecordDisclosure)")
                .Append(binding.RecordDisclosure).AppendLine("),");
        }
        source.Append("            }").AppendLine(trailingComma ? "," : string.Empty);
    }

    private static List<PropertyBinding> Bindings(INamedTypeSymbol root, ImmutableArray<ContextGraphProperty> graph, bool includeNested, int namingPolicy)
    {
        var result = new List<PropertyBinding>();
        Walk(root, ImmutableArray<string>.Empty, ImmutableArray<string>.Empty, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));
        return result.OrderBy(static value => string.Join("\0", value.Path), StringComparer.Ordinal).ToList();

        void Walk(INamedTypeSymbol current, ImmutableArray<string> prefix, ImmutableArray<string> wirePrefix, HashSet<INamedTypeSymbol> ancestry)
        {
            if (prefix.Length >= 16 || !ancestry.Add(current)) return;
            foreach (ContextGraphProperty property in graph.Where(value => SymbolEqualityComparer.Default.Equals(value.DeclaringType, current))
                .OrderBy(static value => value.ApplicationName, StringComparer.Ordinal))
            {
                IPropertySymbol? symbol = current.GetMembers(property.ApplicationName).OfType<IPropertySymbol>().SingleOrDefault();
                if (symbol is null) continue;
                string id = symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == FieldAttribute)?.ConstructorArguments.ElementAtOrDefault(0).Value as string ?? string.Empty;
                if (!ValidId(id)) continue;
                ImmutableArray<string> path = prefix.Add(id);
                ImmutableArray<string> wirePath = wirePrefix.Add(WireName(property, namingPolicy));
                int confidentiality = symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == ConfidentialityAttribute)?.ConstructorArguments.ElementAtOrDefault(0).Value is int value ? value : 0;
                AttributeData? disclosure = symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == DisclosureAttribute);
                int recordDisclosure = disclosure?.NamedArguments.FirstOrDefault(value => value.Key == "RecordRead").Value.Value is int explicitValue
                    ? explicitValue
                    : confidentiality <= 1 ? 0 : 1;
                AttributeData field = symbol.GetAttributes().First(attribute => attribute.AttributeClass?.ToDisplayString() == FieldAttribute);
                bool nestedObject = includeNested && property.PropertyType is INamedTypeSymbol candidate
                    && graph.Any(value => SymbolEqualityComparer.Default.Equals(value.DeclaringType, candidate));
                if (!nestedObject)
                    result.Add(new PropertyBinding(path, wirePath, property.ApplicationName, root, property.PropertyType, confidentiality, recordDisclosure, property.Nullable, field));
                if (nestedObject && property.PropertyType is INamedTypeSymbol nested)
                    Walk(nested, path, wirePath, new HashSet<INamedTypeSymbol>(ancestry, SymbolEqualityComparer.Default));
            }
        }
    }

    private static string WireName(ContextGraphProperty property, int namingPolicy)
    {
        if (!string.IsNullOrEmpty(property.ExplicitWireName)) return property.ExplicitWireName;
        return namingPolicy switch
        {
            1 => char.ToLowerInvariant(property.ApplicationName[0]) + property.ApplicationName.Substring(1),
            2 => ConvertSeparated(property.ApplicationName, '_', false),
            3 => ConvertSeparated(property.ApplicationName, '_', true),
            4 => ConvertSeparated(property.ApplicationName, '-', false),
            5 => ConvertSeparated(property.ApplicationName, '-', true),
            _ => property.ApplicationName,
        };
    }

    private static string Manifest(PropertyBinding binding)
    {
        ITypeSymbol type = binding.PropertyType;
        if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            type = nullable.TypeArguments[0];
        string display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string kind = type.SpecialType switch
        {
            SpecialType.System_String => "String", SpecialType.System_Boolean => "Boolean",
            SpecialType.System_Int32 => "Int32", SpecialType.System_Int64 => "Int64",
            SpecialType.System_UInt32 => "UInt32", SpecialType.System_UInt64 => "UInt64",
            SpecialType.System_Decimal => "Decimal",
            _ => display switch
        {
            "global::System.Guid" => "Guid",
            "global::System.DateTimeOffset" => "UtcDateTime", "global::HPD.Base.BaseBinary" => "Binary",
            "global::HPD.Base.BaseCanonicalJson" => "CanonicalJson", "global::HPD.Base.BaseModuleGeneration" => "ModuleGeneration",
            "global::HPD.Base.RevisionToken" => "Revision",
            _ when type.TypeKind == TypeKind.Enum => "ClosedEnum",
            _ when type is INamedTypeSymbol named && named.IsGenericType
                && named.ConstructedFrom.Name == "BaseRecordId" && named.ConstructedFrom.ContainingNamespace.ToDisplayString() == "HPD.Base" => "RecordId",
            _ => throw new InvalidOperationException("Unsupported generated module scalar."),
        }};
        int presence = NamedEnum(binding.Field, "Presence", 0);
        int nullability = NamedEnum(binding.Field, "Nullability", binding.Nullable ? 1 : 0);
        ImmutableArray<TypedConstant> literals = NamedArray(binding.Field, "AllowedEnumLiterals");
        string qualifier = kind == "ClosedEnum"
            ? "global::HPD.Base.BaseGeneratedSchemaRegistration.EnumQualifier(" + string.Join(", ", literals.Select(value => Literal((string)value.Value!))) + ")"
            : "null";
        string target = "null";
        if (kind == "RecordId" && type is INamedTypeSymbol recordId)
        {
            AttributeData? collection = recordId.TypeArguments[0].GetAttributes().FirstOrDefault(value => value.AttributeClass?.ToDisplayString() == "HPD.Base.BaseCollectionAttribute");
            target = Literal(collection?.ConstructorArguments.ElementAtOrDefault(0).Value as string ?? string.Empty);
        }
        var constraints = new List<string>();
        void Scalar(string source, string target)
        {
            KeyValuePair<string, TypedConstant> argument = binding.Field.NamedArguments.FirstOrDefault(value => value.Key == source);
            if (argument.Key is not null && argument.Value.Value is not null)
                constraints.Add(target + " = " + Convert.ToString(argument.Value.Value, CultureInfo.InvariantCulture));
        }
        void OptionalPositive(string source, string target)
        {
            KeyValuePair<string, TypedConstant> argument = binding.Field.NamedArguments.FirstOrDefault(value => value.Key == source);
            if (argument.Key is not null && argument.Value.Value is int value && value >= 0)
                constraints.Add(target + " = " + value.ToString(CultureInfo.InvariantCulture));
        }
        void Gated(string source, string target)
        {
            if (binding.Field.NamedArguments.FirstOrDefault(value => value.Key == "Has" + source).Value.Value is true)
                Scalar(source, target);
        }
        OptionalPositive("MinimumUtf8Bytes", "MinimumUtf8Bytes");
        OptionalPositive("MaximumUtf8Bytes", "MaximumUtf8Bytes");
        if (binding.Field.NamedArguments.Any(value => value.Key == "StringNormalization"))
            constraints.Add("StringNormalization = (global::HPD.Base.BaseStringNormalizationRequirement)" + NamedEnum(binding.Field, "StringNormalization", 0));
        Gated("MinimumInt32", "MinimumInt32"); Gated("MaximumInt32", "MaximumInt32");
        Gated("MinimumInt64", "MinimumInt64"); Gated("MaximumInt64", "MaximumInt64");
        Gated("MinimumUInt32", "MinimumUInt32"); Gated("MaximumUInt32", "MaximumUInt32");
        Gated("MinimumUInt64", "MinimumUInt64"); Gated("MaximumUInt64", "MaximumUInt64");
        string minimumDecimal = NamedString(binding.Field, "MinimumDecimal");
        string maximumDecimal = NamedString(binding.Field, "MaximumDecimal");
        if (minimumDecimal.Length != 0) constraints.Add("MinimumDecimal = global::HPD.Base.BaseGeneratedSchemaRegistration.Decimal(" + Literal(minimumDecimal) + ")");
        if (maximumDecimal.Length != 0) constraints.Add("MaximumDecimal = global::HPD.Base.BaseGeneratedSchemaRegistration.Decimal(" + Literal(maximumDecimal) + ")");
        if (!literals.IsDefaultOrEmpty)
            constraints.Add("AllowedEnumLiterals = [" + string.Join(", ", literals.Select(value => (string)value.Value!).OrderBy(static value => value, StringComparer.Ordinal).Select(Literal)) + "]");
        OptionalPositive("MaximumBytes", "MaximumBinaryBytes");
        OptionalPositive("MaximumCanonicalJsonBytes", "MaximumCanonicalJsonBytes");
        if (binding.Field.NamedArguments.Any(value => value.Key == "JsonShape"))
            constraints.Add("JsonShape = (global::HPD.Base.BaseJsonShape)" + NamedEnum(binding.Field, "JsonShape", 0));
        OptionalPositive("MaximumJsonDepth", "MaximumJsonDepth");
        OptionalPositive("MaximumJsonArrayItems", "MaximumJsonArrayItems");
        OptionalPositive("MaximumJsonObjectProperties", "MaximumJsonObjectProperties");
        OptionalPositive("MaximumJsonTotalNodes", "MaximumJsonTotalNodes");
        OptionalPositive("MaximumJsonTotalStringUtf8Bytes", "MaximumJsonTotalStringUtf8Bytes");
        OptionalPositive("MaximumJsonTotalNameUtf8Bytes", "MaximumJsonTotalNameUtf8Bytes");
        OptionalPositive("MinimumCollectionItems", "MinimumCollectionItems");
        OptionalPositive("MaximumCollectionItems", "MaximumCollectionItems");
        if (kind == "ModuleGeneration")
        {
            if (!constraints.Any(static value => value.StartsWith("MinimumUtf8Bytes", StringComparison.Ordinal))) constraints.Add("MinimumUtf8Bytes = 1");
            if (!constraints.Any(static value => value.StartsWith("MaximumUtf8Bytes", StringComparison.Ordinal))) constraints.Add("MaximumUtf8Bytes = 19");
            constraints.RemoveAll(static value => value.StartsWith("StringNormalization", StringComparison.Ordinal));
        }
        if (kind == "RecordId")
        {
            if (!constraints.Any(static value => value.StartsWith("MinimumUtf8Bytes", StringComparison.Ordinal))) constraints.Add("MinimumUtf8Bytes = 1");
            if (!constraints.Any(static value => value.StartsWith("MaximumUtf8Bytes", StringComparison.Ordinal))) constraints.Add("MaximumUtf8Bytes = 256");
            if (!constraints.Any(static value => value.StartsWith("StringNormalization", StringComparison.Ordinal))) constraints.Add("StringNormalization = global::HPD.Base.BaseStringNormalizationRequirement.RequireNfc");
        }
        return "new global::HPD.Base.BaseGeneratedModuleScalarManifest(global::HPD.Base.BaseModuleValueKind." + kind
            + ", (global::HPD.Base.BaseFieldPresence)" + presence + ", (global::HPD.Base.BaseFieldNullability)" + nullability
            + ", new global::HPD.Base.BaseScalarConstraintSet { " + string.Join(", ", constraints) + " }, " + qualifier + ", " + target + ")";
    }

    private static string ConvertSeparated(string value, char separator, bool upper)
    {
        var output = new StringBuilder();
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (index > 0 && char.IsUpper(current) && (char.IsLower(value[index - 1]) || index + 1 < value.Length && char.IsLower(value[index + 1]))) output.Append(separator);
            output.Append(upper ? char.ToUpperInvariant(current) : char.ToLowerInvariant(current));
        }
        return output.ToString();
    }

    private static string RenderRecovery(INamedTypeSymbol symbol, INamedTypeSymbol? request, INamedTypeSymbol? result)
    {
        var source = Header(symbol);
        if (request is not null && result is not null)
            source.Append("    public static global::HPD.Base.BaseGeneratedModuleMutationIdentity<").Append(Type(request)).Append(", ").Append(Type(result)).AppendLine("> Identity => throw new global::System.InvalidOperationException(\"base.moduleMutation.invalid\");");
        source.AppendLine("}"); return source.ToString();
    }
    private static StringBuilder Header(INamedTypeSymbol symbol)
    {
        var source = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        if (!symbol.ContainingNamespace.IsGlobalNamespace) source.Append("namespace ").Append(symbol.ContainingNamespace.ToDisplayString()).AppendLine(";");
        source.Append(symbol.DeclaredAccessibility == Accessibility.Internal ? "internal" : "public")
            .Append(" static partial class ").Append(symbol.Name).AppendLine("\n{"); return source;
    }
    private static bool Partial(INamedTypeSymbol symbol) => symbol.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
    private static string NamedString(AttributeData attribute, string name) => attribute.NamedArguments.FirstOrDefault(value => value.Key == name).Value.Value as string ?? string.Empty;
    private static int NamedInt(AttributeData attribute, string name, int fallback) => attribute.NamedArguments.FirstOrDefault(value => value.Key == name).Value.Value is int value ? value : fallback;
    private static int NamedEnum(AttributeData attribute, string name, int fallback) => attribute.NamedArguments.FirstOrDefault(value => value.Key == name).Value.Value is int value ? value : fallback;
    private static ImmutableArray<TypedConstant> NamedArray(AttributeData attribute, string name)
    {
        KeyValuePair<string, TypedConstant> argument = attribute.NamedArguments.FirstOrDefault(value => value.Key == name);
        return argument.Key is not null && argument.Value.Kind == TypedConstantKind.Array
            ? argument.Value.Values
            : ImmutableArray<TypedConstant>.Empty;
    }
    private static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) && Encoding.UTF8.GetByteCount(value) <= 256 && value.All(character => !char.IsControl(character));
    private static string Type(ITypeSymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value ?? string.Empty, true);
    private static string Sanitize(INamedTypeSymbol symbol) => new(symbol.ToDisplayString().Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
    private sealed class PropertyBinding
    {
        internal PropertyBinding(ImmutableArray<string> path, ImmutableArray<string> wirePath, string name, INamedTypeSymbol declaringType, ITypeSymbol propertyType, int confidentiality, int recordDisclosure, bool nullable, AttributeData field)
        {
            Path = path; WirePath = wirePath; Name = name; DeclaringType = declaringType; PropertyType = propertyType; Confidentiality = confidentiality; RecordDisclosure = recordDisclosure; Nullable = nullable; Field = field;
        }
        internal ImmutableArray<string> Path { get; }
        internal ImmutableArray<string> WirePath { get; }
        internal string Name { get; }
        internal INamedTypeSymbol DeclaringType { get; }
        internal ITypeSymbol PropertyType { get; }
        internal int Confidentiality { get; }
        internal int RecordDisclosure { get; }
        internal bool Nullable { get; }
        internal AttributeData Field { get; }
    }
}
