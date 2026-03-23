using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.HuggingFace;

/// <summary>
/// HuggingFace Inference API-specific provider configuration.
/// These options map to the HuggingFace Serverless Inference API parameters.
///
/// JSON Example:
/// <code>
/// {
///   "Provider": {
///     "ProviderKey": "huggingface",
///     "ModelName": "meta-llama/Meta-Llama-3-8B-Instruct",
///     "ApiKey": "hf_...",
///     "ProviderOptionsJson": "{\"maxNewTokens\":250,\"temperature\":0.7}"
///   }
/// }
/// </code>
/// </summary>
public class HuggingFaceProviderConfig
{
    //
    // CORE PARAMETERS
    //

    /// <summary>
    /// The amount of new tokens to be generated. This does not include the input length.
    /// It is an estimate of the size of generated text you want.
    /// Each new token slows down the request, so look for balance between response times and length of text generated.
    /// Maps to GenerateTextRequestParameters.MaxNewTokens.
    /// Default: 250.
    /// </summary>
    [JsonPropertyName("maxNewTokens")]
    public int? MaxNewTokens { get; set; }

    //
    // SAMPLING PARAMETERS
    //

    /// <summary>
    /// The temperature of the sampling operation.
    /// - 1 means regular sampling
    /// - 0 means always take the highest score
    /// - 100.0 is getting closer to uniform probability
    /// Maps to GenerateTextRequestParameters.Temperature.
    /// Default: 1.0.
    /// </summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    /// <summary>
    /// Float to define the tokens that are within the sample operation of text generation.
    /// Add tokens in the sample for more probable to least probable until the sum of
    /// the probabilities is greater than top_p.
    /// Ranges from 0.0 to 1.0.
    /// Maps to GenerateTextRequestParameters.TopP.
    /// </summary>
    [JsonPropertyName("topP")]
    public double? TopP { get; set; }

    /// <summary>
    /// Integer to define the top tokens considered within the sample operation to create new text.
    /// Maps to GenerateTextRequestParameters.TopK.
    /// </summary>
    [JsonPropertyName("topK")]
    public int? TopK { get; set; }

    /// <summary>
    /// The more a token is used within generation the more it is penalized to not be
    /// picked in successive generation passes.
    /// Reduces repetition in generated text.
    /// Maps to GenerateTextRequestParameters.RepetitionPenalty.
    /// Default: 1.0 (no penalty).
    /// </summary>
    [JsonPropertyName("repetitionPenalty")]
    public double? RepetitionPenalty { get; set; }

    //
    // GENERATION CONTROL
    //

    /// <summary>
    /// Whether or not to use sampling. If false, use greedy decoding instead.
    /// Maps to GenerateTextRequestParameters.DoSample.
    /// Default: true.
    /// </summary>
    [JsonPropertyName("doSample")]
    public bool? DoSample { get; set; }

    /// <summary>
    /// The number of propositions/sequences you want to be returned.
    /// Maps to GenerateTextRequestParameters.NumReturnSequences.
    /// Default: 1.
    /// </summary>
    [JsonPropertyName("numReturnSequences")]
    public int? NumReturnSequences { get; set; }

    /// <summary>
    /// If set to false, the return results will not contain the original query
    /// making it easier for prompting.
    /// Maps to GenerateTextRequestParameters.ReturnFullText.
    /// Default: true.
    /// </summary>
    [JsonPropertyName("returnFullText")]
    public bool? ReturnFullText { get; set; }

    //
    // TIMING AND PERFORMANCE
    //

    /// <summary>
    /// The amount of time in seconds that the query should take maximum.
    /// Network can cause some overhead so it will be a soft limit.
    /// Use this in combination with MaxNewTokens for best results.
    /// Maps to GenerateTextRequestParameters.MaxTime.
    /// </summary>
    [JsonPropertyName("maxTime")]
    public double? MaxTime { get; set; }

    //
    // API OPTIONS
    //

    /// <summary>
    /// There is a cache layer on the inference API to speedup requests we have already seen.
    /// Most models can use those results as models are deterministic.
    /// However if you use a non-deterministic model, you can set this parameter to false
    /// to prevent the caching mechanism from being used.
    /// Maps to GenerateTextRequestOptions.UseCache.
    /// Default: true.
    /// </summary>
    [JsonPropertyName("useCache")]
    public bool? UseCache { get; set; }

    /// <summary>
    /// If the model is not ready, wait for it instead of receiving 503.
    /// It limits the number of requests required to get your inference done.
    /// It is advised to only set this flag to true after receiving a 503 error
    /// as it will limit hanging in your application to known places.
    /// Maps to GenerateTextRequestOptions.WaitForModel.
    /// Default: false.
    /// </summary>
    [JsonPropertyName("waitForModel")]
    public bool? WaitForModel { get; set; }

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
