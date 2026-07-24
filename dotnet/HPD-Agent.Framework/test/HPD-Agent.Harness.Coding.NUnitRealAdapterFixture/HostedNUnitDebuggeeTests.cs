using NUnit.Framework;

namespace HPD.Agent.ToolHarness.Coding.NUnitRealAdapterFixture;

public sealed class HostedNUnitDebuggeeTests
{
    [Test]
    public void Hosted_nunit_debuggee_executes()
    {
        var value = 40 + 2;

        Assert.That(value, Is.EqualTo(42));
    }
}
