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
    public void FromConfig_DefaultsToFork()
    {
        var subAgent = SubAgent.FromConfig("test", "Test", "desc", MinimalConfig());

        subAgent.ContextPolicy.Should().Be(SubAgentContextPolicy.Fork);
        subAgent.ForkCompaction.Should().BeNull();
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
        updated.RunConfig.Should().BeSameAs(original.RunConfig);
    }

    [Theory]
    [InlineData(SubAgentContextPolicy.Fork)]
    [InlineData(SubAgentContextPolicy.Fresh)]
    [InlineData(SubAgentContextPolicy.Isolated)]
    [InlineData(SubAgentContextPolicy.ModelChoice)]
    public void FromConfig_AcceptsEveryContextPolicy(SubAgentContextPolicy policy)
    {
        var subAgent = SubAgent.FromConfig("test", "Test", "desc", MinimalConfig(), policy);
        subAgent.ContextPolicy.Should().Be(policy);
    }

    [Fact]
    public void ForkCompaction_IsRejectedForContextsThatCannotFork()
    {
        var compaction = new ApplyThreadForkCompaction(new CompactionSpecification
        {
            Point = new CompactAtCurrentHead(),
            Strategy = new RemovalCompaction(),
            CommitMode = CompactionCommitMode.Hard
        });

        var act = () => SubAgent.FromConfig(
            "test",
            "Test",
            "desc",
            MinimalConfig(),
            SubAgentContextPolicy.Fresh,
            compaction);

        act.Should().Throw<ArgumentException>().WithMessage("*Fork compaction*");
    }

    [Fact]
    public void ModelChoice_DefaultsToFresh()
    {
        SubAgentContexts.Resolve(SubAgentContextPolicy.ModelChoice, null)
            .Should().Be(SubAgentContextPolicy.Fresh);
    }

    [Fact]
    public void ModelChoice_UsesRequestedFork()
    {
        using var document = JsonDocument.Parse("""{"context":"fork"}""");

        var requested = SubAgentContexts.ReadRequestedContext(document.RootElement);

        requested.Should().Be(SubAgentContext.Fork);
        SubAgentContexts.Resolve(SubAgentContextPolicy.ModelChoice, requested)
            .Should().Be(SubAgentContextPolicy.Fork);
    }

    [Fact]
    public void CreateSchema_OnlyExposesContextForModelChoice()
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{}}""");

        var fixedSchema = SubAgentContexts.CreateSchema(document.RootElement, SubAgentContextPolicy.Fork);
        var choiceSchema = SubAgentContexts.CreateSchema(document.RootElement, SubAgentContextPolicy.ModelChoice);

        fixedSchema.GetProperty("properties").TryGetProperty("context", out _).Should().BeFalse();
        choiceSchema.GetProperty("properties").GetProperty("context")
            .GetProperty("enum").GetArrayLength().Should().Be(2);
    }
}
