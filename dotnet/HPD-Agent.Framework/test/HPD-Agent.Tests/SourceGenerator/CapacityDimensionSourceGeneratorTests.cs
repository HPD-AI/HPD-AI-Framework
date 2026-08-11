using System.Text;
using HPD.Agent.Authority;
using HPD.Agent.SourceGenerator.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class CapacityDimensionSourceGeneratorTests
{
    private static readonly string ExactManifest = string.Join('\n', CapacityDimensionRegistryV1.All.Select(static descriptor => string.Join('|',
        descriptor.Id.Value, descriptor.Token, descriptor.Unit, descriptor.Conservation,
        string.Join(',', descriptor.ScopeKinds), descriptor.EmergencyClass, descriptor.MaximumPerCharge,
        descriptor.SchemaVersion, descriptor.SettlementEvidence)));

    [Fact]
    public void Exact_manifest_generates_a_compiling_immutable_registry()
    {
        var (result, compilation) = Run(ExactManifest);

        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var source = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.Equal(14, source.Split("new(new CapacityDimensionId(").Length - 1);
        Assert.Contains("ReadOnlyEntries", source, StringComparison.Ordinal);
        Assert.Contains("public static class CapacityDimensionsV1", source, StringComparison.Ordinal);
        Assert.DoesNotContain(compilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Consumer_without_manifest_is_unaffected()
    {
        var compilation = CSharpCompilation.Create("consumer", [CSharpSyntaxTree.ParseText("internal sealed class C { }")]);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new CapacityDimensionSourceGenerator().AsSourceGenerator());

        var result = driver.RunGenerators(compilation).GetRunResult();

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Multiple_manifests_fail() => AssertInvalid([ExactManifest, ExactManifest]);

    [Theory]
    [InlineData("3|queue-items|Items|Resident|Tenant,Session,Session|None|1024|1|QueueItemsRemoved")]
    [InlineData("3|queue-items|Items|Resident|Session,Tenant,Operation|None|1024|1|QueueItemsRemoved")]
    [InlineData("3|queue-items|Unknown|Resident|Tenant,Session,Operation|None|1024|1|QueueItemsRemoved")]
    [InlineData("3|queue-items|Items|Unknown|Tenant,Session,Operation|None|1024|1|QueueItemsRemoved")]
    [InlineData("3|queue-items|Items|Resident|Tenant,Session,Operation|Unknown|1024|1|QueueItemsRemoved")]
    public void Invalid_closed_metadata_fails(string replacement)
    {
        var rows = ExactManifest.Split('\n');
        rows[2] = replacement;
        AssertInvalid([string.Join('\n', rows)]);
    }

    [Fact]
    public void Missing_or_duplicate_identity_fails()
    {
        AssertInvalid([string.Join('\n', ExactManifest.Split('\n').Skip(1))]);
        var rows = ExactManifest.Split('\n');
        rows[1] = rows[0];
        AssertInvalid([string.Join('\n', rows)]);
    }

    private static (GeneratorDriverRunResult Result, Compilation Compilation) Run(params string[] manifests)
    {
        const string contractStubs = """
            namespace HPD.Agent.Authority;
            public readonly record struct CapacityDimensionId(ushort Value) { public bool IsValid => Value > 0; }
            public enum CapacityUnitV1 : ushort { Bytes=1, Items=2, Nanoseconds=3, Samples=4, Tokens=5, Slots=6 }
            public enum CapacityConservationV1 : ushort { Resident=1, Consumable=2, RateWindow=3, Exclusive=4 }
            public enum CapacityEmergencyClassV1 : ushort { None=0, Authority=1, Privacy=2, Recovery=3 }
            public enum CapacityScopeKindV1 : ushort { Tenant=1, Session=2, Participant=3, Operation=4, Provider=5, Sink=6, Subscriber=7, Custodian=8, Schema=9, Exporter=10, Owner=11 }
            public sealed record CapacityDimensionDescriptorV1
            {
                public CapacityDimensionDescriptorV1(CapacityDimensionId id, string token, CapacityUnitV1 unit, CapacityConservationV1 conservation, global::System.Collections.Generic.IReadOnlyList<CapacityScopeKindV1> scopes, CapacityEmergencyClassV1 emergency, long maximum, ushort version, string evidence) { }
            }
            """;
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "capacity",
            syntaxTrees: [CSharpSyntaxTree.ParseText(contractStubs, new CSharpParseOptions(LanguageVersion.Latest))],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new CapacityDimensionSourceGenerator().AsSourceGenerator()],
            additionalTexts: manifests.Select(static manifest => new TextManifest(manifest)),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return (driver.GetRunResult(), updated);
    }

    private static void AssertInvalid(string[] manifests)
    {
        var (result, _) = Run(manifests);
        var diagnostic = Assert.Single(result.Diagnostics, static item => item.Id == "HPDA003");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    private sealed class TextManifest(string text) : AdditionalText
    {
        public override string Path => "authority-capacity-dimensions-v1.txt";
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text, Encoding.UTF8);
    }
}
