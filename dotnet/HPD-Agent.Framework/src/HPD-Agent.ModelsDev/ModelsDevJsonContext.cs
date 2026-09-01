using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.ModelsDev;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Dictionary<string, ModelsDevProvider>))]
[JsonSerializable(typeof(ModelsDevCachedData))]
[JsonSerializable(typeof(ProviderUsageValuation))]
[JsonSerializable(typeof(ModelsDevValuationProvenance))]
[JsonSerializable(typeof(ModelsDevValuationDetails))]
[JsonSerializable(typeof(ModelsDevRateSelection))]
internal sealed partial class ModelsDevJsonContext : JsonSerializerContext;
