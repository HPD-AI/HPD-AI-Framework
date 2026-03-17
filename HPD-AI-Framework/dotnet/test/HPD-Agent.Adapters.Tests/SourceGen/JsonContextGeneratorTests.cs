using FluentAssertions;
using HPD.Agent.Adapters.Tests.TestInfrastructure;

namespace HPD.Agent.Adapters.Tests.SourceGen;

/// <summary>
/// Tests for <see cref="HPD.Agent.Adapters.SourceGenerator.Generators.JsonContextGenerator"/>.
///
/// <b>Important</b>: <c>JsonContextGenerator</c> is intentionally a no-op.
/// System.Text.Json's source generator cannot see output from other Roslyn generators,
/// so a generated partial class cannot satisfy <c>JsonSerializerContext</c>'s abstract members.
/// Each adapter project must hand-write its own <c>JsonSerializerContext</c> subclass.
///
/// All tests in this class document that <em>no</em> file is ever emitted, regardless of inputs.
/// </summary>
public class JsonContextGeneratorTests
{
    // ── No payloads ───────────────────────────────────────────────────

    [Fact]
    public void JsonContext_NoPayloads_NoFileGenerated()
    {
        var source = "namespace Test; public class Nothing { }";

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().NotContain("AdaptersJsonSerializerContext.g.cs");
    }

    // ── With payloads — still no file (generator is a no-op) ─────────

    [Fact]
    public void JsonContext_OnePayload_NoFileGenerated()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [WebhookPayload]
            public record SlackEvent(string Type);
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        // JsonContextGenerator is a no-op — hand-write JsonSerializerContext in each adapter project.
        names.Should().NotContain("AdaptersJsonSerializerContext.g.cs");
    }

    [Fact]
    public void JsonContext_MultiplePayloads_NoFileGenerated()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [WebhookPayload] public record EventA(string Type);
            [WebhookPayload] public record EventB(string Type);
            [WebhookPayload] public record EventC(string Type);
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().NotContain("AdaptersJsonSerializerContext.g.cs");
    }

    [Fact]
    public void JsonContext_AdapterAndPayload_NoJsonContextFile()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter { }

            [WebhookPayload]
            public record SlackEvent(string Type);
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        // Adapter files are still generated — just not the JSON context.
        names.Should().Contain("SlackAdapterRegistration.g.cs");
        names.Should().Contain("SlackAdapterDispatch.g.cs");
        names.Should().Contain("AdapterRegistry.g.cs");
        names.Should().NotContain("AdaptersJsonSerializerContext.g.cs");
    }

    [Fact]
    public void JsonContext_NoFileGenerated_NoDiagnosticErrors()
    {
        // Verify the no-op produces no generator diagnostics.
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [WebhookPayload]
            public record Payload(string Type);
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);

        result.Diagnostics.Should().NotContain(d => d.Id.StartsWith("HPDA"));
    }
}
