using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace HPD.Agent.Tests.SubAgents;

public class SubAgentContextPolicyTests
{
    private static AgentConfig MinimalConfig() => new()
    {
        Name = "SubAgentUnderTest",
        SystemInstructions = "Test sub-agent."
    };

    [Fact]
    public void FromConfig_DefaultsToHandoff()
    {
        var subAgent = SubAgent.FromConfig("test", "Test", "desc", MinimalConfig());

        subAgent.ContextPolicy.Should().Be(SubAgentContextPolicy.Handoff);
    }

    [Fact]
    public void Availability_DefaultsToRootOnly()
    {
        var subAgent = SubAgent.FromConfig("test", "Test", "desc", MinimalConfig());

        subAgent.Availability.Should().BeSameAs(SubAgentAvailability.RootOnly);
        subAgent.Availability.AllowsInvocationFrom(0).Should().BeTrue();
        subAgent.Availability.AllowsInvocationFrom(1).Should().BeFalse();
    }

    [Fact]
    public void Availability_ThroughDepthUsesChildDepthSemantics()
    {
        var availability = SubAgentAvailability.ThroughDepth(2);

        availability.AllowsInvocationFrom(0).Should().BeTrue();
        availability.AllowsInvocationFrom(1).Should().BeTrue();
        availability.AllowsInvocationFrom(2).Should().BeFalse();
    }

    [Fact]
    public void WithAvailability_PreservesTheRestOfTheDefinition()
    {
        var original = SubAgent.FromConfig("test", "Test", "desc", MinimalConfig());

        var updated = original.WithAvailability(SubAgentAvailability.AnyAllowedDepth);

        updated.Availability.Should().BeSameAs(SubAgentAvailability.AnyAllowedDepth);
        updated.AgentId.Should().Be(original.AgentId);
        updated.Configuration.Should().BeSameAs(original.Configuration);
    }

    [Theory]
    [InlineData(SubAgentContextPolicy.Handoff)]
    [InlineData(SubAgentContextPolicy.Fresh)]
    [InlineData(SubAgentContextPolicy.Isolated)]
    [InlineData(SubAgentContextPolicy.ModelChoice)]
    public void FromConfig_AcceptsEveryContextPolicy(SubAgentContextPolicy policy)
    {
        var subAgent = SubAgent.FromConfig("test", "Test", "desc", MinimalConfig(), policy);
        subAgent.ContextPolicy.Should().Be(policy);
    }

    [Fact]
    public void ModelChoice_DefaultsToFresh()
    {
        SubAgentContexts.Resolve(SubAgentContextPolicy.ModelChoice, null)
            .Should().Be(SubAgentContextPolicy.Fresh);
    }

    [Fact]
    public void ModelChoice_UsesRequestedHandoff()
    {
        using var document = JsonDocument.Parse("""{"context":"handoff"}""");

        var requested = SubAgentContexts.ReadRequestedContext(document.RootElement);

        requested.Should().Be(SubAgentContext.Handoff);
        SubAgentContexts.Resolve(SubAgentContextPolicy.ModelChoice, requested)
            .Should().Be(SubAgentContextPolicy.Handoff);
    }

    [Fact]
    public void CreateSchema_OnlyExposesContextForModelChoice()
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{}}""");

        var fixedSchema = SubAgentContexts.CreateSchema(document.RootElement, SubAgentContextPolicy.Handoff);
        var choiceSchema = SubAgentContexts.CreateSchema(document.RootElement, SubAgentContextPolicy.ModelChoice);

        fixedSchema.GetProperty("properties").TryGetProperty("context", out _).Should().BeFalse();
        choiceSchema.GetProperty("properties").GetProperty("context")
            .GetProperty("enum").GetArrayLength().Should().Be(2);
    }
}
