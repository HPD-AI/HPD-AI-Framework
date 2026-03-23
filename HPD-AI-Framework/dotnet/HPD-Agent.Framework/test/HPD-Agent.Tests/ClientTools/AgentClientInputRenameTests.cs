using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ClientTools;

namespace HPD.Agent.Tests.ClientTools;

/// <summary>
/// Area 7 — AgentRunInput → AgentClientInput rename regression tests.
/// Verifies the rename is complete: the old name is gone and the new name
/// is used correctly throughout AgentRunConfig.
/// </summary>
public class AgentClientInputRenameTests
{
    // ── 7.1  AgentRunConfig.ClientToolInput is typed AgentClientInput ─────────

    [Fact]
    public void AgentRunConfig_ClientToolInput_IsTyped_AgentClientInput()
    {
        var prop = typeof(AgentRunConfig).GetProperty(nameof(AgentRunConfig.ClientToolInput));

        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(AgentClientInput),
            "ClientToolInput must be AgentClientInput, not the old AgentRunInput");
    }

    // ── 7.2  AgentClientInput can be assigned to AgentRunConfig ───────────────

    [Fact]
    public void AgentRunConfig_ClientToolInput_AcceptsAgentClientInput()
    {
        var input = new AgentClientInput
        {
            clientToolKits = Array.Empty<clientToolKitDefinition>()
        };

        var config = new AgentRunConfig
        {
            ClientToolInput = input
        };

        config.ClientToolInput.Should().BeSameAs(input);
    }

    // ── 7.3  AgentClientInput type exists and AgentRunInput does not ──────────

    [Fact]
    public void AgentClientInput_TypeExists()
    {
        var type = typeof(AgentClientInput);
        type.Should().NotBeNull();
        type.Name.Should().Be("AgentClientInput");
    }

    [Fact]
    public void AgentRunInput_TypeDoesNotExist_InClientToolsNamespace()
    {
        // The old type must no longer exist
        var assembly = typeof(AgentClientInput).Assembly;
        var oldType = assembly.GetType("HPD.Agent.ClientTools.AgentRunInput");
        oldType.Should().BeNull("AgentRunInput must have been renamed to AgentClientInput");
    }
}
