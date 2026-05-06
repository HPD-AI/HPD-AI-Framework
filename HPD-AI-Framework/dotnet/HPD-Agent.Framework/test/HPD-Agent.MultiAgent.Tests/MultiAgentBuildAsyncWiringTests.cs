using System.Reflection;
using HPD.Agent;
using HPD.MultiAgent;
using HPD.MultiAgent.Config;
using HPDAgent.Graph.Abstractions;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Area 4 — MultiAgent.BuildAsync() wiring of IterationOptions, StreamingMode,
/// EnableCheckpointing, and predicate-edge forwarding.
/// All assertions operate through the public API or via reflection on
/// the (internal) settings fields where necessary.
/// </summary>
public class MultiAgentBuildAsyncWiringTests
{
    private static AgentConfig Cfg() => new() { Name = "T", SystemInstructions = "T" };

    // Reflection helpers — WorkflowSettingsConfig is public so we can read it from the instance
    private static WorkflowSettingsConfig GetSettings(AgentWorkflowInstance instance)
    {
        var field = typeof(AgentWorkflowInstance)
            .GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull("_settings must exist on AgentWorkflowInstance");
        return (WorkflowSettingsConfig)field!.GetValue(instance)!;
    }

    // ── 4.1  IterationOptions forwarded when set ──────────────────────────────

    [Fact]
    public async Task BuildAsync_WithIterationOptions_ForwardsToGraph()
    {
        // Arrange via config (public API)
        var config = new MultiAgentWorkflowConfig
        {
            Name = "W",
            Agents = new Dictionary<string, AgentNodeConfig>
            {
                ["a"] = new() { Agent = Cfg() }
            },
            Edges = [],
            Settings = new WorkflowSettingsConfig
            {
                IterationOptions = new IterationOptionsConfig
                {
                    MaxIterations = 5,
                    UseChangeAwareIteration = true
                }
            }
        };

        var instance = await AgentWorkflow.FromConfig(config).BuildAsync();
        var settings = GetSettings(instance);

        settings.IterationOptions.Should().NotBeNull();
        settings.IterationOptions!.MaxIterations.Should().Be(5);
        settings.IterationOptions.UseChangeAwareIteration.Should().BeTrue();
    }

    // ── 4.2  No IterationOptions → build succeeds cleanly ────────────────────

    [Fact]
    public async Task BuildAsync_NoIterationOptions_BuildSucceeds()
    {
        var act = async () => await AgentWorkflow.Create()
            .AddAgent("a", Cfg())
            .BuildAsync();

        await act.Should().NotThrowAsync();
    }

    // ── 4.3  EnableCheckpointing=false → build succeeds without DI store ─────

    [Fact]
    public async Task BuildAsync_EnableCheckpointing_False_DoesNotRequireCheckpointStore()
    {
        // Default EnableCheckpointing is false — no IGraphCheckpointStore registered
        var act = async () => await AgentWorkflow.Create()
            .AddAgent("a", Cfg())
            .BuildAsync();

        await act.Should().NotThrowAsync(
            "EnableCheckpointing=false must not attempt to resolve IGraphCheckpointStore");
    }

    // ── 4.4  EnableCheckpointing=true with no store registered → graceful ─────

    [Fact]
    public async Task BuildAsync_EnableCheckpointing_True_NoStore_Graceful()
    {
        var config = new MultiAgentWorkflowConfig
        {
            Name = "W",
            Agents = new Dictionary<string, AgentNodeConfig>
            {
                ["a"] = new() { Agent = Cfg() }
            },
            Edges = [],
            Settings = new WorkflowSettingsConfig { EnableCheckpointing = true }
        };

        // No IGraphCheckpointStore registered in DI → should still build, store will be null
        var act = async () => await AgentWorkflow.FromConfig(config).BuildAsync();
        await act.Should().NotThrowAsync(
            "missing checkpoint store must be handled gracefully");
    }

    // ── 4.5  Predicate edge forwarded to graph ────────────────────────────────

    [Fact]
    public async Task BuildAsync_PassesPredicateEdgeToGraph()
    {
        var workflow = AgentWorkflow.Create()
            .AddAgent("a", Cfg())
            .AddAgent("b", Cfg());

        workflow.From("a").To("b").When(_ => true);

        var instance = await workflow.BuildAsync();
        var edge = instance.Graph.Edges.Single(edge => edge.From == "a" && edge.To == "b");

        edge.Predicate.Should().NotBeNull();
    }

    // ── 4.6  Settings object forwarded to instance ────────────────────────────

    [Fact]
    public async Task BuildAsync_PassesSettingsToInstance()
    {
        var config = new MultiAgentWorkflowConfig
        {
            Name = "W",
            Agents = new Dictionary<string, AgentNodeConfig>
            {
                ["a"] = new() { Agent = Cfg() }
            },
            Edges = [],
            Settings = new WorkflowSettingsConfig
            {
                MaxIterations = 42,
                EnableMetrics = true
            }
        };

        var instance = await AgentWorkflow.FromConfig(config).BuildAsync();
        var settings = GetSettings(instance);

        settings.MaxIterations.Should().Be(42);
        settings.EnableMetrics.Should().BeTrue();
    }
}
