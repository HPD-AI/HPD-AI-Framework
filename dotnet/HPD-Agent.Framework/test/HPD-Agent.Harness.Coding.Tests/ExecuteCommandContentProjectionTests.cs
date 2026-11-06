using HPD.Agent;
using HPD.Agent.ToolHarness.Coding;
using Xunit;

public sealed class ExecuteCommandContentProjectionTests
{
    [Fact]
    public void GeneratedPlan_OffloadsCompleteStreamsAndKeepsSummaryInline()
    {
        var registryType = typeof(CodingToolHarness).Assembly.GetType("HPD.Agent.Generated.ToolHarnessRegistry")
            ?? throw new InvalidOperationException("Coding ToolHarness registry was not generated.");
        var factories = (ToolHarnessFactory[])registryType.GetField("All")!.GetValue(null)!;
        var factory = factories.Single(candidate => candidate.ToolHarnessType == typeof(CodingToolHarness));
        var function = factory.CreateFunctions(CodingHarnessTestFactory.Create(), null, null)
            .OfType<HPDAIFunctionFactory.HPDAIFunction>()
            .Single(candidate => candidate.Name == "ExecuteCommand");
        var plan = Assert.IsType<ContentProjectionPlan>(function.HPDOptions.ContentProjectionPlan);

        Assert.Equal(typeof(ExecuteCommandResult), plan.ResultType);
        Assert.Contains(plan.Paths, path => path.Pointer == "/stdout" &&
            path.Name == "stdout" && path.StreamingCapture);
        Assert.Contains(plan.Paths, path => path.Pointer == "/stderr" &&
            path.Name == "stderr" && path.StreamingCapture);
        Assert.Contains(plan.Paths, path => path.Pointer == "/combinedOutput" &&
            path.Name == "combinedOutput" && path.StreamingCapture);
        Assert.DoesNotContain(plan.Paths, path => path.Pointer == "/summary");
    }
}
