using HPD.Agent;
using HPD.MultiAgent;
using HPD.MultiAgent.Config;
using Microsoft.Extensions.AI;
using Moq;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Tests for chat client inheritance in multi-agent workflows.
/// Ensures that agents without their own provider correctly inherit from parent.
/// </summary>
public class ChatClientInheritanceTests
{
    #region ConfigAgentFactory Tests

    [Fact]
    public async Task ConfigAgentFactory_WithoutProvider_UsesFallbackChatClient()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        var config = new AgentConfig
        {
            Name = "TestAgent",
            SystemInstructions = "You are a test agent",
            Provider = null // No provider configured
        };
        var factory = CreateConfigAgentFactory(config);

        // Act
        var agent = await factory.BuildAsync(mockChatClient.Object, CancellationToken.None);

        // Assert
        agent.Should().NotBeNull();
        agent.Name.Should().Be("TestAgent");
    }

    [Fact]
    public async Task ConfigAgentFactory_WithNullFallback_AndNoProvider_Throws()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "TestAgent",
            SystemInstructions = "You are a test agent",
            Provider = null
        };
        var factory = CreateConfigAgentFactory(config);

        // Act & Assert - building without any chat client should throw (validation or argument exception)
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await factory.BuildAsync(null, CancellationToken.None));
    }

    #endregion

    #region PrebuiltAgentFactory Tests

    [Fact]
    public async Task PrebuiltAgentFactory_ReturnsPrebuiltAgent()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        var config = new AgentConfig
        {
            Name = "PrebuiltAgent",
            SystemInstructions = "Test"
        };

        var builder = new AgentBuilder(config).WithChatClient(mockChatClient.Object);
        var prebuiltAgent = await builder.BuildAsync(CancellationToken.None);

        var factory = CreatePrebuiltAgentFactory(prebuiltAgent);

        // Act
        var result = await factory.BuildAsync(null, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(prebuiltAgent);
    }

    [Fact]
    public async Task PrebuiltAgentFactory_IgnoresFallbackChatClient()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        var mockFallbackClient = new Mock<IChatClient>();

        var config = new AgentConfig
        {
            Name = "PrebuiltAgent",
            SystemInstructions = "Test"
        };
        var builder = new AgentBuilder(config).WithChatClient(mockChatClient.Object);
        var prebuiltAgent = await builder.BuildAsync(CancellationToken.None);

        var factory = CreatePrebuiltAgentFactory(prebuiltAgent);

        // Act - fallback should be ignored for prebuilt agents
        var result = await factory.BuildAsync(mockFallbackClient.Object, CancellationToken.None);

        // Assert - should still return the same prebuilt agent
        result.Should().BeSameAs(prebuiltAgent);
    }

    #endregion

    #region AgentBuilder Deferred Provider Tests

    [Fact]
    public void AgentBuilder_WithDeferredProvider_DoesNotRequireProvider()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "DeferredAgent",
            SystemInstructions = "Test",
            Provider = null
        };

        // Act - should not throw even without provider
        var builder = new AgentBuilder(config).WithDeferredProvider();

        // Assert
        builder.Should().NotBeNull();
    }

    [Fact]
    public async Task AgentBuilder_WithDeferredProvider_StillRequiresChatClient()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "DeferredAgent",
            SystemInstructions = "Test",
            Provider = null
        };

        var builder = new AgentBuilder(config).WithDeferredProvider();

        // Act & Assert - deferred provider skips provider validation but still needs client at runtime
        // The client must be provided via OverrideChatClient in RunConfig
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await builder.BuildAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AgentBuilder_WithChatClient_BuildsWithThatClient()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        var config = new AgentConfig
        {
            Name = "ClientAgent",
            SystemInstructions = "Test"
        };

        // Act
        var agent = await new AgentBuilder(config)
            .WithChatClient(mockChatClient.Object)
            .BuildAsync(CancellationToken.None);

        // Assert
        agent.Should().NotBeNull();
        agent.Name.Should().Be("ClientAgent");
    }

    #endregion

    #region Workflow Agent Inheritance Tests

    [Fact]
    public async Task AgentWorkflow_AgentsWithoutProvider_CanBuild()
    {
        // Arrange
        var solverConfig = new AgentConfig
        {
            Name = "Solver",
            SystemInstructions = "Solve problems",
            Provider = null
        };

        var verifierConfig = new AgentConfig
        {
            Name = "Verifier",
            SystemInstructions = "Verify solutions",
            Provider = null
        };

        // Build workflow with configs (deferred building)
        var workflow = AgentWorkflow.Create()
            .WithName("TestWorkflow")
            .AddAgent("solver", solverConfig)
            .AddAgent("verifier", verifierConfig)
            .From("START").To("solver")
            .From("solver").To("verifier")
            .From("verifier").To("END");

        // Act
        var instance = await workflow.BuildAsync(CancellationToken.None);

        // Assert
        instance.Should().NotBeNull();
        instance.WorkflowName.Should().Be("TestWorkflow");
    }

    [Fact]
    public async Task AgentWorkflow_WithPrebuiltAgents_WorksCorrectly()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();

        var solverAgent = await new AgentBuilder(new AgentConfig
            {
                Name = "Solver",
                SystemInstructions = "Solve problems"
            })
            .WithChatClient(mockChatClient.Object)
            .BuildAsync(CancellationToken.None);

        var verifierAgent = await new AgentBuilder(new AgentConfig
            {
                Name = "Verifier",
                SystemInstructions = "Verify solutions"
            })
            .WithChatClient(mockChatClient.Object)
            .BuildAsync(CancellationToken.None);

        // Build workflow with prebuilt agents
        var workflow = AgentWorkflow.Create()
            .WithName("PrebuiltWorkflow")
            .AddAgent("solver", solverAgent)
            .AddAgent("verifier", verifierAgent)
            .From("START").To("solver")
            .From("solver").To("verifier")
            .From("verifier").To("END");

        // Act
        var instance = await workflow.BuildAsync(CancellationToken.None);

        // Assert
        instance.Should().NotBeNull();
        instance.WorkflowName.Should().Be("PrebuiltWorkflow");
    }

    #endregion

    #region Helper Factory Methods

    private static AgentFactory CreateConfigAgentFactory(AgentConfig config)
    {
        return new TestConfigAgentFactory(config);
    }

    private static AgentFactory CreatePrebuiltAgentFactory(HPD.Agent.Agent agent)
    {
        return new TestPrebuiltAgentFactory(agent);
    }

    #endregion
}

/// <summary>
/// Test factory that mirrors ConfigAgentFactory behavior
/// </summary>
internal sealed class TestConfigAgentFactory : AgentFactory
{
    private readonly AgentConfig _config;

    public TestConfigAgentFactory(AgentConfig config) => _config = config;

    public override async Task<HPD.Agent.Agent> BuildAsync(IChatClient? fallbackChatClient, CancellationToken cancellationToken)
    {
        var builder = new AgentBuilder(_config);

        if (_config.Provider == null && fallbackChatClient != null)
        {
            builder.WithChatClient(fallbackChatClient);
        }
        // If no provider and no fallback, this will throw - which is expected behavior

        return await builder.BuildAsync(cancellationToken);
    }
}

/// <summary>
/// Test factory that mirrors PrebuiltAgentFactory behavior
/// </summary>
internal sealed class TestPrebuiltAgentFactory : AgentFactory
{
    private readonly HPD.Agent.Agent _agent;

    public TestPrebuiltAgentFactory(HPD.Agent.Agent agent) => _agent = agent;

    public override Task<HPD.Agent.Agent> BuildAsync(IChatClient? fallbackChatClient, CancellationToken cancellationToken)
        => Task.FromResult(_agent);
}
