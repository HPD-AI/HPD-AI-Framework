using System.Collections.Immutable;
using HPD.Agent.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class CustomInputSourceGeneratorTests
{
    [Fact]
    public void AgentInputOptIn_ControlsPublicExposure()
    {
        const string source = """
            using HPD.Agent;
            using HPD.Agent.Serialization;
            using System.Text.Json.Serialization;

            namespace GeneratedInputs;

            [AgentInput]
            public sealed record BeginAnalysisInputEvent(string DatasetId) : AgentInputEvent;

            public sealed record InternalCheckpointInputEvent(string Token) : AgentInputEvent;

            [JsonSerializable(typeof(BeginAnalysisInputEvent))]
            [JsonSerializable(typeof(InternalCheckpointInputEvent))]
            internal partial class GeneratedInputsJsonContext : JsonSerializerContext;
            """;

        var result = Run(source, OutputKind.ConsoleApplication);

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        var generatedSources = result.Results
            .SelectMany(generator => generator.GeneratedSources)
            .ToArray();
        var module = generatedSources
            .Single(generated => generated.HintName == "GeneratedAgentInputModule.g.cs")
            .SourceText
            .ToString();
        Assert.Contains("InputType = typeof(global::GeneratedInputs.BeginAnalysisInputEvent)", module);
        Assert.Contains("InputType = typeof(global::GeneratedInputs.InternalCheckpointInputEvent)", module);
        Assert.Contains("IsPublic = true", module);
        Assert.Contains("IsPublic = false", module);

        var application = generatedSources
            .Single(generated => generated.HintName == "GeneratedAgentInputComposition.g.cs")
            .SourceText
            .ToString();
        Assert.Contains("AgentInputComposition.Create", application);
        Assert.Contains("AgentInputCompositionHost.RegisterApplication", application);
    }

    [Fact]
    public void InputWithoutJsonMetadata_ReportsDiagnostic()
    {
        const string source = """
            using HPD.Agent;

            namespace GeneratedInputs;

            public sealed record MissingMetadataInputEvent(string Value) : AgentInputEvent;
            """;

        var result = Run(source, OutputKind.DynamicallyLinkedLibrary);

        var diagnostic = Assert.Single(result.Diagnostics, value => value.Id == "HPDINP002");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("MissingMetadataInputEvent", diagnostic.GetMessage());
    }

    [Fact]
    public void AgentInputOnNonInput_ReportsDiagnostic()
    {
        const string source = """
            using HPD.Agent.Serialization;

            namespace GeneratedInputs;

            [AgentInput]
            public sealed record NotAnInputEvent(string Value);
            """;

        var result = Run(source, OutputKind.DynamicallyLinkedLibrary);

        var diagnostic = Assert.Single(result.Diagnostics, value => value.Id == "HPDINP005");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("does not derive from AgentInputEvent", diagnostic.GetMessage());
    }

    private static GeneratorDriverRunResult Run(string source, OutputKind outputKind)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToImmutableArray<MetadataReference>();
        var compilation = CSharpCompilation.Create(
            "GeneratedAgentInputs",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(outputKind));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new CustomInputSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        return driver.RunGenerators(compilation).GetRunResult();
    }
}
