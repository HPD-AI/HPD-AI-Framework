using System.Text.Json.Serialization;
using HPD.Agent.Serialization;

[assembly: HpdAgentEventModule("hpd.agent.sandbox.core", typeof(HPD.Agent.Sandbox.Events.SandboxCoreEventJsonContext))]

namespace HPD.Agent.Sandbox.Events;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProcessIsolationViolationEvent))]
[JsonSerializable(typeof(ProcessIsolationInitializedEvent))]
internal sealed partial class SandboxCoreEventJsonContext : JsonSerializerContext;
