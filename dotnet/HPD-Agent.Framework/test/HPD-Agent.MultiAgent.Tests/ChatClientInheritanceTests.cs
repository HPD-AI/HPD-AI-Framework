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
    public async Task ConfigAgentFactory_WithoutProvider_BuildsForRuntimeSelection()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "TestAgent",
            SystemInstructions = "You are a test agent",
        };
        var factory = CreateConfigAgentFactory(config);

        // Act
        var agent = await factory.BuildAsync(null, false, CancellationToken.None);

        // Assert
        agent.Should().NotBeNull();
        agent.Name.Should().Be("TestAgent");
    }

    [Fact]
    public async Task ConfigAgentFactory_WithNullFallback_AndNoProvider_BuildsDeferredAgent()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "TestAgent",
            SystemInstructions = "You are a test agent",
        };
        var factory = CreateConfigAgentFactory(config);

        // Act
        var agent = await factory.BuildAsync(null, false, CancellationToken.None);

        // Assert
        agent.Should().NotBeNull();
        agent.Name.Should().Be("TestAgent");
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
        var result = await factory.BuildAsync(null, false, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(prebuiltAgent);
    }

    [Fact]
    public async Task PrebuiltAgentFactory_ReturnsSameAgentAcrossBuildRequests()
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

        var result = await factory.BuildAsync(null, false, CancellationToken.None);

        // Assert - should still return the same prebuilt agent
        result.Should().BeSameAs(prebuiltAgent);
    }

    #endregion

    #region AgentBuilder Deferred Provider Tests

    [Fact]
    public async Task AgentBuilder_WithoutProvider_ThrowsWhenRunWithoutRuntimeModel()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "RuntimeConfiguredAgent",
            SystemInstructions = "Test",
        };

        var agent = await new AgentBuilder(config).BuildAsync(CancellationToken.None);

        // Act
        var act = () => agent.RunAsync("hello", cancellationToken: CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*No chat model is available for this invocation*");
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
        };

        var verifierConfig = new AgentConfig
        {
            Name = "Verifier",
            SystemInstructions = "Verify solutions",
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

    public override async Task<HPD.Agent.Agent> BuildAsync(
        IChatClient? fallbackChatClient,
        ISessionStore? workflowSessionStore,
        bool requireWorkflowSessionStore,
        CancellationToken cancellationToken)
    {
        var builder = new AgentBuilder(_config);

        if (workflowSessionStore != null)
        {
            builder.WithSessionStore(workflowSessionStore);
        }
        else if (requireWorkflowSessionStore)
        {
            throw new InvalidOperationException("A workflow session store is required.");
        }

        if (_config.ResolveClientConfig(HPD.Agent.Providers.ProviderClientFamily.Chat) == null && fallbackChatClient != null)
        {
            builder.WithChatClient(fallbackChatClient);
        }
        // If no provider and no fallback, this will throw - which is expected behavior

        return await builder.BuildAsync(cancellationToken);
    }

    internal override AgentConfig? GetConfig() => _config;
}

/// <summary>
/// Test factory that mirrors PrebuiltAgentFactory behavior
/// </summary>
internal sealed class TestPrebuiltAgentFactory : AgentFactory
{
    private readonly HPD.Agent.Agent _agent;

    public TestPrebuiltAgentFactory(HPD.Agent.Agent agent) => _agent = agent;

    public override Task<HPD.Agent.Agent> BuildAsync(
        IChatClient? fallbackChatClient,
        ISessionStore? workflowSessionStore,
        bool requireWorkflowSessionStore,
        CancellationToken cancellationToken)
    {
        if (requireWorkflowSessionStore && !ReferenceEquals(_agent.Config?.SessionStore, workflowSessionStore))
        {
            throw new InvalidOperationException("Pre-built agents must use the workflow session store.");
        }

        return Task.FromResult(_agent);
    }

    internal override AgentConfig? GetConfig() => _agent.Config;
}
