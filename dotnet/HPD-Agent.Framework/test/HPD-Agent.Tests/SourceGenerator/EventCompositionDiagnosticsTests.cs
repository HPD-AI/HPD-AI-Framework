using HPD.Agent.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class EventCompositionDiagnosticsTests
{
    private const string Contracts = """
        namespace HPD.Agent
        {
            public abstract record AgentEvent;
            public interface AgentStructEvent { }
        }
        namespace HPD.Agent.Serialization
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class EventTypeAttribute(string discriminator) : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class HpdAgentEventModuleManifestAttribute(string moduleId, System.Type provider, params System.Type[] dependencies) : System.Attribute { }
        }
        """;

    [Fact]
    public void Empty_discriminator_reports_HPDAEVT005_at_the_declaration()
    {
        var result = Run("""
            using HPD.Agent;
            using HPD.Agent.Serialization;
            [EventType(" ")]
            public sealed record BadEvent : AgentEvent;
            """);

        var diagnostic = Assert.Single(result.Diagnostics, static value => value.Id == "HPDAEVT005");
        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.Contains("SCREAMING_SNAKE_CASE", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lower_case")]
    [InlineData("BAD-NAME")]
    [InlineData("1_EVENT")]
    public void Noncanonical_discriminator_reports_HPDAEVT005(string discriminator)
    {
        var result = Run($$"""
            using HPD.Agent;
            using HPD.Agent.Serialization;
            [EventType("{{discriminator}}")] public sealed record BadEvent : AgentEvent;
            """);

        Assert.Single(result.Diagnostics, static value => value.Id == "HPDAEVT005");
    }

    [Fact]
    public void Duplicate_discriminator_reports_HPDAEVT005_at_an_event()
    {
        var result = Run("""
            using HPD.Agent;
            using HPD.Agent.Serialization;
            [EventType("SAME")] public sealed record FirstEvent : AgentEvent;
            [EventType("SAME")] public sealed record SecondEvent : AgentEvent;
            """);

        var diagnostic = Assert.Single(result.Diagnostics, static value => value.Id == "HPDAEVT005");
        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.Contains("FirstEvent", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("SecondEvent", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Application_with_uncloseable_manifest_reports_HPDAEVT007()
    {
        var result = Run("""
            using HPD.Agent.Serialization;
            [assembly: HpdAgentEventModuleManifest("", typeof(PublicProvider))]
            public static class PublicProvider { }
            """, OutputKind.ConsoleApplication);

        var diagnostic = Assert.Single(result.Diagnostics, static value => value.Id == "HPDAEVT007");
        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.Contains("stable module ID", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static GeneratorDriverRunResult Run(string source, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "event-composition-diagnostics",
            [CSharpSyntaxTree.ParseText(Contracts), CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(outputKind));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new CustomEventSourceGenerator().AsSourceGenerator());
        return driver.RunGenerators(compilation).GetRunResult();
    }
}
