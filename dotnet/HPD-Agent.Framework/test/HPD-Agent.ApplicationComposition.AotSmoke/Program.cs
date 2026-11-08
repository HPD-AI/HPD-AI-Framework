using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.Serialization;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHPD();

using var provider = services.BuildServiceProvider();
var composition = provider.GetRequiredService<HPDApplicationComposition>();

if (!composition.Providers.Descriptors.TryGet("openai", out _))
    return 1;
if (composition.Events.Fragments.Count == 0)
    return 2;
if (composition.Inputs.Fragments.Count == 0)
    return 3;
if (!composition.Events.Codec.TryGetByType(typeof(AutomaticCompositionEvent), out _))
    return 4;
if (!composition.Inputs.TryGetByType(typeof(AutomaticCompositionInput), out _))
    return 5;

Console.WriteLine("automatic application composition succeeded");
return 0;

[EventType("AUTOMATIC_COMPOSITION_EVENT")]
public sealed record AutomaticCompositionEvent(string Value) : AgentEvent;

[AgentInput]
[EventType("AUTOMATIC_COMPOSITION_INPUT")]
public sealed record AutomaticCompositionInput(string Value) : AgentInputEvent;

[JsonSerializable(typeof(AutomaticCompositionEvent))]
[JsonSerializable(typeof(AutomaticCompositionInput))]
internal partial class AutomaticCompositionJsonContext : JsonSerializerContext;
