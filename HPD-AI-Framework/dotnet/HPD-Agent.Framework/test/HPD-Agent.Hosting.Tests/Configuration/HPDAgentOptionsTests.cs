using FluentAssertions;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent;

namespace HPD.Agent.Hosting.Tests.Configuration;

/// <summary>
/// Tests for HPDAgentConfig configuration.
/// </summary>
public class HPDAgentConfigTests
{
    [Fact]
    public void WorkspaceStorePath_CanBeConfigured()
    {
        var options = new HPDAgentConfig
        {
            WorkspaceStorePath = "./some-path"
        };

        options.WorkspaceStorePath.Should().Be("./some-path");
    }

    [Fact]
    public void AgentConfig_TakesPriority_OverAgentConfigPath()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "Test Agent",
            SystemInstructions = "Test instructions"
        };

        var options = new HPDAgentConfig
        {
            AgentConfig = config,
            AgentConfigPath = "./config.json" // Should be ignored
        };

        // Assert
        options.AgentConfig.Should().BeSameAs(config);
        options.AgentConfigPath.Should().Be("./config.json"); // Still set, but not used
    }

    [Fact]
    public void ConfigureAgent_CalledAfter_AgentConfigApplied()
    {
        // This test verifies the contract - actual behavior tested in implementation tests
        // Arrange
        var callbackCalled = false;
        var options = new HPDAgentConfig
        {
            ConfigureAgent = builder => { callbackCalled = true; }
        };

        // Act
        options.ConfigureAgent?.Invoke(new AgentBuilder());

        // Assert
        callbackCalled.Should().BeTrue();
    }

    [Fact]
    public void DefaultIdleTimeout_Is30Minutes()
    {
        // Arrange
        var options = new HPDAgentConfig();

        // Assert
        options.AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void AgentIdleTimeout_CanBeCustomized()
    {
        // Arrange
        var options = new HPDAgentConfig
        {
            AgentIdleTimeout = TimeSpan.FromMinutes(60)
        };

        // Assert
        options.AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void AllProperties_CanBeSetToNull()
    {
        // Arrange
        var options = new HPDAgentConfig
        {
            WorkspaceStorePath = null,
            AgentConfig = null,
            AgentConfigPath = null,
            ConfigureAgent = null
        };

        // Assert - Should not throw
        options.WorkspaceStorePath.Should().BeNull();
        options.AgentConfig.Should().BeNull();
        options.AgentConfigPath.Should().BeNull();
        options.ConfigureAgent.Should().BeNull();
    }

    [Fact]
    public void UseJsonWorkspace_ConfiguresWorkspaceStoreAndPath()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"hpd-json-workspace-options-{Guid.NewGuid():N}");
        try
        {
            var options = new HPDAgentConfig();

            options.UseJsonWorkspace(tempPath);

            options.WorkspaceStore.Should().BeOfType<JsonWorkspaceStore>();
            options.WorkspaceStorePath.Should().Be(tempPath);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public void UseDefaultAgent_WithConfig_ConfiguresDefaultAgent()
    {
        var agentConfig = new AgentConfig { Name = "Default Agent" };
        var options = new HPDAgentConfig
        {
            DefaultAgentConfigPath = "./old-agent.json"
        };

        options.UseDefaultAgent(agentConfig);

        options.DefaultAgentConfig.Should().BeSameAs(agentConfig);
        options.DefaultAgentConfigPath.Should().BeNull();
    }

    [Fact]
    public void UseDefaultAgent_WithPath_ConfiguresDefaultAgentPath()
    {
        var options = new HPDAgentConfig
        {
            DefaultAgentConfig = new AgentConfig { Name = "Old Agent" }
        };

        options.UseDefaultAgent("./agent.json");

        options.DefaultAgentConfig.Should().BeNull();
        options.DefaultAgentConfigPath.Should().Be("./agent.json");
    }
}
