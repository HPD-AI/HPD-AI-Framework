using Amazon.Runtime;
using Amazon.Runtime.Credentials;
using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Bedrock;

/// <summary>Provides AWS Bedrock configuration extensions.</summary>
public static class AgentBuilderExtensions
{
    /// <summary>Configures Bedrock chat with a named AWS credential identity.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="model">The Bedrock model identifier.</param>
    /// <param name="region">The AWS region.</param>
    /// <param name="credentialName">The external-identity registration name.</param>
    /// <param name="configure">An optional AWS SDK configuration callback.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithBedrock(
        this AgentBuilder builder,
        string model,
        string? region = null,
        string credentialName = "aws-default",
        Action<BedrockProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model ID is required for AWS Bedrock.", nameof(model));
        if (string.IsNullOrWhiteSpace(credentialName))
            throw new ArgumentException("Credential registration name is required.", nameof(credentialName));

        var providerConfig = new BedrockProviderConfig
        {
            Region = region ?? System.Environment.GetEnvironmentVariable("AWS_REGION")
                ?? System.Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
        };
        configure?.Invoke(providerConfig);
        if (string.IsNullOrWhiteSpace(providerConfig.Region))
            throw new ArgumentException("AWS region is required.", nameof(region));

        if (credentialName == "aws-default")
        {
            builder.AddProviderExternalIdentity(
                new ProviderExternalIdentityRegistration<AWSCredentials>(
                    credentialName, static () => DefaultAWSCredentialsIdentityResolver.GetCredentials(
                        new Amazon.BedrockRuntime.AmazonBedrockRuntimeConfig())));
        }
        builder.ProviderRegistry.Register(new BedrockProvider());
        builder.Config.SetChatClientConfig(new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "bedrock",
                Backend = "aws",
                Authentication = new ExternalIdentityProviderAuthentication
                {
                    CredentialName = credentialName
                }
            },
            ModelName = model,
            ProviderConfig = providerConfig
        });
        return builder;
    }
}
