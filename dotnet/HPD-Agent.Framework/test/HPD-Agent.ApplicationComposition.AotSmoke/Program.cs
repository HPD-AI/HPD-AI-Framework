using HPD.Agent;
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

Console.WriteLine("automatic application composition succeeded");
return 0;
