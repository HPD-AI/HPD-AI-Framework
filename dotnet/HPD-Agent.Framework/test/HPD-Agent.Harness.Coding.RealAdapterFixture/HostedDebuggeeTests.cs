namespace HPD.Agent.ToolHarness.Coding.RealAdapterFixture;

public sealed class HostedDebuggeeTests
{
    [Fact]
    public void Hosted_debuggee_executes()
    {
        var value = 40 + 2;

        Assert.Equal(42, value);
    }
}
