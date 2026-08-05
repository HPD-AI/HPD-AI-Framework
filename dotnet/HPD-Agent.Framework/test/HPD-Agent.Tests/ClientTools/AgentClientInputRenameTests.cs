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
    // ── 7.1  AgentToolsRunConfig.ClientInput is typed AgentClientInput ─────────

    [Fact]
    public void AgentToolsRunConfig_ClientInput_IsTyped_AgentClientInput()
    {
        var prop = typeof(AgentToolsRunConfig).GetProperty(nameof(AgentToolsRunConfig.ClientInput));

        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(AgentClientInput),
            "ClientInput must be AgentClientInput, not the old AgentRunInput");
    }

    // ── 7.2  AgentClientInput can be assigned to AgentRunConfig ───────────────

    [Fact]
    public void AgentToolsRunConfig_ClientInput_AcceptsAgentClientInput()
    {
        var input = new AgentClientInput
        {
            clientToolHarnesses = Array.Empty<clientToolHarnessDefinition>()
        };

        var config = new AgentRunConfig
        {
            Tools = new AgentToolsRunConfig { ClientInput = input }
        };

        config.Tools.ClientInput.Should().BeSameAs(input);
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
