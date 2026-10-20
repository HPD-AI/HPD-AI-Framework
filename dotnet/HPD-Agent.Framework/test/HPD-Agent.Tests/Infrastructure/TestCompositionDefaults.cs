using System.Runtime.CompilerServices;
using HPD.Agent.Serialization;

namespace HPD.Agent.Tests.Infrastructure;

internal static class TestCompositionDefaults
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        AgentEventCompositionHost.SetDefaultApplication(
            typeof(TestCompositionDefaults).Assembly.GetName().Name!);
    }
}
