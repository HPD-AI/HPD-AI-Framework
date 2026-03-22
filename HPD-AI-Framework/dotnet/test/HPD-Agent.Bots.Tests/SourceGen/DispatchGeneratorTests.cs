using FluentAssertions;
using HPD.Agent.Bots.Tests.TestInfrastructure;

namespace HPD.Agent.Bots.Tests.SourceGen;

/// <summary>
/// Tests for <see cref="HPD.Agent.Bots.SourceGenerator.Generators.DispatchGenerator"/>.
/// Verifies that <c>HandleWebhookAsync</c> and related dispatch infrastructure is generated
/// correctly based on the adapter's attribute configuration.
/// </summary>
public class DispatchGeneratorTests
{
    private static readonly string MinimalBot = """
        using HPD.Agent.Bots;
        namespace Test;
        [HpdBot("slack")]
        public partial class SlackBot { }
        """;

    private static readonly string BotWithPreDispatch = """
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

    private static readonly string BotWithBodyExtractor = """
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

    private static readonly string BotWithBothHooks = """
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

            [HpdBodyExtractor]
            private (string? eventType, byte[] dispatchBytes) ExtractDispatch(HttpContext ctx, byte[] bodyBytes)
                => (null, bodyBytes);
        }
        """;

    private static readonly string BotWithOneHandler = """
        using HPD.Agent.Bots;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        namespace Test;
        [HpdBot("slack")]
        public partial class SlackBot
        {
            [HpdWebhookHandler("app_mention")]
            private Task<IResult> HandleMention(HttpContext ctx, byte[] body, CancellationToken ct)
                => Task.FromResult(Results.Ok());
        }
        """;

    private static readonly string BotWithMultipleHandlers = """
        using HPD.Agent.Bots;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        namespace Test;
        [HpdBot("slack")]
        public partial class SlackBot
        {
            [HpdWebhookHandler("app_mention")]
            private Task<IResult> HandleMention(HttpContext ctx, byte[] body, CancellationToken ct)
                => Task.FromResult(Results.Ok());

            [HpdWebhookHandler("message")]
            private Task<IResult> HandleMessage(HttpContext ctx, byte[] body, CancellationToken ct)
                => Task.FromResult(Results.Ok());

            [HpdWebhookHandler("block_actions")]
            private Task<IResult> HandleBlockAction(HttpContext ctx, byte[] body, CancellationToken ct)
                => Task.FromResult(Results.Ok());
        }
        """;

    private static string GetDispatch(string source)
    {
        var result = SourceGenHelper.RunGenerator(source, out _);
        return SourceGenHelper.GetGeneratedFile(result, "SlackBotDispatch.g.cs")
               ?? throw new InvalidOperationException("SlackBotDispatch.g.cs was not generated");
    }

    // ── Entry point ───────────────────────────────────────────────────

    [Fact]
    public void Dispatch_GeneratesHandleWebhookAsyncMethod()
    {
        var dispatch = GetDispatch(MinimalBot);

        dispatch.Should().Contain("public async Task<IResult> HandleWebhookAsync(HttpContext ctx)");
    }

    [Fact]
    public void Dispatch_GeneratesFileNamed_BotClassDispatch()
    {
        var result = SourceGenHelper.RunGenerator(MinimalBot, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().Contain("SlackBotDispatch.g.cs");
    }

    // ── Body reading ──────────────────────────────────────────────────

    [Fact]
    public void Dispatch_ReadsBodyOnce_WithCopyToAsync()
    {
        var dispatch = GetDispatch(MinimalBot);

        // Body is read once into a MemoryStream
        dispatch.Should().Contain("CopyToAsync");
    }

    // ── Pre-dispatch hook ─────────────────────────────────────────────

    [Fact]
    public void Dispatch_WithPreDispatch_EmitsHookCall()
    {
        var dispatch = GetDispatch(BotWithPreDispatch);

        dispatch.Should().Contain("await PreDispatchAsync(ctx, bodyBytes)");
    }

    [Fact]
    public void Dispatch_WithPreDispatch_ShortCircuitsOnNonNull()
    {
        var dispatch = GetDispatch(BotWithPreDispatch);

        dispatch.Should().Contain("if (preResult is not null) return preResult;");
    }

    [Fact]
    public void Dispatch_WithoutPreDispatch_OmitsHookCall()
    {
        var dispatch = GetDispatch(MinimalBot);

        dispatch.Should().NotContain("PreDispatchAsync");
    }

    // ── Body extractor hook ───────────────────────────────────────────

    [Fact]
    public void Dispatch_WithBodyExtractor_EmitsHookCall()
    {
        var dispatch = GetDispatch(BotWithBodyExtractor);

        dispatch.Should().Contain("ExtractDispatch(ctx, bodyBytes)");
    }

    [Fact]
    public void Dispatch_WithBodyExtractor_OmitsDefaultExtractEventType()
    {
        var dispatch = GetDispatch(BotWithBodyExtractor);

        dispatch.Should().NotContain("ExtractEventType(");
    }

    [Fact]
    public void Dispatch_WithoutBodyExtractor_EmitsDefaultExtractEventType()
    {
        var dispatch = GetDispatch(MinimalBot);

        dispatch.Should().Contain("ExtractEventType(");
    }

    [Fact]
    public void Dispatch_WithoutBodyExtractor_DefaultExtractorHandlesEventCallbackEnvelope()
    {
        var dispatch = GetDispatch(MinimalBot);

        dispatch.Should().Contain("event_callback");
    }

    [Fact]
    public void Dispatch_WithBothHooks_EmitsBothCalls()
    {
        var dispatch = GetDispatch(BotWithBothHooks);

        dispatch.Should().Contain("await PreDispatchAsync(ctx, bodyBytes)");
        dispatch.Should().Contain("ExtractDispatch(ctx, bodyBytes)");
    }

    // ── No Slack-specific hardcoding ──────────────────────────────────

    [Fact]
    public void Dispatch_DoesNotContainHardcodedUrlVerification()
    {
        var dispatch = GetDispatch(MinimalBot);

        dispatch.Should().NotContain("url_verification");
    }

    [Fact]
    public void Dispatch_DoesNotContainHardcodedFormUrlencoded()
    {
        var dispatch = GetDispatch(MinimalBot);

        dispatch.Should().NotContain("application/x-www-form-urlencoded");
    }

    [Fact]
    public void Dispatch_DoesNotContainWebhookSignatureVerifier()
    {
        var dispatch = GetDispatch(MinimalBot);

        dispatch.Should().NotContain("WebhookSignatureVerifier");
    }

    // ── Exception mapping ─────────────────────────────────────────────

    [Fact]
    public void Dispatch_CatchesBotAuthenticationException_Returns401()
    {
        var dispatch = GetDispatch(MinimalBot);

        dispatch.Should().Contain("BotAuthenticationException");
        dispatch.Should().Contain("Results.Unauthorized()");
    }

    [Fact]
    public void Dispatch_CatchesBotRateLimitException_Returns429()
    {
        var dispatch = GetDispatch(MinimalBot);

        dispatch.Should().Contain("BotRateLimitException");
        dispatch.Should().Contain("Results.StatusCode(429)");
    }

    [Fact]
    public void Dispatch_CatchesBotPermissionException_Returns403()
    {
        var dispatch = GetDispatch(MinimalBot);

        dispatch.Should().Contain("BotPermissionException");
        dispatch.Should().Contain("Results.Forbid()");
    }

    [Fact]
    public void Dispatch_CatchesBotNotFoundException_Returns404()
    {
        var dispatch = GetDispatch(MinimalBot);

        dispatch.Should().Contain("BotNotFoundException");
        dispatch.Should().Contain("Results.NotFound()");
    }

    // ── Event dispatch switch ─────────────────────────────────────────

    [Fact]
    public void Dispatch_SingleHandler_GeneratesSwitchCase()
    {
        var dispatch = GetDispatch(BotWithOneHandler);

        dispatch.Should().Contain("\"app_mention\"");
        dispatch.Should().Contain("HandleMention(");
    }

    [Fact]
    public void Dispatch_MultipleHandlers_AllCasesPresent()
    {
        var dispatch = GetDispatch(BotWithMultipleHandlers);

        dispatch.Should().Contain("\"app_mention\"");
        dispatch.Should().Contain("\"message\"");
        dispatch.Should().Contain("\"block_actions\"");
    }

    [Fact]
    public void Dispatch_UnknownEventType_DefaultArmReturnsOk()
    {
        var dispatch = GetDispatch(MinimalBot);

        dispatch.Should().Contain("default: return Results.Ok()");
    }

    [Fact]
    public void Dispatch_HandlerWithMultipleEventTypes_GeneratesCasePerType()
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
                private Task<IResult> HandleBoth(HttpContext ctx, byte[] body, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        var dispatch = GetDispatch(source);

        dispatch.Should().Contain("\"message\"");
        dispatch.Should().Contain("\"app_mention\"");
        // Both cases route to the same method
        dispatch.Should().Contain("HandleBoth(");
    }

    // ── Partial class structure ───────────────────────────────────────

    [Fact]
    public void Dispatch_GeneratesPartialClassExtension()
    {
        var dispatch = GetDispatch(MinimalBot);

        dispatch.Should().Contain("public partial class SlackBot");
    }

    [Fact]
    public void Dispatch_GeneratedInCorrectNamespace()
    {
        var dispatch = GetDispatch(MinimalBot);

        dispatch.Should().Contain("namespace Test");
    }
}
