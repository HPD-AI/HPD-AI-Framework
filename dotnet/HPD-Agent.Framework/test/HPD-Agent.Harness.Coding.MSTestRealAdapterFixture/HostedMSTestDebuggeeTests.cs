using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HPD.Agent.ToolHarness.Coding.MSTestRealAdapterFixture;

[TestClass]
public sealed class HostedMSTestDebuggeeTests
{
    [TestMethod]
    public void Hosted_mstest_debuggee_executes()
    {
        var value = 40 + 2;

        Assert.AreEqual(42, value);
    }
}
