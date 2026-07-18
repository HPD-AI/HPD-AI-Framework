using FluentAssertions;

namespace HPD.Agent.TUI.Tests;

public sealed class SubAgentTuiExtensionsTests
{
    [Fact]
    public void AddSubAgentTui_RegistersFrameworkOwnedNavigationCommand()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddSubAgentTui()
            .Build();

        registry.TryFindSlashCommand("/subagents", out var command, out _).Should().BeTrue();
        command.Description.Should().Contain("durable subagent conversation");
    }
}
