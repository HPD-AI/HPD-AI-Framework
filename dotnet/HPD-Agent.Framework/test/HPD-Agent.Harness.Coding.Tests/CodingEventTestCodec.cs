using HPD.Agent.Serialization;
using HPD.Agent.ToolHarness.Coding;

namespace HPD.Agent.ToolHarness.Coding.Tests;

internal static class CodingEventTestCodec
{
    internal static AgentEventComposition Composition { get; } = AgentEventComposition.Create([
        CoreAgentEventModule.Fragment,
        CodingAgentEventModule.Fragment
    ]);
    internal static AgentEventCodec Codec => Composition.Codec;
}
