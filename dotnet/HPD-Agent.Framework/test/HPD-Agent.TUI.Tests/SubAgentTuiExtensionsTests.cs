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

    [Fact]
    public async Task AddSubAgentTui_AcceptsApplicationOwnedMenuActions()
    {
        var invoked = false;
        var options = new AgentTuiSubAgentMenuOptions();

        options.AddAction(
            "Configure model",
            (_, _, _) =>
            {
                invoked = true;
                return ValueTask.CompletedTask;
            });

        options.Actions.Should().ContainSingle();
        options.Actions[0].Title(null!).Should().Be("Configure model");
        await options.Actions[0].ExecuteAsync(null!, null!, CancellationToken.None);
        invoked.Should().BeTrue();
    }

    [Fact]
    public void SubAgentMenuAction_TitleIsResolvedWhenRead()
    {
        var label = "Same as parent";
        var options = new AgentTuiSubAgentMenuOptions();
        options.AddAction(
            _ => $"Model policy · {label}",
            static (_, _, _) => ValueTask.CompletedTask);

        options.Actions[0].Title(null!).Should().Be("Model policy · Same as parent");

        label = "GPT-5.5";
        options.Actions[0].Title(null!).Should().Be("Model policy · GPT-5.5");
    }
}
