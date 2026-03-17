using System.Text.Json.Serialization;

namespace HPD.RAG.EmbeddingProviders.Bedrock;

/// <summary>
/// AWS Bedrock-specific embedding configuration.
///
/// JSON Example (ProviderOptionsJson):
/// <code>
/// {
///   "region": "us-east-1",
///   "accessKeyId": "AKIA...",
///   "secretAccessKey": "..."
/// }
/// </code>
///
/// When accessKeyId/secretAccessKey are omitted, the AWS default credential chain is used
/// (environment variables, ~/.aws/credentials, IAM role, etc.).
/// </summary>
public sealed class BedrockEmbeddingConfig
{
    /// <summary>
    /// AWS region name (e.g. "us-east-1"). Required.
    /// Can also be provided via AWS_REGION / AWS_DEFAULT_REGION environment variables.
    /// </summary>
    [JsonPropertyName("region")]
    public string? Region { get; set; }

    /// <summary>
    /// AWS access key ID. Optional — use the default credential chain when omitted.
    /// </summary>
    [JsonPropertyName("accessKeyId")]
    public string? AccessKeyId { get; set; }

    /// <summary>
    /// AWS secret access key. Optional — use the default credential chain when omitted.
    /// </summary>
    [JsonPropertyName("secretAccessKey")]
    public string? SecretAccessKey { get; set; }
}
