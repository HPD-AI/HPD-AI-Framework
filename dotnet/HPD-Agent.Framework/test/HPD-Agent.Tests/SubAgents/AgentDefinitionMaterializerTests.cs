using FluentAssertions;

namespace HPD.Agent.Tests.SubAgents;

public sealed class AgentDefinitionMaterializerTests
{
    [Fact]
    public async Task MaterializeAsync_ParentFallbackStoresIndependentChildSnapshot()
    {
        var store = new InMemoryAgentStore();
        var parent = new AgentConfig { Name = "parent", SystemInstructions = "original" };
        var definition = SubAgent.FromParent("coding/worker", "worker", "Works on a task.");

        await new AgentDefinitionMaterializer(store).MaterializeAsync(
            definition, parent, "TestHarness.Worker", CancellationToken.None);
        parent.SystemInstructions = "mutated";

        var stored = await store.LoadAsync("coding/worker");
        stored.Should().NotBeNull();
        stored!.Config.Should().NotBeSameAs(parent);
        stored.Config.SystemInstructions.Should().Be("original");
        stored.Config.AgentId.Should().Be("coding/worker");
    }

    [Fact]
    public async Task MaterializeAsync_ConflictingOwnerFailsBeforeRuntime()
    {
        var store = new InMemoryAgentStore();
        var materializer = new AgentDefinitionMaterializer(store);
        var definition = SubAgent.FromConfig(
            "coding/reviewer", "reviewer", "Reviews work.", new AgentConfig { Name = "reviewer" });
        await materializer.MaterializeAsync(definition, new AgentConfig(), "HarnessA.Reviewer", CancellationToken.None);

        var act = () => materializer.MaterializeAsync(
            definition, new AgentConfig(), "HarnessB.Reviewer", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already owned by*HarnessA.Reviewer*");
    }
}
