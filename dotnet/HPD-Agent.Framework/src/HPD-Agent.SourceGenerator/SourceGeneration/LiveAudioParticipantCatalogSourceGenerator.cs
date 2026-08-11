using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HPD.Agent.SourceGenerator.SourceGeneration;

/// <summary>Generates application-scoped live-Audio participant manifests and exact catalogs.</summary>
[Generator]
public sealed class LiveAudioParticipantCatalogSourceGenerator : IIncrementalGenerator
{
    private const string DeclarationAttribute = "HPD.Agent.Audio.HpdLiveAudioParticipantFactoryAttribute";
    private const string ManifestAttribute = "HPD.Agent.Audio.HpdLiveAudioParticipantManifestAttribute";
    private const string FactoryInterface = "HPD.Agent.Audio.ILiveAudioParticipantFactoryV1";
    private const string CatalogBase = "HPD.Agent.Audio.LiveAudioParticipantFactoryCatalogV1";
    private static readonly DiagnosticDescriptor Invalid = new("HPDA005", "Invalid live-Audio participant catalog",
        "Live-Audio participant catalog is invalid: {0}", "HPD.Audio", DiagnosticSeverity.Error, true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var local = context.SyntaxProvider.ForAttributeWithMetadataName(DeclarationAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (value, _) => ReadLocal(value))
            .Where(static value => value is not null).Collect();
        var manualCatalogs = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (value, _) => ManualCatalog(value))
            .Where(static value => value is not null).Collect();
        context.RegisterSourceOutput(local.Combine(context.CompilationProvider).Combine(manualCatalogs),
            static (production, pair) => Emit(production, pair.Left.Left, pair.Left.Right, pair.Right));
    }

    private static Entry? ReadLocal(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type || context.Attributes.Length != 1) return null;
        var attribute = context.Attributes[0];
        if (attribute.ConstructorArguments.Length != 7) return null;
        var dependencies = attribute.NamedArguments.FirstOrDefault(static value => value.Key == "Dependencies").Value;
        return new Entry(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), Identity(type),
            attribute.ConstructorArguments[0].Value as string ?? string.Empty,
            U16(attribute.ConstructorArguments[1]), U16(attribute.ConstructorArguments[2]),
            I64(attribute.ConstructorArguments[3]), I64(attribute.ConstructorArguments[4]), I64(attribute.ConstructorArguments[5]),
            attribute.ConstructorArguments[6].Values.Select(U16).ToImmutableArray(),
            dependencies.Kind == TypedConstantKind.Array
                ? dependencies.Values.Select(static value => value.Value as string ?? string.Empty).ToImmutableArray()
                : ImmutableArray<string>.Empty,
            type.AllInterfaces.Any(value => value.ToDisplayString() == FactoryInterface), IsApplicationVisible(type),
            IsConcreteClosed(type), type.Locations.FirstOrDefault());
    }

    private static INamedTypeSymbol? ManualCatalog(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax declaration ||
            context.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type) return null;
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == CatalogBase) return type;
        return null;
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<Entry?> localValues, Compilation compilation,
        ImmutableArray<INamedTypeSymbol?> manualCatalogs)
    {
        foreach (var manual in manualCatalogs.Where(static value => value is not null))
            context.ReportDiagnostic(Diagnostic.Create(Invalid, manual!.Locations.FirstOrDefault(),
                $"{manual.ToDisplayString()} manually derives from the generated-only catalog base"));
        if (manualCatalogs.Any(static value => value is not null)) return;
        var local = localValues.Where(static value => value is not null).Cast<Entry>().ToArray();
        foreach (var entry in local)
            context.AddSource($"{Safe(entry.TypeName)}.LiveAudioParticipantManifest.g.cs", ManifestSource(entry));

        var all = new List<Entry>(local);
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        foreach (var attribute in reference.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != ManifestAttribute || attribute.ConstructorArguments.Length != 9 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol type) continue;
            all.Add(new Entry(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), Identity(type),
                attribute.ConstructorArguments[1].Value as string ?? string.Empty, U16(attribute.ConstructorArguments[2]),
                U16(attribute.ConstructorArguments[3]), I64(attribute.ConstructorArguments[4]), I64(attribute.ConstructorArguments[5]),
                I64(attribute.ConstructorArguments[6]), attribute.ConstructorArguments[7].Values.Select(U16).ToImmutableArray(),
                attribute.ConstructorArguments[8].Values.Select(static value => value.Value as string ?? string.Empty).ToImmutableArray(), true,
                IsApplicationVisible(type), IsConcreteClosed(type), null));
        }
        if (all.Count == 0) return;
        all.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
        var error = Validate(all);
        if (error is not null) { context.ReportDiagnostic(Diagnostic.Create(Invalid, error.Value.Location, error.Value.Message)); return; }
        context.AddSource("GeneratedLiveAudioParticipantCatalogV1.g.cs", CatalogSource(all));
    }

    private static (Location? Location, string Message)? Validate(IReadOnlyList<Entry> values)
    {
        if (values.Count > 64) return (null, "more than 64 declarations");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!value.ImplementsFactory) return (value.Location, $"{value.TypeName} does not implement {FactoryInterface}");
            if (!value.ApplicationVisible || !value.ConcreteClosed)
                return (value.Location, $"{value.TypeName} must be public, concrete, non-generic, and application-accessible");
            if (value.FactoryIdentity.Length is < 1 or > 512 ||
                value.FactoryIdentity.Any(static character => character is < (char)0x21 or > (char)0x7e))
                return (value.Location, $"{value.TypeName} produces an invalid canonical factory identity");
            if (!Ascii(value.Key) || !keys.Add(value.Key)) return (value.Location, $"invalid or duplicate key '{value.Key}'");
            if (value.Owner is < 2 or > 11 || value.Axis is < 2 or > 11 || value.Prepare <= 0 || value.Drain <= 0 || value.Terminate <= 0)
                return (value.Location, $"invalid owner, axis, or deadline for '{value.Key}'");
            if (value.Owner != value.Axis || value.Capacities.Length is < 1 or > 16 || value.Capacities.Any(static item => item is < 1 or > 14) ||
                value.Capacities.Distinct().Count() != value.Capacities.Length)
                return (value.Location, $"invalid generation fence or capacity set for '{value.Key}'");
            if (value.Dependencies.Length > 16 || value.Dependencies.Any(item => !Ascii(item) || item == value.Key) ||
                value.Dependencies.Distinct(StringComparer.Ordinal).Count() != value.Dependencies.Length)
                return (value.Location, $"invalid dependencies for '{value.Key}'");
        }
        var unknown = values.SelectMany(static value => value.Dependencies).FirstOrDefault(value => !keys.Contains(value));
        if (unknown is not null) return (null, $"dependency '{unknown}' is absent from the application exact set");
        var remaining = values.ToDictionary(static value => value.Key,
            static value => new HashSet<string>(value.Dependencies, StringComparer.Ordinal), StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            var ready = remaining.Where(static value => value.Value.Count == 0).Select(static value => value.Key).ToArray();
            if (ready.Length == 0) return (null, "participant dependency graph contains a cycle");
            foreach (var key in ready) remaining.Remove(key);
            foreach (var dependencies in remaining.Values) foreach (var key in ready) dependencies.Remove(key);
        }
        return null;
    }

    private static string ManifestSource(Entry value) => $$"""
        // <auto-generated/>
        #nullable enable
        [assembly: global::HPD.Agent.Audio.HpdLiveAudioParticipantManifestAttribute(typeof({{value.TypeName}}), {{Literal(value.Key)}}, {{value.Owner}}, {{value.Axis}}, {{value.Prepare}}L, {{value.Drain}}L, {{value.Terminate}}L, new ushort[] { {{Join(value.Capacities, static item => item.ToString(CultureInfo.InvariantCulture))}} }, new string[] { {{Join(value.Dependencies, Literal)}} })]
        """;

    private static string CatalogSource(IReadOnlyList<Entry> values)
    {
        var builder = new StringBuilder("// <auto-generated/>\n#nullable enable\nnamespace HPD.Agent.Audio.Generated;\n\n");
        builder.AppendLine("/// <summary>Provides the exact generated live-Audio participant catalog for this application compilation.</summary>");
        builder.AppendLine("public sealed class GeneratedLiveAudioParticipantCatalogV1 : global::HPD.Agent.Audio.LiveAudioParticipantFactoryCatalogV1\n{");
        builder.AppendLine("    private static readonly global::HPD.Agent.Audio.LiveAudioParticipantFactoryRegistrationV1[] Registrations =\n    [");
        foreach (var value in values)
            builder.Append("        new(typeof(").Append(value.TypeName).Append("), ").Append(Literal(value.FactoryIdentity))
                .Append(", new global::HPD.Agent.Audio.LiveAudioParticipantDescriptorV1(new global::HPD.Agent.Authority.BoundedAscii(").Append(Literal(value.Key))
                .Append("), (global::HPD.Agent.Authority.OwnerSliceId)").Append(value.Owner).Append(", (global::HPD.Agent.Authority.AuthorityAxisId)").Append(value.Axis)
                .Append(", new global::HPD.Agent.Authority.BoundedAscii[] { ").Append(Join(value.Dependencies, static item => "new global::HPD.Agent.Authority.BoundedAscii(" + Literal(item) + ")"))
                .Append(" }, new global::HPD.Agent.Authority.CapacityDimensionId[] { ").Append(Join(value.Capacities, static item => "new global::HPD.Agent.Authority.CapacityDimensionId(" + item.ToString(CultureInfo.InvariantCulture) + ")"))
                .Append(" }, new global::HPD.Agent.Authority.DurationNs(").Append(value.Prepare).Append("L), new global::HPD.Agent.Authority.DurationNs(").Append(value.Drain)
                .Append("L), new global::HPD.Agent.Authority.DurationNs(").Append(value.Terminate).AppendLine("L))),");
        builder.AppendLine("    ];");
        builder.AppendLine("    private GeneratedLiveAudioParticipantCatalogV1(global::System.Collections.Generic.IEnumerable<global::HPD.Agent.Audio.ILiveAudioParticipantFactoryV1> factories) : base(Registrations, factories) { }");
        builder.AppendLine("    /// <summary>Creates the catalog from explicitly constructed instances after exact-set validation.</summary>");
        builder.AppendLine("    /// <param name=\"factories\">The explicit factory instances.</param>");
        builder.AppendLine("    /// <returns>The immutable validated application catalog.</returns>");
        builder.AppendLine("    public static GeneratedLiveAudioParticipantCatalogV1 Create(global::System.Collections.Generic.IEnumerable<global::HPD.Agent.Audio.ILiveAudioParticipantFactoryV1> factories) => new(factories);");
        builder.AppendLine("}"); return builder.ToString();
    }

    private static ushort U16(TypedConstant value) => Convert.ToUInt16(value.Value, CultureInfo.InvariantCulture);
    private static long I64(TypedConstant value) => Convert.ToInt64(value.Value, CultureInfo.InvariantCulture);
    private static bool Ascii(string value) => value.Length is > 0 and <= 64 && value.All(static c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.');
    private static string Safe(string value)
    {
        uint hash = 2166136261;
        foreach (var character in value) { hash ^= character; hash *= 16777619; }
        return new string(value.Where(char.IsLetterOrDigit).ToArray()) + "." + hash.ToString("x8", CultureInfo.InvariantCulture);
    }
    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);
    private static string Join<T>(IEnumerable<T> values, Func<T, string> format) => string.Join(", ", values.Select(format));
    private static string Identity(INamedTypeSymbol type) => type.ContainingAssembly.Name + ":" +
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    private static bool IsApplicationVisible(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
            if (current.DeclaredAccessibility != Accessibility.Public) return false;
        return true;
    }
    private static bool IsConcreteClosed(INamedTypeSymbol type)
    {
        if (type.IsAbstract) return false;
        for (var current = type; current is not null; current = current.ContainingType)
            if (current.Arity != 0) return false;
        return true;
    }
    private sealed record Entry(string TypeName, string FactoryIdentity, string Key, ushort Owner, ushort Axis, long Prepare,
        long Drain, long Terminate, ImmutableArray<ushort> Capacities, ImmutableArray<string> Dependencies,
        bool ImplementsFactory, bool ApplicationVisible, bool ConcreteClosed, Location? Location);
}
