using FluentAssertions;
using HPD.Agent.Bots.Tests.TestInfrastructure;

namespace HPD.Agent.Bots.Tests.SourceGen;

/// <summary>
/// Tests for <see cref="HPD.Agent.Bots.SourceGenerator.Generators.RegistrationGenerator"/>.
/// Verifies that <c>Add{Pascal}Bot()</c> and <c>Map{Pascal}Webhook()</c> extension methods
/// are generated correctly for each <c>[HpdBot]</c> class.
/// </summary>
public class RegistrationGeneratorTests
{
    private static readonly string MinimalSlackBot = """
        using HPD.Agent.Bots;
        namespace My.Bots;
        [HpdBot("slack")]
        public partial class SlackBot { }
        """;

    // ── File names ────────────────────────────────────────────────────

    [Fact]
    public void Registration_GeneratesFileNamed_BotClassRegistration()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackBot, out _);

        var names = SourceGenHelper.GetGeneratedFileNames(result);
        names.Should().Contain("SlackBotRegistration.g.cs");
    }

    // ── DI extension ──────────────────────────────────────────────────

    [Fact]
    public void Registration_GeneratesAddBotExtensionMethod()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackBot, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source.Should().NotBeNull();
        source!.Should().Contain("AddSlackBot(");
    }

    [Fact]
    public void Registration_AddBot_TakesActionOfConfig()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackBot, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        // The configure callback uses the Config type derived from the class name
        source!.Should().Contain("Action<SlackBotConfig>");
    }

    [Fact]
    public void Registration_AddBot_RegistersBotAsSingleton()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackBot, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source!.Should().Contain("TryAddSingleton<SlackBot>");
    }

    [Fact]
    public void Registration_AddBot_RegistersPlatformSessionMapper()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackBot, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source!.Should().Contain("PlatformSessionMapper");
    }

    [Fact]
    public void Registration_AddBot_CallsServicesConfigure()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackBot, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source!.Should().Contain("services.Configure(configure)");
    }

    // ── Endpoint extension ────────────────────────────────────────────

    [Fact]
    public void Registration_GeneratesMapWebhookExtensionMethod()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackBot, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source!.Should().Contain("MapSlackWebhook(");
    }

    [Fact]
    public void Registration_DefaultPath_MatchesBotName()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackBot, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source!.Should().Contain("/webhooks/slack");
    }

    [Fact]
    public void Registration_MapWebhook_CallsMapPost()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackBot, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source!.Should().Contain("MapPost(");
    }

    [Fact]
    public void Registration_MapWebhook_WiresHandleWebhookAsync()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackBot, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source!.Should().Contain("HandleWebhookAsync");
    }

    // ── Pascal casing ─────────────────────────────────────────────────

    [Fact]
    public void Registration_PascalCasesMethodNames_FromBotName()
    {
        // Bot named "teams" should produce AddTeamsBot / MapTeamsWebhook
        var source = """
            using HPD.Agent.Bots;
            namespace My.Bots;
            [HpdBot("teams")]
            public partial class TeamsBot { }
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var generated = SourceGenHelper.GetGeneratedFile(result, "TeamsBotRegistration.g.cs");

        generated.Should().NotBeNull();
        generated!.Should().Contain("AddTeamsBot(");
        generated.Should().Contain("MapTeamsWebhook(");
    }

    // ── Namespace placement ───────────────────────────────────────────

    [Fact]
    public void Registration_GeneratedCode_UsesBotNamespace()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackBot, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source!.Should().Contain("namespace My.Bots");
    }

    // ── Multiple adapters ─────────────────────────────────────────────

    [Fact]
    public void Registration_MultipleBots_GeneratesSeparateFiles()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace My.Bots;
            [HpdBot("slack")]
            public partial class SlackBot { }
            [HpdBot("teams")]
            public partial class TeamsBot { }
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().Contain("SlackBotRegistration.g.cs");
        names.Should().Contain("TeamsBotRegistration.g.cs");
    }

    // ── Socket transport branch ───────────────────────────────────────

    /// <summary>
    /// Source for an adapter that has [HpdSocketTransport] with a valid service type.
    /// The ValidSocketService class extends BotWebSocketService to pass HPDA008.
    /// </summary>
    private static readonly string SlackBotWithSocket = """
        using HPD.Agent.Bots;
        using System.Net.WebSockets;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.Logging.Abstractions;
        namespace My.Bots;

        public sealed class SlackSocketModeService : BotWebSocketService
        {
            public SlackSocketModeService() : base(NullLogger.Instance) { }
            protected override Task<System.Uri> GetConnectionUriAsync(CancellationToken ct)
                => Task.FromResult(new System.Uri("ws://localhost"));
            protected override Task RunSessionAsync(ClientWebSocket ws, CancellationToken ct)
                => Task.CompletedTask;
        }

        [HpdBot("slack")]
        [HpdSocketTransport(typeof(SlackSocketModeService), ConfigProperty = "AppToken")]
        public partial class SlackBot { }
        """;

    [Fact]
    public void Registration_WithSocketTransport_EmitsCaptureLocalAndConfigure()
    {
        var result = SourceGenHelper.RunGenerator(SlackBotWithSocket, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source.Should().NotBeNull();
        // configure is called once upfront into a local
        source!.Should().Contain("var _cfg = new SlackBotConfig()");
        source.Should().Contain("configure(_cfg)");
        // Then registered via services.Configure<T> for full options infrastructure support
        source.Should().Contain("services.Configure<SlackBotConfig>(configure)");
    }

    [Fact]
    public void Registration_WithSocketTransport_EmitsConditionalAddHostedService()
    {
        var result = SourceGenHelper.RunGenerator(SlackBotWithSocket, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source.Should().NotBeNull();
        source!.Should().Contain("_cfg.AppToken is not null");
        source.Should().Contain("AddHostedService");
        source.Should().Contain("SlackSocketModeService");
    }

    [Fact]
    public void Registration_WithSocketTransport_DoesNotUseOptionsCreate()
    {
        // Options.Create() was the previous (broken) approach — must not appear
        var result = SourceGenHelper.RunGenerator(SlackBotWithSocket, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source.Should().NotBeNull();
        source!.Should().NotContain("Options.Create(",
            "IOptionsMonitor and IOptionsSnapshot would not be satisfied by Options.Create");
    }

    [Fact]
    public void Registration_WithoutSocketTransport_UsesServicesConfigure_NotLocalCapture()
    {
        // Bots without [HpdSocketTransport] must still use the original simple path
        var result = SourceGenHelper.RunGenerator(MinimalSlackBot, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source.Should().NotBeNull();
        source!.Should().Contain("services.Configure(configure)");
        source.Should().NotContain("var _cfg = ");
        source.Should().NotContain("AddHostedService");
    }

    [Fact]
    public void Registration_WithSocketTransport_StillRegistersBotSingleton()
    {
        var result = SourceGenHelper.RunGenerator(SlackBotWithSocket, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source.Should().NotBeNull();
        source!.Should().Contain("TryAddSingleton<SlackBot>");
    }

    [Fact]
    public void Registration_WithSocketTransport_StillRegistersPlatformSessionMapper()
    {
        var result = SourceGenHelper.RunGenerator(SlackBotWithSocket, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source.Should().NotBeNull();
        source!.Should().Contain("PlatformSessionMapper");
    }

    [Fact]
    public void Registration_WithSocketTransport_StillGeneratesMapWebhookExtension()
    {
        // Socket mode does not remove the HTTP webhook endpoint option
        var result = SourceGenHelper.RunGenerator(SlackBotWithSocket, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackBotRegistration.g.cs");

        source.Should().NotBeNull();
        source!.Should().Contain("MapSlackWebhook(");
    }
}