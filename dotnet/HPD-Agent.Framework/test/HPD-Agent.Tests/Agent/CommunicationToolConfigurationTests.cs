using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Communication;

public sealed class CommunicationToolConfigurationTests
{
    [Fact]
    public async Task StandaloneAgent_DoesNotRegisterCommunicationToolsByDefault()
    {
        var client = new FakeChatClient();
        client.EnqueueTextResponse("done");
        await using var agent = TestAgentFactory.Create(
            new AgentConfig { Name = "standalone" },
            client);

        await agent.RunAsync("hello");

        var tools = Assert.Single(client.CapturedRequestSnapshots).ToolNames;
        Assert.DoesNotContain("Parent", tools);
        Assert.DoesNotContain("AskUser", tools);
    }

    [Fact]
    public async Task AskUser_RequiresBuildCapabilityAndHonorsRunOverride()
    {
        var client = new FakeChatClient();
        client.EnqueueTextResponse("first");
        client.EnqueueTextResponse("second");
        await using var agent = TestAgentFactory.Create(
            new AgentConfig
            {
                Name = "questions",
                AllowUserQuestions = true
            },
            client);

        await agent.RunAsync("use the agent default");
        await agent.RunAsync("disable for this run", runConfig: new AgentRunConfig
        {
            AllowUserQuestions = false
        });

        Assert.Contains("AskUser", client.CapturedRequestSnapshots[0].ToolNames);
        Assert.DoesNotContain("AskUser", client.CapturedRequestSnapshots[1].ToolNames);
    }

    [Fact]
    public async Task RunConfig_CannotEnableAnUnbuiltCommunicationCapability()
    {
        var client = new FakeChatClient();
        client.EnqueueTextResponse("done");
        await using var agent = TestAgentFactory.Create(
            new AgentConfig { Name = "standalone" },
            client);

        var exception = await Assert.ThrowsAsync<AgentRunConfigurationException>(() =>
            agent.RunAsync("ask", runConfig: new AgentRunConfig
            {
                AllowUserQuestions = true,
                EnableParentCommunication = true
            }));

        Assert.Equal("user_questions_not_enabled", exception.Code);
        Assert.Empty(client.CapturedRequestSnapshots);
    }

    [Fact]
    public async Task ParentRunOverride_RequiresBuiltParentCapability()
    {
        var client = new FakeChatClient();
        client.EnqueueTextResponse("done");
        await using var agent = TestAgentFactory.Create(
            new AgentConfig { Name = "standalone" },
            client);

        var exception = await Assert.ThrowsAsync<AgentRunConfigurationException>(() =>
            agent.RunAsync("parent", runConfig: new AgentRunConfig
            {
                EnableParentCommunication = true
            }));

        Assert.Equal("parent_communication_not_enabled", exception.Code);
        Assert.Empty(client.CapturedRequestSnapshots);
    }

    [Fact]
    public async Task Parent_RemainsAbsentFromRootExecutionEvenWhenCapabilityIsBuilt()
    {
        var client = new FakeChatClient();
        client.EnqueueTextResponse("done");
        await using var agent = TestAgentFactory.Create(
            new AgentConfig
            {
                Name = "standalone-with-parent-capability",
                EnableParentCommunication = true
            },
            client);

        await agent.RunAsync("hello");

        Assert.DoesNotContain("Parent", Assert.Single(client.CapturedRequestSnapshots).ToolNames);
    }

    [Fact]
    public void BuilderMethods_ConfigureCommunicationCapabilities()
    {
        var builder = new AgentBuilder()
            .WithParentCommunication()
            .WithUserQuestions();

        Assert.True(builder.Config.EnableParentCommunication);
        Assert.True(builder.Config.AllowUserQuestions);

        builder
            .WithParentCommunication(false)
            .WithUserQuestions(false);

        Assert.False(builder.Config.EnableParentCommunication);
        Assert.False(builder.Config.AllowUserQuestions);
    }
}
