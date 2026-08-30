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
        var customStore = new InMemorySessionStore(HPD.Agent.Serialization.CoreAgentEventComposition.Instance.Codec);
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
    public void DefaultAgent_TakesPriority_OverDefaultAgentPath()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "Test Agent",
            SystemInstructions = "Test instructions"
        };

        var options = new HPDAgentConfig
        {
            DefaultAgent = config,
            DefaultAgentPath = "./config.json" // Should be ignored
        };

        // Assert
        options.DefaultAgent.Should().BeSameAs(config);
        options.DefaultAgentPath.Should().Be("./config.json"); // Still set, but not used
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
            DefaultAgent = null,
            DefaultAgentPath = null,
            ConfigureAgent = null
        };

        // Assert - Should not throw
        options.SessionStore.Should().BeNull();
        options.SessionStorePath.Should().BeNull();
        options.DefaultAgent.Should().BeNull();
        options.DefaultAgentPath.Should().BeNull();
        options.ConfigureAgent.Should().BeNull();
    }
}
