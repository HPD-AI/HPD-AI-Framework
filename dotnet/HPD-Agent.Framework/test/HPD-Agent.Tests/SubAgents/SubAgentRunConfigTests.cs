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
            ProviderKey = "openrouter",
            ModelId = "parent-model",
            Security = new AgentSecurityProfile
            {
                Approval = AgentApprovalPolicy.AutoApprove,
                Sandbox = AgentSandboxPolicy.Disabled,
                SandboxEscape = AgentSandboxEscapePolicy.Deny
            },
            Chat = new ChatRunConfig { Temperature = 0.25 },
            CoalesceDeltas = true,
            SystemInstructions = "Parent persona",
            UserMessage = "Parent input",
            StructuredOutput = new StructuredOutputOptions()
        };

        var child = SubAgentRunConfig.Inherit().Resolve(parent);

        child.ProviderKey.Should().Be("openrouter");
        child.ModelId.Should().Be("parent-model");
        child.Security.Should().Be(parent.Security);
        child.Security.Should().NotBeSameAs(parent.Security);
        child.Chat!.Temperature.Should().Be(0.25);
        child.CoalesceDeltas.Should().BeTrue();
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
            SystemInstructions = "Parent persona",
            UserMessage = "Parent input",
            ContextOverrides = new Dictionary<string, object> { ["tenant"] = "one" },
            PermissionOverrides = new Dictionary<string, bool> { ["shell"] = true }
        };

        var child = SubAgentRunConfig
            .InheritOnly(SubAgentRunConfigFields.All)
            .Resolve(parent);

        child.Should().NotBeSameAs(parent);
        child.SystemInstructions.Should().Be("Parent persona");
        child.UserMessage.Should().Be("Parent input");
        child.ContextOverrides.Should().NotBeSameAs(parent.ContextOverrides);
        child.PermissionOverrides.Should().NotBeSameAs(parent.PermissionOverrides);
    }

    [Fact]
    public void IsolatedWithOverride_UsesChildOnlyConfiguration()
    {
        var parent = new AgentRunConfig
        {
            ProviderKey = "parent-provider",
            SystemInstructions = "Parent persona"
        };

        var child = SubAgentRunConfig
            .Isolated()
            .Override(config =>
            {
                config.ProviderKey = "child-provider";
                config.SystemInstructions = "Child override";
            })
            .Resolve(parent);

        child.ProviderKey.Should().Be("child-provider");
        child.SystemInstructions.Should().Be("Child override");
    }

    [Fact]
    public void Override_RunsAfterInheritance()
    {
        var parent = new AgentRunConfig
        {
            ProviderKey = "parent-provider",
            Chat = new ChatRunConfig { Temperature = 0.8 }
        };

        var child = SubAgentRunConfig
            .Inherit()
            .Override(config => config.Chat!.Temperature = 0.1)
            .Resolve(parent);

        child.ProviderKey.Should().Be("parent-provider");
        child.Chat!.Temperature.Should().Be(0.1);
        parent.Chat!.Temperature.Should().Be(0.8);
    }
}
