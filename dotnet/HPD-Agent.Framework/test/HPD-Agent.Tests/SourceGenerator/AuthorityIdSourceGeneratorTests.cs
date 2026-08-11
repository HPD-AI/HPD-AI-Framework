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
        var result = Run(CreateManifest());

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var source = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.Equal(46, source.Split("public readonly record struct ").Length - 1);
        Assert.Contains("public readonly record struct JournalFactId", source, StringComparison.Ordinal);
        Assert.Contains("public readonly record struct TransportGenerationId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateFamily_FailsGeneration()
    {
        var manifest = CreateManifest() + "\nten|DuplicateId|S1|S1|authority";
        var result = Run(manifest);

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
        rows[0] = $"ten|TenantId|{owner}|{allocatorOwner}|{kind}";

        var result = Run(string.Join("\n", rows));

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "HPDA001");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
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

    private static GeneratorDriverRunResult Run(string manifest)
    {
        var compilation = CSharpCompilation.Create("authority", [CSharpSyntaxTree.ParseText("internal sealed class C { }")]);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new AuthorityIdSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new TextManifest(manifest)],
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));
        return driver.RunGenerators(compilation).GetRunResult();
    }

    private static string CreateManifest() => string.Join("\n", AuthorityIdFamilyRegistryV1.All.Select(
        row => $"{row.Token}|{row.Type}|{row.Owner}|{row.AllocatorOwner}|{row.Kind}"));

    private sealed class TextManifest(string text) : AdditionalText
    {
        public override string Path => "authority-id-families-v1.txt";
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text, Encoding.UTF8);
    }
}
