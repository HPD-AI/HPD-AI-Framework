using FluentAssertions;
using HPD.Agent.Bots.Tests.TestInfrastructure;

namespace HPD.Agent.Bots.Tests.SourceGen;

/// <summary>
/// Tests for <see cref="HPD.Agent.Bots.SourceGenerator.Generators.RegistryGenerator"/>.
/// Verifies that the assembly-scoped <c>BotRegistry.All</c> catalog is generated correctly.
/// </summary>
public class RegistryGeneratorTests
{
    // ── No adapters ───────────────────────────────────────────────────

    [Fact]
    public void Registry_NoBots_NoFileGenerated()
    {
        var source = "namespace Test; public class Nothing { }";

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().NotContain("BotRegistry.g.cs");
    }

    // ── Single adapter ────────────────────────────────────────────────

    [Fact]
    public void Registry_OneBot_GeneratesFile()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot { }
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().Contain("BotRegistry.g.cs");
    }

    [Fact]
    public void Registry_OneBot_ContainsBotName()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "BotRegistry.g.cs");

        registry.Should().NotBeNull();
        registry!.Should().Contain("\"slack\"");
    }

    [Fact]
    public void Registry_OneBot_ContainsTypeOfEntry()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "BotRegistry.g.cs");

        registry!.Should().Contain("typeof(Test.SlackBot)");
    }

    [Fact]
    public void Registry_OneBot_ContainsDefaultPath()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "BotRegistry.g.cs");

        registry!.Should().Contain("/webhooks/slack");
    }

    [Fact]
    public void Registry_OneBot_MapEndpointDelegateCallsExtension()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "BotRegistry.g.cs");

        registry!.Should().Contain("MapSlackWebhook(");
    }

    // ── Multiple adapters ─────────────────────────────────────────────

    [Fact]
    public void Registry_MultipleBots_AllEntriesPresent()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot { }
            [HpdBot("teams")]
            public partial class TeamsBot { }
            [HpdBot("discord")]
            public partial class DiscordBot { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "BotRegistry.g.cs");

        registry.Should().NotBeNull();
        registry!.Should().Contain("\"slack\"");
        registry.Should().Contain("\"teams\"");
        registry.Should().Contain("\"discord\"");
    }

    [Fact]
    public void Registry_MultipleBots_DefaultPathsPerBot()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot { }
            [HpdBot("teams")]
            public partial class TeamsBot { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "BotRegistry.g.cs");

        registry!.Should().Contain("/webhooks/slack");
        registry.Should().Contain("/webhooks/teams");
    }

    // ── Namespace and accessibility ───────────────────────────────────

    [Fact]
    public void Registry_IsInHpdAgentBotsGeneratedNamespace()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "BotRegistry.g.cs");

        registry!.Should().Contain("namespace HPD.Agent.Bots.Generated");
    }

    [Fact]
    public void Registry_ClassIsInternalStatic()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "BotRegistry.g.cs");

        registry!.Should().Contain("internal static class BotRegistry");
    }

    [Fact]
    public void Registry_AllArrayIsPublicReadonly()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "BotRegistry.g.cs");

        registry!.Should().Contain("public static readonly BotRegistration[] All");
    }

    [Fact]
    public void Registry_EmitsGeneratedProvider()
    {
        var source = """
            using HPD.Agent.Bots;
            namespace Test;
            [HpdBot("slack")]
            public partial class SlackBot { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "BotRegistry.g.cs");

        registry!.Should().Contain("internal sealed class GeneratedBotRegistryProvider : IBotRegistryProvider");
        registry.Should().Contain("=> BotRegistry.All");
    }
}
