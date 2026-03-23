using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Bedrock;

/// <summary>
/// AWS Bedrock-specific provider configuration using the AWS BedrockRuntime SDK.
/// These options map to Bedrock's InferenceConfiguration and ToolConfiguration.
///
/// JSON Example:
/// <code>
/// {
///   "Provider": {
///     "ProviderKey": "bedrock",
///     "ModelName": "anthropic.claude-3-5-sonnet-20241022-v2:0",
///     "ProviderOptionsJson": "{\"region\":\"us-east-1\",\"maxTokens\":4096,\"temperature\":0.7}"
///   }
/// }
/// </code>
/// </summary>
public class BedrockProviderConfig
{
    //
    // CORE PARAMETERS
    //

    /// <summary>
    /// Maximum number of tokens to generate. Default: model-specific default.
    /// Maps to InferenceConfiguration.MaxTokens.
    /// Range: Minimum value of 1.
    /// </summary>
    [JsonPropertyName("maxTokens")]
    public int? MaxTokens { get; set; }

    //
    // SAMPLING PARAMETERS
    //

    /// <summary>
    /// The likelihood of the model selecting higher-probability options while generating a response.
    /// A lower value makes the model more likely to choose higher-probability options,
    /// while a higher value makes the model more likely to choose lower-probability options.
    /// Range: 0.0 to 1.0.
    /// Maps to InferenceConfiguration.Temperature.
    /// </summary>
    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    /// <summary>
    /// The percentage of most-likely candidates that the model considers for the next token.
    /// For example, if you choose a value of 0.8 for topP, the model selects from
    /// the top 80% of the probability distribution of tokens that could be next in the sequence.
    /// Range: 0.0 to 1.0.
    /// Maps to InferenceConfiguration.TopP.
    /// </summary>
    [JsonPropertyName("topP")]
    public float? TopP { get; set; }

    /// <summary>
    /// A list of stop sequences. A stop sequence is a sequence of characters that causes
    /// the model to stop generating the response.
    /// Maximum of 2500 stop sequences.
    /// Maps to InferenceConfiguration.StopSequences.
    /// </summary>
    [JsonPropertyName("stopSequences")]
    public List<string>? StopSequences { get; set; }

    //
    // AWS CREDENTIALS & REGION
    //

    /// <summary>
    /// AWS Region where the Bedrock service is hosted (e.g., "us-east-1", "us-west-2").
    /// Required. Can also be set via AWS_REGION environment variable.
    /// </summary>
    [JsonPropertyName("region")]
    public string? Region { get; set; }

    /// <summary>
    /// AWS Access Key ID for authentication.
    /// Optional. If not provided, the SDK will use the default AWS credential chain:
    /// 1. Environment variables (AWS_ACCESS_KEY_ID)
    /// 2. AWS credentials file (~/.aws/credentials)
    /// 3. IAM role (for EC2, ECS, Lambda, etc.)
    /// </summary>
    [JsonPropertyName("accessKeyId")]
    public string? AccessKeyId { get; set; }

    /// <summary>
    /// AWS Secret Access Key for authentication.
    /// Optional. If not provided, the SDK will use the default AWS credential chain:
    /// 1. Environment variables (AWS_SECRET_ACCESS_KEY)
    /// 2. AWS credentials file (~/.aws/credentials)
    /// 3. IAM role (for EC2, ECS, Lambda, etc.)
    /// </summary>
    [JsonPropertyName("secretAccessKey")]
    public string? SecretAccessKey { get; set; }

    /// <summary>
    /// AWS Session Token for temporary credentials (when using STS/AssumeRole).
    /// Optional. Only needed when using temporary security credentials.
    /// Can also be set via AWS_SESSION_TOKEN environment variable.
    /// </summary>
    [JsonPropertyName("sessionToken")]
    public string? SessionToken { get; set; }

    /// <summary>
    /// AWS profile name to use from the credentials file (~/.aws/credentials).
    /// Optional. If not specified, uses the "default" profile.
    /// Can also be set via AWS_PROFILE environment variable.
    /// </summary>
    [JsonPropertyName("profileName")]
    public string? ProfileName { get; set; }

    //
    // TOOL/FUNCTION CALLING
    //

    /// <summary>
    /// Tool choice behavior control. Options:
    /// - "auto" (default): Model decides whether to call tools
    /// - "any": Model must call at least one tool (RequiredChatToolMode in M.E.AI)
    /// - "tool": Force a specific tool by name (set ToolChoiceName)
    ///
    /// Maps to ToolConfiguration.ToolChoice.
    /// Note: Tools are defined at the request level, not in config.
    /// </summary>
    [JsonPropertyName("toolChoice")]
    public string? ToolChoice { get; set; }

    /// <summary>
    /// Specific tool name to force when ToolChoice is "tool".
    /// Only used when ToolChoice = "tool".
    /// </summary>
    [JsonPropertyName("toolChoiceName")]
    public string? ToolChoiceName { get; set; }

    //
    // ADVANCED OPTIONS
    //

    /// <summary>
    /// Additional model request fields to pass to Bedrock.
    /// This is a flexible dictionary for model-specific parameters not covered
    /// by the standard InferenceConfiguration options.
    /// These are passed directly to the additionalModelRequestFields parameter.
    /// </summary>
    [JsonPropertyName("additionalModelRequestFields")]
    public Dictionary<string, object>? AdditionalModelRequestFields { get; set; }

    //
    // PROMPT CACHING (Claude 3.5+ models)
    //

    /// <summary>
    /// Enable prompt caching for supported models (Claude 3.5+ on Bedrock).
    /// When enabled, frequently used context (like system prompts) can be cached
    /// to reduce latency and costs for subsequent requests.
    /// Default: false.
    /// Note: Requires setting cache points in message AdditionalProperties.
    /// </summary>
    [JsonPropertyName("enablePromptCaching")]
    public bool EnablePromptCaching { get; set; }

    //
    // GUARDRAILS
    //

    /// <summary>
    /// Guardrail identifier to apply to the request.
    /// Guardrails help filter harmful content and enforce safety policies.
    /// Format: "guardrail-id" or "arn:aws:bedrock:region:account:guardrail/guardrail-id"
    /// </summary>
    [JsonPropertyName("guardrailIdentifier")]
    public string? GuardrailIdentifier { get; set; }

    /// <summary>
    /// Version of the guardrail to use.
    /// Can be a specific version number or "DRAFT".
    /// Required if GuardrailIdentifier is specified.
    /// </summary>
    [JsonPropertyName("guardrailVersion")]
    public string? GuardrailVersion { get; set; }

    /// <summary>
    /// Trace behavior for guardrail evaluation.
    /// When enabled, includes detailed guardrail assessment in the response.
    /// Default: "disabled"
    /// Options: "enabled", "disabled"
    /// </summary>
    [JsonPropertyName("guardrailTrace")]
    public string? GuardrailTrace { get; set; }

    //
    // SERVICE CONFIGURATION
    //

    /// <summary>
    /// Request timeout in milliseconds.
    /// Default: SDK default (typically 100 seconds for Bedrock).
    /// </summary>
    [JsonPropertyName("requestTimeoutMs")]
    public int? RequestTimeoutMs { get; set; }

    /// <summary>
    /// Maximum number of retry attempts for failed requests.
    /// Default: SDK default (typically 3 retries).
    /// </summary>
    [JsonPropertyName("maxRetryAttempts")]
    public int? MaxRetryAttempts { get; set; }

    /// <summary>
    /// Use FIPS-compliant endpoints for Bedrock (US only).
    /// Required for certain government/compliance scenarios.
    /// Default: false
    /// </summary>
    [JsonPropertyName("useFipsEndpoint")]
    public bool UseFipsEndpoint { get; set; }

    /// <summary>
    /// Custom endpoint URL to use instead of the standard Bedrock endpoint.
    /// Useful for VPC endpoints, local testing, or custom routing.
    /// Example: "https://vpce-xxx.bedrock-runtime.us-east-1.vpce.amazonaws.com"
    /// </summary>
    [JsonPropertyName("serviceUrl")]
    public string? ServiceUrl { get; set; }
}
