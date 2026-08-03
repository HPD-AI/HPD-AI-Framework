using FluentAssertions;
using HPD.Agent.StructuredOutput;
using Xunit;

namespace HPD.Agent.Tests.SubAgents;

public sealed class SubAgentRunConfigTests
{
    [Fact]
    public void SubAgent_DefaultsToRunEnvironmentInheritance()
    {
        var definition = SubAgent.FromConfig(
            "test/reviewer",
            "reviewer",
            "Reviews a change.",
            new AgentConfig { Name = "Reviewer" });

        definition.RunConfig.InheritedFields.Should().Be(SubAgentRunConfigFields.Default);
    }

    [Fact]
    public void WithRunConfig_ReturnsNewDeclarationWithoutMutatingOriginal()
    {
        var definition = SubAgent.FromConfig(
            "test/reviewer",
            "reviewer",
            "Reviews a change.",
            new AgentConfig { Name = "Reviewer" });

        var isolated = definition.WithRunConfig(SubAgentRunConfig.Isolated());

        isolated.Should().NotBeSameAs(definition);
        isolated.RunConfig.InheritedFields.Should().Be(SubAgentRunConfigFields.None);
        definition.RunConfig.InheritedFields.Should().Be(SubAgentRunConfigFields.Default);
    }

    [Fact]
    public void DefaultSelection_InheritsEnvironmentWithoutParentBehavior()
    {
        var parent = new AgentRunConfig
        {
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig
            {
                ProviderKey = "openrouter",
                ModelName = "parent-model",
                Temperature = 0.25
            } },
            Security = new AgentSecurityRunConfig
            {
                Approval = AgentApprovalPolicy.AutoApprove,
                Sandbox = new AgentSandboxRunConfig
                {
                    Mode = AgentSandboxPolicy.Disabled,
                    Escape = AgentSandboxEscapePolicy.Deny
                }
            },
            Streaming = new StreamingRunConfig { CoalesceDeltas = true },
            SystemInstructions = new SystemInstructionsRunConfig { Override = "Parent persona" },
            UserMessage = "Parent input",
            StructuredOutput = new StructuredOutputOptions()
        };

        var child = SubAgentRunConfig.Inherit().Resolve(parent);

        child.Clients.Chat!.ProviderKey.Should().Be("openrouter");
        child.Clients.Chat.ModelName.Should().Be("parent-model");
        child.Security.Should().Be(parent.Security);
        child.Security.Should().NotBeSameAs(parent.Security);
        child.Clients.Chat.Temperature.Should().Be(0.25);
        child.Streaming!.CoalesceDeltas.Should().BeTrue();
        child.SystemInstructions.Should().BeNull();
        child.UserMessage.Should().BeNull();
        child.StructuredOutput.Should().BeNull();
    }

    [Fact]
    public void IncludeAndExclude_ReturnIndependentSelections()
    {
        var defaults = SubAgentRunConfig.Inherit();
        var instructions = defaults.Include(SubAgentRunConfigFields.Instructions);
        var withoutChat = instructions.Exclude(SubAgentRunConfigFields.Chat);

        defaults.InheritedFields.Should().Be(SubAgentRunConfigFields.Default);
        instructions.InheritedFields.Should().HaveFlag(SubAgentRunConfigFields.Instructions);
        withoutChat.InheritedFields.Should().NotHaveFlag(SubAgentRunConfigFields.Chat);
        withoutChat.InheritedFields.Should().HaveFlag(SubAgentRunConfigFields.Instructions);
    }

    [Fact]
    public void InheritAll_CopiesParentBehaviorIntoIndependentRootSnapshot()
    {
        var parent = new AgentRunConfig
        {
            SystemInstructions = new SystemInstructionsRunConfig { Override = "Parent persona" },
            UserMessage = "Parent input",
            Context = new AgentContextRunConfig
            {
                Properties = new Dictionary<string, object> { ["tenant"] = "one" }
            },
            Security = new AgentSecurityRunConfig
            {
                PermissionOverrides = new Dictionary<string, bool> { ["shell"] = true }
            }
        };

        var child = SubAgentRunConfig
            .InheritOnly(SubAgentRunConfigFields.All)
            .Resolve(parent);

        child.Should().NotBeSameAs(parent);
        child.SystemInstructions!.Override.Should().Be("Parent persona");
        child.UserMessage.Should().Be("Parent input");
        child.Context!.Properties.Should().NotBeSameAs(parent.Context!.Properties);
        child.Security.PermissionOverrides.Should().NotBeSameAs(parent.Security.PermissionOverrides);
    }

    [Fact]
    public void IsolatedWithOverride_UsesChildOnlyConfiguration()
    {
        var parent = new AgentRunConfig
        {
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig { ProviderKey = "parent-provider" } },
            SystemInstructions = new SystemInstructionsRunConfig { Override = "Parent persona" }
        };

        var child = SubAgentRunConfig
            .Isolated()
            .Override(config =>
            {
                config.Clients.Chat ??= new ChatClientConfig();
                config.Clients.Chat.ProviderKey = "child-provider";
                config.SystemInstructions = new SystemInstructionsRunConfig { Override = "Child override" };
            })
            .Resolve(parent);

        child.Clients.Chat!.ProviderKey.Should().Be("child-provider");
        child.SystemInstructions!.Override.Should().Be("Child override");
    }

    [Fact]
    public void Override_RunsAfterInheritance()
    {
        var parent = new AgentRunConfig
        {
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig
            {
                ProviderKey = "parent-provider",
                Temperature = 0.8
            } }
        };

        var child = SubAgentRunConfig
            .Inherit()
            .Override(config => config.Clients.Chat!.Temperature = 0.1)
            .Resolve(parent);

        child.Clients.Chat!.ProviderKey.Should().Be("parent-provider");
        child.Clients.Chat.Temperature.Should().Be(0.1);
        parent.Clients.Chat!.Temperature.Should().Be(0.8);
    }
}
