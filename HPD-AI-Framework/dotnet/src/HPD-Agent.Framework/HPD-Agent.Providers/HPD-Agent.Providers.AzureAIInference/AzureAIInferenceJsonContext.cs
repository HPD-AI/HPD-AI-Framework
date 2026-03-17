using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.AzureAIInference;

/// <summary>
/// JSON serialization context for Azure AI Inference provider types.
/// Enables AOT-compatible serialization for FFI scenarios.
/// Note: While this context is AOT-ready, the Azure AI Inference SDK itself
/// is NOT AOT compatible (AotCompatOptOut=true in the SDK).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AzureAIInferenceProviderConfig))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
public partial class AzureAIInferenceJsonContext : JsonSerializerContext
{
}
