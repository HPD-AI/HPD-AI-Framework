using FluentAssertions;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent;

namespace HPD.Agent.Hosting.Tests.Configuration;

/// <summary>
/// Tests for hosted-agent host options.
/// </summary>
public class HPDAgentHostOptionsTests
{
    [Fact]
    public void SessionStore_TakesPriority_OverSessionStorePath()
    {
        // Arrange
        var customStore = new InMemorySessionStore(HPD.Agent.Serialization.CoreAgentEventComposition.Instance.Codec);
        var options = new HPDAgentHostOptions
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

        var options = new HPDAgentHostOptions
        {
            DefaultAgent = config,
            DefaultAgentPath = "./config.json" // Should be ignored
        };

        // Assert
        options.DefaultAgent.Should().BeSameAs(config);
        options.DefaultAgentPath.Should().Be("./config.json"); // Still set, but not used
    }

    [Fact]
    public void ConfigureEachAgentBuilder_CanBeAssignedAndInvoked()
    {
        // Arrange
        var callbackCalled = false;
        var options = new HPDAgentHostOptions
        {
            ConfigureEachAgentBuilder = builder => { callbackCalled = true; }
        };

        // Act
        options.ConfigureEachAgentBuilder?.Invoke(new AgentBuilder());

        // Assert
        callbackCalled.Should().BeTrue();
    }

    [Fact]
    public void DefaultIdleTimeout_Is30Minutes()
    {
        // Arrange
        var options = new HPDAgentHostOptions();

        // Assert
        options.AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void PersistAfterTurn_DefaultsToTrue()
    {
        var options = new HPDAgentHostOptions();

        options.PersistAfterTurn.Should().BeTrue();
    }

    [Fact]
    public void AgentIdleTimeout_CanBeCustomized()
    {
        // Arrange
        var options = new HPDAgentHostOptions
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
        var options = new HPDAgentHostOptions
        {
            SessionStore = null,
            SessionStorePath = null,
            DefaultAgent = null,
            DefaultAgentPath = null,
            ConfigureEachAgentBuilder = null
        };

        // Assert - Should not throw
        options.SessionStore.Should().BeNull();
        options.SessionStorePath.Should().BeNull();
        options.DefaultAgent.Should().BeNull();
        options.DefaultAgentPath.Should().BeNull();
        options.ConfigureEachAgentBuilder.Should().BeNull();
    }
}
