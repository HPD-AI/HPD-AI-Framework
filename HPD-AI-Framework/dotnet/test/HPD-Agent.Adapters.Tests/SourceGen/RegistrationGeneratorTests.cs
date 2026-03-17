using FluentAssertions;
using HPD.Agent.Adapters.Tests.TestInfrastructure;

namespace HPD.Agent.Adapters.Tests.SourceGen;

/// <summary>
/// Tests for <see cref="HPD.Agent.Adapters.SourceGenerator.Generators.RegistrationGenerator"/>.
/// Verifies that <c>Add{Pascal}Adapter()</c> and <c>Map{Pascal}Webhook()</c> extension methods
/// are generated correctly for each <c>[HpdAdapter]</c> class.
/// </summary>
public class RegistrationGeneratorTests
{
    private static readonly string MinimalSlackAdapter = """
        using HPD.Agent.Adapters;
        namespace My.Adapters;
        [HpdAdapter("slack")]
        public partial class SlackAdapter { }
        """;

    // ── File names ────────────────────────────────────────────────────

    [Fact]
    public void Registration_GeneratesFileNamed_AdapterClassRegistration()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackAdapter, out _);

        var names = SourceGenHelper.GetGeneratedFileNames(result);
        names.Should().Contain("SlackAdapterRegistration.g.cs");
    }

    // ── DI extension ──────────────────────────────────────────────────

    [Fact]
    public void Registration_GeneratesAddAdapterExtensionMethod()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackAdapter, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source.Should().NotBeNull();
        source!.Should().Contain("AddSlackAdapter(");
    }

    [Fact]
    public void Registration_AddAdapter_TakesActionOfConfig()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackAdapter, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        // The configure callback uses the Config type derived from the class name
        source!.Should().Contain("Action<SlackAdapterConfig>");
    }

    [Fact]
    public void Registration_AddAdapter_RegistersAdapterAsSingleton()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackAdapter, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source!.Should().Contain("TryAddSingleton<SlackAdapter>");
    }

    [Fact]
    public void Registration_AddAdapter_RegistersPlatformSessionMapper()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackAdapter, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source!.Should().Contain("PlatformSessionMapper");
    }

    [Fact]
    public void Registration_AddAdapter_CallsServicesConfigure()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackAdapter, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source!.Should().Contain("services.Configure(configure)");
    }

    // ── Endpoint extension ────────────────────────────────────────────

    [Fact]
    public void Registration_GeneratesMapWebhookExtensionMethod()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackAdapter, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source!.Should().Contain("MapSlackWebhook(");
    }

    [Fact]
    public void Registration_DefaultPath_MatchesAdapterName()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackAdapter, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source!.Should().Contain("/webhooks/slack");
    }

    [Fact]
    public void Registration_MapWebhook_CallsMapPost()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackAdapter, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source!.Should().Contain("MapPost(");
    }

    [Fact]
    public void Registration_MapWebhook_WiresHandleWebhookAsync()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackAdapter, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source!.Should().Contain("HandleWebhookAsync");
    }

    // ── Pascal casing ─────────────────────────────────────────────────

    [Fact]
    public void Registration_PascalCasesMethodNames_FromAdapterName()
    {
        // Adapter named "teams" should produce AddTeamsAdapter / MapTeamsWebhook
        var source = """
            using HPD.Agent.Adapters;
            namespace My.Adapters;
            [HpdAdapter("teams")]
            public partial class TeamsAdapter { }
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var generated = SourceGenHelper.GetGeneratedFile(result, "TeamsAdapterRegistration.g.cs");

        generated.Should().NotBeNull();
        generated!.Should().Contain("AddTeamsAdapter(");
        generated.Should().Contain("MapTeamsWebhook(");
    }

    // ── Namespace placement ───────────────────────────────────────────

    [Fact]
    public void Registration_GeneratedCode_UsesAdapterNamespace()
    {
        var result = SourceGenHelper.RunGenerator(MinimalSlackAdapter, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source!.Should().Contain("namespace My.Adapters");
    }

    // ── Multiple adapters ─────────────────────────────────────────────

    [Fact]
    public void Registration_MultipleAdapters_GeneratesSeparateFiles()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace My.Adapters;
            [HpdAdapter("slack")]
            public partial class SlackAdapter { }
            [HpdAdapter("teams")]
            public partial class TeamsAdapter { }
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().Contain("SlackAdapterRegistration.g.cs");
        names.Should().Contain("TeamsAdapterRegistration.g.cs");
    }

    // ── Socket transport branch ───────────────────────────────────────

    /// <summary>
    /// Source for an adapter that has [HpdSocketTransport] with a valid service type.
    /// The ValidSocketService class extends AdapterWebSocketService to pass HPDA008.
    /// </summary>
    private static readonly string SlackAdapterWithSocket = """
        using HPD.Agent.Adapters;
        using System.Net.WebSockets;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.Logging.Abstractions;
        namespace My.Adapters;

        public sealed class SlackSocketModeService : AdapterWebSocketService
        {
            public SlackSocketModeService() : base(NullLogger.Instance) { }
            protected override Task<System.Uri> GetConnectionUriAsync(CancellationToken ct)
                => Task.FromResult(new System.Uri("ws://localhost"));
            protected override Task RunSessionAsync(ClientWebSocket ws, CancellationToken ct)
                => Task.CompletedTask;
        }

        [HpdAdapter("slack")]
        [HpdSocketTransport(typeof(SlackSocketModeService), ConfigProperty = "AppToken")]
        public partial class SlackAdapter { }
        """;

    [Fact]
    public void Registration_WithSocketTransport_EmitsCaptureLocalAndConfigure()
    {
        var result = SourceGenHelper.RunGenerator(SlackAdapterWithSocket, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source.Should().NotBeNull();
        // configure is called once upfront into a local
        source!.Should().Contain("var _cfg = new SlackAdapterConfig()");
        source.Should().Contain("configure(_cfg)");
        // Then registered via services.Configure<T> for full options infrastructure support
        source.Should().Contain("services.Configure<SlackAdapterConfig>(configure)");
    }

    [Fact]
    public void Registration_WithSocketTransport_EmitsConditionalAddHostedService()
    {
        var result = SourceGenHelper.RunGenerator(SlackAdapterWithSocket, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source.Should().NotBeNull();
        source!.Should().Contain("_cfg.AppToken is not null");
        source.Should().Contain("AddHostedService");
        source.Should().Contain("SlackSocketModeService");
    }

    [Fact]
    public void Registration_WithSocketTransport_DoesNotUseOptionsCreate()
    {
        // Options.Create() was the previous (broken) approach — must not appear
        var result = SourceGenHelper.RunGenerator(SlackAdapterWithSocket, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source.Should().NotBeNull();
        source!.Should().NotContain("Options.Create(",
            "IOptionsMonitor and IOptionsSnapshot would not be satisfied by Options.Create");
    }

    [Fact]
    public void Registration_WithoutSocketTransport_UsesServicesConfigure_NotLocalCapture()
    {
        // Adapters without [HpdSocketTransport] must still use the original simple path
        var result = SourceGenHelper.RunGenerator(MinimalSlackAdapter, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source.Should().NotBeNull();
        source!.Should().Contain("services.Configure(configure)");
        source.Should().NotContain("var _cfg = ");
        source.Should().NotContain("AddHostedService");
    }

    [Fact]
    public void Registration_WithSocketTransport_StillRegistersAdapterSingleton()
    {
        var result = SourceGenHelper.RunGenerator(SlackAdapterWithSocket, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source.Should().NotBeNull();
        source!.Should().Contain("TryAddSingleton<SlackAdapter>");
    }

    [Fact]
    public void Registration_WithSocketTransport_StillRegistersPlatformSessionMapper()
    {
        var result = SourceGenHelper.RunGenerator(SlackAdapterWithSocket, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source.Should().NotBeNull();
        source!.Should().Contain("PlatformSessionMapper");
    }

    [Fact]
    public void Registration_WithSocketTransport_StillGeneratesMapWebhookExtension()
    {
        // Socket mode does not remove the HTTP webhook endpoint option
        var result = SourceGenHelper.RunGenerator(SlackAdapterWithSocket, out _);
        var source = SourceGenHelper.GetGeneratedFile(result, "SlackAdapterRegistration.g.cs");

        source.Should().NotBeNull();
        source!.Should().Contain("MapSlackWebhook(");
    }
}