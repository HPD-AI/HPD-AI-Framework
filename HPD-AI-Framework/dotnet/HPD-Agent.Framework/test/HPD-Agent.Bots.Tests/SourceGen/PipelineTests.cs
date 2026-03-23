using FluentAssertions;
using HPD.Agent.Bots.Tests.TestInfrastructure;

namespace HPD.Agent.Bots.Tests.SourceGen;

/// <summary>
/// Tests for the <see cref="HPD.Agent.Bots.SourceGenerator.BotSourceGenerator"/> pipeline —
/// how it resolves <c>BotInfo</c>, <c>StreamingInfo</c>,
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

    // ── BotInfo extraction ────────────────────────────────────────

    [Fact]
    public void Pipeline_BotName_TakenFromAttribute()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("my-platform")]
            public partial class MyBot { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "BotRegistry.g.cs");

        registry!.Should().Contain("\"my-platform\"");
    }

    [Fact]
    public void Pipeline_BotWithNoHandlers_StillGeneratesDispatch()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot { }
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().Contain("SlackBotDispatch.g.cs");
    }

    // ── HasPreDispatch / HasBodyExtractor detection ───────────────────

    [Fact]
    public void Pipeline_HasPreDispatch_EmitsHookCallInDispatch()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot
            {
                [HpdPreDispatch]
                private async Task<IResult?> PreDispatchAsync(HttpContext ctx, byte[] bodyBytes)
                    => null;
            }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var dispatch = SourceGenHelper.GetGeneratedFile(result, "SlackBotDispatch.g.cs");

        dispatch!.Should().Contain("await PreDispatchAsync(ctx, bodyBytes)");
        dispatch.Should().Contain("if (preResult is not null) return preResult;");
    }

    [Fact]
    public void Pipeline_HasBodyExtractor_EmitsHookCallInDispatch()
    {
        var source = """
            using HPD.Agent.Bots;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot
            {
                [HpdBodyExtractor]
                private (string? eventType, byte[] dispatchBytes) ExtractDispatch(HttpContext ctx, byte[] bodyBytes)
                    => (null, bodyBytes);
            }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var dispatch = SourceGenHelper.GetGeneratedFile(result, "SlackBotDispatch.g.cs");

        dispatch!.Should().Contain("ExtractDispatch(ctx, bodyBytes)");
        dispatch.Should().NotContain("ExtractEventType(");
    }

    // ── StreamingInfo extraction ──────────────────────────────────────

    [Fact]
    public void Pipeline_StreamingInfo_ExtractsStrategyAndDebounce()
    {
        // Streaming info is stored in BotInfo and could influence generated output.
        // Currently the dispatch generator doesn't write streaming info to the file,
        // but we verify the adapter info was successfully resolved (no diagnostic errors).
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            [HpdStreaming(StreamingStrategy.PostAndEdit, DebounceMs = 200)]
            public partial class SlackBot { }
            """;

        var result      = SourceGenHelper.RunGenerator(source, out _);
        var diagnostics = result.Diagnostics;

        diagnostics.Should().NotContain(d => d.Id.StartsWith("HPDA"));
        SourceGenHelper.GetGeneratedFileNames(result).Should().Contain("SlackBotRegistration.g.cs");
    }

    // ── HandlerInfo extraction ────────────────────────────────────────

    [Fact]
    public void Pipeline_HandlerWithMultipleAttributes_EachEventTypeGeneratedAsSwitchCase()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot
            {
                [HpdWebhookHandler("message")]
                [HpdWebhookHandler("app_mention")]
                private Task<IResult> Handle(HttpContext ctx, byte[] body, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var dispatch = SourceGenHelper.GetGeneratedFile(result, "SlackBotDispatch.g.cs");

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
            using HPD.Agent.Bots;
            using System.Threading;
            using System.Threading.Tasks;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot
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
    public void Pipeline_PayloadOnly_NoBotFiles()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [WebhookPayload]
            public record MyEvent(string Type);
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        // JsonContextGenerator is a no-op (STJ source gen cannot consume Roslyn generator output)
        // so [WebhookPayload]-only source produces no generated files at all.
        names.Should().NotContain(n => n.Contains("Registration") || n.Contains("Dispatch") || n == "BotRegistry.g.cs");
        names.Should().NotContain("BotsJsonSerializerContext.g.cs");
    }

    [Fact]
    public void Pipeline_BotOnly_NoJsonContext()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot { }
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().NotContain("BotsJsonSerializerContext.g.cs");
    }

    // ── Generated code compiles ───────────────────────────────────────

    [Fact]
    public void Pipeline_MinimalBot_GeneratedCodeCompilesWithZeroErrors()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot
            {
                private SlackBotConfig _config = new();
            }
            public class SlackBotConfig
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
    public void Pipeline_MultipleBotsAndPayloads_AllFilesEmitted()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]  public partial class SlackBot { }
            [HpdBot("teams")]  public partial class TeamsBot { }
            [WebhookPayload] public record EventA(string Type);
            [WebhookPayload] public record EventB(string Type);
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().Contain("SlackBotRegistration.g.cs");
        names.Should().Contain("SlackBotDispatch.g.cs");
        names.Should().Contain("TeamsBotRegistration.g.cs");
        names.Should().Contain("TeamsBotDispatch.g.cs");
        names.Should().Contain("BotRegistry.g.cs");
        // JsonContextGenerator is a no-op — no BotsJsonSerializerContext.g.cs is emitted
        names.Should().NotContain("BotsJsonSerializerContext.g.cs");
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
