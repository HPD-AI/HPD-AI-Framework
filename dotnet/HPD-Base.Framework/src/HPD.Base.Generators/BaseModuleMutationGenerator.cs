using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
                && symbol.DeclaredAccessibility == Accessibility.Public && Partial(symbol);
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
            List<PropertyBinding> requestBindings = Bindings(request, validation.UnionGraph.PropertiesForRoot(request), includeNested: true);
            List<PropertyBinding> resultBindings = Bindings(result, validation.UnionGraph.PropertiesForRoot(result), includeNested: false);
            if (requestBindings.Count == 0 || resultBindings.Count == 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(Invalid, symbol.Locations.FirstOrDefault(), id,
                    "request and result DTO properties must carry stable BaseField identities"));
                context.AddSource(Sanitize(symbol) + ".HPDBaseModuleMutationRecovery.g.cs", SourceText.From(RenderRecovery(symbol, request, result), Encoding.UTF8));
                continue;
            }
            context.AddSource(Sanitize(symbol) + ".HPDBaseModuleMutation.g.cs",
                SourceText.From(Render(symbol, serializerContext, request, result, properties, requestBindings, resultBindings), Encoding.UTF8));
        }
    }

    private static string Render(INamedTypeSymbol symbol, INamedTypeSymbol context, INamedTypeSymbol request, INamedTypeSymbol result,
        ImmutableArray<ContextGraphProperty> properties, List<PropertyBinding> requestBindings, List<PropertyBinding> resultBindings)
    {
        var source = Header(symbol);
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
        source.AppendLine("    private static class __HPDBaseSerializerFactory\n    {");
        source.AppendLine("        [global::System.CodeDom.Compiler.GeneratedCode(\"HPD.Base.Generators\", \"50\")]");
        source.Append("        internal static ").Append(Type(context)).AppendLine(" Create() => new(global::HPD.Base.BaseSerializerGeneratedContract.CreateOptions(null));");
        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString();
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
                .Append(binding.Path.Length == 1 ? "Create" : "CreatePath").Append('<').Append(Type(binding.DeclaringType)).Append(", ").Append(Type(binding.PropertyType)).Append(">(");
            if (binding.Path.Length == 1) source.Append(Literal(binding.Path[0]));
            else
            {
                source.Append("new string[] { ");
                foreach (string edge in binding.Path) source.Append(Literal(edge)).Append(", ");
                source.Append('}');
            }
            source.Append(", ").Append(Literal(binding.Name)).Append(", (global::HPD.Base.BaseFieldConfidentiality)")
                .Append(binding.Confidentiality).Append(", (global::HPD.Base.BaseRecordDisclosure)")
                .Append(binding.RecordDisclosure).Append(", ").Append(binding.Nullable ? "true" : "false").AppendLine("),");
        }
        source.Append("            }").AppendLine(trailingComma ? "," : string.Empty);
    }

    private static List<PropertyBinding> Bindings(INamedTypeSymbol root, ImmutableArray<ContextGraphProperty> graph, bool includeNested)
    {
        var result = new List<PropertyBinding>();
        Walk(root, ImmutableArray<string>.Empty, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));
        return result.OrderBy(static value => string.Join("\0", value.Path), StringComparer.Ordinal).ToList();

        void Walk(INamedTypeSymbol current, ImmutableArray<string> prefix, HashSet<INamedTypeSymbol> ancestry)
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
                int confidentiality = symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == ConfidentialityAttribute)?.ConstructorArguments.ElementAtOrDefault(0).Value is int value ? value : 0;
                AttributeData? disclosure = symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == DisclosureAttribute);
                int recordDisclosure = disclosure?.NamedArguments.FirstOrDefault(value => value.Key == "RecordRead").Value.Value is int explicitValue
                    ? explicitValue
                    : confidentiality <= 1 ? 0 : 1;
                result.Add(new PropertyBinding(path, property.ApplicationName, root, property.PropertyType, confidentiality, recordDisclosure, property.Nullable));
                if (includeNested && property.PropertyType is INamedTypeSymbol nested
                    && graph.Any(value => SymbolEqualityComparer.Default.Equals(value.DeclaringType, nested)))
                    Walk(nested, path, new HashSet<INamedTypeSymbol>(ancestry, SymbolEqualityComparer.Default));
            }
        }
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
        source.Append("public static partial class ").Append(symbol.Name).AppendLine("\n{"); return source;
    }
    private static bool Partial(INamedTypeSymbol symbol) => symbol.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
    private static string NamedString(AttributeData attribute, string name) => attribute.NamedArguments.FirstOrDefault(value => value.Key == name).Value.Value as string ?? string.Empty;
    private static int NamedInt(AttributeData attribute, string name, int fallback) => attribute.NamedArguments.FirstOrDefault(value => value.Key == name).Value.Value is int value ? value : fallback;
    private static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) && Encoding.UTF8.GetByteCount(value) <= 256 && value.All(character => !char.IsControl(character));
    private static string Type(ITypeSymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value ?? string.Empty, true);
    private static string Sanitize(INamedTypeSymbol symbol) => new(symbol.ToDisplayString().Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
    private sealed class PropertyBinding
    {
        internal PropertyBinding(ImmutableArray<string> path, string name, INamedTypeSymbol declaringType, ITypeSymbol propertyType, int confidentiality, int recordDisclosure, bool nullable)
        {
            Path = path; Name = name; DeclaringType = declaringType; PropertyType = propertyType; Confidentiality = confidentiality; RecordDisclosure = recordDisclosure; Nullable = nullable;
        }
        internal ImmutableArray<string> Path { get; }
        internal string Name { get; }
        internal INamedTypeSymbol DeclaringType { get; }
        internal ITypeSymbol PropertyType { get; }
        internal int Confidentiality { get; }
        internal int RecordDisclosure { get; }
        internal bool Nullable { get; }
    }
}
