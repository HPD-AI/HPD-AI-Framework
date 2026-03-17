using FluentAssertions;
using HPD.Agent.Adapters.Tests.TestInfrastructure;

namespace HPD.Agent.Adapters.Tests.SourceGen;

/// <summary>
/// Tests for <see cref="HPD.Agent.Adapters.SourceGenerator.Generators.RegistryGenerator"/>.
/// Verifies that the assembly-scoped <c>AdapterRegistry.All</c> catalog is generated correctly.
/// </summary>
public class RegistryGeneratorTests
{
    // ── No adapters ───────────────────────────────────────────────────

    [Fact]
    public void Registry_NoAdapters_NoFileGenerated()
    {
        var source = "namespace Test; public class Nothing { }";

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().NotContain("AdapterRegistry.g.cs");
    }

    // ── Single adapter ────────────────────────────────────────────────

    [Fact]
    public void Registry_OneAdapter_GeneratesFile()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter { }
            """;

        var result = SourceGenHelper.RunGenerator(source, out _);
        var names  = SourceGenHelper.GetGeneratedFileNames(result);

        names.Should().Contain("AdapterRegistry.g.cs");
    }

    [Fact]
    public void Registry_OneAdapter_ContainsAdapterName()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "AdapterRegistry.g.cs");

        registry.Should().NotBeNull();
        registry!.Should().Contain("\"slack\"");
    }

    [Fact]
    public void Registry_OneAdapter_ContainsTypeOfEntry()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "AdapterRegistry.g.cs");

        registry!.Should().Contain("typeof(Test.SlackAdapter)");
    }

    [Fact]
    public void Registry_OneAdapter_ContainsDefaultPath()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "AdapterRegistry.g.cs");

        registry!.Should().Contain("/webhooks/slack");
    }

    [Fact]
    public void Registry_OneAdapter_MapEndpointDelegateCallsExtension()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "AdapterRegistry.g.cs");

        registry!.Should().Contain("MapSlackWebhook(");
    }

    // ── Multiple adapters ─────────────────────────────────────────────

    [Fact]
    public void Registry_MultipleAdapters_AllEntriesPresent()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter { }
            [HpdAdapter("teams")]
            public partial class TeamsAdapter { }
            [HpdAdapter("discord")]
            public partial class DiscordAdapter { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "AdapterRegistry.g.cs");

        registry.Should().NotBeNull();
        registry!.Should().Contain("\"slack\"");
        registry.Should().Contain("\"teams\"");
        registry.Should().Contain("\"discord\"");
    }

    [Fact]
    public void Registry_MultipleAdapters_DefaultPathsPerAdapter()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter { }
            [HpdAdapter("teams")]
            public partial class TeamsAdapter { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "AdapterRegistry.g.cs");

        registry!.Should().Contain("/webhooks/slack");
        registry.Should().Contain("/webhooks/teams");
    }

    // ── Namespace and accessibility ───────────────────────────────────

    [Fact]
    public void Registry_IsInHpdAgentAdaptersGeneratedNamespace()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "AdapterRegistry.g.cs");

        registry!.Should().Contain("namespace HPD.Agent.Adapters.Generated");
    }

    [Fact]
    public void Registry_ClassIsInternalStatic()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "AdapterRegistry.g.cs");

        registry!.Should().Contain("internal static class AdapterRegistry");
    }

    [Fact]
    public void Registry_AllArrayIsPublicReadonly()
    {
        var source = """
            using HPD.Agent.Adapters;
            namespace Test;
            [HpdAdapter("slack")]
            public partial class SlackAdapter { }
            """;

        var result   = SourceGenHelper.RunGenerator(source, out _);
        var registry = SourceGenHelper.GetGeneratedFile(result, "AdapterRegistry.g.cs");

        registry!.Should().Contain("public static readonly AdapterRegistration[] All");
    }
}
