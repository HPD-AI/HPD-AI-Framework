using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Ollama;

/// <summary>
/// Ollama-specific provider configuration.
/// These options map directly to Ollama's RequestOptions.
///
/// JSON Example:
/// <code>
/// {
///   "Provider": {
///     "ProviderKey": "ollama",
///     "ModelName": "llama3:8b",
///     "Endpoint": "http://localhost:11434",
///     "ProviderOptionsJson": "{\"temperature\":0.7,\"numPredict\":2048,\"numCtx\":4096}"
///   }
/// }
/// </code>
/// </summary>
public class OllamaProviderConfig
{
    //
    // CORE PARAMETERS
    //

    /// <summary>
    /// Maximum number of tokens to predict when generating text.
    /// Default: 128, -1 = infinite generation, -2 = fill context.
    /// Maps to RequestOptions.NumPredict.
    /// </summary>
    [JsonPropertyName("numPredict")]
    public int? NumPredict { get; set; }

    /// <summary>
    /// Sets the size of the context window used to generate the next token.
    /// Default: 2048.
    /// Maps to RequestOptions.NumCtx.
    /// </summary>
    [JsonPropertyName("numCtx")]
    public int? NumCtx { get; set; }

    //
    // SAMPLING PARAMETERS
    //

    /// <summary>
    /// The temperature of the model. Increasing the temperature will make the
    /// model answer more creatively. Default: 0.8.
    /// Maps to RequestOptions.Temperature.
    /// </summary>
    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    /// <summary>
    /// Works together with top-k. A higher value (e.g., 0.95) will lead to
    /// more diverse text, while a lower value (e.g., 0.5) will generate more
    /// focused and conservative text. Default: 0.9.
    /// Maps to RequestOptions.TopP.
    /// </summary>
    [JsonPropertyName("topP")]
    public float? TopP { get; set; }

    /// <summary>
    /// Reduces the probability of generating nonsense. A higher value
    /// (e.g. 100) will give more diverse answers, while a lower value (e.g. 10)
    /// will be more conservative. Default: 40.
    /// Maps to RequestOptions.TopK.
    /// </summary>
    [JsonPropertyName("topK")]
    public int? TopK { get; set; }

    /// <summary>
    /// Alternative to the top_p, and aims to ensure a balance of quality and variety.
    /// min_p represents the minimum probability for a token to be considered,
    /// relative to the probability of the most likely token. For example,
    /// with min_p=0.05 and the most likely token having a probability of 0.9,
    /// logits with a value less than 0.05*0.9=0.045 are filtered out. Default: 0.0.
    /// Maps to RequestOptions.MinP.
    /// </summary>
    [JsonPropertyName("minP")]
    public float? MinP { get; set; }

    /// <summary>
    /// The typical-p value to use for sampling. Locally Typical Sampling implementation
    /// described in the paper https://arxiv.org/abs/2202.00666. Default: 1.0.
    /// Maps to RequestOptions.TypicalP.
    /// </summary>
    [JsonPropertyName("typicalP")]
    public float? TypicalP { get; set; }

    /// <summary>
    /// Tail free sampling is used to reduce the impact of less probable
    /// tokens from the output. A higher value (e.g., 2.0) will reduce the
    /// impact more, while a value of 1.0 disables this setting. Default: 1.
    /// Maps to RequestOptions.TfsZ.
    /// </summary>
    [JsonPropertyName("tfsZ")]
    public float? TfsZ { get; set; }

    //
    // REPETITION CONTROL
    //

    /// <summary>
    /// Sets how strongly to penalize repetitions.
    /// A higher value (e.g., 1.5) will penalize repetitions more strongly,
    /// while a lower value (e.g., 0.9) will be more lenient. Default: 1.1.
    /// Maps to RequestOptions.RepeatPenalty.
    /// </summary>
    [JsonPropertyName("repeatPenalty")]
    public float? RepeatPenalty { get; set; }

    /// <summary>
    /// Sets how far back for the model to look back to prevent repetition.
    /// Default: 64, 0 = disabled, -1 = num_ctx.
    /// Maps to RequestOptions.RepeatLastN.
    /// </summary>
    [JsonPropertyName("repeatLastN")]
    public int? RepeatLastN { get; set; }

    /// <summary>
    /// The penalty to apply to tokens based on their presence in the prompt.
    /// Default: 0.0.
    /// Maps to RequestOptions.PresencePenalty.
    /// </summary>
    [JsonPropertyName("presencePenalty")]
    public float? PresencePenalty { get; set; }

    /// <summary>
    /// The penalty to apply to tokens based on their frequency in the prompt.
    /// Default: 0.0.
    /// Maps to RequestOptions.FrequencyPenalty.
    /// </summary>
    [JsonPropertyName("frequencyPenalty")]
    public float? FrequencyPenalty { get; set; }

    /// <summary>
    /// Penalize newline tokens. Default: True.
    /// Maps to RequestOptions.PenalizeNewline.
    /// </summary>
    [JsonPropertyName("penalizeNewline")]
    public bool? PenalizeNewline { get; set; }

    //
    // DETERMINISM
    //

    /// <summary>
    /// Sets the random number seed to use for generation.
    /// Setting this to a specific number will make the model generate the same
    /// text for the same prompt. Default: 0.
    /// Maps to RequestOptions.Seed.
    /// </summary>
    [JsonPropertyName("seed")]
    public int? Seed { get; set; }

    /// <summary>
    /// Sets the stop sequences to use. When this pattern is encountered the
    /// LLM will stop generating text and return. Multiple stop patterns may
    /// be set by specifying multiple separate stop parameters in a modelfile.
    /// Maps to RequestOptions.Stop.
    /// </summary>
    [JsonPropertyName("stop")]
    public string[]? Stop { get; set; }

    //
    // MIROSTAT SAMPLING
    //

    /// <summary>
    /// Enable Mirostat sampling for controlling perplexity.
    /// Default: 0, 0 = disabled, 1 = Mirostat, 2 = Mirostat 2.0.
    /// Maps to RequestOptions.MiroStat.
    /// </summary>
    [JsonPropertyName("miroStat")]
    public int? MiroStat { get; set; }

    /// <summary>
    /// Influences how quickly the algorithm responds to feedback from the
    /// generated text. A lower learning rate will result in slower adjustments,
    /// while a higher learning rate will make the algorithm more responsive.
    /// Default: 0.1.
    /// Maps to RequestOptions.MiroStatEta.
    /// </summary>
    [JsonPropertyName("miroStatEta")]
    public float? MiroStatEta { get; set; }

    /// <summary>
    /// Controls the balance between coherence and diversity of the output.
    /// A lower value will result in more focused and coherent text.
    /// Default: 5.0.
    /// Maps to RequestOptions.MiroStatTau.
    /// </summary>
    [JsonPropertyName("miroStatTau")]
    public float? MiroStatTau { get; set; }

    //
    // CONTEXT AND MEMORY
    //

    /// <summary>
    /// Number of tokens to keep from the initial prompt.
    /// Default: 4, -1 = all.
    /// Maps to RequestOptions.NumKeep.
    /// </summary>
    [JsonPropertyName("numKeep")]
    public int? NumKeep { get; set; }

    //
    // PERFORMANCE AND HARDWARE
    //

    /// <summary>
    /// The number of layers to send to the GPU(s). On macOS it defaults to
    /// 1 to enable metal support, 0 to disable.
    /// Maps to RequestOptions.NumGpu.
    /// </summary>
    [JsonPropertyName("numGpu")]
    public int? NumGpu { get; set; }

    /// <summary>
    /// This option controls which GPU is used for small tensors. The overhead of
    /// splitting the computation across all GPUs is not worthwhile. The GPU will
    /// use slightly more VRAM to store a scratch buffer for temporary results.
    /// By default, GPU 0 is used.
    /// Maps to RequestOptions.MainGpu.
    /// </summary>
    [JsonPropertyName("mainGpu")]
    public int? MainGpu { get; set; }

    /// <summary>
    /// Enable low VRAM mode. Default: False.
    /// Maps to RequestOptions.LowVram.
    /// </summary>
    [JsonPropertyName("lowVram")]
    public bool? LowVram { get; set; }

    /// <summary>
    /// Enable f16 key/value. Default: False.
    /// Maps to RequestOptions.F16kv.
    /// </summary>
    [JsonPropertyName("f16kv")]
    public bool? F16kv { get; set; }

    /// <summary>
    /// Return logits for all the tokens, not just the last one. Default: False.
    /// Maps to RequestOptions.LogitsAll.
    /// </summary>
    [JsonPropertyName("logitsAll")]
    public bool? LogitsAll { get; set; }

    //
    // THREADING AND BATCH PROCESSING
    //

    /// <summary>
    /// Sets the number of threads to use during computation. By default,
    /// Ollama will detect this for optimal performance.
    /// It is recommended to set this value to the number of physical CPU cores
    /// your system has (as opposed to the logical number of cores).
    /// Maps to RequestOptions.NumThread.
    /// </summary>
    [JsonPropertyName("numThread")]
    public int? NumThread { get; set; }

    /// <summary>
    /// Prompt processing maximum batch size. Default: 512.
    /// Maps to RequestOptions.NumBatch.
    /// </summary>
    [JsonPropertyName("numBatch")]
    public int? NumBatch { get; set; }

    /// <summary>
    /// The number of GQA groups in the transformer layer. Required for some
    /// models, for example it is 8 for llama2:70b.
    /// Maps to RequestOptions.NumGqa.
    /// </summary>
    [JsonPropertyName("numGqa")]
    public int? NumGqa { get; set; }

    //
    // MEMORY MANAGEMENT
    //

    /// <summary>
    /// Models are mapped into memory by default, which allows the system to
    /// load only the necessary parts as needed. Disabling mmap makes loading
    /// slower but reduces pageouts if you're not using mlock. If the model is
    /// bigger than your RAM, turning off mmap stops it from loading. Default: True.
    /// Maps to RequestOptions.UseMmap.
    /// </summary>
    [JsonPropertyName("useMmap")]
    public bool? UseMmap { get; set; }

    /// <summary>
    /// Lock the model in memory to prevent swapping. This can improve
    /// performance, but it uses more RAM and may slow down loading. Default: False.
    /// Maps to RequestOptions.UseMlock.
    /// </summary>
    [JsonPropertyName("useMlock")]
    public bool? UseMlock { get; set; }

    /// <summary>
    /// Enable NUMA support. Default: False.
    /// Maps to RequestOptions.Numa.
    /// </summary>
    [JsonPropertyName("numa")]
    public bool? Numa { get; set; }

    /// <summary>
    /// Load only the vocabulary, not the weights. Default: False.
    /// Maps to RequestOptions.VocabOnly.
    /// </summary>
    [JsonPropertyName("vocabOnly")]
    public bool? VocabOnly { get; set; }

    //
    // OLLAMA-SPECIFIC OPTIONS
    //

    /// <summary>
    /// Gets or sets the KeepAlive property, which decides how long a given model should stay loaded.
    /// Format: duration string like "5m", "1h", "300s", or "-1" to keep loaded indefinitely.
    /// Maps to ChatRequest.KeepAlive.
    /// </summary>
    [JsonPropertyName("keepAlive")]
    public string? KeepAlive { get; set; }

    /// <summary>
    /// Gets or sets the format to return a response in. Currently accepts "json" or JsonSchema or null.
    /// Maps to ChatRequest.Format.
    /// </summary>
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    /// <summary>
    /// Gets or sets the full prompt or prompt template (overrides what is defined in the Modelfile).
    /// Maps to ChatRequest.Template.
    /// </summary>
    [JsonPropertyName("template")]
    public string? Template { get; set; }

    /// <summary>
    /// Enable or disable thinking for reasoning models like openthinker, qwen3,
    /// deepseek-r1, phi4-reasoning. Can be boolean (true/false) or string ("high", "medium", "low").
    /// This might cause errors with non-reasoning models.
    /// More information: https://github.com/ollama/ollama/releases/tag/v0.9.0
    /// Maps to ChatRequest.Think.
    /// </summary>
    [JsonPropertyName("think")]
    public object? Think { get; set; }

    //
    // ADVANCED OPTIONS
    //

    /// <summary>
    /// Additional custom parameters to pass to the model.
    /// This is a flexible dictionary for model-specific parameters not covered
    /// by the standard options.
    /// </summary>
    [JsonPropertyName("additionalProperties")]
    public Dictionary<string, object>? AdditionalProperties { get; set; }
}
