using HPD.Agent.SourceGenerator.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class LiveAudioParticipantCatalogSourceGeneratorTests
{
    [Fact]
    public void Opaque_qualification_is_derived_emitted_and_compiles_as_one_complete_value()
    {
        var source = Preamble + """
            [HpdLiveAudioParticipantFactory("provider", OwnerSliceId.S5, AuthorityAxisId.Provider, 10, 20, 30, 2,
                OpaqueProviderKey = "openai", OpaqueMaximumOutstandingOperations = 8,
                OpaqueMaximumSubmittedBytes = 4096, OpaqueMaximumAgeNanoseconds = 1000000,
                OpaqueControl = LiveAudioOpaqueResidenceControlV1.ObservationOnly)]
            public sealed class Provider : ILiveAudioParticipantFactoryV1 { }
            """;
        var (result, compilation) = Run(source);
        Assert.DoesNotContain(result.Diagnostics, static value => value.Severity == DiagnosticSeverity.Error);
        var catalog = CatalogText(result); var manifest = Assert.Single(result.GeneratedTrees,
            static value => value.FilePath.Contains("LiveAudioParticipantManifest.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("LiveAudioOpaqueResidenceQualificationV1", catalog, StringComparison.Ordinal);
        Assert.Contains("4096UL", catalog, StringComparison.Ordinal);
        Assert.Contains("1000000L", catalog, StringComparison.Ordinal);
        Assert.Contains("new byte[]", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain(compilation.GetDiagnostics(), static value => value.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Referenced_opaque_qualification_is_retained_in_the_application_exact_set()
    {
        var leaf = CompileLeafSource(Preamble + """
            [HpdLiveAudioParticipantFactory("provider", OwnerSliceId.S5, AuthorityAxisId.Provider, 10,20,30,2,
                OpaqueProviderKey="openai", OpaqueMaximumOutstandingOperations=8,
                OpaqueMaximumSubmittedBytes=4096, OpaqueMaximumAgeNanoseconds=1000000,
                OpaqueControl=LiveAudioOpaqueResidenceControlV1.ObservationOnly)]
            public sealed class Provider : ILiveAudioParticipantFactoryV1 { }
            """);
        var (result, compilation) = Run(Preamble, [leaf]);
        Assert.DoesNotContain(result.Diagnostics, static value => value.Severity == DiagnosticSeverity.Error);
        Assert.Contains("LiveAudioOpaqueResidenceQualificationV1", CatalogText(result), StringComparison.Ordinal);
        Assert.DoesNotContain(compilation.GetDiagnostics(), static value => value.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("OpaqueProviderKey = \"openai\"")]
    [InlineData("OpaqueMaximumOutstandingOperations = 1")]
    [InlineData("OpaqueProviderKey = \"openai\", OpaqueMaximumOutstandingOperations = 65, OpaqueMaximumSubmittedBytes = 1, OpaqueMaximumAgeNanoseconds = 1, OpaqueControl = LiveAudioOpaqueResidenceControlV1.ObservationOnly")]
    public void Partial_or_unbounded_opaque_qualification_is_HPDA005(string values)
    {
        AssertInvalid(Preamble + $"[HpdLiveAudioParticipantFactory(\"provider\", OwnerSliceId.S5, AuthorityAxisId.Provider, 10,20,30,2, {values})] public sealed class Provider : ILiveAudioParticipantFactoryV1 {{ }}");
    }

    [Fact]
    public void Aggregate_allocation_emits_authenticated_carrier_and_sorted_dimensions()
    {
        var source = Preamble + """
            [HpdLiveAudioParticipantFactory("graph", OwnerSliceId.S2, AuthorityAxisId.Graph, 10, 20, 30, 2, 1)]
            [HpdGraphParticipantAllocation(new string[] { "node-b", "node-a" }, new ushort[] { 2, 1 }, new string[] { "22222222222222222222222222222222", "11111111111111111111111111111111" }, new ulong[] { 9, 7 }, new byte[] { 2, 1 })]
            public sealed class Media : ILiveAudioParticipantFactoryV1 { }
            """;
        var (result, compilation) = Run(source);
        Assert.DoesNotContain(result.Diagnostics, static value => value.Severity == DiagnosticSeverity.Error);
        var catalog = CatalogText(result); Assert.Contains("Hash256.FromBytes", catalog, StringComparison.Ordinal);
        Assert.Contains("new byte[]", catalog, StringComparison.Ordinal);
        var manifest = Assert.Single(result.GeneratedTrees, static value => value.FilePath.Contains("LiveAudioParticipantManifest.g.cs", StringComparison.Ordinal)).GetText().ToString();
        var expectedCarrier = Convert.FromHexString("a40001016567726170680282666e6f64652d62666e6f64652d610382a4000101501111111111111111111111111111111102070301a4000201502222222222222222222222222222222202090302");
        var expectedFingerprint = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("hpd-graph-participant-allocation-declaration-v1\0").Concat(expectedCarrier).ToArray());
        var carrierLiteral = string.Join(", ", expectedCarrier); var fingerprintLiteral = string.Join(", ", expectedFingerprint);
        Assert.Contains(carrierLiteral, catalog, StringComparison.Ordinal); Assert.Contains(carrierLiteral, manifest, StringComparison.Ordinal);
        Assert.Contains(fingerprintLiteral, catalog, StringComparison.Ordinal); Assert.Contains(fingerprintLiteral, manifest, StringComparison.Ordinal);
        Assert.DoesNotContain(compilation.GetDiagnostics(), static value => value.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("OwnerSliceId.S5, AuthorityAxisId.Provider", "new string[] { \"node\" }", "new ushort[] { 1 }", "new string[] { \"11111111111111111111111111111111\" }", "new ulong[] { 1 }", "new byte[] { 1 }")]
    [InlineData("OwnerSliceId.S2, AuthorityAxisId.Graph", "new string[65]", "new ushort[] { 1 }", "new string[] { \"11111111111111111111111111111111\" }", "new ulong[] { 1 }", "new byte[] { 1 }")]
    [InlineData("OwnerSliceId.S2, AuthorityAxisId.Graph", "new string[] { \"node\" }", "new ushort[] { 1 }", "new string[] { \"00000000000000000000000000000000\" }", "new ulong[] { 1 }", "new byte[] { 1 }")]
    [InlineData("OwnerSliceId.S2, AuthorityAxisId.Graph", "new string[] { \"node\" }", "new ushort[] { 1 }", "new string[] { \"11111111111111111111111111111111\" }", "new ulong[] { 0 }", "new byte[] { 1 }")]
    [InlineData("OwnerSliceId.S2, AuthorityAxisId.Graph", "new string[] { \"node\" }", "new ushort[] { 1 }", "new string[] { \"11111111111111111111111111111111\" }", "new ulong[] { 1 }", "new byte[] { 3 }")]
    public void Allocation_mutations_emit_HPDA007_and_no_sources(string ownerAxis, string nodes, string dimensions, string purposes, string amounts, string policies)
    {
        var source = Preamble + $"[HpdLiveAudioParticipantFactory(\"graph\", {ownerAxis}, 10, 20, 30, 1)] [HpdGraphParticipantAllocation({nodes}, {dimensions}, {purposes}, {amounts}, {policies})] public sealed class Media : ILiveAudioParticipantFactoryV1 {{ }}";
        var (result, _) = Run(source); Assert.Contains(result.Diagnostics, static value => value.Id == "HPDA007"); Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Two_nonempty_allocations_emit_HPDA007_and_no_sources()
    {
        var source = Preamble + "[HpdLiveAudioParticipantFactory(\"a\", OwnerSliceId.S2, AuthorityAxisId.Graph, 10,20,30,1)] [HpdGraphParticipantAllocation(new string[] { \"a-node\" }, new ushort[] {1}, new string[] {\"11111111111111111111111111111111\"}, new ulong[] {1}, new byte[] {1})] public sealed class Media : ILiveAudioParticipantFactoryV1 { } [HpdLiveAudioParticipantFactory(\"b\", OwnerSliceId.S2, AuthorityAxisId.Graph, 10,20,30,1)] [HpdGraphParticipantAllocation(new string[] { \"b-node\" }, new ushort[] {1}, new string[] {\"11111111111111111111111111111111\"}, new ulong[] {1}, new byte[] {1})] public sealed class Provider : ILiveAudioParticipantFactoryV1 { }";
        var (result, _) = Run(source); Assert.Contains(result.Diagnostics, static value => value.Id == "HPDA007"); Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Orphan_allocation_is_diagnosed_exactly_once_and_emits_nothing()
    {
        var (result, _) = Run(Preamble + "[HpdGraphParticipantAllocation(new string[] { \"node\" }, new ushort[] {1}, new string[] {\"11111111111111111111111111111111\"}, new ulong[] {1}, new byte[] {1})] public sealed class Media { }");
        Assert.Single(result.Diagnostics, static value => value.Id == "HPDA007"); Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Referenced_allocation_is_authenticated_and_conflicting_local_is_rejected()
    {
        var leaf = CompileLeafSource(Preamble + "[HpdLiveAudioParticipantFactory(\"leaf\", OwnerSliceId.S2, AuthorityAxisId.Graph, 10,20,30,1)] [HpdGraphParticipantAllocation(new string[] { \"leaf-node\" }, new ushort[] {1}, new string[] {\"11111111111111111111111111111111\"}, new ulong[] {1}, new byte[] {1})] public sealed class LeafFactory : ILiveAudioParticipantFactoryV1 { }");
        var success = Run(Preamble, [leaf]); Assert.DoesNotContain(success.Result.Diagnostics, static value => value.Severity == DiagnosticSeverity.Error); Assert.Contains("Hash256.FromBytes", CatalogText(success.Result), StringComparison.Ordinal);
        var conflict = Run(Preamble + "[HpdLiveAudioParticipantFactory(\"local\", OwnerSliceId.S2, AuthorityAxisId.Graph, 10,20,30,1)] [HpdGraphParticipantAllocation(new string[] { \"local-node\" }, new ushort[] {1}, new string[] {\"11111111111111111111111111111111\"}, new ulong[] {1}, new byte[] {1})] public sealed class Media : ILiveAudioParticipantFactoryV1 { }", [leaf]);
        Assert.Contains(conflict.Result.Diagnostics, static value => value.Id == "HPDA007"); Assert.Empty(conflict.Result.GeneratedTrees);
    }

    [Theory]
    [InlineData("11111111111111111111111111111111")]
    [InlineData("22222222222222222222222222222222")]
    public void Referenced_same_key_identical_changed_or_local_shadow_is_HPDA007(string purpose)
    {
        var leaf = CompileLeafSource(Preamble + "[HpdLiveAudioParticipantFactory(\"shared\", OwnerSliceId.S2, AuthorityAxisId.Graph, 10,20,30,1)] [HpdGraphParticipantAllocation(new string[] { \"node\" }, new ushort[] {1}, new string[] {\"11111111111111111111111111111111\"}, new ulong[] {1}, new byte[] {1})] public sealed class LeafFactory : ILiveAudioParticipantFactoryV1 { }");
        var local = Preamble + $"[HpdLiveAudioParticipantFactory(\"shared\", OwnerSliceId.S2, AuthorityAxisId.Graph, 10,20,30,1)] [HpdGraphParticipantAllocation(new string[] {{ \"node\" }}, new ushort[] {{1}}, new string[] {{\"{purpose}\"}}, new ulong[] {{1}}, new byte[] {{1}})] public sealed class Media : ILiveAudioParticipantFactoryV1 {{ }}";
        var (result, _) = Run(local,[leaf]); Assert.Contains(result.Diagnostics, static value => value.Id=="HPDA007"); Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Ordinary_empty_same_key_duplicate_remains_HPDA005()
    {
        var (result, _) = Run(Declarations("same","same",null));
        Assert.Contains(result.Diagnostics, static value => value.Id=="HPDA005"); Assert.DoesNotContain(result.Diagnostics, static value => value.Id=="HPDA007");
    }

    [Fact]
    public void Raw_referenced_legacy_nine_argument_manifest_is_accepted_without_allocation()
    {
        var reference = CompileRawReference(RawManifestAttribute(""));
        var (result, _) = Run(Preamble, [reference]);
        Assert.DoesNotContain(result.Diagnostics, static value => value.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("GraphParticipantAllocationDeclarationBytes", CatalogText(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("inverse-partial")]
    [InlineData("malformed")]
    [InlineData("truncated")]
    [InlineData("noncanonical")]
    [InlineData("bad-fingerprint")]
    [InlineData("duplicate-node")]
    [InlineData("zero-purpose")]
    public void Raw_referenced_eleven_argument_manifest_attacks_are_HPDA007_and_atomic(string mutation)
    {
        var canonical = Convert.FromHexString("a40001016567726170680281646e6f64650381a4000101501111111111111111111111111111111102010301");
        byte[] carrier = mutation switch
        {
            "partial" => canonical,
            "inverse-partial" => [],
            "malformed" => [0xff],
            "truncated" => canonical[..^1],
            "noncanonical" => [.. canonical, 0x00],
            "duplicate-node" => Convert.FromHexString("a40001016567726170680282646e6f6465646e6f64650381a4000101501111111111111111111111111111111102010301"),
            "zero-purpose" => Convert.FromHexString("a40001016567726170680281646e6f64650381a4000101500000000000000000000000000000000002010301"),
            _ => canonical,
        };
        var fingerprint = mutation == "partial" ? Array.Empty<byte>() : AllocationFingerprintBytes(carrier);
        if (mutation == "bad-fingerprint") fingerprint[0] ^= 1;
        var reference = CompileRawReference(RawManifestAttribute($", new byte[] {{ {string.Join(", ", carrier)} }}, new byte[] {{ {string.Join(", ", fingerprint)} }}"));
        var (result, _) = Run(Preamble, [reference]);
        Assert.Contains(result.Diagnostics, static value => value.Id == "HPDA007");
        Assert.Empty(result.GeneratedTrees);
    }
    [Fact]
    public void Exact_application_declarations_generate_sorted_manifest_and_catalog()
    {
        var (result, compilation) = Run(Declarations("media", "provider", providerDependency: "media"));

        Assert.DoesNotContain(result.Diagnostics, static value => value.Severity == DiagnosticSeverity.Error);
        Assert.Equal(3, result.GeneratedTrees.Length);
        var catalog = Assert.Single(result.GeneratedTrees,
            static value => value.FilePath.EndsWith("GeneratedLiveAudioParticipantCatalogV1.g.cs", StringComparison.Ordinal));
        var text = catalog.GetText().ToString();
        Assert.True(text.IndexOf("BoundedAscii(\"media\")", StringComparison.Ordinal) <
                    text.IndexOf("BoundedAscii(\"provider\")", StringComparison.Ordinal));
        Assert.Contains("LiveAudioParticipantFactoryRegistrationV1", text, StringComparison.Ordinal);
        Assert.Contains(": base(Registrations, factories)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(compilation.GetDiagnostics(), static value => value.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Duplicate_key_or_missing_dependency_fails_without_catalog()
    {
        AssertInvalid(Declarations("media", "media", providerDependency: null));
        AssertInvalid(Declarations("media", "provider", providerDependency: "missing"));
    }

    [Fact]
    public void Dependency_cycle_fails_without_catalog() => AssertInvalid(Preamble + """
        [HpdLiveAudioParticipantFactory("media", OwnerSliceId.S2, AuthorityAxisId.Graph, 10, 20, 30, 1, Dependencies = new string[] { "provider" })]
        public sealed class Media : ILiveAudioParticipantFactoryV1 { }
        [HpdLiveAudioParticipantFactory("provider", OwnerSliceId.S5, AuthorityAxisId.Provider, 10, 20, 30, 2, Dependencies = new string[] { "media" })]
        public sealed class Provider : ILiveAudioParticipantFactoryV1 { }
        """);

    [Fact]
    public void Non_factory_declaration_fails()
    {
        AssertInvalid(Preamble + "[HpdLiveAudioParticipantFactory(\"media\", OwnerSliceId.S2, AuthorityAxisId.Graph, 1, 1, 1, 1)] public sealed class Bad { }");
    }

    [Theory]
    [InlineData("internal sealed class Hidden")]
    [InlineData("public abstract class Abstract")]
    [InlineData("public sealed class Open<T>")]
    public void Inaccessible_abstract_or_open_generic_factory_fails(string declaration) => AssertInvalid(Preamble +
        "[HpdLiveAudioParticipantFactory(\"media\", OwnerSliceId.S2, AuthorityAxisId.Graph, 1, 1, 1, 1)] " +
        declaration + " : ILiveAudioParticipantFactoryV1 { }");

    [Fact]
    public void Factory_nested_in_open_generic_container_fails() => AssertInvalid(Preamble + """
        public sealed class Outer<T>
        {
            [HpdLiveAudioParticipantFactory("media", OwnerSliceId.S2, AuthorityAxisId.Graph, 1, 1, 1, 1)]
            public sealed class Factory : ILiveAudioParticipantFactoryV1 { }
        }
        """);

    [Fact]
    public void Non_ascii_or_overlong_canonical_factory_identity_fails()
    {
        AssertInvalid(Preamble +
            "[HpdLiveAudioParticipantFactory(\"media\", OwnerSliceId.S2, AuthorityAxisId.Graph, 1, 1, 1, 1)] " +
            "public sealed class Fáctory : ILiveAudioParticipantFactoryV1 { }");
        var longName = new string('A', 510);
        AssertInvalid(Preamble +
            "[HpdLiveAudioParticipantFactory(\"media\", OwnerSliceId.S2, AuthorityAxisId.Graph, 1, 1, 1, 1)] " +
            $"public sealed class {longName} : ILiveAudioParticipantFactoryV1 {{ }}");
    }

    [Fact]
    public void Manual_catalog_subclass_is_rejected() => AssertInvalid(Preamble +
        "public sealed class ManualCatalog : LiveAudioParticipantFactoryCatalogV1 { " +
        "public ManualCatalog(LiveAudioParticipantFactoryRegistrationV1[] r, System.Collections.Generic.IEnumerable<ILiveAudioParticipantFactoryV1> f) : base(r, f) { } }");

    [Fact]
    public void Referenced_leaf_manifest_participates_in_each_application_root_exact_set()
    {
        var leaf = CompileLeaf("leaf-media");
        var first = Run(Preamble, [leaf]);
        var second = Run(Declarations("local-media", "local-provider", "local-media"), [leaf]);

        var firstCatalog = CatalogText(first.Result);
        var secondCatalog = CatalogText(second.Result);
        Assert.Contains("BoundedAscii(\"leaf-media\")", firstCatalog, StringComparison.Ordinal);
        Assert.Contains("BoundedAscii(\"leaf-media\")", secondCatalog, StringComparison.Ordinal);
        Assert.Contains("BoundedAscii(\"local-media\")", secondCatalog, StringComparison.Ordinal);
        Assert.NotEqual(firstCatalog, secondCatalog);
    }

    private static void AssertInvalid(string source)
    {
        var (result, _) = Run(source);
        Assert.Contains(result.Diagnostics, static value => value.Id == "HPDA005" && value.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.GeneratedTrees,
            static value => value.FilePath.EndsWith("GeneratedLiveAudioParticipantCatalogV1.g.cs", StringComparison.Ordinal));
    }

    private static (GeneratorDriverRunResult Result, Compilation Compilation) Run(string source,
        IEnumerable<MetadataReference>? extraReferences = null)
    {
        var references = PlatformReferences().Concat(extraReferences ?? []);
        var compilation = CSharpCompilation.Create("live-audio-root",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new LiveAudioParticipantCatalogSourceGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return (driver.GetRunResult(), updated);
    }

    private static MetadataReference CompileLeaf(string key)
    {
        var source = Preamble + $$"""
            [HpdLiveAudioParticipantFactory("{{key}}", OwnerSliceId.S2, AuthorityAxisId.Graph, 10, 20, 30, 1)]
            public sealed class LeafFactory : ILiveAudioParticipantFactoryV1 { }
            """;
        return CompileLeafSource(source);
    }

    private static MetadataReference CompileLeafSource(string source)
    {
        var compilation = CSharpCompilation.Create("participant-leaf-" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))], PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new LiveAudioParticipantCatalogSourceGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        using var stream = new MemoryStream();
        var emitted = updated.Emit(stream);
        Assert.True(emitted.Success, string.Join(System.Environment.NewLine, emitted.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static MetadataReference CompileRawReference(string source)
    {
        var compilation = CSharpCompilation.Create("participant-raw-" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)),
             CSharpSyntaxTree.ParseText(Preamble, new CSharpParseOptions(LanguageVersion.Latest))], PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(System.Environment.NewLine, emitted.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static string RawManifestAttribute(string allocationArguments) =>
        $"[assembly: HPD.Agent.Audio.HpdLiveAudioParticipantManifestAttribute(typeof(HPD.Agent.Audio.LeafFactory), \"leaf\", 2, 2, 10, 20, 30, new ushort[] {{ 1 }}, new string[] {{ }}{allocationArguments})] namespace HPD.Agent.Audio {{ public sealed class LeafFactory : ILiveAudioParticipantFactoryV1 {{ }} }}";

    private static byte[] AllocationFingerprintBytes(byte[] carrier) =>
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("hpd-graph-participant-allocation-declaration-v1\0").Concat(carrier).ToArray());

    private static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator).Select(static path => MetadataReference.CreateFromFile(path));

    private static string CatalogText(GeneratorDriverRunResult result) => Assert.Single(result.GeneratedTrees,
        static value => value.FilePath.EndsWith("GeneratedLiveAudioParticipantCatalogV1.g.cs", StringComparison.Ordinal))
        .GetText().ToString();

    private static string Declarations(string first, string second, string? providerDependency) => Preamble + $$"""
        [HpdLiveAudioParticipantFactory("{{first}}", OwnerSliceId.S2, AuthorityAxisId.Graph, 10, 20, 30, 1)]
        public sealed class Media : ILiveAudioParticipantFactoryV1 { }
        [HpdLiveAudioParticipantFactory("{{second}}", OwnerSliceId.S5, AuthorityAxisId.Provider, 10, 20, 30, 2, Dependencies = new string[] { {{(providerDependency is null ? "" : "\"" + providerDependency + "\"")}} })]
        public sealed class Provider : ILiveAudioParticipantFactoryV1 { }
        """;

    private const string Preamble = """
        using System;
        using HPD.Agent.Authority;
        using HPD.Agent.Audio;
        namespace HPD.Agent.Authority
        {
            public enum OwnerSliceId : ushort { S2=2, S5=5 }
            public enum AuthorityAxisId : ushort { Graph=2, Provider=5 }
            public readonly record struct BoundedAscii(string Value);
            public readonly record struct CapacityDimensionId(ushort Value);
            public readonly record struct DurationNs(long Nanoseconds);
            public readonly record struct Hash256 { public static Hash256 FromBytes(byte[] bytes) => new(); }
            public readonly record struct StableId128 { public static StableId128 FromBytes(byte[] bytes) => new(); }
            public readonly record struct ProviderId { public static ProviderId FromValue(StableId128 value) => new(); }
        }
        namespace HPD.Agent.Audio
        {
            using HPD.Agent.Authority;
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class HpdLiveAudioParticipantFactoryAttribute(string key, OwnerSliceId owner, AuthorityAxisId axis, long prepare, long drain, long terminate, params ushort[] capacities) : Attribute
            { public string[] Dependencies { get; set; } = []; public string OpaqueProviderKey { get; set; } = string.Empty; public ushort OpaqueMaximumOutstandingOperations { get; set; } public ulong OpaqueMaximumSubmittedBytes { get; set; } public long OpaqueMaximumAgeNanoseconds { get; set; } public LiveAudioOpaqueResidenceControlV1 OpaqueControl { get; set; } }
            [AttributeUsage(AttributeTargets.Assembly, AllowMultiple=true)]
            public sealed class HpdLiveAudioParticipantManifestAttribute : Attribute { public HpdLiveAudioParticipantManifestAttribute(Type type, string key, ushort owner, ushort axis, long prepare, long drain, long terminate, ushort[] capacities, string[] dependencies) { } public HpdLiveAudioParticipantManifestAttribute(Type type, string key, ushort owner, ushort axis, long prepare, long drain, long terminate, ushort[] capacities, string[] dependencies, byte[] bytes, byte[] fingerprint) { } public HpdLiveAudioParticipantManifestAttribute(Type type, string key, ushort owner, ushort axis, long prepare, long drain, long terminate, ushort[] capacities, string[] dependencies, byte[] bytes, byte[] fingerprint, byte[] providerId, ushort operations, ulong submittedBytes, long age, byte control) { } }
            public enum LiveAudioOpaqueResidenceControlV1 : byte { ObservationOnly=1, AdapterAcknowledgedCancellation=2 }
            public sealed class LiveAudioOpaqueResidenceQualificationV1 { public LiveAudioOpaqueResidenceQualificationV1(ProviderId providerId, ushort operations, ulong bytes, DurationNs age, LiveAudioOpaqueResidenceControlV1 control) { } }
            public interface ILiveAudioParticipantFactoryV1 { }
            public sealed class LiveAudioParticipantDescriptorV1 { public LiveAudioParticipantDescriptorV1(BoundedAscii key, OwnerSliceId owner, AuthorityAxisId axis, BoundedAscii[] dependencies, CapacityDimensionId[] capacities, DurationNs prepare, DurationNs drain, DurationNs terminate) { } }
            public sealed class LiveAudioParticipantFactoryRegistrationV1 { public LiveAudioParticipantFactoryRegistrationV1(Type type, string identity, LiveAudioParticipantDescriptorV1 descriptor) { } public LiveAudioParticipantFactoryRegistrationV1(Type type, string identity, LiveAudioParticipantDescriptorV1 descriptor, ReadOnlyMemory<byte> bytes, Hash256? fingerprint) { } public LiveAudioParticipantFactoryRegistrationV1(Type type, string identity, LiveAudioParticipantDescriptorV1 descriptor, ReadOnlyMemory<byte> bytes, Hash256? fingerprint, LiveAudioOpaqueResidenceQualificationV1 qualification) { } }
            public abstract class LiveAudioParticipantFactoryCatalogV1 { protected LiveAudioParticipantFactoryCatalogV1(LiveAudioParticipantFactoryRegistrationV1[] registrations, System.Collections.Generic.IEnumerable<ILiveAudioParticipantFactoryV1> factories) { } }
        }
        """;
}
