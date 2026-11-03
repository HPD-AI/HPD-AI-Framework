using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class InstanceMethodContextTests
{
    [Fact]
    public void CollapseInstructions_RejectNonConstantAttributeArguments()
    {
        const string source = """
            using HPD.Agent;

            [Collapse(
                "Dynamic ToolHarness",
                FunctionResult = GetActivationMessage(),
                SystemPrompt = Rules)]
            public class DynamicToolHarness
            {
                public static string Rules => "Static rules";

                public static string GetActivationMessage() => "Activated";

                [AIFunctionAttribute]
                public string TestFunction() => "result";
            }
            """;

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.True(
            diagnostics.Count(diagnostic => diagnostic.Id == "CS0182") == 2,
            string.Join(System.Environment.NewLine, diagnostics));
        Assert.DoesNotContain("instance.GetActivationMessage()", generatedCode);
        Assert.DoesNotContain("instance.Rules", generatedCode);
    }

    private static (string GeneratedCode, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            [new global::HPDToolSourceGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedCode = string.Join("\n\n", outputCompilation.SyntaxTrees
            .Where(static tree => tree.FilePath.Contains("g.cs"))
            .Select(static tree => tree.GetText().ToString()));
        return (
            generatedCode,
            diagnostics.AddRange(outputCompilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
    }
}
