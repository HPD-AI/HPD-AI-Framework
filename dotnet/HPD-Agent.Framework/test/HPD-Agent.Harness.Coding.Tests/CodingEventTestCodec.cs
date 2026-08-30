using HPD.Agent.Serialization;
using HPD.Agent.ToolHarness.Coding;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Agent.ToolHarness.Coding.Tests;

internal static class CodingEventTestCodec
{
    internal static AgentEventComposition Composition { get; } = AgentEventComposition.Create([
        CoreAgentEventModule.Fragment,
        GeneratedAgentEventModule_HPD_Agent_Harness_Coding_ab3285cb.Fragment
    ]);
    internal static AgentEventCodec Codec => Composition.Codec;

    internal static JsonTypeInfo<TEvent> TypeInfo<TEvent>() where TEvent : AgentEvent =>
        Codec.TryGetByType(typeof(TEvent), out var descriptor)
            ? (JsonTypeInfo<TEvent>)descriptor.JsonTypeInfo
            : throw new InvalidOperationException($"Missing generated event metadata for {typeof(TEvent)}.");
}
