using HPD.Agent.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class EventContentPolicyDiagnosticsTests
{
    private const string Contracts = """
        namespace HPD.Agent
        {
            public abstract record AgentEvent;
            public abstract record AgentInputEvent;
            public interface AgentStructEvent { }
            public enum ContentSource { User = 0, Agent = 1, System = 2 }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PersistEventContentAttribute(string kind) : System.Attribute
            {
                public string Kind { get; } = kind;
                public string ContentType { get; init; } = "application/json";
                public ContentSource Origin { get; init; } = ContentSource.Agent;
                public string? Scope { get; init; }
            }
        }
        """;

    [Theory]
    [InlineData("[PersistEventContent(\"x\")] public abstract record Bad : AgentEvent;", "abstract")]
    [InlineData("[PersistEventContent(\"x\")] public sealed record Bad<T> : AgentEvent;", "open generic")]
    [InlineData("[PersistEventContent(\"x\")] public sealed record Bad;", "does not derive")]
    [InlineData("[PersistEventContent(\"x\")] public sealed record Bad : AgentInputEvent;", "input contracts")]
    [InlineData("[PersistEventContent(\"\")] public sealed record Bad : AgentEvent;", "kind")]
    [InlineData("[PersistEventContent(\"x\", ContentType = \" \" )] public sealed record Bad : AgentEvent;", "ContentType")]
    [InlineData("[PersistEventContent(\"x\", Scope = \" \" )] public sealed record Bad : AgentEvent;", "Scope")]
    [InlineData("[PersistEventContent(\"x\", Origin = (ContentSource)42)] public sealed record Bad : AgentEvent;", "Origin")]
    public void Invalid_policy_is_rejected(string declaration, string reason)
    {
        var result = Run($$"""
            using HPD.Agent;
            {{declaration}}
            """);

        var diagnostic = Assert.Single(result.Diagnostics, static item => item.Id == "HPDAEVT004");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(reason, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Concrete_event_with_representable_policy_is_valid()
    {
        var result = Run("""
            using HPD.Agent;
            [PersistEventContent("result", ContentType = "application/json", Scope = "session", Origin = ContentSource.System)]
            public sealed record Good : AgentEvent;
            """);

        Assert.DoesNotContain(result.Diagnostics, static item => item.Id == "HPDAEVT004");
    }

    private static GeneratorDriverRunResult Run(string source)
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "event-policy-diagnostics",
            [CSharpSyntaxTree.ParseText(Contracts), CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new CustomEventSourceGenerator().AsSourceGenerator());
        return driver.RunGenerators(compilation).GetRunResult();
    }
}
