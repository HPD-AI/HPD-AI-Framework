using System.Collections.Immutable;
using HPD.Agent.ToolHarness.Coding.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugAdapterSourceGeneratorTests
{
    [Theory]
    [InlineData("internal", "fixture", "[DebugAdapterLanguages(\"fixture\")]", "HPDDBG001")]
    [InlineData("public", " ", "[DebugAdapterLanguages(\"fixture\")]", "HPDDBG002")]
    [InlineData("public", "fixture", "", "HPDDBG004")]
    [InlineData("public", "fixture", "[DebugAdapterLanguages(\"fixture\", \"FIXTURE\")]", "HPDDBG006")]
    public void Core_declaration_contract_diagnostics_are_enforced(
        string accessibility,
        string id,
        string languages,
        string expectedId)
    {
        var source = $$"""
            using HPD.Agent.ToolHarness.Coding.Debugging;
            using HPD.Agent.ToolHarness.Coding.Debugging.Attributes;

            [HpdDebugAdapter("{{id}}")]
            {{languages}}
            [DebugAdapterTargetKinds(DebugTargetKind.SourceFile)]
            [DebugAdapterCommandHint("adapter")]
            {{accessibility}} sealed class FixtureAdapter;
            """;

        Run([new DebugAdapterSourceGenerator()], source).Diagnostics
            .Should().Contain(diagnostic => diagnostic.Id == expectedId);
    }

    [Theory]
    [InlineData("[DebugAdapterFileExtensions(\"cs\")]", "HPDDBG005")]
    [InlineData("", "HPDDBG008")]
    [InlineData("[DebugAdapterTargetKinds(DebugTargetKind.None)]\n[DebugAdapterCommandHint(\"adapter\")]", "HPDDBG007")]
    [InlineData("[DebugAdapterFactory(typeof(string))]", "HPDDBG009")]
    [InlineData("[DebugAdapterRootMarkers(\"../secret\")]\n[DebugAdapterTargetKinds(DebugTargetKind.SourceFile)]\n[DebugAdapterCommandHint(\"adapter\")]", "HPDDBG011")]
    [InlineData("[DebugAdapterTargetKinds(DebugTargetKind.SourceFile)]\n[DebugAdapterCommandHint(\" \")]", "HPDDBG012")]
    [InlineData("[DebugAdapterTargetKinds(DebugTargetKind.SourceFile)]\n[DebugAdapterCommandHint(\"./adapter\")]", "HPDDBG012")]
    [InlineData("[DebugAdapterTargetKinds(DebugTargetKind.SourceFile)]\n[DebugAdapterCommandHint(\"adapter\")]\n[DebugAdapterArgumentHints(\" \")]", "HPDDBG013")]
    [InlineData("[DebugAdapterTargetKinds(DebugTargetKind.SourceFile)]\n[DebugAdapterCommandHint(\"adapter\")]\n[DebugAdapterInstallGuidance(\"not safe\")]", "HPDDBG014")]
    [InlineData("[DebugAdapterTargetKinds((DebugTargetKind)64)]\n[DebugAdapterCommandHint(\"adapter\")]", "HPDDBG015")]
    [InlineData("[DebugAdapterTargetKinds(DebugTargetKind.SourceFile)]\n[DebugAdapterCommandHint(\"adapter\")]\n[DebugAdapterPriority(10001)]", "HPDDBG016")]
    [InlineData("[DebugAdapterTargetKinds(DebugTargetKind.SourceFile)]\n[DebugAdapterCommandHint(\"adapter\")]\n[DebugAdapterFactory(typeof(StandardDebugAdapterFactory))]", "HPDDBG017")]
    public void Invalid_declarations_report_actionable_diagnostics(string varyingAttributes, string expectedId)
    {
        var source = $$"""
            using HPD.Agent.ToolHarness.Coding.Debugging;
            using HPD.Agent.ToolHarness.Coding.Debugging.Attributes;

            [HpdDebugAdapter("fixture")]
            [DebugAdapterLanguages("fixture")]
            {{varyingAttributes}}
            public sealed class FixtureAdapter;
            """;

        Run([new DebugAdapterSourceGenerator()], source).Diagnostics
            .Should().Contain(diagnostic => diagnostic.Id == expectedId);
    }

    [Fact]
    public void Blank_language_reports_actionable_diagnostic()
    {
        const string source = """
            using HPD.Agent.ToolHarness.Coding.Debugging;
            using HPD.Agent.ToolHarness.Coding.Debugging.Attributes;

            [HpdDebugAdapter("fixture")]
            [DebugAdapterLanguages(" ")]
            [DebugAdapterTargetKinds(DebugTargetKind.SourceFile)]
            [DebugAdapterCommandHint("adapter")]
            public sealed class FixtureAdapter;
            """;

        Run([new DebugAdapterSourceGenerator()], source).Diagnostics
            .Should().Contain(diagnostic => diagnostic.Id == "HPDDBG010");
    }

    [Fact]
    public void Invalid_metadata_diagnostic_points_to_the_exact_attribute_argument()
    {
        const string source = """
            using HPD.Agent.ToolHarness.Coding.Debugging;
            using HPD.Agent.ToolHarness.Coding.Debugging.Attributes;

            [HpdDebugAdapter("fixture")]
            [DebugAdapterLanguages("fixture")]
            [DebugAdapterRootMarkers("safe", "../secret")]
            [DebugAdapterTargetKinds(DebugTargetKind.SourceFile)]
            [DebugAdapterCommandHint("adapter")]
            public sealed class FixtureAdapter;
            """;

        var diagnostic = Run([new DebugAdapterSourceGenerator()], source).Diagnostics
            .Single(item => item.Id == "HPDDBG011");

        source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)
            .Should().Be("\"../secret\"");
    }

    [Fact]
    public void Valid_declaration_emits_complete_catalog_entry_without_debug_diagnostics()
    {
        const string source = """
            using HPD.Agent.ToolHarness.Coding.Debugging;
            using HPD.Agent.ToolHarness.Coding.Debugging.Attributes;

            [HpdDebugAdapter("fixture")]
            [DebugAdapterLanguages("fixture")]
            [DebugAdapterFileExtensions(".fixture")]
            [DebugAdapterRootMarkers("fixture.json")]
            [DebugAdapterTargetKinds(DebugTargetKind.SourceFile | DebugTargetKind.Process)]
            [DebugAdapterCommandHint("fixture-debug")]
            [DebugAdapterArgumentHints("--dap")]
            [DebugAdapterInstallGuidance("fixture.install")]
            [DebugAdapterPriority(42)]
            public sealed class FixtureAdapter;
            """;

        var result = Run([new DebugAdapterSourceGenerator()], source);

        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id.StartsWith("HPDDBG", StringComparison.Ordinal));
        result.GeneratedHintNames.Should().ContainSingle().Which.Should().Be("DebugAdapterCatalog.g.cs");
        result.CompilationErrors.Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_ids_are_rejected_deterministically()
    {
        const string source = """
            using HPD.Agent.ToolHarness.Coding.Debugging;
            using HPD.Agent.ToolHarness.Coding.Debugging.Attributes;

            [HpdDebugAdapter("duplicate")]
            [DebugAdapterLanguages("a")]
            [DebugAdapterTargetKinds(DebugTargetKind.SourceFile)]
            [DebugAdapterCommandHint("a")]
            public sealed class FirstAdapter;

            [HpdDebugAdapter("duplicate")]
            [DebugAdapterLanguages("b")]
            [DebugAdapterTargetKinds(DebugTargetKind.SourceFile)]
            [DebugAdapterCommandHint("b")]
            public sealed class SecondAdapter;
            """;

        Run([new DebugAdapterSourceGenerator()], source).Diagnostics
            .Should().ContainSingle(diagnostic => diagnostic.Id == "HPDDBG003");
    }

    [Fact]
    public void Language_server_and_debug_generators_coexist_in_one_compilation()
    {
        const string source = """
            using HPDOS.ToolHarnesses.Middleware;
            using HPD.Agent.ToolHarness.Coding.Debugging;
            using HPD.Agent.ToolHarness.Coding.Debugging.Attributes;

            [HpdLanguageServer("fixture-lsp")]
            [LanguageServerExtensions(".fixture")]
            [LanguageServerExecutable("fixture-lsp")]
            public sealed class FixtureLanguageServer;

            [HpdDebugAdapter("fixture-debug")]
            [DebugAdapterLanguages("fixture")]
            [DebugAdapterFileExtensions(".fixture")]
            [DebugAdapterTargetKinds(DebugTargetKind.SourceFile)]
            [DebugAdapterCommandHint("fixture-debug")]
            public sealed class FixtureDebugAdapter;
            """;

        var result = Run([new LanguageServerSourceGenerator(), new DebugAdapterSourceGenerator()], source);

        result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.GeneratedHintNames.Should().BeEquivalentTo("LanguageServerRegistry.g.cs", "DebugAdapterCatalog.g.cs");
        result.CompilationErrors.Should().BeEmpty();
    }

    private static GeneratorResult Run(IReadOnlyList<IIncrementalGenerator> generators, string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .GroupBy(reference => reference.Display, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "CombinedCodingGeneratorFixture",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators.Select(generator => generator.AsSourceGenerator()),
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var runResult = driver.GetRunResult();
        return new GeneratorResult(
            diagnostics.Concat(runResult.Diagnostics)
                .GroupBy(diagnostic => (diagnostic.Id, diagnostic.Location.SourceSpan, diagnostic.GetMessage()))
                .Select(group => group.First())
                .ToImmutableArray(),
            runResult.Results.SelectMany(result => result.GeneratedSources).Select(sourceResult => sourceResult.HintName).ToArray(),
            output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToImmutableArray());
    }

    private sealed record GeneratorResult(
        ImmutableArray<Diagnostic> Diagnostics,
        IReadOnlyList<string> GeneratedHintNames,
        ImmutableArray<Diagnostic> CompilationErrors);
}
