using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class SkillDiagnosticsTests
{
    [Theory]
    [InlineData("private", "valid description", "HPDSKILL017")]
    [InlineData("public", "", "HPDSKILL002")]
    public void SkillDeclaration_ReportsActionableShapeDiagnostic(
        string accessibility,
        string description,
        string expectedId)
    {
        var source = $$"""
            using HPD.Agent;
            public partial class Harness
            {
                [Skill]
                {{accessibility}} Skill Guidance() => Skill.Create(
                    "guidance",
                    "{{description}}",
                    SkillInstructions.FromText("Follow the guide."));
            }
            """;

        Assert.Contains(RunGenerator(source), diagnostic => diagnostic.Id == expectedId);
    }

    [Fact]
    public void SkillFunctionReference_RejectsMemberWithoutAIFunction()
    {
        const string source = """
            using HPD.Agent;
            public partial class Harness
            {
                public string Plain() => "plain";
                [Skill]
                public Skill Guidance() => Skill.Create(
                    "guidance",
                    "Provides concrete guidance.",
                    SkillInstructions.FromText("Follow the guide."),
                    [SkillCapabilities.Function<Harness>(nameof(Plain))]);
            }
            """;

        Assert.Contains(RunGenerator(source), diagnostic => diagnostic.Id == "HPDSKILL006");
    }

    [Fact]
    public void SkillResources_RejectDuplicateModelNames()
    {
        const string source = """
            using HPD.Agent;
            public partial class Harness
            {
                [Skill]
                public Skill Guidance() => Skill.Create(
                    "guidance",
                    "Provides concrete guidance.",
                    SkillInstructions.FromText("Follow the guide."),
                    [
                        SkillCapabilities.Resource("guide", "Reads guide one.", "one"),
                        SkillCapabilities.Resource("guide", "Reads guide two.", "two")
                    ]);
            }
            """;

        Assert.Contains(RunGenerator(source), diagnostic => diagnostic.Id == "HPDSKILL009");
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location));
        var compilation = CSharpCompilation.Create(
            "SkillDiagnosticFixture",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(
            generators: [new global::HPDToolSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics;
    }
}
