using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Builders;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Area 3 — GraphBuilder.WithIterationOptions()
/// Verifies the new fluent method stores options and forwards them to the built graph.
/// </summary>
public class GraphBuilderIterationOptionsTests
{
    private static GraphBuilder BaseBuilder(string name = "test") =>
        new GraphBuilder().WithName(name).AddStartNode().AddEndNode();

    // ── 3.1  Fluent chain returns the same builder ────────────────────────────

    [Fact]
    public void WithIterationOptions_ReturnsBuilder()
    {
        var builder = BaseBuilder();
        var options = new IterationOptions { MaxIterations = 10 };

        var returned = builder.WithIterationOptions(options);

        returned.Should().BeSameAs(builder);
    }

    // ── 3.2  Built graph carries the options ──────────────────────────────────

    [Fact]
    public void WithIterationOptions_AppliedToBuiltGraph()
    {
        var options = new IterationOptions
        {
            MaxIterations = 7,
            UseChangeAwareIteration = true,
            EnableAutoConvergence = false
        };

        var graph = BaseBuilder()
            .WithIterationOptions(options)
            .Build();

        graph.IterationOptions.Should().NotBeNull();
        graph.IterationOptions!.MaxIterations.Should().Be(7);
        graph.IterationOptions.UseChangeAwareIteration.Should().BeTrue();
        graph.IterationOptions.EnableAutoConvergence.Should().BeFalse();
    }

    // ── 3.3  Omitting WithIterationOptions leaves IterationOptions null ───────

    [Fact]
    public void WithIterationOptions_NotCalled_GraphHasNullOptions()
    {
        var graph = BaseBuilder().Build();

        graph.IterationOptions.Should().BeNull();
    }

    // ── 3.4  Null argument throws ─────────────────────────────────────────────

    [Fact]
    public void WithIterationOptions_NullArg_Throws()
    {
        var builder = BaseBuilder();

        var act = () => builder.WithIterationOptions(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
