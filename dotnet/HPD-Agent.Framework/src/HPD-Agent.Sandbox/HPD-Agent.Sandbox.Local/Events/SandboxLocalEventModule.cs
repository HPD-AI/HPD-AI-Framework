using System.Text.Json.Serialization;
using HPD.Agent.Serialization;

[assembly: HpdAgentEventModule("hpd.agent.sandbox.local", typeof(HPD.Agent.Sandbox.Local.Events.SandboxLocalEventJsonContext))]

namespace HPD.Agent.Sandbox.Local.Events;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LocalProcessInvocationStartingEvent))]
[JsonSerializable(typeof(LocalProcessInvocationStartedEvent))]
[JsonSerializable(typeof(LocalProcessInvocationCompletedEvent))]
[JsonSerializable(typeof(LocalProcessInvocationFailedEvent))]
[JsonSerializable(typeof(LocalProcessInvocationTimedOutEvent))]
[JsonSerializable(typeof(LocalProcessInvocationCancelledEvent))]
[JsonSerializable(typeof(LocalProcessInvocationKilledEvent))]
internal sealed partial class SandboxLocalEventJsonContext : JsonSerializerContext;
