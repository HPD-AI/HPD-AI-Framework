using HPD.Agent;
using HPD.Agent.TUI;
using HPD.Agent.ToolHarness.Coding.TUI;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Tests;

public sealed class CodingHarnessTuiProjectTests
{
    [Fact]
    public void AddCodingHarnessTui_ReturnsBuilder()
    {
        var builder = new HpdAgentTuiBuilder();

        var result = builder.AddCodingHarnessTui();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void ProjectReferences_CanSeeCodingHarnessEvents()
    {
        typeof(FileEditAppliedEvent).Should().BeAssignableTo<AgentEvent>();
    }
}
