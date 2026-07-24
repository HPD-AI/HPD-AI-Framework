namespace HPD.Agent.ToolHarness.Coding.RealAdapterFixture;

public sealed class HostedCrashTests
{
    [Fact]
    public void Testhost_crashes_after_debugger_attachment()
        => System.Environment.FailFast("HPD testhost crash qualification");
}
