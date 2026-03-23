using Amazon;
using Amazon.BedrockRuntime;
using Amazon.Runtime;
using Microsoft.Extensions.AI;
using HPD.RAG.Core.Providers.Embedding;

namespace HPD.RAG.EmbeddingProviders.Bedrock;

/// <summary>
/// AWS Bedrock embedding provider for HPD.RAG.
/// Uses AWSSDK.BedrockRuntime with the MEAI adapter (AsEmbeddingGenerator).
///
/// Config fields used: ModelName (required).
/// Typed config: BedrockEmbeddingConfig for Region, AccessKeyId, SecretAccessKey.
/// When credentials are omitted the AWS default credential chain is used.
/// </summary>
internal sealed class BedrockEmbeddingProviderFeatures : IEmbeddingProviderFeatures
{
    public string ProviderKey => "bedrock";
    public string DisplayName => "AWS Bedrock";

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingConfig config, IServiceProvider? services = null)
    {
        if (string.IsNullOrWhiteSpace(config.ModelName))
            throw new InvalidOperationException(
                "ModelName is required for the Bedrock embedding provider.");

        var typedConfig = config.GetTypedConfig<BedrockEmbeddingConfig>();

        // Resolve region: typed config > environment variables
        string? region = typedConfig?.Region
            ?? Environment.GetEnvironmentVariable("AWS_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");

        if (string.IsNullOrWhiteSpace(region))
            throw new InvalidOperationException(
                "Region is required for the Bedrock embedding provider. " +
                "Set it via BedrockEmbeddingConfig.Region, AWS_REGION, or AWS_DEFAULT_REGION.");

        var regionEndpoint = RegionEndpoint.GetBySystemName(region);
        var clientConfig = new AmazonBedrockRuntimeConfig { RegionEndpoint = regionEndpoint };

        IAmazonBedrockRuntime bedrockRuntime;
        if (!string.IsNullOrWhiteSpace(typedConfig?.AccessKeyId) &&
            !string.IsNullOrWhiteSpace(typedConfig?.SecretAccessKey))
        {
            var credentials = new BasicAWSCredentials(typedConfig.AccessKeyId, typedConfig.SecretAccessKey);
            bedrockRuntime = new AmazonBedrockRuntimeClient(credentials, clientConfig);
        }
        else
        {
            // Use default credential chain (env vars, ~/.aws/credentials, IAM role)
            bedrockRuntime = new AmazonBedrockRuntimeClient(clientConfig);
        }

        return bedrockRuntime.AsIEmbeddingGenerator(config.ModelName);
    }
}
