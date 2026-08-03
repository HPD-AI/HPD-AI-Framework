using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Threading;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.Runtime;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.Bedrock;

/// <summary>
/// AWS Bedrock provider implementation using the AWS BedrockRuntime SDK.
/// Supports all Bedrock models including Claude, Llama, Mistral, and more.
/// </summary>
/// <remarks>
/// <para>
/// This provider uses AWS SDK for .NET:
/// - AWSSDK.BedrockRuntime for chat completions
/// - AWSSDK.Core for AWS client configuration
/// - AWSSDK.Extensions.Bedrock.MEAI for Microsoft.Extensions.AI integration
/// </para>
/// <para>
/// Supported Model Families:
/// - Anthropic Claude (claude-3-5-sonnet, claude-3-opus, claude-3-sonnet, claude-3-haiku)
/// - Meta Llama (llama3-70b, llama3-8b, llama2-70b, llama2-13b)
/// - Mistral AI (mistral-7b, mixtral-8x7b, mistral-large)
/// - Amazon Titan (titan-text-express, titan-text-lite, titan-embed)
/// - Cohere (command, command-light, command-r, command-r-plus)
/// - AI21 Labs (jurassic-2-ultra, jurassic-2-mid)
/// </para>
/// <para>
/// Authentication methods:
/// 1. AWS Default Credential Chain (recommended)
///    - Environment variables (AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY)
///    - AWS credentials file (~/.aws/credentials)
///    - IAM role (for EC2, ECS, Lambda, etc.)
/// 2. Explicit credentials via BedrockProviderConfig
/// 3. AWS profile from credentials file
/// </para>
/// </remarks>
internal class BedrockProvider : IChatClientProvider
{
    public string ProviderKey => "bedrock";
    public string DisplayName => "AWS Bedrock";

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
    {
        // Get typed config
        var bedrockConfig = config.ProviderConfig as BedrockProviderConfig;

        var secrets = services?.GetService<ISecretResolver>();

        // Resolve region from explicit config, HPD secret resolvers, or the canonical environment variable.
        string? region = bedrockConfig?.Region
            ?? ResolveOptionalSecret(secrets, "bedrock:Region")
            ?? GetEnvironmentRegion();

        if (string.IsNullOrEmpty(region))
        {
            throw new InvalidOperationException(
                "For AWS Bedrock, the AWS Region must be configured via BedrockProviderConfig.Region, " +
                "AWS_REGION, AWS_DEFAULT_REGION, or the HPD secret resolver.");
        }

        string? modelName = config.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For AWS Bedrock, the ModelName (model ID) must be configured.");
        }

        // Create the Bedrock Runtime client
        IAmazonBedrockRuntime bedrockRuntime = CreateBedrockRuntimeClient(region, bedrockConfig, secrets);

        // Convert to IChatClient using the MEAI extension
        IChatClient chatClient = bedrockRuntime.AsIChatClient(modelName);

        return chatClient;
    }

    private static IAmazonBedrockRuntime CreateBedrockRuntimeClient(
        string region,
        BedrockProviderConfig? config,
        ISecretResolver? secrets)
    {
        var regionEndpoint = RegionEndpoint.GetBySystemName(region);

        // Create client configuration
        var clientConfig = new AmazonBedrockRuntimeConfig
        {
            RegionEndpoint = regionEndpoint
        };

        // Apply advanced configuration options
        if (config != null)
        {
            // Custom service URL (e.g., VPC endpoint)
            if (!string.IsNullOrEmpty(config.ServiceUrl))
            {
                clientConfig.ServiceURL = config.ServiceUrl;
            }

            // FIPS endpoint
            if (config.UseFipsEndpoint.HasValue)
                clientConfig.UseFIPSEndpoint = config.UseFipsEndpoint.Value;

            if (config.UseDualstackEndpoint.HasValue)
                clientConfig.UseDualstackEndpoint = config.UseDualstackEndpoint.Value;

            if (config.UseHttp.HasValue)
                clientConfig.UseHttp = config.UseHttp.Value;

            if (!string.IsNullOrEmpty(config.AuthenticationRegion))
            {
                clientConfig.AuthenticationRegion = config.AuthenticationRegion;
            }

            if (!string.IsNullOrEmpty(config.AuthenticationServiceName))
                clientConfig.AuthenticationServiceName = config.AuthenticationServiceName;

            if (config.AuthSchemePreference is { Count: > 0 })
                clientConfig.AuthSchemePreference = config.AuthSchemePreference;

            if (config.SigV4aSigningRegionSet is { Count: > 0 })
                clientConfig.SigV4aSigningRegionSet = config.SigV4aSigningRegionSet;

            if (config.IgnoreConfiguredEndpointUrls.HasValue)
                clientConfig.IgnoreConfiguredEndpointUrls = config.IgnoreConfiguredEndpointUrls.Value;

            if (config.DisableHostPrefixInjection.HasValue)
                clientConfig.DisableHostPrefixInjection = config.DisableHostPrefixInjection.Value;

            if (config.EndpointDiscoveryEnabled.HasValue)
                clientConfig.EndpointDiscoveryEnabled = config.EndpointDiscoveryEnabled.Value;

            if (config.DisableRequestCompression.HasValue)
                clientConfig.DisableRequestCompression = config.DisableRequestCompression.Value;

            if (config.RequestMinCompressionSizeBytes.HasValue)
                clientConfig.RequestMinCompressionSizeBytes = config.RequestMinCompressionSizeBytes.Value;

            if (!string.IsNullOrEmpty(config.ClientAppId))
                clientConfig.ClientAppId = config.ClientAppId;

            if (config.ThrottleRetries.HasValue)
                clientConfig.ThrottleRetries = config.ThrottleRetries.Value;

            if (config.FastFailRequests.HasValue)
                clientConfig.FastFailRequests = config.FastFailRequests.Value;

            if (config.CacheHttpClient.HasValue)
                clientConfig.CacheHttpClient = config.CacheHttpClient.Value;

            if (config.HttpClientCacheSize.HasValue)
                clientConfig.HttpClientCacheSize = config.HttpClientCacheSize.Value;

            if (!string.IsNullOrEmpty(config.ProxyHost))
                clientConfig.ProxyHost = config.ProxyHost;

            if (config.ProxyPort.HasValue)
                clientConfig.ProxyPort = config.ProxyPort.Value;

            if (config.MaxConnectionsPerServer.HasValue)
                clientConfig.MaxConnectionsPerServer = config.MaxConnectionsPerServer.Value;

            if (config.LogResponse.HasValue)
                clientConfig.LogResponse = config.LogResponse.Value;

            if (config.BufferSize.HasValue)
                clientConfig.BufferSize = config.BufferSize.Value;

            if (config.ProgressUpdateIntervalMs.HasValue)
                clientConfig.ProgressUpdateInterval = config.ProgressUpdateIntervalMs.Value;

            if (config.ResignRetries.HasValue)
                clientConfig.ResignRetries = config.ResignRetries.Value;

            if (config.AllowAutoRedirect.HasValue)
                clientConfig.AllowAutoRedirect = config.AllowAutoRedirect.Value;

            if (config.LogMetrics.HasValue)
                clientConfig.LogMetrics = config.LogMetrics.Value;

            if (config.DisableLogging.HasValue)
                clientConfig.DisableLogging = config.DisableLogging.Value;

            // Timeouts
            if (config.RequestTimeoutMs.HasValue)
            {
                clientConfig.Timeout = TimeSpan.FromMilliseconds(config.RequestTimeoutMs.Value);
            }

            if (config.ConnectTimeoutMs.HasValue)
            {
                clientConfig.ConnectTimeout = TimeSpan.FromMilliseconds(config.ConnectTimeoutMs.Value);
            }

            // Retry configuration
            if (config.MaxRetryAttempts.HasValue)
            {
                clientConfig.MaxErrorRetry = config.MaxRetryAttempts.Value;
            }

            if (config.RetryMode.HasValue)
                clientConfig.RetryMode = config.RetryMode.Value;

            if (config.DefaultConfigurationMode.HasValue)
                clientConfig.DefaultConfigurationMode = config.DefaultConfigurationMode.Value;

            if (config.MaxStaleConnectionRetries.HasValue)
                clientConfig.MaxStaleConnectionRetries = config.MaxStaleConnectionRetries.Value;
        }

        // Determine which credential type to use
        AWSCredentials? credentials = null;

        if (config != null)
        {
            // Priority 1: Explicit credentials in config
            var accessKeyId = config.AccessKeyId
                ?? ResolveOptionalSecret(secrets, "bedrock:AccessKeyId");
            var secretAccessKey = config.SecretAccessKey
                ?? ResolveOptionalSecret(secrets, "bedrock:SecretAccessKey");
            var sessionToken = config.SessionToken
                ?? ResolveOptionalSecret(secrets, "bedrock:SessionToken");

            if (!string.IsNullOrEmpty(accessKeyId) && !string.IsNullOrEmpty(secretAccessKey))
            {
                if (!string.IsNullOrEmpty(sessionToken))
                {
                    // Temporary credentials with session token
                    credentials = new SessionAWSCredentials(
                        accessKeyId,
                        secretAccessKey,
                        sessionToken);
                }
                else
                {
                    // Basic credentials
                    credentials = new BasicAWSCredentials(accessKeyId, secretAccessKey);
                }
            }
            // Priority 2: AWS profile
            else if (!string.IsNullOrEmpty(config.ProfileName))
            {
                credentials = new Amazon.Runtime.CredentialManagement.CredentialProfileStoreChain()
                    .TryGetAWSCredentials(config.ProfileName, out var profileCredentials)
                    ? profileCredentials
                    : throw new InvalidOperationException($"AWS profile '{config.ProfileName}' not found in credentials file.");
            }
        }

        // Priority 3: Use AWS default credential chain
        if (credentials != null)
        {
            return new AmazonBedrockRuntimeClient(credentials, clientConfig);
        }
        else
        {
            // This will use the default AWS credential chain:
            // 1. Environment variables (AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_SESSION_TOKEN)
            // 2. AWS credentials file (~/.aws/credentials)
            // 3. IAM role (for EC2, ECS, Lambda, etc.)
            return new AmazonBedrockRuntimeClient(clientConfig);
        }
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new BedrockErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://aws.amazon.com/bedrock/"),
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = true,
                        ["SupportsFunctionCalling"] = true,
                        ["SupportsVision"] = true
                    }
                }
            }
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name (model ID) is required for AWS Bedrock");

        // Get typed config for validation
        var bedrockConfig = config.ProviderConfig as BedrockProviderConfig;

        // Validate Bedrock-specific config if present
        if (bedrockConfig != null)
        {
            // Validate credentials pairing
            if (!string.IsNullOrEmpty(bedrockConfig.AccessKeyId) && string.IsNullOrEmpty(bedrockConfig.SecretAccessKey))
            {
                errors.Add("SecretAccessKey is required when AccessKeyId is specified");
            }

            if (!string.IsNullOrEmpty(bedrockConfig.SecretAccessKey) && string.IsNullOrEmpty(bedrockConfig.AccessKeyId))
            {
                errors.Add("AccessKeyId is required when SecretAccessKey is specified");
            }

            if (bedrockConfig.RequestTimeoutMs is <= 0)
                errors.Add("RequestTimeoutMs must be greater than 0 when specified");

            if (bedrockConfig.ConnectTimeoutMs is <= 0)
                errors.Add("ConnectTimeoutMs must be greater than 0 when specified");

            if (bedrockConfig.MaxRetryAttempts is < 0)
                errors.Add("MaxRetryAttempts must be greater than or equal to 0 when specified");

            if (bedrockConfig.MaxStaleConnectionRetries is < 0)
                errors.Add("MaxStaleConnectionRetries must be greater than or equal to 0 when specified");

            if (bedrockConfig.RequestMinCompressionSizeBytes is < 0)
                errors.Add("RequestMinCompressionSizeBytes must be greater than or equal to 0 when specified");

            if (bedrockConfig.HttpClientCacheSize is <= 0)
                errors.Add("HttpClientCacheSize must be greater than 0 when specified");

            if (bedrockConfig.ProxyPort is <= 0 or > 65535)
                errors.Add("ProxyPort must be between 1 and 65535 when specified");

            if (bedrockConfig.MaxConnectionsPerServer is <= 0)
                errors.Add("MaxConnectionsPerServer must be greater than 0 when specified");

            if (bedrockConfig.BufferSize is <= 0)
                errors.Add("BufferSize must be greater than 0 when specified");

            if (bedrockConfig.ProgressUpdateIntervalMs is <= 0)
                errors.Add("ProgressUpdateIntervalMs must be greater than 0 when specified");
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    private static string? ResolveOptionalSecret(ISecretResolver? secrets, string key)
        => secrets?.ResolveAsync(key, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult()
            ?.Value;

    private static string? GetEnvironmentRegion()
        => System.Environment.GetEnvironmentVariable("AWS_REGION")
            ?? System.Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");
}
