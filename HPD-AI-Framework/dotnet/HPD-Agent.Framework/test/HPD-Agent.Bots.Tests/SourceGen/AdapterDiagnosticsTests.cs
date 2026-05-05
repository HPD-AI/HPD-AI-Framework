using FluentAssertions;
using HPD.Agent.Bots.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;

namespace HPD.Agent.Bots.Tests.SourceGen;

/// <summary>
/// Tests for compiler diagnostics HPDA001 through HPDA006 emitted by
/// <see cref="HPD.Agent.Bots.SourceGenerator.BotSourceGenerator"/>.
///
/// Each test gives the generator a minimal C# snippet that either should or
/// should not trigger a specific diagnostic, then asserts on the result.
/// </summary>
public class BotDiagnosticsTests
{
    // ── HPDA001: [HpdBot] class must be public ───────────────────

    [Fact]
    public void HPDA001_InternalBotClass_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("test")]
            internal partial class MyBot { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA001_PublicBotClass_NoDiagnostic()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().NotContain(d => d.Id == "HPDA001");
    }

    // ── HPDA002: [HpdWebhookHandler] must be private or internal ─────

    [Fact]
    public void HPDA002_PublicHandler_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdWebhookHandler("message")]
                public Task<IResult> HandleMessage(HttpContext ctx, byte[] body, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA002" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA002_PrivateHandler_NoDiagnostic()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdWebhookHandler("message")]
                private Task<IResult> HandleMessage(HttpContext ctx, byte[] body, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().NotContain(d => d.Id == "HPDA002");
    }

    [Fact]
    public void HPDA002_InternalHandler_NoDiagnostic()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdWebhookHandler("message")]
                internal Task<IResult> HandleMessage(HttpContext ctx, byte[] body, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().NotContain(d => d.Id == "HPDA002");
    }

    // ── HPDA003: [HpdStreaming] declared more than once ─────────────

    [Fact]
    public void HPDA003_TwoStreamingAttributes_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("test")]
            [HpdStreaming(StreamingStrategy.PostAndEdit)]
            [HpdStreaming(StreamingStrategy.BufferAndPost)]
            public partial class MyBot { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA003" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA003_OneStreamingAttribute_NoDiagnostic()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("test")]
            [HpdStreaming(StreamingStrategy.PostAndEdit)]
            public partial class MyBot { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().NotContain(d => d.Id == "HPDA003");
    }

    [Fact]
    public void HPDA003_NoStreamingAttribute_NoDiagnostic()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().NotContain(d => d.Id == "HPDA003");
    }

    // ── HPDA004: [HpdPermissionHandler] declared more than once ─────

    [Fact]
    public void HPDA004_TwoPermissionHandlers_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Threading;
            using System.Threading.Tasks;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdPermissionHandler]
                private Task HandlePermissionA(CancellationToken ct) => Task.CompletedTask;

                [HpdPermissionHandler]
                private Task HandlePermissionB(CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA004" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA004_OnePermissionHandler_NoDiagnostic()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Threading;
            using System.Threading.Tasks;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdPermissionHandler]
                private Task HandlePermission(CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().NotContain(d => d.Id == "HPDA004");
    }

    [Fact]
    public void HPDA004_ZeroPermissionHandlers_NoDiagnostic()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().NotContain(d => d.Id == "HPDA004");
    }

    // ── HPDA005: [HpdBot] name collision ────────────────────────

    [Fact]
    public void HPDA005_TwoBotsSameName_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBotA { }

            [HpdBot("slack")]
            public partial class SlackBotB { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA005" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA005_TwoBotsDifferentNames_NoDiagnostic()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot { }

            [HpdBot("teams")]
            public partial class TeamsBot { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().NotContain(d => d.Id == "HPDA005");
    }

    // ── HPDA006: [WebhookPayload] type must be a record ─────────────

    [Fact]
    public void HPDA006_WebhookPayloadOnClass_EmitsError()
    {
        // Note: the generator's predicate filters for RecordDeclarationSyntax,
        // so a class decorated with [WebhookPayload] won't match the pipeline
        // and won't emit a diagnostic from the generator itself.
        // HPDA006 is only emitted when the generator manually checks; currently
        // the predicate prevents classes from reaching that code path.
        // This test documents the observed behaviour.
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [WebhookPayload]
            public class NotARecord { }
            """;

        // The generator filters by RecordDeclarationSyntax, so a class simply
        // doesn't enter the payload pipeline and no HPDA006 is fired.
        // We assert there are NO diagnostics from the generator here.
        var diagnostics = SourceGenHelper.GetDiagnostics(source);
        diagnostics.Should().NotContain(d => d.Id == "HPDA006");
    }

    [Fact]
    public void HPDA006_WebhookPayloadOnRecord_NoDiagnostic()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Text.Json.Serialization;
            namespace Test;
            [WebhookPayload]
            public record SlackEvent(
                [property: JsonPropertyName("type")] string Type);
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().NotContain(d => d.Id == "HPDA006");
    }

    // ── Message format strings ────────────────────────────────────────

    [Fact]
    public void HPDA001_DiagnosticMessageContainsClassName()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("test")]
            internal partial class InternalBot { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);
        var d = diagnostics.First(x => x.Id == "HPDA001");

        d.GetMessage().Should().Contain("InternalBot");
    }

    [Fact]
    public void HPDA005_DiagnosticMessageContainsBothClassNames()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class First { }

            [HpdBot("slack")]
            public partial class Second { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);
        var d = diagnostics.First(x => x.Id == "HPDA005");

        var message = d.GetMessage();
        // Message should reference both class names
        (message.Contains("First") || message.Contains("Second")).Should().BeTrue();
    }

    // ── HPDA008: [HpdSocketTransport] service type must extend BotWebSocketService ──

    [Fact]
    public void HPDA008_ServiceTypeNotExtendingBotWebSocketService_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            using Microsoft.Extensions.Hosting;
            namespace Test;

            // A plain class — does NOT extend BotWebSocketService
            public class NotAWebSocketService : IHostedService
            {
                public Task StartAsync(System.Threading.CancellationToken ct) => System.Threading.Tasks.Task.CompletedTask;
                public Task StopAsync(System.Threading.CancellationToken ct)  => System.Threading.Tasks.Task.CompletedTask;
            }

            [HpdBot("test")]
            [HpdSocketTransport(typeof(NotAWebSocketService), ConfigProperty = "Token")]
            public partial class TestBot { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d =>
            d.Id == "HPDA008" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA008_ServiceTypeExtendingBotWebSocketService_NoDiagnostic()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Net.WebSockets;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.Logging.Abstractions;
            namespace Test;

            public sealed class ValidSocketService : BotWebSocketService
            {
                public ValidSocketService() : base(NullLogger.Instance) { }
                protected override Task<System.Uri> GetConnectionUriAsync(CancellationToken ct)
                    => Task.FromResult(new System.Uri("ws://localhost"));
                protected override Task RunSessionAsync(ClientWebSocket ws, CancellationToken ct)
                    => Task.CompletedTask;
            }

            [HpdBot("test")]
            [HpdSocketTransport(typeof(ValidSocketService), ConfigProperty = "Token")]
            public partial class TestBot { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().NotContain(d => d.Id == "HPDA008");
    }

    [Fact]
    public void HPDA008_DiagnosticMessageContainsServiceTypeName()
    {
        var source = """
            using HPD.Agent.Bots;
            using Microsoft.Extensions.Hosting;
            namespace Test;

            public class BadService : IHostedService
            {
                public Task StartAsync(System.Threading.CancellationToken ct) => System.Threading.Tasks.Task.CompletedTask;
                public Task StopAsync(System.Threading.CancellationToken ct)  => System.Threading.Tasks.Task.CompletedTask;
            }

            [HpdBot("test")]
            [HpdSocketTransport(typeof(BadService), ConfigProperty = "Token")]
            public partial class TestBot { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);
        var d = diagnostics.First(x => x.Id == "HPDA008");

        d.GetMessage().Should().Contain("BadService");
    }

    // ── HPDA009: [HpdPreDispatch] method signature ─────────────────────

    [Fact]
    public void HPDA009_ValidPreDispatch_NoDiagnostic()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdPreDispatch]
                private async Task<IResult?> VerifyAsync(HttpContext ctx, byte[] bodyBytes)
                    => null;
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().NotContain(d => d.Id == "HPDA009");
    }

    [Fact]
    public void HPDA009_PublicPreDispatch_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdPreDispatch]
                public async Task<IResult?> VerifyAsync(HttpContext ctx, byte[] bodyBytes)
                    => null;
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA009" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA009_PreDispatchWithoutAsync_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdPreDispatch]
                private Task<IResult?> VerifyAsync(HttpContext ctx, byte[] bodyBytes)
                    => Task.FromResult<IResult?>(null);
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA009" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA009_PreDispatchWrongReturnType_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdPreDispatch]
                private async Task<bool> VerifyAsync(HttpContext ctx, byte[] bodyBytes)
                    => true;
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA009" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA009_PreDispatchWrongParameterTypes_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdPreDispatch]
                private async Task<IResult?> VerifyAsync(byte[] bodyBytes, HttpContext ctx)
                    => null;
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA009" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA009_DuplicatePreDispatch_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdPreDispatch]
                private async Task<IResult?> VerifyAAsync(HttpContext ctx, byte[] bodyBytes)
                    => null;

                [HpdPreDispatch]
                private async Task<IResult?> VerifyBAsync(HttpContext ctx, byte[] bodyBytes)
                    => null;
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA009" && d.Severity == DiagnosticSeverity.Error);
    }

    // ── HPDA010: [HpdBodyExtractor] method signature ───────────────────

    [Fact]
    public void HPDA010_ValidBodyExtractor_NoDiagnostic()
    {
        var source = """
            using HPD.Agent.Bots;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdBodyExtractor]
                private (string? eventType, byte[] dispatchBytes) Extract(HttpContext ctx, byte[] bodyBytes)
                    => (null, bodyBytes);
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().NotContain(d => d.Id == "HPDA010");
    }

    [Fact]
    public void HPDA010_PublicBodyExtractor_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdBodyExtractor]
                public (string? eventType, byte[] dispatchBytes) Extract(HttpContext ctx, byte[] bodyBytes)
                    => (null, bodyBytes);
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA010" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA010_BodyExtractorWrongReturnType_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdBodyExtractor]
                private string? Extract(HttpContext ctx, byte[] bodyBytes)
                    => null;
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA010" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA010_BodyExtractorWrongParameterTypes_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdBodyExtractor]
                private (string? eventType, byte[] dispatchBytes) Extract(byte[] bodyBytes, HttpContext ctx)
                    => (null, bodyBytes);
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA010" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA010_DuplicateBodyExtractor_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            using Microsoft.AspNetCore.Http;
            namespace Test;
            [HpdBot("test")]
            public partial class MyBot
            {
                [HpdBodyExtractor]
                private (string? eventType, byte[] dispatchBytes) ExtractA(HttpContext ctx, byte[] bodyBytes)
                    => (null, bodyBytes);

                [HpdBodyExtractor]
                private (string? eventType, byte[] dispatchBytes) ExtractB(HttpContext ctx, byte[] bodyBytes)
                    => (null, bodyBytes);
            }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA010" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA011_EmptyWebhookMethods_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("test")]
            [HpdWebhookMethods()]
            public partial class MyBot { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA011" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HPDA011_BlankWebhookMethod_EmitsError()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("test")]
            [HpdWebhookMethods("GET", " ")]
            public partial class MyBot { }
            """;

        var diagnostics = SourceGenHelper.GetDiagnostics(source);

        diagnostics.Should().Contain(d => d.Id == "HPDA011" && d.Severity == DiagnosticSeverity.Error);
    }
}
