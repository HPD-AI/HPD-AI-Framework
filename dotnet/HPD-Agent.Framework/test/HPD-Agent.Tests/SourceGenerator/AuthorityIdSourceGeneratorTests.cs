using HPD.Agent.Authority;
using HPD.Agent.SourceGenerator.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class AuthorityIdSourceGeneratorTests
{
    [Fact]
    public void ExactManifest_GeneratesAllSemanticWrappers()
    {
        var (result, compilation) = Run(CreateManifest());

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var source = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.Equal(47, source.Split("public readonly record struct ").Length - 1);
        Assert.Contains("internal readonly record struct GlobalParticipantAllocatorJournalId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public readonly record struct GlobalParticipantAllocatorJournalId", source, StringComparison.Ordinal);
        var internalWrapper = source[source.IndexOf("internal readonly record struct GlobalParticipantAllocatorJournalId", StringComparison.Ordinal)..];
        Assert.DoesNotContain("///", internalWrapper, StringComparison.Ordinal);
        Assert.Contains("public readonly record struct JournalFactId", source, StringComparison.Ordinal);
        Assert.Contains("public readonly record struct TransportGenerationId", source, StringComparison.Ordinal);
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DuplicateFamily_FailsGeneration()
    {
        var manifest = CreateManifest() + "\nten|DuplicateId|S1|S1|authority|public";
        var (result, _) = Run(manifest);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "HPDA001");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    [Theory]
    [InlineData("UnknownOwner", "S1", "authority")]
    [InlineData("S1", "UnknownAllocator", "authority")]
    [InlineData("S1", "S1", "unknown-kind")]
    public void UnregisteredAuthorityMetadata_FailsGeneration(string owner, string allocatorOwner, string kind)
    {
        var rows = CreateManifest().Split('\n');
        rows[0] = $"ten|TenantId|{owner}|{allocatorOwner}|{kind}|public";

        var (result, _) = Run(string.Join("\n", rows));

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "HPDA001");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    [Theory]
    [InlineData("1InvalidId")]
    [InlineData("invalidId")]
    public void InvalidWrapperIdentifier_FailsGeneration(string type)
    {
        var rows = CreateManifest().Split('\n');
        rows[0] = $"ten|{type}|S1|S1|correlation|public";

        AssertInvalid(string.Join("\n", rows));
    }

    [Fact]
    public void DuplicateWrapperType_FailsGeneration()
    {
        var rows = CreateManifest().Split('\n');
        rows[1] = "prn|TenantId|S9|S9|privacy|public";

        AssertInvalid(string.Join("\n", rows));
    }

    [Fact]
    public void MalformedColumnCount_FailsGeneration()
    {
        var rows = CreateManifest().Split('\n');
        rows[0] = "ten|TenantId|S1|S1";

        AssertInvalid(string.Join("\n", rows));
    }

    [Fact]
    public void InvalidVisibility_FailsGeneration()
    {
        var rows = CreateManifest().Split('\n');
        rows[0] = "ten|TenantId|S1|S1|correlation|protected";
        AssertInvalid(string.Join("\n", rows));
    }

    [Fact]
    public void CompiledXml_ExcludesInternalAllocatorFamily()
    {
        var (_, compilation) = Run(CreateManifest());
        using var pe = new MemoryStream();
        using var xml = new MemoryStream();
        var emitted = compilation.Emit(pe, xmlDocumentationStream: xml);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var documentation = Encoding.UTF8.GetString(xml.ToArray());
        Assert.Contains("TenantId", documentation, StringComparison.Ordinal);
        Assert.DoesNotContain("GlobalParticipantAllocatorJournalId", documentation, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingFamily_FailsGeneration() => AssertInvalid(string.Join("\n", CreateManifest().Split('\n').Skip(1)));

    [Fact]
    public void MultipleMatchingManifests_FailGeneration()
    {
        var (result, _) = Run(CreateManifest(), CreateManifest());

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "HPDA001");
        Assert.Contains("expected one manifest", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void ConsumerWithoutManifest_IsUnaffected()
    {
        var compilation = CSharpCompilation.Create("consumer", [CSharpSyntaxTree.ParseText("internal sealed class C { }")]);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new AuthorityIdSourceGenerator().AsSourceGenerator());

        var result = driver.RunGenerators(compilation).GetRunResult();

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedTrees);
    }

    private static (GeneratorDriverRunResult Result, Compilation Compilation) Run(params string[] manifests)
    {
        const string source = """
            namespace HPD.Agent.Authority;
            internal readonly struct StableId128
            {
                internal static StableId128 CreateRandom() => default;
                internal static bool TryParse(string? text, string family, out StableId128 value) { value = default; return false; }
                internal string Format(string family) => string.Empty;
                internal bool TryWriteBytes(global::System.Span<byte> destination) => false;
            }
            """;
        var compilation = CSharpCompilation.Create(
            "authority",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new AuthorityIdSourceGenerator().AsSourceGenerator()],
            additionalTexts: manifests.Select(static manifest => new TextManifest(manifest)),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
        return (driver.GetRunResult(), updatedCompilation);
    }

    private static void AssertInvalid(string manifest)
    {
        var (result, _) = Run(manifest);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "HPDA001");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    private static string CreateManifest() => string.Join("\n", AuthorityIdFamilyRegistryV1.All.Select(
        row => $"{row.Token}|{row.Type}|{row.Owner}|{row.AllocatorOwner}|{row.Kind}|{row.Visibility}"));

    private sealed class TextManifest(string text) : AdditionalText
    {
        public override string Path => "authority-id-families-v1.txt";
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text, Encoding.UTF8);
    }
}
