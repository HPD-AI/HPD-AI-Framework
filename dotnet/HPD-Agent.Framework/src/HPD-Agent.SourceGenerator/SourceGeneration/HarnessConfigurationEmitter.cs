using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

/// <summary>Generates immutable settings, patch codecs and schemas from the same symbol model.</summary>
internal static class HarnessConfigurationEmitter
{
    internal static INamedTypeSymbol? ConfigType(INamedTypeSymbol? owner)
    {
        var constructors = owner?.InstanceConstructors.Where(ctor => ctor.DeclaredAccessibility == Accessibility.Public).ToArray();
        if (constructors is not { Length: 1 }) return null;
        var constructor = constructors[0];
        var types = constructor.Parameters.Select(parameter => parameter.Type).OfType<INamedTypeSymbol>()
            .Where(type => type.OriginalDefinition.ToDisplayString() == "HPD.Agent.IHarnessConfig<T>")
            .Select(type => type.TypeArguments[0]).OfType<INamedTypeSymbol>().Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToArray();
        return types.SingleOrDefault(candidate => types.All(required => Assignable(candidate, required)));
    }
    private static bool Assignable(INamedTypeSymbol candidate, INamedTypeSymbol required)
    {
        for (var type = candidate; type is not null; type = type.BaseType)
            if (SymbolEqualityComparer.Default.Equals(type, required)) return true;
        return false;
    }
    internal static string Name(INamedTypeSymbol type) => "global::HPD.Agent.Generated.HarnessSettings_" +
        new string(type.ToDisplayString().Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
    private static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier | SymbolDisplayMiscellaneousOptions.UseSpecialTypes));
    private static string Quote(string text) => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(text, true);
    private static string JsonName(IPropertySymbol property) => char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
    private static List<IPropertySymbol> Properties(INamedTypeSymbol type)
    {
        var result = new List<IPropertySymbol>(); var names = new HashSet<string>(StringComparer.Ordinal);
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
                if (!property.IsStatic && !property.IsIndexer && property.DeclaredAccessibility == Accessibility.Public && names.Add(property.Name)) result.Add(property);
        return result;
    }
    private static bool Run(IPropertySymbol property) => property.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "HPD.Agent.HarnessRunSettingAttribute");
    private static bool Array(ITypeSymbol type) => type is INamedTypeSymbol named && named.OriginalDefinition.ToDisplayString() == "System.Collections.Immutable.ImmutableArray<T>";
    private static bool Dictionary(ITypeSymbol type) => type is INamedTypeSymbol named && named.OriginalDefinition.ToDisplayString() == "System.Collections.Immutable.ImmutableDictionary<TKey, TValue>" && named.TypeArguments[0].SpecialType == SpecialType.System_String;
    private static bool Nullable(ITypeSymbol type) => type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    private static bool IsNullable(ITypeSymbol type) => Nullable(type) || type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated;
    private static ITypeSymbol Underlying(ITypeSymbol type) => Nullable(type) ? ((INamedTypeSymbol)type).TypeArguments[0] : type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
    private static bool Leaf(ITypeSymbol type) => type.SpecialType is SpecialType.System_String or SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal || type.TypeKind == TypeKind.Enum ||
        new[] { "System.TimeSpan", "System.DateTime", "System.DateTimeOffset", "System.Guid", "System.Text.Json.JsonElement" }.Contains(type.ToDisplayString());

    internal static string? Validate(INamedTypeSymbol root, IAssemblySymbol assembly)
    {
        var visiting = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        string? Check(ITypeSymbol input)
        {
            var type = Underlying(input);
            if (Leaf(type)) return null;
            if (Array(type)) return Check(((INamedTypeSymbol)type).TypeArguments[0]);
            if (Dictionary(type)) return Check(((INamedTypeSymbol)type).TypeArguments[1]);
            if (type is not INamedTypeSymbol record || !record.IsRecord || record.IsValueType || record.IsAbstract || record.IsGenericType)
                return $"'{type}' is not a supported deeply immutable record or scalar";
            if (!visiting.Add(record)) return $"'{type}' contains a recursive settings graph";
            for (var declaration = record; declaration is not null && declaration.SpecialType != SpecialType.System_Object; declaration = declaration.BaseType)
            {
                if (!SymbolEqualityComparer.Default.Equals(declaration.ContainingAssembly, assembly))
                {
                    if (!declaration.ContainingAssembly.GetAttributes().Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString() == "HPD.Agent.HarnessImmutableContractAttribute" &&
                        attribute.ConstructorArguments.Length == 1 && SymbolEqualityComparer.Default.Equals(attribute.ConstructorArguments[0].Value as ITypeSymbol, declaration)))
                        return $"'{declaration}' has no compiled immutable settings contract";
                    continue;
                }
                foreach (var field in declaration.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
                    if (!field.IsReadOnly || !field.IsImplicitlyDeclared) return $"'{record}' contains unverified mutable or custom field '{field.Name}'";
            }
            foreach (var property in Properties(record))
            {
                if (property.SetMethod is null || !property.SetMethod.IsInitOnly || property.SetMethod.DeclaredAccessibility != Accessibility.Public)
                    return $"'{record}.{property.Name}' must be a public init-only setting";
                var error = Check(property.Type);
                if (error is not null) return error;
            }
            visiting.Remove(record); return null;
        }
        var failure = Check(root);
        if (failure is not null) return failure;
        if (!root.InstanceConstructors.Any(ctor => ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length == 0) || Properties(root).Any(property => property.IsRequired))
            return $"'{root}' must supply parameterless library defaults without required members";
        return null;
    }

    internal static string Generate(IEnumerable<INamedTypeSymbol> roots, IAssemblySymbol assembly)
    {
        var all = new List<INamedTypeSymbol>();
        void Add(ITypeSymbol input)
        {
            var type = Underlying(input);
            if (Array(type)) { Add(((INamedTypeSymbol)type).TypeArguments[0]); return; }
            if (Dictionary(type)) { Add(((INamedTypeSymbol)type).TypeArguments[1]); return; }
            if (Leaf(type) || type is not INamedTypeSymbol named || all.Any(value => SymbolEqualityComparer.Default.Equals(value, named))) return;
            if (!SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, assembly)) return;
            all.Add(named); foreach (var property in Properties(named)) Add(property.Type);
        }
        foreach (var root in roots) Add(root);
        var sb = new StringBuilder("// <auto-generated/>\n#nullable enable\n#pragma warning disable\n");
        foreach (var type in all)
            sb.AppendLine($"[assembly: global::HPD.Agent.HarnessImmutableContract(typeof({TypeName(type)}))]");
        foreach (var type in all)
        {
            var fqn = TypeName(type); var codec = Name(type).Replace("global::HPD.Agent.Generated.", ""); var properties = Properties(type);
            sb.AppendLine("namespace HPD.Agent.Generated {");
            sb.AppendLine($"public static class {codec} {{");
            sb.AppendLine($"public static readonly global::HPD.Agent.HarnessConfigurationContract<{fqn}> Contract = new(() => new {fqn}(), json => Read(json, \"config\"), value => global::HPD.Agent.HarnessConfigJson.Encode(writer => Write(writer, value)), {Schema(type, false)}{(type.AllInterfaces.Any(value => value.ToDisplayString() == "HPD.Agent.IHarnessSettingsValidator") ? ", value => ((global::HPD.Agent.IHarnessSettingsValidator)value).ValidateSettings()" : "")});");
            sb.AppendLine($"public static {fqn} Read(global::System.Text.Json.JsonElement json, string path) {{");
            sb.AppendLine($"global::HPD.Agent.HarnessConfigJson.Object(json, new string[] {{ {string.Join(", ", properties.Select(property => Quote(JsonName(property))))} }}, path);");
            sb.AppendLine($"return new {fqn} {{");
            foreach (var property in properties)
                sb.AppendLine($"@{property.Name} = global::HPD.Agent.HarnessConfigJson.Read(() => {Read(property.Type, $"json.GetProperty({Quote(JsonName(property))})", $"path + {Quote("." + JsonName(property))}")}, path + {Quote("." + JsonName(property))}),");
            sb.AppendLine("}; }");
            sb.AppendLine($"public static void Write(global::System.Text.Json.Utf8JsonWriter writer, {fqn} value) {{");
            sb.AppendLine($"if (value.GetType() != typeof({fqn})) throw new global::System.InvalidOperationException(\"Unknown settings subtype.\"); writer.WriteStartObject();");
            foreach (var property in properties)
                sb.AppendLine($"writer.WritePropertyName({Quote(JsonName(property))}); {Write(property.Type, "value.@" + property.Name)}");
            sb.AppendLine("writer.WriteEndObject(); } }"); sb.AppendLine("}");
            var ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
            if (ns is not null) sb.AppendLine($"namespace {ns} {{");
            sb.AppendLine($"public sealed class {type.Name}Overrides : global::HPD.Agent.IHarnessOverrides {{");
            sb.AppendLine($"public global::System.Type ConfigurationType => typeof({fqn});");
            foreach (var property in properties)
            {
                var propType = Underlying(property.Type);
                var patchType = !Leaf(propType) && !Array(propType) && !Dictionary(propType) ? PatchName((INamedTypeSymbol)propType) + (IsNullable(property.Type) ? "?" : "") : TypeName(property.Type);
                sb.AppendLine($"public global::HPD.Agent.HarnessOverrideValue<{patchType}> @{property.Name} {{ get; init; }}");
            }
            sb.AppendLine("public global::System.Text.Json.JsonElement ToJson() => global::HPD.Agent.HarnessConfigJson.Encode(writer => { writer.WriteStartObject();");
            foreach (var property in properties)
            {
                var propType = Underlying(property.Type); var value = "this.@" + property.Name + ".Value";
                var write = !Leaf(propType) && !Array(propType) && !Dictionary(propType)
                    ? (IsNullable(property.Type) ? $"if ({value} is null) writer.WriteNullValue(); else " : "") + $"{value}.ToJson().WriteTo(writer);"
                    : Write(property.Type, value);
                sb.AppendLine($"if (this.@{property.Name}.IsSpecified) {{ writer.WritePropertyName({Quote(JsonName(property))}); {write} }}");
            }
            sb.AppendLine("writer.WriteEndObject(); }); }"); if (ns is not null) sb.AppendLine("}");
        }
        return sb.ToString();
    }
    private static string PatchName(INamedTypeSymbol type) => "global::" + (type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString() + ".") + type.Name + "Overrides";
    private static string Schema(ITypeSymbol input, bool run)
    {
        var type = Underlying(input); var kind = type.ToDisplayString() == "System.Text.Json.JsonElement" ? "Json" : Leaf(type) ? "Scalar" : Array(type) ? "Array" : Dictionary(type) ? "Dictionary" : "Object";
        var extra = kind == "Object" ? ", new global::System.Collections.Generic.Dictionary<string, global::HPD.Agent.HarnessSettingSchema> { " + string.Join(", ", Properties((INamedTypeSymbol)type).Select(property => $"[{Quote(JsonName(property))}] = {Schema(property.Type, run || Run(property))}")) + " }" : "";
        return $"new global::HPD.Agent.HarnessSettingSchema(global::HPD.Agent.HarnessSettingKind.{kind}, {run.ToString().ToLowerInvariant()}, {IsNullable(input).ToString().ToLowerInvariant()}{extra})";
    }
    private static string Read(ITypeSymbol input, string json, string path)
    {
        var type = Underlying(input);
        if (IsNullable(input)) return $"({json}.ValueKind == global::System.Text.Json.JsonValueKind.Null ? ({TypeName(input)})null : {Read(type, json, path)})";
        if (Array(type)) return $"global::HPD.Agent.HarnessConfigJson.Array({json}, static (item, itemPath) => {Read(((INamedTypeSymbol)type).TypeArguments[0], "item", "itemPath")}, {path})";
        if (Dictionary(type)) return $"global::HPD.Agent.HarnessConfigJson.Dictionary({json}, static (item, itemPath) => {Read(((INamedTypeSymbol)type).TypeArguments[1], "item", "itemPath")}, {path})";
        if (type.TypeKind == TypeKind.Enum) return $"global::HPD.Agent.HarnessConfigJson.EnumValue<{TypeName(type)}>({json})";
        var method = type.SpecialType switch
        {
            SpecialType.System_String => "GetString() ?? throw new global::System.Text.Json.JsonException(\"Null string is not allowed.\")", SpecialType.System_Boolean => "GetBoolean()", SpecialType.System_Byte => "GetByte()", SpecialType.System_SByte => "GetSByte()",
            SpecialType.System_Int16 => "GetInt16()", SpecialType.System_UInt16 => "GetUInt16()", SpecialType.System_Int32 => "GetInt32()", SpecialType.System_UInt32 => "GetUInt32()", SpecialType.System_Int64 => "GetInt64()", SpecialType.System_UInt64 => "GetUInt64()", SpecialType.System_Single => "GetSingle()", SpecialType.System_Double => "GetDouble()", SpecialType.System_Decimal => "GetDecimal()", _ => null
        };
        if (method is not null) return json + "." + method;
        return type.ToDisplayString() switch
        {
            "System.TimeSpan" => $"global::System.TimeSpan.ParseExact({json}.GetString()!, \"c\", global::System.Globalization.CultureInfo.InvariantCulture)",
            "System.DateTime" => json + ".GetDateTime()", "System.DateTimeOffset" => json + ".GetDateTimeOffset()", "System.Guid" => json + ".GetGuid()", "System.Text.Json.JsonElement" => json + ".Clone()",
            _ => $"{Name((INamedTypeSymbol)type)}.Read({json}, {path})"
        };
    }
    private static string Write(ITypeSymbol input, string value)
    {
        var type = Underlying(input);
        if (IsNullable(input)) return $"if ({value} is null) writer.WriteNullValue(); else {{ {Write(type, Nullable(input) ? value + ".Value" : value)} }}";
        if (Array(type)) return $"global::HPD.Agent.HarnessConfigJson.WriteArray(writer, {value}, static (writer, item) => {{ {Write(((INamedTypeSymbol)type).TypeArguments[0], "item")} }});";
        if (Dictionary(type)) return $"global::HPD.Agent.HarnessConfigJson.WriteDictionary(writer, {value}, static (writer, item) => {{ {Write(((INamedTypeSymbol)type).TypeArguments[1], "item")} }});";
        if (type.TypeKind == TypeKind.Enum) return $"writer.WriteStringValue({value}.ToString());";
        if (type.SpecialType == SpecialType.System_String) return $"writer.WriteStringValue({value});";
        if (type.SpecialType == SpecialType.System_Boolean) return $"writer.WriteBooleanValue({value});";
        if (Leaf(type)) return type.ToDisplayString() switch
        {
            "System.TimeSpan" => $"writer.WriteStringValue({value}.ToString(\"c\", global::System.Globalization.CultureInfo.InvariantCulture));",
            "System.DateTime" or "System.DateTimeOffset" or "System.Guid" => $"writer.WriteStringValue({value});",
            "System.Text.Json.JsonElement" => $"{value}.WriteTo(writer);",
            _ => $"writer.WriteNumberValue({value});"
        };
        return $"{Name((INamedTypeSymbol)type)}.Write(writer, {value});";
    }
}
