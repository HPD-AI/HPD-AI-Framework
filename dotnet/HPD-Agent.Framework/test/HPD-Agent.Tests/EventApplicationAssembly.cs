using HPD.Agent.Serialization;

namespace HPD.Agent.Tests;

internal static class TestEventApplication
{
    internal static AgentEventComposition Composition =>
        AgentEventCompositionHost.TryGetApplication(typeof(TestEventApplication).Assembly.GetName().Name!, out var composition)
            ? composition
            : throw new InvalidOperationException("The generated test event composition was not initialized.");

    internal static AgentEventCodec Codec => Composition.Codec;
}
