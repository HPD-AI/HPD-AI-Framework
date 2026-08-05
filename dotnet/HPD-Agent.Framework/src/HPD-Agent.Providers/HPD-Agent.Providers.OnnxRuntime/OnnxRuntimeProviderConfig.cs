using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.OnnxRuntime;

/// <summary>
/// ONNX Runtime GenAI-specific provider configuration.
/// </summary>
/// <remarks>
/// Provider configuration is for local model loading, ONNX execution providers,
/// and chat-client construction. Per-request generator/search options belong on
/// <see cref="OnnxRuntimeChatRequestOptions"/> or generic chat runtime options.
/// </remarks>
public class OnnxRuntimeProviderConfig : global::HPD.Agent.IProviderConfig
{
    /// <summary>
    /// Path to the ONNX Runtime GenAI model directory containing the model files.
    /// Can also be set via the ONNX_MODEL_PATH environment variable.
    /// </summary>
    [JsonPropertyName("modelPath")]
    public string? ModelPath { get; set; }

    /// <summary>
    /// Ordered ONNX Runtime execution providers to use, such as cpu, cuda, dml, qnn, openvino, trt, or webgpu.
    /// If unset, ONNX Runtime uses the platform default provider chain.
    /// </summary>
    [JsonPropertyName("providers")]
    public List<string>? Providers { get; set; }

    /// <summary>
    /// Provider-specific ONNX Runtime execution options.
    /// The first key is the provider name; the nested keys and values are passed to Config.SetProviderOption.
    /// </summary>
    [JsonPropertyName("executionProviderOptions")]
    public Dictionary<string, Dictionary<string, string>>? ExecutionProviderOptions { get; set; }

    /// <summary>Gets or sets a process-local formatter used to create the native model prompt.</summary>
    /// <remarks>This executable delegate is runtime-only and is never serialized.</remarks>
    [JsonIgnore]
    public Func<IEnumerable<ChatMessage>, ChatOptions?, string>? PromptFormatter { get; set; }

    /// <summary>
    /// Hardware device type for decoder execution, such as cpu, gpu, or npu.
    /// Applied to each configured provider.
    /// </summary>
    [JsonPropertyName("hardwareDeviceType")]
    public string? HardwareDeviceType { get; set; }

    /// <summary>
    /// Hardware device ID for decoder execution.
    /// Applied to each configured provider.
    /// </summary>
    [JsonPropertyName("hardwareDeviceId")]
    public uint? HardwareDeviceId { get; set; }

    /// <summary>
    /// Hardware vendor ID for decoder execution.
    /// Applied to each configured provider.
    /// </summary>
    [JsonPropertyName("hardwareVendorId")]
    public uint? HardwareVendorId { get; set; }

    /// <summary>
    /// Whether to cache the most recent conversation in the ONNX Runtime GenAI chat client.
    /// Enable this only when the chat client is not shared across concurrent conversations.
    /// </summary>
    [JsonPropertyName("enableCaching")]
    public bool EnableCaching { get; set; }

    /// <summary>
    /// Enables HPD's experimental structured tool-calling adapter for ONNX Runtime GenAI.
    /// HPD asks the local model for a constrained JSON tool-call envelope and converts it to tool calls.
    /// </summary>
    [JsonPropertyName("enableStructuredToolCalling")]
    public bool EnableStructuredToolCalling { get; set; }
}
