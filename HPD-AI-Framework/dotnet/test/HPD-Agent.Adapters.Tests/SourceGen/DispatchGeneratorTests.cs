using FluentAssertions;
using HPD.Agent.Adapters.Tests.TestInfrastructure;

namespace HPD.Agent.Adapters.Tests.SourceGen;

/// <summary>
/// Tests for <see cref="HPD.Agent.Adapters.SourceGenerator.Generators.DispatchGenerator"/>.
/// Verifies that <c>HandleWebhookAsync</c> and related dispatch infrastructure is generated
/// correctly based on the adapter's attribute configuration.
/// </summary>
public class DispatchGeneratorTests
{
    private static readonly string MinimalAdapter = """
        using HPD.Agent.Adapters;
        namespace Test;
        [HpdAdapter("slack")]
        public partial class SlackAdapter { }
        """;

    private static readonly string AdapterWithPreDispatch = """
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

    private static readonly string AdapterWithBodyExtractor = """
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

    private static readonly string AdapterWithBothHooks = """
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

            [HpdBodyExtractor]
            private (string? eventType, byte[] dispatchBytes) ExtractDispatch(HttpContext ctx, byte[] bodyBytes)
                => (null, bodyBytes);
        }
        """;

    private static readonly string AdapterWithOneHandler = """
        using HPD.Agent.Adapters;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        namespace Test;
        [HpdAdapter("slack")]
        public partial class SlackAdapter
        {
            [HpdWebhookHandler("app_mention")]
            private Task<IResult> HandleMention(HttpContext ctx, byte[] body, CancellationToken ct)
                => Task.FromResult(Results.Ok());
        }
        """;

    private static readonly string AdapterWithMultipleHandlers = """
        using HPD.Agent.Adapters;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        namespace Test;
        [HpdAdapter("slack")]
        public partial class SlackAdapter
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
        return SourceGenHelper.GetGeneratedFile(result, "SlackAdapterDispatch.g.cs")
               ?? throw new InvalidOperationException("SlackAdapterDispatch.g.cs was not generated");
    }

    // ── Entry point ───────────────────────────────────────────────────

    [Fact]
    public void Dispatch_GeneratesHandleWebhookAsyncMethod()
    {
        var dispatch = GetDispatch(MinimalAdapter);

        dispatch.Should().Contain("public async Task<IResult> HandleWebhookAsync(HttpContext ctx)");
    }

    [Fact]
    public void Dispatch_GeneratesFileNamed_AdapterClassDispatch()
    {
        var result = SourceGenHelper.RunGenerator(MinimalAdapter, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().Contain("SlackAdapterDispatch.g.cs");
    }

    // ── Body reading ──────────────────────────────────────────────────

    [Fact]
    public void Dispatch_ReadsBodyOnce_WithCopyToAsync()
    {
        var dispatch = GetDispatch(MinimalAdapter);

        // Body is read once into a MemoryStream
        dispatch.Should().Contain("CopyToAsync");
    }

    // ── Pre-dispatch hook ─────────────────────────────────────────────

    [Fact]
    public void Dispatch_WithPreDispatch_EmitsHookCall()
    {
        var dispatch = GetDispatch(AdapterWithPreDispatch);

        dispatch.Should().Contain("await PreDispatchAsync(ctx, bodyBytes)");
    }

    [Fact]
    public void Dispatch_WithPreDispatch_ShortCircuitsOnNonNull()
    {
        var dispatch = GetDispatch(AdapterWithPreDispatch);

        dispatch.Should().Contain("if (preResult is not null) return preResult;");
    }

    [Fact]
    public void Dispatch_WithoutPreDispatch_OmitsHookCall()
    {
        var dispatch = GetDispatch(MinimalAdapter);

        dispatch.Should().NotContain("PreDispatchAsync");
    }

    // ── Body extractor hook ───────────────────────────────────────────

    [Fact]
    public void Dispatch_WithBodyExtractor_EmitsHookCall()
    {
        var dispatch = GetDispatch(AdapterWithBodyExtractor);

        dispatch.Should().Contain("ExtractDispatch(ctx, bodyBytes)");
    }

    [Fact]
    public void Dispatch_WithBodyExtractor_OmitsDefaultExtractEventType()
    {
        var dispatch = GetDispatch(AdapterWithBodyExtractor);

        dispatch.Should().NotContain("ExtractEventType(");
    }

    [Fact]
    public void Dispatch_WithoutBodyExtractor_EmitsDefaultExtractEventType()
    {
        var dispatch = GetDispatch(MinimalAdapter);

        dispatch.Should().Contain("ExtractEventType(");
    }

    [Fact]
    public void Dispatch_WithoutBodyExtractor_DefaultExtractorHandlesEventCallbackEnvelope()
    {
        var dispatch = GetDispatch(MinimalAdapter);

        dispatch.Should().Contain("event_callback");
    }

    [Fact]
    public void Dispatch_WithBothHooks_EmitsBothCalls()
    {
        var dispatch = GetDispatch(AdapterWithBothHooks);

        dispatch.Should().Contain("await PreDispatchAsync(ctx, bodyBytes)");
        dispatch.Should().Contain("ExtractDispatch(ctx, bodyBytes)");
    }

    // ── No Slack-specific hardcoding ──────────────────────────────────

    [Fact]
    public void Dispatch_DoesNotContainHardcodedUrlVerification()
    {
        var dispatch = GetDispatch(MinimalAdapter);

        dispatch.Should().NotContain("url_verification");
    }

    [Fact]
    public void Dispatch_DoesNotContainHardcodedFormUrlencoded()
    {
        var dispatch = GetDispatch(MinimalAdapter);

        dispatch.Should().NotContain("application/x-www-form-urlencoded");
    }

    [Fact]
    public void Dispatch_DoesNotContainWebhookSignatureVerifier()
    {
        var dispatch = GetDispatch(MinimalAdapter);

        dispatch.Should().NotContain("WebhookSignatureVerifier");
    }

    // ── Exception mapping ─────────────────────────────────────────────

    [Fact]
    public void Dispatch_CatchesAdapterAuthenticationException_Returns401()
    {
        var dispatch = GetDispatch(MinimalAdapter);

        dispatch.Should().Contain("AdapterAuthenticationException");
        dispatch.Should().Contain("Results.Unauthorized()");
    }

    [Fact]
    public void Dispatch_CatchesAdapterRateLimitException_Returns429()
    {
        var dispatch = GetDispatch(MinimalAdapter);

        dispatch.Should().Contain("AdapterRateLimitException");
        dispatch.Should().Contain("Results.StatusCode(429)");
    }

    [Fact]
    public void Dispatch_CatchesAdapterPermissionException_Returns403()
    {
        var dispatch = GetDispatch(MinimalAdapter);

        dispatch.Should().Contain("AdapterPermissionException");
        dispatch.Should().Contain("Results.Forbid()");
    }

    [Fact]
    public void Dispatch_CatchesAdapterNotFoundException_Returns404()
    {
        var dispatch = GetDispatch(MinimalAdapter);

        dispatch.Should().Contain("AdapterNotFoundException");
        dispatch.Should().Contain("Results.NotFound()");
    }

    // ── Event dispatch switch ─────────────────────────────────────────

    [Fact]
    public void Dispatch_SingleHandler_GeneratesSwitchCase()
    {
        var dispatch = GetDispatch(AdapterWithOneHandler);

        dispatch.Should().Contain("\"app_mention\"");
        dispatch.Should().Contain("HandleMention(");
    }

    [Fact]
    public void Dispatch_MultipleHandlers_AllCasesPresent()
    {
        var dispatch = GetDispatch(AdapterWithMultipleHandlers);

        dispatch.Should().Contain("\"app_mention\"");
        dispatch.Should().Contain("\"message\"");
        dispatch.Should().Contain("\"block_actions\"");
    }

    [Fact]
    public void Dispatch_UnknownEventType_DefaultArmReturnsOk()
    {
        var dispatch = GetDispatch(MinimalAdapter);

        dispatch.Should().Contain("default: return Results.Ok()");
    }

    [Fact]
    public void Dispatch_HandlerWithMultipleEventTypes_GeneratesCasePerType()
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
        var dispatch = GetDispatch(MinimalAdapter);

        dispatch.Should().Contain("public partial class SlackAdapter");
    }

    [Fact]
    public void Dispatch_GeneratedInCorrectNamespace()
    {
        var dispatch = GetDispatch(MinimalAdapter);

        dispatch.Should().Contain("namespace Test");
    }
}
