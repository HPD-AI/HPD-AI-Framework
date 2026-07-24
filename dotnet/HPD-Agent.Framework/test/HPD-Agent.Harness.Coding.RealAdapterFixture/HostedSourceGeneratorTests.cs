using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HPD.Agent.ToolHarness.Coding.RealAdapterFixture;

public sealed class HostedSourceGeneratorTests
{
    [Fact]
    public void Source_generator_executes_inside_testhost()
    {
        var compilation = CSharpCompilation.Create("GeneratedConsumer");
        var driver = CSharpGeneratorDriver.Create(
            new QualificationGenerator().AsSourceGenerator());

        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updated,
            out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Single(updated.SyntaxTrees);
    }
}

[Generator]
public sealed class QualificationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
        => context.RegisterPostInitializationOutput(Generate);

    private static void Generate(IncrementalGeneratorPostInitializationContext context)
    {
        var source = "internal static class GeneratedValue { internal const int Value = 42; }";
        context.AddSource("GeneratedValue.g.cs", source);
    }
}
