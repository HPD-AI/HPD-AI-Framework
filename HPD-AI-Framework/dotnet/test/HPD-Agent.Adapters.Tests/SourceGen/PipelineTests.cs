using FluentAssertions;
using HPD.Agent.Adapters.Tests.TestInfrastructure;

namespace HPD.Agent.Adapters.Tests.SourceGen;

/// <summary>
/// Tests for the <see cref="HPD.Agent.Adapters.SourceGenerator.AdapterSourceGenerator"/> pipeline —
/// how it resolves <c>AdapterInfo</c>, <c>StreamingInfo</c>,
/// <c>HandlerInfo</c>, and <c>WebhookPayloadInfo</c> from symbol metadata.
/// These tests inspect generated output to verify the pipeline extracted the right values.
/// </summary>
public class PipelineTests
{
    // ── No attributes → no output ─────────────────────────────────────

    [Fact]
    public void Pipeline_NoAttributes_ProducesNoFiles()
    {
        var source = "namespace Test; public class Plain { }";

        var result = SourceGenHelper.RunGenerator(source, out _);

        SourceGenHelper.GetGeneratedFileNames(result).Should().BeEmpty();
    }

    // ── AdapterInfo extraction ────────────────────────────────────────

    [Fact]
    public void Pipeline_AdapterName_TakenFromAttribute()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("my-platform")]
            public partial class MyAdapter { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "AdapterRegistry.g.cs");

        registry!.Should().Contain("\"my-platform\"");
    }

    [Fact]
    public void Pipeline_AdapterWithNoHandlers_StillGeneratesDispatch()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter { }
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().Contain("SlackAdapterDispatch.g.cs");
    }

    // ── HasPreDispatch / HasBodyExtractor detection ───────────────────

    [Fact]
    public void Pipeline_HasPreDispatch_EmitsHookCallInDispatch()
    {
        var source = """
            using HPD.Agent.Adapters;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter
            {
                [HpdPreDispatch]
                private async Task<IResult?> PreDispatchAsync(HttpContext ctx, byte[] bodyBytes)
                    => null;
            }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var dispatch = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterDispatch.g.cs");

        dispatch!.Should().Contain("await PreDispatchAsync(ctx, bodyBytes)");
        dispatch.Should().Contain("if (preResult is not null) return preResult;");
    }

    [Fact]
    public void Pipeline_HasBodyExtractor_EmitsHookCallInDispatch()
    {
        var source = """
            using HPD.Agent.Adapters;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter
            {
                [HpdBodyExtractor]
                private (string? eventType, byte[] dispatchBytes) ExtractDispatch(HttpContext ctx, byte[] bodyBytes)
                    => (null, bodyBytes);
            }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var dispatch = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterDispatch.g.cs");

        dispatch!.Should().Contain("ExtractDispatch(ctx, bodyBytes)");
        dispatch.Should().NotContain("ExtractEventType(");
    }

    // ── StreamingInfo extraction ──────────────────────────────────────

    [Fact]
    public void Pipeline_StreamingInfo_ExtractsStrategyAndDebounce()
    {
        // Streaming info is stored in AdapterInfo and could influence generated output.
        // Currently the dispatch generator doesn't write streaming info to the file,
        // but we verify the adapter info was successfully resolved (no diagnostic errors).
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            [HpdStreaming(StreamingStrategy.PostAndEdit, DebounceMs = 200)]
            public partial class SlackAdapter { }
            """;

        var result      = SourceGenHelper.RunGenerator(source, out _);
        var diagnostics = result.Diagnostics;

        diagnostics.Should().NotContain(d => d.Id.StartsWith("HPDA"));
        SourceGenHelper.GetGeneratedFileNames(result).Should().Contain("SlackAdapterRegistration.g.cs");
    }

    // ── HandlerInfo extraction ────────────────────────────────────────

    [Fact]
    public void Pipeline_HandlerWithMultipleAttributes_EachEventTypeGeneratedAsSwitchCase()
    {
        var source = """
            using HPD.Agent.Adapters;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter
            {
                [HpdWebhookHandler("message")]
                [HpdWebhookHandler("app_mention")]
                private Task<IResult> Handle(HttpContext ctx, byte[] body, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var dispatch = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterDispatch.g.cs");

        dispatch!.Should().Contain("\"message\"");
        dispatch.Should().Contain("\"app_mention\"");
        // Each event type becomes its own switch case
        var caseCount = CountOccurrences(dispatch, "case \"");
        caseCount.Should().BeGreaterThanOrEqualTo(2); // one case per event type
    }

    // ── Permission handler detection ──────────────────────────────────

    [Fact]
    public void Pipeline_PermissionHandler_DoesNotCauseErrors()
    {
        var source = """
            using HPD.Agent.Adapters;
            using System.Threading;
            using System.Threading.Tasks;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter
            {
                [HpdPermissionHandler]
                private Task HandlePerm(CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);

        result.Diagnostics.Should().NotContain(d => d.Id.StartsWith("HPDA"));
    }

    // ── PayloadInfo extraction ────────────────────────────────────────

    [Fact]
    public void Pipeline_PayloadOnly_NoAdapterFiles()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [WebhookPayload]
            public record MyEvent(string Type);
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        // JsonContextGenerator is a no-op (STJ source gen cannot consume Roslyn generator output)
        // so [WebhookPayload]-only source produces no generated files at all.
        names.Should().NotContain(n => n.Contains("Registration") || n.Contains("Dispatch") || n == "AdapterRegistry.g.cs");
        names.Should().NotContain("AdaptersJsonSerializerContext.g.cs");
    }

    [Fact]
    public void Pipeline_AdapterOnly_NoJsonContext()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter { }
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().NotContain("AdaptersJsonSerializerContext.g.cs");
    }

    // ── Generated code compiles ───────────────────────────────────────

    [Fact]
    public void Pipeline_MinimalAdapter_GeneratedCodeCompilesWithZeroErrors()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter
            {
                private SlackAdapterConfig _config = new();
            }
            public class SlackAdapterConfig
            {
                public string SigningSecret { get; set; } = "";
            }
            """;

        SourceGenHelper.RunGenerator(source, out var outputCompilation);
        var errors = SourceGenHelper.GetCompilationErrors(outputCompilation);

        // Filter out errors from test assembly itself (unresolved framework refs in test compilation)
        // We only care that the GENERATOR did not produce syntax/logic errors in its own output
        var generatorErrors = errors
            .Where(d => d.Location.SourceTree?.FilePath.EndsWith(".g.cs") == true)
            .ToList();

        generatorErrors.Should().BeEmpty(
            because: "generated code should be syntactically and logically valid C#");
    }

    [Fact]
    public void Pipeline_MultipleAdaptersAndPayloads_AllFilesEmitted()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]  public partial class SlackAdapter { }
            [HpdAdapter("teams")]  public partial class TeamsAdapter { }
            [WebhookPayload] public record EventA(string Type);
            [WebhookPayload] public record EventB(string Type);
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().Contain("SlackAdapterRegistration.g.cs");
        names.Should().Contain("SlackAdapterDispatch.g.cs");
        names.Should().Contain("TeamsAdapterRegistration.g.cs");
        names.Should().Contain("TeamsAdapterDispatch.g.cs");
        names.Should().Contain("AdapterRegistry.g.cs");
        // JsonContextGenerator is a no-op — no AdaptersJsonSerializerContext.g.cs is emitted
        names.Should().NotContain("AdaptersJsonSerializerContext.g.cs");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
