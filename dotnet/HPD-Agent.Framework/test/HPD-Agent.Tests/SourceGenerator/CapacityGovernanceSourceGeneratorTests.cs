using System.Text;
using HPD.Agent.Authority;
using HPD.Agent.SourceGenerator.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class CapacityGovernanceSourceGeneratorTests
{
    private static readonly string ExactManifest = string.Join('\n', CapacityGovernanceRegistryV1.All.Select(static row => string.Join('|',
        row.DimensionId.Value, CapacityDimensionRegistryV1.Get(row.DimensionId).Token, row.ScopeKind,
        IdentityType(row.ScopeKind), row.NormalLimit, row.EmergencyReserve)));

    [Fact]
    public void Exact_manifest_generates_the_closed_immutable_catalog()
    {
        var (result, compilation) = Run(ExactManifest);

        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var source = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.Equal(14, source.Split("new(new CapacityDimensionId(").Length - 1);
        Assert.Contains("ReadOnlyEntries", source, StringComparison.Ordinal);
        Assert.DoesNotContain(compilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Throws<ArgumentOutOfRangeException>(() => CapacityGovernanceRegistryV1.Get(new CapacityDimensionId(15)));
    }

    [Fact]
    public void Consumer_without_manifest_is_unaffected()
    {
        var compilation = CSharpCompilation.Create("consumer", [CSharpSyntaxTree.ParseText("internal sealed class C { }")]);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new CapacityGovernanceSourceGenerator().AsSourceGenerator());

        var result = driver.RunGenerators(compilation).GetRunResult();

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Multiple_manifests_fail() => AssertInvalid([ExactManifest, ExactManifest]);

    [Theory]
    [InlineData(0, "1|media-bytes|Participant|ParticipantId|16777215|0")]
    [InlineData(0, "1|media-bytes|Tenant|TenantId|16777216|0")]
    [InlineData(9, "10|journal-bytes|Session|SessionId|983040|65535")]
    [InlineData(13, "14|recovery-work|Owner|RuntimeGenerationId|992|32")]
    public void Any_limit_scope_identity_or_reserve_drift_fails(int index, string replacement)
    {
        var rows = ExactManifest.Split('\n');
        rows[index] = replacement;
        AssertInvalid([string.Join('\n', rows)]);
    }

    [Fact]
    public void Missing_or_duplicate_rows_fail()
    {
        AssertInvalid([string.Join('\n', ExactManifest.Split('\n').Skip(1))]);
        var rows = ExactManifest.Split('\n');
        rows[1] = rows[0];
        AssertInvalid([string.Join('\n', rows)]);
    }

    private static string IdentityType(CapacityScopeKindV1 kind) => kind switch
    {
        CapacityScopeKindV1.Participant => "ParticipantId",
        CapacityScopeKindV1.Operation => "OperationId",
        CapacityScopeKindV1.Provider => "ProviderId",
        CapacityScopeKindV1.Sink => "SinkGenerationId",
        CapacityScopeKindV1.Subscriber => "SubscriberId",
        CapacityScopeKindV1.Session => "SessionId",
        CapacityScopeKindV1.Custodian => "CustodianDescriptorId",
        CapacityScopeKindV1.Schema => "SchemaId",
        CapacityScopeKindV1.Exporter => "ExportId",
        CapacityScopeKindV1.Owner => "OwnerSliceId",
        _ => throw new InvalidOperationException($"Unregistered governance scope {kind}.")
    };

    private static (GeneratorDriverRunResult Result, Compilation Compilation) Run(params string[] manifests)
    {
        const string contractStubs = """
            namespace HPD.Agent.Authority;
            public readonly record struct CapacityDimensionId(ushort Value) { public bool IsValid => Value > 0; }
            public enum CapacityScopeKindV1 : ushort { Tenant=1, Session=2, Participant=3, Operation=4, Provider=5, Sink=6, Subscriber=7, Custodian=8, Schema=9, Exporter=10, Owner=11 }
            public sealed record CapacityScopeLimitV1
            {
                internal CapacityScopeLimitV1(CapacityDimensionId id, CapacityScopeKindV1 scope, long normal, long reserve) { }
            }
            """;
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator).Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("capacity-governance",
            [CSharpSyntaxTree.ParseText(contractStubs, new CSharpParseOptions(LanguageVersion.Latest))], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new CapacityGovernanceSourceGenerator().AsSourceGenerator()],
            additionalTexts: manifests.Select(static manifest => new TextManifest(manifest)),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return (driver.GetRunResult(), updated);
    }

    private static void AssertInvalid(string[] manifests)
    {
        var (result, _) = Run(manifests);
        var diagnostic = Assert.Single(result.Diagnostics, static item => item.Id == "HPDA004");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    private sealed class TextManifest(string text) : AdditionalText
    {
        public override string Path => "authority-capacity-governance-v1.txt";
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text, Encoding.UTF8);
    }
}
