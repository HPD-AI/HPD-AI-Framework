using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Bedrock;

/// <summary>
/// Auto-discovers and registers the AWS Bedrock provider on assembly load.
/// Also registers the provider-specific config type for FFI/JSON serialization.
/// </summary>
public static class BedrockProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        // Register provider factory
        ProviderContributionRegistry.RegisterProviderFactory(() => new BedrockProvider());

        // Register config type for FFI/JSON serialization (AOT-compatible)
        ProviderContributionRegistry.RegisterProviderConfigType<BedrockProviderConfig>(
            "bedrock",
            json => JsonSerializer.Deserialize(json, BedrockJsonContext.Default.BedrockProviderConfig),
            config => JsonSerializer.Serialize(config, BedrockJsonContext.Default.BedrockProviderConfig));

        // Register environment variable aliases
        ProviderContributionRegistry.RegisterSecretAlias("bedrock:AccessKeyId", "AWS_ACCESS_KEY_ID");
        ProviderContributionRegistry.RegisterSecretAlias("bedrock:SecretAccessKey", "AWS_SECRET_ACCESS_KEY");
        ProviderContributionRegistry.RegisterSecretAlias("bedrock:SessionToken", "AWS_SESSION_TOKEN");
        ProviderContributionRegistry.RegisterSecretAlias("bedrock:Region", "AWS_REGION");
    }
}
