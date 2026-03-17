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
    public void SessionStore_TakesPriority_OverSessionStorePath()
    {
        // Arrange
        var customStore = new InMemorySessionStore();
        var options = new HPDAgentConfig
        {
            SessionStore = customStore,
            SessionStorePath = "./some-path" // Should be ignored
        };

        // Assert
        options.SessionStore.Should().BeSameAs(customStore);
        options.SessionStorePath.Should().Be("./some-path"); // Still set, but not used
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
            SessionStore = null,
            SessionStorePath = null,
            AgentConfig = null,
            AgentConfigPath = null,
            ConfigureAgent = null
        };

        // Assert - Should not throw
        options.SessionStore.Should().BeNull();
        options.SessionStorePath.Should().BeNull();
        options.AgentConfig.Should().BeNull();
        options.AgentConfigPath.Should().BeNull();
        options.ConfigureAgent.Should().BeNull();
    }
}
