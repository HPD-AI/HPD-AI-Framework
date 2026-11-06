using HPD.Agent;
using HPD.Agent.Middleware;
using Xunit;

namespace HPD.Agent.Tests.Tools;

public sealed class FieldOffloadProjectionTests
{
    [Fact]
    public void SourceGeneratedPlan_IncludesMarkedPublicFields()
    {
        var registryType = typeof(FieldOffloadHarness).Assembly.GetType("HPD.Agent.Generated.ToolHarnessRegistry")
            ?? throw new InvalidOperationException("ToolHarness registry was not generated.");
        var factories = (ToolHarnessFactory[])registryType.GetField("All")!.GetValue(null)!;
        var factory = factories.Single(candidate => candidate.ToolHarnessType == typeof(FieldOffloadHarness));
        var function = factory.CreateFunctions(new FieldOffloadHarness(), null, null)
            .OfType<HPDAIFunctionFactory.HPDAIFunction>()
            .Single();
        var path = Assert.Single(function.HPDOptions.ContentProjectionPlan!.Paths);

        Assert.Equal("/evidence", path.Pointer);
        Assert.Equal(ContentSensitivity.Restricted, path.Sensitivity);
    }
}

public partial class FieldOffloadHarness
{
    [AIFunction]
    public FieldOffloadResult Run(FunctionExecutionContext context) =>
        new() { Summary = "inline", Evidence = "field evidence" };
}

public sealed class FieldOffloadResult
{
    public string Summary = "";

    [Offload(Sensitivity = ContentSensitivity.Restricted)]
    public string Evidence = "";
}
