using HPD.Agent.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class DurableEventDiagnosticsTests
{
    private const string Contracts = """
        namespace HPD.Agent
        {
            public abstract record AgentEvent;
            public abstract record AgentInputEvent;
            public interface AgentStructEvent { }
        }
        namespace HPD.Agent.Serialization
        {
            [System.AttributeUsage(System.AttributeTargets.All)]
            public sealed class DurableEventAttribute : System.Attribute { }
        }
        """;

    [Theory]
    [InlineData("public abstract record Bad : HPD.Agent.AgentEvent;", "abstract")]
    [InlineData("public sealed record Bad<T> : HPD.Agent.AgentEvent;", "open generic")]
    [InlineData("public sealed record Bad;", "does not derive")]
    [InlineData("public sealed record Bad : HPD.Agent.AgentInputEvent;", "input contracts")]
    [InlineData("public readonly record struct Bad : HPD.Agent.AgentStructEvent;", "struct events")]
    public void Durable_event_rejects_non_journal_contracts(string declaration, string reason)
    {
        var source = $$"""
            using HPD.Agent.Serialization;
            [DurableEvent]
            {{declaration}}
            """;
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "durable-event-diagnostics",
            [CSharpSyntaxTree.ParseText(Contracts), CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new CustomEventSourceGenerator().AsSourceGenerator());

        var result = driver.RunGenerators(compilation).GetRunResult();

        var diagnostic = Assert.Single(result.Diagnostics, static item => item.Id == "HPDAEVT003");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(reason, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Concrete_closed_agent_event_is_a_valid_durable_target()
    {
        var source = """
            using HPD.Agent.Serialization;
            [DurableEvent]
            public sealed record Good : HPD.Agent.AgentEvent;
            """;
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "durable-event-diagnostics",
            [CSharpSyntaxTree.ParseText(Contracts), CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new CustomEventSourceGenerator().AsSourceGenerator());

        var result = driver.RunGenerators(compilation).GetRunResult();

        Assert.DoesNotContain(result.Diagnostics, static item => item.Id == "HPDAEVT003");
    }
}
