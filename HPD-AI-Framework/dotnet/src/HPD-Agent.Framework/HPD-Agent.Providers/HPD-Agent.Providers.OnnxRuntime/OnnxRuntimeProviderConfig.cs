using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.OnnxRuntime;

/// <summary>
/// ONNX Runtime GenAI-specific provider configuration.
/// These options map to GeneratorParams search options and Config provider options.
///
/// JSON Example:
/// <code>
/// {
///   "Provider": {
///     "ProviderKey": "onnx-runtime",
///     "ModelName": "phi-3-mini",
///     "ProviderOptionsJson": "{\"modelPath\":\"/path/to/model\",\"maxLength\":2048,\"temperature\":0.7}"
///   }
/// }
/// </code>
/// </summary>
public class OnnxRuntimeProviderConfig
{
    //
    // REQUIRED PARAMETERS
    //

    /// <summary>
    /// Path to the ONNX model directory containing the model files.
    /// This is required for ONNX Runtime to load the model.
    /// Can also be set via the ONNX_MODEL_PATH environment variable.
    /// </summary>
    [JsonPropertyName("modelPath")]
    public string? ModelPath { get; set; }

    //
    // CORE GENERATION PARAMETERS
    //

    /// <summary>
    /// Maximum length for final sequence length.
    /// If omitted or 0, will be set to model.context_length.
    /// Maps to GeneratorParams SetSearchOption("max_length", value).
    /// </summary>
    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; set; }

    /// <summary>
    /// Minimum length for final sequence length.
    /// Generation will continue until at least this many tokens are generated.
    /// Maps to GeneratorParams SetSearchOption("min_length", value).
    /// </summary>
    [JsonPropertyName("minLength")]
    public int? MinLength { get; set; }

    /// <summary>
    /// Batch size of inputs. Default is 1.
    /// Maps to GeneratorParams SetSearchOption("batch_size", value).
    /// </summary>
    [JsonPropertyName("batchSize")]
    public int? BatchSize { get; set; }

    //
    // SAMPLING PARAMETERS
    //

    /// <summary>
    /// True to do randomized sampling through top_k and top_p.
    /// If false, the top logit score is chosen (greedy decoding).
    /// Default is false.
    /// Maps to GeneratorParams SetSearchOption("do_sample", value).
    /// </summary>
    [JsonPropertyName("doSample")]
    public bool? DoSample { get; set; }

    /// <summary>
    /// Temperature to control randomness during generation.
    /// Higher values (e.g., 1.0+) make output more random.
    /// Lower values (e.g., 0.1-0.5) make output more focused and deterministic.
    /// Default is 1.0.
    /// Only used when DoSample is true.
    /// Maps to GeneratorParams SetSearchOption("temperature", value).
    /// </summary>
    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    /// <summary>
    /// Number of highest probability vocabulary tokens to keep for top-k-filtering.
    /// Only the top K tokens with highest probability will be considered.
    /// Default is 50.
    /// Only used when DoSample is true.
    /// Maps to GeneratorParams SetSearchOption("top_k", value).
    /// </summary>
    [JsonPropertyName("topK")]
    public int? TopK { get; set; }

    /// <summary>
    /// Use nucleus sampling (Top-P). Only the most probable tokens with probabilities
    /// that add up to top_p or higher are kept for generation.
    /// If set to a float between 0 and 1, enables nucleus sampling.
    /// For example, 0.9 means only consider tokens that make up the top 90% probability mass.
    /// Only used when DoSample is true.
    /// Maps to GeneratorParams SetSearchOption("top_p", value).
    /// </summary>
    [JsonPropertyName("topP")]
    public float? TopP { get; set; }

    //
    // REPETITION CONTROL
    //

    /// <summary>
    /// Repetition penalty to reduce likelihood of repeating tokens.
    /// Values > 1.0 penalize repetition. 1.0 means no penalty.
    /// Typical values are 1.0 to 1.2.
    /// Maps to GeneratorParams SetSearchOption("repetition_penalty", value).
    /// </summary>
    [JsonPropertyName("repetitionPenalty")]
    public float? RepetitionPenalty { get; set; }

    /// <summary>
    /// Size of n-grams to prevent from repeating.
    /// If set to a value > 0, prevents n-grams of this size from appearing twice.
    /// For example, no_repeat_ngram_size=2 prevents any 2-word phrase from repeating.
    /// Maps to GeneratorParams SetSearchOption("no_repeat_ngram_size", value).
    /// Note: This parameter is marked as unused in the ONNX Runtime GenAI source.
    /// </summary>
    [JsonPropertyName("noRepeatNgramSize")]
    public int? NoRepeatNgramSize { get; set; }

    //
    // BEAM SEARCH PARAMETERS
    //

    /// <summary>
    /// Number of beams for beam search. 1 means no beam search (greedy decoding).
    /// Higher values explore more possibilities but are slower.
    /// Default is 1.
    /// Maps to GeneratorParams SetSearchOption("num_beams", value).
    /// </summary>
    [JsonPropertyName("numBeams")]
    public int? NumBeams { get; set; }

    /// <summary>
    /// Number of sequences to return after beam search. Default is 1.
    /// Must be less than or equal to NumBeams.
    /// Maps to GeneratorParams SetSearchOption("num_return_sequences", value).
    /// </summary>
    [JsonPropertyName("numReturnSequences")]
    public int? NumReturnSequences { get; set; }

    /// <summary>
    /// Whether to stop the beam search when at least num_beams sentences are finished per batch.
    /// Default is true.
    /// Maps to GeneratorParams SetSearchOption("early_stopping", value).
    /// </summary>
    [JsonPropertyName("earlyStopping")]
    public bool? EarlyStopping { get; set; }

    /// <summary>
    /// Exponential penalty to the length that is used with beam-based generation.
    /// length_penalty > 1.0 promotes longer sequences.
    /// length_penalty < 1.0 encourages shorter sequences.
    /// Default is 1.0 (no penalty).
    /// Maps to GeneratorParams SetSearchOption("length_penalty", value).
    /// </summary>
    [JsonPropertyName("lengthPenalty")]
    public float? LengthPenalty { get; set; }

    /// <summary>
    /// Diversity penalty for beam search groups.
    /// Encourages different beams to produce diverse outputs.
    /// Higher values increase diversity.
    /// Maps to GeneratorParams SetSearchOption("diversity_penalty", value).
    /// Note: This parameter is marked as unused in the ONNX Runtime GenAI source.
    /// </summary>
    [JsonPropertyName("diversityPenalty")]
    public float? DiversityPenalty { get; set; }

    //
    // PERFORMANCE OPTIMIZATION
    //

    /// <summary>
    /// The past/present key-value tensors are shared and allocated once to max_length.
    /// This can improve performance by reusing memory buffers.
    /// Only supported on CUDA.
    /// Default is false.
    /// Maps to GeneratorParams SetSearchOption("past_present_share_buffer", value).
    /// </summary>
    [JsonPropertyName("pastPresentShareBuffer")]
    public bool? PastPresentShareBuffer { get; set; }

    /// <summary>
    /// Chunk size for prefill chunking during context processing.
    /// If set to a value > 0, enables chunking with the specified chunk size.
    /// This can help with memory management for long contexts.
    /// Maps to GeneratorParams SetSearchOption("chunk_size", value).
    /// </summary>
    [JsonPropertyName("chunkSize")]
    public int? ChunkSize { get; set; }

    //
    // RANDOMNESS CONTROL
    //

    /// <summary>
    /// Seed for the random number generator used in sampling.
    /// -1 = Seed with random device (non-deterministic).
    /// Any other value = Use value to seed RNG (deterministic).
    /// Default is -1.
    /// Maps to GeneratorParams SetSearchOption("random_seed", value).
    /// </summary>
    [JsonPropertyName("randomSeed")]
    public int? RandomSeed { get; set; }

    //
    // STOP SEQUENCES
    //

    /// <summary>
    /// Custom text sequences that will cause the model to stop generating.
    /// If the model generates one of these sequences, generation stops.
    /// Example: ["<|end|>", "<|user|>", "<|system|>"]
    /// Note: This is handled by the OnnxRuntimeGenAIChatClient options, not GeneratorParams.
    /// </summary>
    [JsonPropertyName("stopSequences")]
    public List<string>? StopSequences { get; set; }

    //
    // PROMPT FORMATTING
    //

    /// <summary>
    /// Whether to enable conversation caching for better performance.
    /// This should only be set to true when the chat client is not shared across multiple uses.
    /// Only one thread of conversation is cached at any given time.
    /// Default is false.
    /// Note: This is handled by the OnnxRuntimeGenAIChatClient options, not GeneratorParams.
    /// </summary>
    [JsonPropertyName("enableCaching")]
    public bool EnableCaching { get; set; }

    //
    // CONSTRAINED DECODING
    //

    /// <summary>
    /// Type of guidance/constraint to apply during generation.
    /// Options: "json", "grammar", "regex", etc.
    /// Use SetGuidance to set this along with GuidanceData.
    /// Maps to GeneratorParams.SetGuidance(type, data, enableFFTokens).
    /// </summary>
    [JsonPropertyName("guidanceType")]
    public string? GuidanceType { get; set; }

    /// <summary>
    /// Data for the guidance constraint (e.g., JSON schema, grammar specification, regex pattern).
    /// The format depends on the GuidanceType.
    /// Maps to GeneratorParams.SetGuidance(type, data, enableFFTokens).
    /// </summary>
    [JsonPropertyName("guidanceData")]
    public string? GuidanceData { get; set; }

    /// <summary>
    /// Whether to enable fast-forward tokens for guidance.
    /// This can improve performance for constrained decoding.
    /// Default is false.
    /// Maps to GeneratorParams.SetGuidance(type, data, enableFFTokens).
    /// </summary>
    [JsonPropertyName("guidanceEnableFFTokens")]
    public bool GuidanceEnableFFTokens { get; set; }

    //
    // EXECUTION PROVIDER CONFIGURATION
    //

    /// <summary>
    /// List of execution providers to use (e.g., "cuda", "cpu", "dml", "qnn", "openvino", "trt", "webgpu").
    /// If not specified, ONNX Runtime will use the default providers for the platform.
    /// The order matters - providers are tried in the order specified.
    /// Example: ["cuda", "cpu"] tries CUDA first, falls back to CPU.
    /// Maps to Config.ClearProviders() then Config.AppendProvider() for each.
    /// </summary>
    [JsonPropertyName("providers")]
    public List<string>? Providers { get; set; }

    /// <summary>
    /// Provider-specific options as key-value pairs.
    /// Format: { "provider_name": { "option_name": "option_value" } }
    /// Example: { "cuda": { "device_id": "0", "cudnn_conv_algo_search": "DEFAULT" } }
    /// Maps to Config.SetProviderOption(provider, option, value).
    /// </summary>
    [JsonPropertyName("providerOptions")]
    public Dictionary<string, Dictionary<string, string>>? ProviderOptions { get; set; }

    //
    // HARDWARE DEVICE FILTERING (for decoders)
    //

    /// <summary>
    /// Hardware device type for decoder execution (e.g., "cpu", "gpu", "npu").
    /// Maps to Config.SetDecoderProviderOptionsHardwareDeviceType().
    /// </summary>
    [JsonPropertyName("hardwareDeviceType")]
    public string? HardwareDeviceType { get; set; }

    /// <summary>
    /// Hardware device ID for decoder execution (e.g., 0, 1, 2 for multi-GPU systems).
    /// Maps to Config.SetDecoderProviderOptionsHardwareDeviceId().
    /// </summary>
    [JsonPropertyName("hardwareDeviceId")]
    public uint? HardwareDeviceId { get; set; }

    /// <summary>
    /// Hardware vendor ID for decoder execution.
    /// Maps to Config.SetDecoderProviderOptionsHardwareVendorId().
    /// </summary>
    [JsonPropertyName("hardwareVendorId")]
    public uint? HardwareVendorId { get; set; }

    //
    // ADAPTERS (LoRA)
    //

    /// <summary>
    /// Path to adapter file for Multi-LoRA support.
    /// ONNX Runtime GenAI supports loading adapters to modify model behavior.
    /// Example: "/path/to/adapters.onnx_adapter"
    /// </summary>
    [JsonPropertyName("adapterPath")]
    public string? AdapterPath { get; set; }

    /// <summary>
    /// Name of the adapter to activate.
    /// Maps to Generator.SetActiveAdapter(adapters, adapterName).
    /// </summary>
    [JsonPropertyName("adapterName")]
    public string? AdapterName { get; set; }
}
