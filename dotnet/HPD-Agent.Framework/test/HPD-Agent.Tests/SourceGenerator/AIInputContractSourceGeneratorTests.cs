using System.Collections.Immutable;
using HPD.Agent.SourceGenerator.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class AIInputContractSourceGeneratorTests
{
    [Fact]
    public void AnnotatedPartialRecord_EmitsDirectReusableContract()
    {
        const string source = """
            using HPD.Agent;

            namespace GeneratedContracts;

            [AIInputContract]
            public sealed partial record SummarizeInput(string InputFile, int Limit = 20);
            """;
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location));
        var compilation = CSharpCompilation.Create(
            "GeneratedInputContract",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new AIInputContractSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var runResult = driver.GetRunResult();
        var generated = string.Join(
            "\n",
            output.SyntaxTrees
                .Where(tree => tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
                .Select(tree => tree.GetText().ToString()));
        var errors = diagnostics.Concat(output.GetDiagnostics())
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        Assert.Empty(errors);
        Assert.True(
            runResult.Results.Sum(result => result.GeneratedSources.Length) > 0,
            string.Join("\n", runResult.Results.Select(result =>
                $"exception={result.Exception}; diagnostics={string.Join(" | ", result.Diagnostics)}")));
        Assert.Contains("IAIInputContract<global::GeneratedContracts.SummarizeInput>", generated);
        Assert.Contains("BindAIInputContract", generated);
        Assert.Contains("AIInputContract.Create", generated);
        Assert.DoesNotContain("JsonSerializer.Deserialize", generated);
    }
}
