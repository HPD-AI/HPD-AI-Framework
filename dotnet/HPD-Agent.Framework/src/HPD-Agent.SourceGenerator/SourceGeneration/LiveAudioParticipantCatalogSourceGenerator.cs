using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
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
    private const string AllocationAttribute = "HPD.Agent.Audio.HpdGraphParticipantAllocationAttribute";
    private const string ManifestAttribute = "HPD.Agent.Audio.HpdLiveAudioParticipantManifestAttribute";
    private const string FactoryInterface = "HPD.Agent.Audio.ILiveAudioParticipantFactoryV1";
    private const string CatalogBase = "HPD.Agent.Audio.LiveAudioParticipantFactoryCatalogV1";
    private const int MaximumGraphParticipantAllocationNodes = 64;
    private const int MaximumGraphParticipantAllocationTemplates = 14;
    private const int MaximumGraphParticipantAllocationNodeKeyUtf8Bytes = 64;
    private const int MaximumGraphParticipantAllocationCarrierBytes = 16384;
    private static readonly DiagnosticDescriptor Invalid = new("HPDA005", "Invalid live-Audio participant catalog",
        "Live-Audio participant catalog is invalid: {0}", "HPD.Audio", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor InvalidAllocation = new("HPDA007", "Invalid graph participant allocation",
        "Graph participant allocation is invalid: {0}", "HPD.Audio", DiagnosticSeverity.Error, true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var local = context.SyntaxProvider.ForAttributeWithMetadataName(DeclarationAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (value, _) => ReadLocal(value))
            .Where(static value => value is not null).Collect();
        var allocations = context.SyntaxProvider.ForAttributeWithMetadataName(AllocationAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (value, _) => value.TargetSymbol as INamedTypeSymbol)
            .Where(static value => value is not null).Collect();
        var manualCatalogs = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (value, _) => ManualCatalog(value))
            .Where(static value => value is not null).Collect();
        context.RegisterSourceOutput(local.Combine(allocations).Combine(context.CompilationProvider).Combine(manualCatalogs),
            static (production, pair) => Emit(production, pair.Left.Left.Left, pair.Left.Left.Right, pair.Left.Right, pair.Right));
    }

    private static Entry? ReadLocal(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type || context.Attributes.Length != 1) return null;
        var attribute = context.Attributes[0];
        if (attribute.ConstructorArguments.Length != 7) return null;
        var dependencies = attribute.NamedArguments.FirstOrDefault(static value => value.Key == "Dependencies").Value;
        var opaqueProviderKey = attribute.NamedArguments.FirstOrDefault(static value => value.Key == "OpaqueProviderKey").Value.Value as string ?? string.Empty;
        var opaqueOperationsValue = attribute.NamedArguments.FirstOrDefault(static value => value.Key == "OpaqueMaximumOutstandingOperations").Value.Value;
        var opaqueBytesValue = attribute.NamedArguments.FirstOrDefault(static value => value.Key == "OpaqueMaximumSubmittedBytes").Value.Value;
        var opaqueAgeValue = attribute.NamedArguments.FirstOrDefault(static value => value.Key == "OpaqueMaximumAgeNanoseconds").Value.Value;
        var opaqueControlValue = attribute.NamedArguments.FirstOrDefault(static value => value.Key == "OpaqueControl").Value.Value;
        var opaqueOperations = opaqueOperationsValue is null ? (ushort)0 : Convert.ToUInt16(opaqueOperationsValue, CultureInfo.InvariantCulture);
        var opaqueBytes = opaqueBytesValue is null ? 0UL : Convert.ToUInt64(opaqueBytesValue, CultureInfo.InvariantCulture);
        var opaqueAge = opaqueAgeValue is null ? 0L : Convert.ToInt64(opaqueAgeValue, CultureInfo.InvariantCulture);
        var opaqueControl = opaqueControlValue is null ? (byte)0 : Convert.ToByte(opaqueControlValue, CultureInfo.InvariantCulture);
        var opaqueProviderId = opaqueProviderKey.Length == 0 ? ImmutableArray<byte>.Empty : DeriveProviderId(opaqueProviderKey);
        var allocation = type.GetAttributes().Where(static value => value.AttributeClass?.ToDisplayString() == AllocationAttribute).ToArray();
        var allocationResult = TryBuildGraphParticipantAllocationCarrier(type, allocation, out var allocationBytes, out var allocationFingerprint);
        return new Entry(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), Identity(type),
            attribute.ConstructorArguments[0].Value as string ?? string.Empty,
            U16(attribute.ConstructorArguments[1]), U16(attribute.ConstructorArguments[2]),
            I64(attribute.ConstructorArguments[3]), I64(attribute.ConstructorArguments[4]), I64(attribute.ConstructorArguments[5]),
            attribute.ConstructorArguments[6].Values.Select(U16).ToImmutableArray(),
            dependencies.Kind == TypedConstantKind.Array
                ? dependencies.Values.Select(static value => value.Value as string ?? string.Empty).ToImmutableArray()
                : ImmutableArray<string>.Empty,
            type.AllInterfaces.Any(value => value.ToDisplayString() == FactoryInterface), IsApplicationVisible(type),
            IsConcreteClosed(type), type.Locations.FirstOrDefault(), allocationBytes, allocationFingerprint, allocationResult,
            opaqueProviderKey, opaqueProviderId, opaqueOperations, opaqueBytes, opaqueAge, opaqueControl);
    }

    private static INamedTypeSymbol? ManualCatalog(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax declaration ||
            context.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type) return null;
        // The framework owns a private explicit-catalog implementation nested
        // inside the abstract base. It is an internal construction detail, not
        // an application-provided catalog and must not trigger HPDA005.
        if (type.ContainingType?.ToDisplayString() == CatalogBase) return null;
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == CatalogBase) return type;
        return null;
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<Entry?> localValues,
        ImmutableArray<INamedTypeSymbol?> allocationSymbols, Compilation compilation,
        ImmutableArray<INamedTypeSymbol?> manualCatalogs)
    {
        foreach (var manual in manualCatalogs.Where(static value => value is not null))
            context.ReportDiagnostic(Diagnostic.Create(Invalid, manual!.Locations.FirstOrDefault(),
                $"{manual.ToDisplayString()} manually derives from the generated-only catalog base"));
        if (manualCatalogs.Any(static value => value is not null)) return;
        var local = localValues.Where(static value => value is not null).Cast<Entry>().ToArray();
        var factoryTypes = new HashSet<string>(local.Select(static value => value.TypeName), StringComparer.Ordinal);
        var orphan = allocationSymbols.Where(static value => value is not null)
            .FirstOrDefault(value => !factoryTypes.Contains(value!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        if (orphan is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidAllocation, orphan.Locations.FirstOrDefault(),
                "allocation must decorate one participant factory"));
            return;
        }
        var all = new List<Entry>(local);
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        foreach (var attribute in reference.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != ManifestAttribute || attribute.ConstructorArguments.Length is not (9 or 11 or 16) ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol type) continue;
            var allocationBytes = ImmutableArray<byte>.Empty; var allocationFingerprint = ImmutableArray<byte>.Empty;
            if (attribute.ConstructorArguments.Length >= 11)
            {
                allocationBytes = attribute.ConstructorArguments[9].Values.Select(static value => Convert.ToByte(value.Value, CultureInfo.InvariantCulture)).ToImmutableArray();
                allocationFingerprint = attribute.ConstructorArguments[10].Values.Select(static value => Convert.ToByte(value.Value, CultureInfo.InvariantCulture)).ToImmutableArray();
            }
            var opaqueProviderId = ImmutableArray<byte>.Empty; ushort opaqueOperations = 0; ulong opaqueBytes = 0; long opaqueAge = 0; byte opaqueControl = 0;
            if (attribute.ConstructorArguments.Length == 16)
            {
                opaqueProviderId = attribute.ConstructorArguments[11].Values.Select(static value => Convert.ToByte(value.Value, CultureInfo.InvariantCulture)).ToImmutableArray();
                opaqueOperations = U16(attribute.ConstructorArguments[12]);
                opaqueBytes = Convert.ToUInt64(attribute.ConstructorArguments[13].Value, CultureInfo.InvariantCulture);
                opaqueAge = I64(attribute.ConstructorArguments[14]);
                opaqueControl = Convert.ToByte(attribute.ConstructorArguments[15].Value, CultureInfo.InvariantCulture);
            }
            all.Add(new Entry(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), Identity(type),
                attribute.ConstructorArguments[1].Value as string ?? string.Empty, U16(attribute.ConstructorArguments[2]),
                U16(attribute.ConstructorArguments[3]), I64(attribute.ConstructorArguments[4]), I64(attribute.ConstructorArguments[5]),
                I64(attribute.ConstructorArguments[6]), attribute.ConstructorArguments[7].Values.Select(U16).ToImmutableArray(),
                attribute.ConstructorArguments[8].Values.Select(static value => value.Value as string ?? string.Empty).ToImmutableArray(), true,
                IsApplicationVisible(type), IsConcreteClosed(type), null, allocationBytes, allocationFingerprint, null,
                string.Empty, opaqueProviderId, opaqueOperations, opaqueBytes, opaqueAge, opaqueControl));
        }
        if (all.Count == 0) return;
        if (local.Any(static value => !TryBuildGraphParticipantAllocationCarrier(value)))
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidAllocation, null, "local allocation carrier failed final validation"));
            return;
        }
        all.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
        var error = Validate(all);
        if (error is not null) { context.ReportDiagnostic(Diagnostic.Create(error.Value.Allocation ? InvalidAllocation : Invalid, error.Value.Location, error.Value.Message)); return; }
        foreach (var entry in local)
            context.AddSource($"{Safe(entry.TypeName)}.LiveAudioParticipantManifest.g.cs", ManifestSource(entry));
        context.AddSource("GeneratedLiveAudioParticipantCatalogV1.g.cs", CatalogSource(all));
    }

    private static (Location? Location, string Message, bool Allocation)? Validate(IReadOnlyList<Entry> values)
    {
        if (values.Count > 64) return (null, "more than 64 declarations", false);
        var allocationCollision = values.GroupBy(static value => value.Key, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1 && group.Any(static value => !value.AllocationBytes.IsEmpty));
        if (allocationCollision is not null)
        {
            var distinct = allocationCollision.Select(static value => Convert.ToBase64String(value.AllocationBytes.ToArray()))
                .Distinct(StringComparer.Ordinal).Count();
            return (allocationCollision.First().Location,
                distinct == 1 ? $"duplicate allocation for '{allocationCollision.Key}'" : $"conflicting allocation or local shadow for '{allocationCollision.Key}'", true);
        }
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!value.ImplementsFactory) return (value.Location, $"{value.TypeName} does not implement {FactoryInterface}", false);
            if (!value.ApplicationVisible || !value.ConcreteClosed)
                return (value.Location, $"{value.TypeName} must be public, concrete, non-generic, and application-accessible", false);
            if (value.FactoryIdentity.Length is < 1 or > 512 ||
                value.FactoryIdentity.Any(static character => character is < (char)0x21 or > (char)0x7e))
                return (value.Location, $"{value.TypeName} produces an invalid canonical factory identity", false);
            if (!Ascii(value.Key) || !keys.Add(value.Key)) return (value.Location, $"invalid or duplicate key '{value.Key}'", false);
            if (value.Owner is < 2 or > 11 || value.Axis is < 2 or > 11 || value.Prepare <= 0 || value.Drain <= 0 || value.Terminate <= 0)
                return (value.Location, $"invalid owner, axis, or deadline for '{value.Key}'", false);
            if (value.Owner != value.Axis || value.Capacities.Length is < 1 or > 16 || value.Capacities.Any(static item => item is < 1 or > 14) ||
                value.Capacities.Distinct().Count() != value.Capacities.Length)
                return (value.Location, $"invalid generation fence or capacity set for '{value.Key}'", false);
            if (value.Dependencies.Length > 16 || value.Dependencies.Any(item => !Ascii(item) || item == value.Key) ||
                value.Dependencies.Distinct(StringComparer.Ordinal).Count() != value.Dependencies.Length)
                return (value.Location, $"invalid dependencies for '{value.Key}'", false);
            if (value.AllocationError is not null) return (value.Location, value.AllocationError, true);
            if (value.AllocationBytes.IsEmpty != value.AllocationFingerprint.IsEmpty)
                return (value.Location, $"allocation carrier is only partially present for '{value.Key}'", true);
            if (!value.AllocationBytes.IsEmpty && (value.Owner != 2 || value.Axis != 2 ||
                !TryValidateCarrier(value.AllocationBytes, value.AllocationFingerprint, value.Key, value.Capacities)))
                return (value.Location, $"allocation carrier does not authenticate '{value.Key}'", true);
            var noOpaque = value.OpaqueProviderId.IsEmpty && value.OpaqueOperations == 0 && value.OpaqueBytes == 0 && value.OpaqueAge == 0 && value.OpaqueControl == 0;
            if (!noOpaque && (value.OpaqueProviderId.Length != 16 || value.OpaqueProviderId.All(static item => item == 0) ||
                value.OpaqueOperations is 0 or > 64 || value.OpaqueBytes is 0 or > 4_194_304 ||
                value.OpaqueAge is <= 0 or > 10_000_000_000 || value.OpaqueControl is < 1 or > 2 ||
                value.OpaqueProviderKey.Length != 0 && !Ascii(value.OpaqueProviderKey)))
                return (value.Location, $"opaque residence qualification is partial or invalid for '{value.Key}'", false);
        }
        if (values.Count(static value => !value.AllocationBytes.IsEmpty) > 1) return (null, "more than one nonempty aggregate allocation", true);
        var unknown = values.SelectMany(static value => value.Dependencies).FirstOrDefault(value => !keys.Contains(value));
        if (unknown is not null) return (null, $"dependency '{unknown}' is absent from the application exact set", false);
        var remaining = values.ToDictionary(static value => value.Key,
            static value => new HashSet<string>(value.Dependencies, StringComparer.Ordinal), StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            var ready = remaining.Where(static value => value.Value.Count == 0).Select(static value => value.Key).ToArray();
            if (ready.Length == 0) return (null, "participant dependency graph contains a cycle", false);
            foreach (var key in ready) remaining.Remove(key);
            foreach (var dependencies in remaining.Values) foreach (var key in ready) dependencies.Remove(key);
        }
        return null;
    }

    private static string ManifestSource(Entry value) => !value.OpaqueProviderId.IsEmpty ? $$"""
        // <auto-generated/>
        #nullable enable
        [assembly: global::HPD.Agent.Audio.HpdLiveAudioParticipantManifestAttribute(typeof({{value.TypeName}}), {{Literal(value.Key)}}, {{value.Owner}}, {{value.Axis}}, {{value.Prepare}}L, {{value.Drain}}L, {{value.Terminate}}L, new ushort[] { {{Join(value.Capacities, static item => item.ToString(CultureInfo.InvariantCulture))}} }, new string[] { {{Join(value.Dependencies, Literal)}} }, new byte[] { {{Join(value.AllocationBytes, static item => item.ToString(CultureInfo.InvariantCulture))}} }, new byte[] { {{Join(value.AllocationFingerprint, static item => item.ToString(CultureInfo.InvariantCulture))}} }, new byte[] { {{Join(value.OpaqueProviderId, static item => item.ToString(CultureInfo.InvariantCulture))}} }, {{value.OpaqueOperations}}, {{value.OpaqueBytes}}UL, {{value.OpaqueAge}}L, {{value.OpaqueControl}})]
        """ : value.AllocationBytes.IsEmpty ? $$"""
        // <auto-generated/>
        #nullable enable
        [assembly: global::HPD.Agent.Audio.HpdLiveAudioParticipantManifestAttribute(typeof({{value.TypeName}}), {{Literal(value.Key)}}, {{value.Owner}}, {{value.Axis}}, {{value.Prepare}}L, {{value.Drain}}L, {{value.Terminate}}L, new ushort[] { {{Join(value.Capacities, static item => item.ToString(CultureInfo.InvariantCulture))}} }, new string[] { {{Join(value.Dependencies, Literal)}} })]
        """ : $$"""
        // <auto-generated/>
        #nullable enable
        [assembly: global::HPD.Agent.Audio.HpdLiveAudioParticipantManifestAttribute(typeof({{value.TypeName}}), {{Literal(value.Key)}}, {{value.Owner}}, {{value.Axis}}, {{value.Prepare}}L, {{value.Drain}}L, {{value.Terminate}}L, new ushort[] { {{Join(value.Capacities, static item => item.ToString(CultureInfo.InvariantCulture))}} }, new string[] { {{Join(value.Dependencies, Literal)}} }, new byte[] { {{Join(value.AllocationBytes, static item => item.ToString(CultureInfo.InvariantCulture))}} }, new byte[] { {{Join(value.AllocationFingerprint, static item => item.ToString(CultureInfo.InvariantCulture))}} })]
        """;

    private static string CatalogSource(IReadOnlyList<Entry> values)
    {
        var builder = new StringBuilder("// <auto-generated/>\n#nullable enable\nnamespace HPD.Agent.Audio.Generated;\n\n");
        builder.AppendLine("/// <summary>Provides the exact generated live-Audio participant catalog for this application compilation.</summary>");
        builder.AppendLine("public sealed class GeneratedLiveAudioParticipantCatalogV1 : global::HPD.Agent.Audio.LiveAudioParticipantFactoryCatalogV1\n{");
        builder.AppendLine("    private static readonly global::HPD.Agent.Audio.LiveAudioParticipantFactoryRegistrationV1[] Registrations =\n    [");
        foreach (var value in values)
        {
            builder.Append("        new(typeof(").Append(value.TypeName).Append("), ").Append(Literal(value.FactoryIdentity))
                .Append(", new global::HPD.Agent.Audio.LiveAudioParticipantDescriptorV1(new global::HPD.Agent.Authority.BoundedAscii(").Append(Literal(value.Key))
                .Append("), (global::HPD.Agent.Authority.OwnerSliceId)").Append(value.Owner).Append(", (global::HPD.Agent.Authority.AuthorityAxisId)").Append(value.Axis)
                .Append(", new global::HPD.Agent.Authority.BoundedAscii[] { ").Append(Join(value.Dependencies, static item => "new global::HPD.Agent.Authority.BoundedAscii(" + Literal(item) + ")"))
                .Append(" }, new global::HPD.Agent.Authority.CapacityDimensionId[] { ").Append(Join(value.Capacities, static item => "new global::HPD.Agent.Authority.CapacityDimensionId(" + item.ToString(CultureInfo.InvariantCulture) + ")"))
                .Append(" }, new global::HPD.Agent.Authority.DurationNs(").Append(value.Prepare).Append("L), new global::HPD.Agent.Authority.DurationNs(").Append(value.Drain)
                .Append("L), new global::HPD.Agent.Authority.DurationNs(").Append(value.Terminate).Append("L))");
            if (!value.AllocationBytes.IsEmpty)
                builder.Append(", new byte[] { ").Append(Join(value.AllocationBytes, static item => item.ToString(CultureInfo.InvariantCulture)))
                    .Append(" }, global::HPD.Agent.Authority.Hash256.FromBytes(new byte[] { ").Append(Join(value.AllocationFingerprint, static item => item.ToString(CultureInfo.InvariantCulture))).Append(" })");
            if (!value.OpaqueProviderId.IsEmpty)
            {
                if (value.AllocationBytes.IsEmpty) builder.Append(", global::System.ReadOnlyMemory<byte>.Empty, null");
                builder.Append(", new global::HPD.Agent.Audio.LiveAudioOpaqueResidenceQualificationV1(global::HPD.Agent.Authority.ProviderId.FromValue(global::HPD.Agent.Authority.StableId128.FromBytes(new byte[] { ")
                    .Append(Join(value.OpaqueProviderId, static item => item.ToString(CultureInfo.InvariantCulture))).Append(" })), ")
                    .Append(value.OpaqueOperations).Append(", ").Append(value.OpaqueBytes).Append("UL, new global::HPD.Agent.Authority.DurationNs(")
                    .Append(value.OpaqueAge).Append("L), (global::HPD.Agent.Audio.LiveAudioOpaqueResidenceControlV1)").Append(value.OpaqueControl).Append(")");
            }
            builder.AppendLine("),");
        }
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
    private static ImmutableArray<byte> DeriveProviderId(string providerKey)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashPart(hash, "hpd.provider-id.v1"); AppendHashPart(hash, providerKey);
        var digest = hash.GetHashAndReset(); if (digest.Take(16).All(static value => value == 0)) digest[15] = 1;
        return digest.Take(16).ToImmutableArray();
    }
    private static void AppendHashPart(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length); hash.AppendData(length.ToArray()); hash.AppendData(bytes);
    }
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
    private static string? TryBuildGraphParticipantAllocationCarrier(INamedTypeSymbol type,
        IReadOnlyList<AttributeData> attributes, out ImmutableArray<byte> carrier, out ImmutableArray<byte> fingerprint)
    {
        carrier=ImmutableArray<byte>.Empty; fingerprint=ImmutableArray<byte>.Empty;
        if (attributes.Count==0) return null;
        if (attributes.Count!=1) return "more than one allocation attribute";
        var factory=type.GetAttributes().SingleOrDefault(static value => value.AttributeClass?.ToDisplayString()==DeclarationAttribute);
        if (factory is null || attributes[0].ConstructorArguments.Length!=5) return "allocation must decorate one participant factory";
        var nodes=Strings(attributes[0].ConstructorArguments[0]); var dimensions=UShorts(attributes[0].ConstructorArguments[1]);
        var purposes=Strings(attributes[0].ConstructorArguments[2]); var amounts=ULongs(attributes[0].ConstructorArguments[3]); var policies=Bytes(attributes[0].ConstructorArguments[4]);
        var capacities=factory.ConstructorArguments[6].Values.Select(U16).ToImmutableArray();
        var key=factory.ConstructorArguments[0].Value as string ?? string.Empty; var owner=U16(factory.ConstructorArguments[1]); var axis=U16(factory.ConstructorArguments[2]);
        if (owner!=2 || axis!=2) return "allocation factory must have OwnerSliceId.S2 and AuthorityAxisId.Graph";
        if (nodes.Length is <1 or >MaximumGraphParticipantAllocationNodes || nodes.Distinct(StringComparer.Ordinal).Count()!=nodes.Length || nodes.Any(static value=>!Printable(value,MaximumGraphParticipantAllocationNodeKeyUtf8Bytes))) return "allocation node set is invalid";
        if (dimensions.Length is <1 or >MaximumGraphParticipantAllocationTemplates || purposes.Length!=dimensions.Length || amounts.Length!=dimensions.Length || policies.Length!=dimensions.Length) return "allocation template arrays are invalid";
        var tuples=dimensions.Select((dimension,index)=>(Dimension:dimension,Purpose:purposes[index],Amount:amounts[index],Policy:policies[index])).OrderBy(static value=>value.Dimension).ToArray();
        if (tuples.Select(static value=>value.Dimension).Distinct().Count()!=tuples.Length || !tuples.Select(static value=>value.Dimension).SequenceEqual(capacities.OrderBy(static value=>value)) || tuples.Any(static value=>value.Dimension is <1 or >14 || value.Amount is 0 or >Int64.MaxValue || value.Policy is <1 or >2 || !Purpose(value.Purpose,out _))) return "allocation charge templates are invalid";
        var output=new List<byte>(); Map(output,4); UInt(output,0); UInt(output,1); UInt(output,1); Text(output,key); UInt(output,2); Array(output,nodes.Length); foreach(var node in nodes)Text(output,node); UInt(output,3); Array(output,tuples.Length);
        foreach(var tuple in tuples){Purpose(tuple.Purpose,out var purposeIdBytes); Map(output,4); UInt(output,0);UInt(output,tuple.Dimension);UInt(output,1);ByteString(output,purposeIdBytes);UInt(output,2);UInt(output,tuple.Amount);UInt(output,3);UInt(output,tuple.Policy);}
        if(output.Count>MaximumGraphParticipantAllocationCarrierBytes)return "allocation carrier exceeds 16384 bytes";
        carrier=output.ToImmutableArray(); using var sha=SHA256.Create(); fingerprint=sha.ComputeHash(Encoding.UTF8.GetBytes("hpd-graph-participant-allocation-declaration-v1\0").Concat(output).ToArray()).ToImmutableArray(); return null;
    }
    private static bool TryBuildGraphParticipantAllocationCarrier(Entry value) => value.AllocationError is null &&
        (value.AllocationBytes.IsEmpty || TryValidateCarrier(value.AllocationBytes, value.AllocationFingerprint, value.Key, value.Capacities));
    private static bool TryValidateCarrier(ImmutableArray<byte> carrier,ImmutableArray<byte> fingerprint,string key,ImmutableArray<ushort> capacities)
    {
        if(carrier.Length is 0 or >MaximumGraphParticipantAllocationCarrierBytes || fingerprint.Length!=32)return false;
        try
        {
            var cursor=0; if(ReadHead(carrier,ref cursor,5)!=4 || UIntRead(carrier,ref cursor)!=0 || UIntRead(carrier,ref cursor)!=1 || UIntRead(carrier,ref cursor)!=1 || TextRead(carrier,ref cursor)!=key || UIntRead(carrier,ref cursor)!=2)return false;
            var nodeCount=checked((int)ReadHead(carrier,ref cursor,4)); if(nodeCount is <1 or >MaximumGraphParticipantAllocationNodes)return false;var nodes=new List<string>(nodeCount);for(var i=0;i<nodeCount;i++){var node=TextRead(carrier,ref cursor);if(!Printable(node,MaximumGraphParticipantAllocationNodeKeyUtf8Bytes))return false;nodes.Add(node);}if(nodes.Distinct(StringComparer.Ordinal).Count()!=nodes.Count)return false;
            if(UIntRead(carrier,ref cursor)!=3)return false;var count=checked((int)ReadHead(carrier,ref cursor,4));if(count is <1 or >MaximumGraphParticipantAllocationTemplates)return false;var dimensions=new List<ushort>(count);var purposes=new List<string>(count);var amounts=new List<ulong>(count);var policies=new List<byte>(count);
            for(var i=0;i<count;i++){if(ReadHead(carrier,ref cursor,5)!=4 || UIntRead(carrier,ref cursor)!=0)return false;dimensions.Add(checked((ushort)UIntRead(carrier,ref cursor)));if(UIntRead(carrier,ref cursor)!=1)return false;var purpose=ByteStringRead(carrier,ref cursor);if(purpose.Length!=16 || !purpose.Any(static value=>value!=0))return false;purposes.Add(string.Concat(purpose.Select(static value=>value.ToString("x2",CultureInfo.InvariantCulture))));if(UIntRead(carrier,ref cursor)!=2)return false;var amount=UIntRead(carrier,ref cursor);amounts.Add(amount);if(UIntRead(carrier,ref cursor)!=3)return false;var policy=UIntRead(carrier,ref cursor);policies.Add(checked((byte)policy));if(amount is 0 or >Int64.MaxValue || policy is <1 or >2)return false;}
            if(cursor!=carrier.Length || !dimensions.SequenceEqual(capacities.OrderBy(static value=>value)))return false;var rebuilt=BuildCarrier(key,nodes,dimensions,purposes,amounts,policies);if(!carrier.SequenceEqual(rebuilt))return false;using var sha=SHA256.Create();return sha.ComputeHash(Encoding.UTF8.GetBytes("hpd-graph-participant-allocation-declaration-v1\0").Concat(carrier).ToArray()).SequenceEqual(fingerprint);
        }catch{return false;}
    }
    private static ImmutableArray<byte> BuildCarrier(string key,IReadOnlyList<string> nodes,IReadOnlyList<ushort> dimensions,IReadOnlyList<string> purposes,IReadOnlyList<ulong> amounts,IReadOnlyList<byte> policies){var output=new List<byte>();Map(output,4);UInt(output,0);UInt(output,1);UInt(output,1);Text(output,key);UInt(output,2);Array(output,nodes.Count);foreach(var node in nodes)Text(output,node);UInt(output,3);Array(output,dimensions.Count);for(var i=0;i<dimensions.Count;i++){Purpose(purposes[i],out var purposeIdBytes);Map(output,4);UInt(output,0);UInt(output,dimensions[i]);UInt(output,1);ByteString(output,purposeIdBytes);UInt(output,2);UInt(output,amounts[i]);UInt(output,3);UInt(output,policies[i]);}return output.ToImmutableArray();}
    private static ImmutableArray<string> Strings(TypedConstant value)=>value.Kind==TypedConstantKind.Array?value.Values.Select(static item=>item.Value as string??string.Empty).ToImmutableArray():ImmutableArray<string>.Empty;
    private static ImmutableArray<ushort> UShorts(TypedConstant value)=>value.Kind==TypedConstantKind.Array?value.Values.Select(U16).ToImmutableArray():ImmutableArray<ushort>.Empty;
    private static ImmutableArray<ulong> ULongs(TypedConstant value)=>value.Kind==TypedConstantKind.Array?value.Values.Select(static item=>Convert.ToUInt64(item.Value,CultureInfo.InvariantCulture)).ToImmutableArray():ImmutableArray<ulong>.Empty;
    private static ImmutableArray<byte> Bytes(TypedConstant value)=>value.Kind==TypedConstantKind.Array?value.Values.Select(static item=>Convert.ToByte(item.Value,CultureInfo.InvariantCulture)).ToImmutableArray():ImmutableArray<byte>.Empty;
    private static bool Printable(string value,int maximum)=>Encoding.UTF8.GetByteCount(value) is >=1 and <=64 && Encoding.UTF8.GetByteCount(value)<=maximum && value.All(static c=>c is >=(char)0x21 and <=(char)0x7e);
    private static bool Purpose(string value,out byte[] purposeIdBytes){purposeIdBytes=System.Array.Empty<byte>();if(value.Length!=32 || value.Any(static c=>c is not(>='0' and<='9') and not(>='a' and<='f')))return false;purposeIdBytes=new byte[16];for(var i=0;i<16;i++)purposeIdBytes[i]=(byte)((Hex(value[i*2])<<4)|Hex(value[i*2+1]));return purposeIdBytes.Any(static value=>value!=0);}
    private static int Hex(char c)=>c<='9'?c-'0':c-'a'+10;
    private static void Map(List<byte> b,int n)=>Head(b,5,(ulong)n); private static void Array(List<byte>b,int n)=>Head(b,4,(ulong)n); private static void UInt(List<byte>b,ulong n)=>Head(b,0,n);
    private static void Text(List<byte>b,string s){var x=Encoding.UTF8.GetBytes(s);Head(b,3,(ulong)x.Length);b.AddRange(x);} private static void ByteString(List<byte>b,byte[]x){Head(b,2,(ulong)x.Length);b.AddRange(x);}
    private static void Head(List<byte>b,int major,ulong n){if(n<24)b.Add((byte)((major<<5)|(int)n));else if(n<=byte.MaxValue){b.Add((byte)((major<<5)|24));b.Add((byte)n);}else if(n<=ushort.MaxValue){b.Add((byte)((major<<5)|25));b.Add((byte)(n>>8));b.Add((byte)n);}else if(n<=uint.MaxValue){b.Add((byte)((major<<5)|26));for(var s=24;s>=0;s-=8)b.Add((byte)(n>>s));}else{b.Add((byte)((major<<5)|27));for(var s=56;s>=0;s-=8)b.Add((byte)(n>>s));}}
    private static ulong ReadHead(ImmutableArray<byte>b,ref int p,int major){var x=b[p++];if((x>>5)!=major)throw new FormatException();var a=x&31;if(a<24)return(ulong)a;var count=a switch{24=>1,25=>2,26=>4,27=>8,_=>throw new FormatException()};ulong n=0;for(var i=0;i<count;i++)n=(n<<8)|b[p++];if((a==24&&n<24)||(a==25&&n<=byte.MaxValue)||(a==26&&n<=ushort.MaxValue)||(a==27&&n<=uint.MaxValue))throw new FormatException();return n;}
    private static ulong UIntRead(ImmutableArray<byte>b,ref int p)=>ReadHead(b,ref p,0); private static string TextRead(ImmutableArray<byte>b,ref int p){var n=checked((int)ReadHead(b,ref p,3));var s=Encoding.UTF8.GetString(b.Skip(p).Take(n).ToArray());p+=n;return s;} private static byte[] ByteStringRead(ImmutableArray<byte>b,ref int p){var n=checked((int)ReadHead(b,ref p,2));var x=b.Skip(p).Take(n).ToArray();p+=n;return x;}
    private sealed record Entry(string TypeName, string FactoryIdentity, string Key, ushort Owner, ushort Axis, long Prepare,
        long Drain, long Terminate, ImmutableArray<ushort> Capacities, ImmutableArray<string> Dependencies,
        bool ImplementsFactory, bool ApplicationVisible, bool ConcreteClosed, Location? Location,
        ImmutableArray<byte> AllocationBytes, ImmutableArray<byte> AllocationFingerprint, string? AllocationError,
        string OpaqueProviderKey, ImmutableArray<byte> OpaqueProviderId, ushort OpaqueOperations,
        ulong OpaqueBytes, long OpaqueAge, byte OpaqueControl);
}
