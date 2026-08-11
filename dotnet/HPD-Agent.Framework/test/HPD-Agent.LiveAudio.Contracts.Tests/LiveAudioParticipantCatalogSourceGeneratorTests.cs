using HPD.Agent.SourceGenerator.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class LiveAudioParticipantCatalogSourceGeneratorTests
{
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
        var compilation = CSharpCompilation.Create("participant-leaf",
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
            public readonly record struct Hash256;
        }
        namespace HPD.Agent.Audio
        {
            using HPD.Agent.Authority;
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class HpdLiveAudioParticipantFactoryAttribute(string key, OwnerSliceId owner, AuthorityAxisId axis, long prepare, long drain, long terminate, params ushort[] capacities) : Attribute
            { public string[] Dependencies { get; set; } = []; }
            [AttributeUsage(AttributeTargets.Assembly, AllowMultiple=true)]
            public sealed class HpdLiveAudioParticipantManifestAttribute(Type type, string key, ushort owner, ushort axis, long prepare, long drain, long terminate, ushort[] capacities, string[] dependencies) : Attribute;
            public interface ILiveAudioParticipantFactoryV1 { }
            public sealed class LiveAudioParticipantDescriptorV1 { public LiveAudioParticipantDescriptorV1(BoundedAscii key, OwnerSliceId owner, AuthorityAxisId axis, BoundedAscii[] dependencies, CapacityDimensionId[] capacities, DurationNs prepare, DurationNs drain, DurationNs terminate) { } }
            public sealed class LiveAudioParticipantFactoryRegistrationV1 { public LiveAudioParticipantFactoryRegistrationV1(Type type, string identity, LiveAudioParticipantDescriptorV1 descriptor) { } }
            public abstract class LiveAudioParticipantFactoryCatalogV1 { protected LiveAudioParticipantFactoryCatalogV1(LiveAudioParticipantFactoryRegistrationV1[] registrations, System.Collections.Generic.IEnumerable<ILiveAudioParticipantFactoryV1> factories) { } }
        }
        """;
}
