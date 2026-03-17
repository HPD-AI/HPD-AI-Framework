using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.Runtime;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI;

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
internal class BedrockProvider : IProviderFeatures
{
    public string ProviderKey => "bedrock";
    public string DisplayName => "AWS Bedrock";

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public IChatClient CreateChatClient(ProviderConfig config, IServiceProvider? services = null)
    {
        // Get typed config
        var bedrockConfig = config.GetTypedProviderConfig<BedrockProviderConfig>();

        // Resolve region from multiple sources
        string? region = bedrockConfig?.Region
            ?? Environment.GetEnvironmentVariable("AWS_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");

        if (string.IsNullOrEmpty(region))
        {
            throw new InvalidOperationException(
                "For AWS Bedrock, the AWS Region must be configured via BedrockProviderConfig.Region, " +
                "the AWS_REGION environment variable, or the AWS_DEFAULT_REGION environment variable.");
        }

        string? modelName = config.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For AWS Bedrock, the ModelName (model ID) must be configured.");
        }

        // Create the Bedrock Runtime client
        IAmazonBedrockRuntime bedrockRuntime = CreateBedrockRuntimeClient(region, bedrockConfig);

        // Convert to IChatClient using the MEAI extension
        IChatClient chatClient = bedrockRuntime.AsIChatClient(modelName);

        // Apply client factory middleware if provided
        if (config.AdditionalProperties?.TryGetValue("ClientFactory", out var factoryObj) == true &&
            factoryObj is Func<IChatClient, IChatClient> clientFactory)
        {
            chatClient = clientFactory(chatClient);
        }

        return chatClient;
    }

    private static IAmazonBedrockRuntime CreateBedrockRuntimeClient(string region, BedrockProviderConfig? config)
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
            if (config.UseFipsEndpoint)
            {
                clientConfig.UseFIPSEndpoint = true;
            }

            // Timeouts
            if (config.RequestTimeoutMs.HasValue)
            {
                clientConfig.Timeout = TimeSpan.FromMilliseconds(config.RequestTimeoutMs.Value);
            }

            // Retry configuration
            if (config.MaxRetryAttempts.HasValue)
            {
                clientConfig.MaxErrorRetry = config.MaxRetryAttempts.Value;
            }
        }

        // Determine which credential type to use
        AWSCredentials? credentials = null;

        if (config != null)
        {
            // Priority 1: Explicit credentials in config
            if (!string.IsNullOrEmpty(config.AccessKeyId) && !string.IsNullOrEmpty(config.SecretAccessKey))
            {
                if (!string.IsNullOrEmpty(config.SessionToken))
                {
                    // Temporary credentials with session token
                    credentials = new SessionAWSCredentials(
                        config.AccessKeyId,
                        config.SecretAccessKey,
                        config.SessionToken);
                }
                else
                {
                    // Basic credentials
                    credentials = new BasicAWSCredentials(config.AccessKeyId, config.SecretAccessKey);
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
            SupportsStreaming = true,
            SupportsFunctionCalling = true, // Bedrock supports tool use
            SupportsVision = true,
            DocumentationUrl = "https://aws.amazon.com/bedrock/"
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public ProviderValidationResult ValidateConfiguration(ProviderConfig config)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name (model ID) is required for AWS Bedrock");

        // Get typed config for validation
        var bedrockConfig = config.GetTypedProviderConfig<BedrockProviderConfig>();

        // Validate region from multiple sources
        string? region = bedrockConfig?.Region
            ?? Environment.GetEnvironmentVariable("AWS_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");

        if (string.IsNullOrEmpty(region))
            errors.Add("AWS Region is required for Bedrock. Configure it via BedrockProviderConfig.Region, AWS_REGION, or AWS_DEFAULT_REGION environment variable.");

        // Validate Bedrock-specific config if present
        if (bedrockConfig != null)
        {
            // Validate Temperature range
            if (bedrockConfig.Temperature.HasValue && (bedrockConfig.Temperature.Value < 0 || bedrockConfig.Temperature.Value > 1))
            {
                errors.Add("Temperature must be between 0 and 1 for AWS Bedrock");
            }

            // Validate TopP range
            if (bedrockConfig.TopP.HasValue && (bedrockConfig.TopP.Value < 0 || bedrockConfig.TopP.Value > 1))
            {
                errors.Add("TopP must be between 0 and 1 for AWS Bedrock");
            }

            // Validate MaxTokens minimum
            if (bedrockConfig.MaxTokens.HasValue && bedrockConfig.MaxTokens.Value < 1)
            {
                errors.Add("MaxTokens must be at least 1");
            }

            // Validate StopSequences count
            if (bedrockConfig.StopSequences != null && bedrockConfig.StopSequences.Count > 2500)
            {
                errors.Add("StopSequences cannot exceed 2500 items");
            }

            // Validate ToolChoice
            if (!string.IsNullOrEmpty(bedrockConfig.ToolChoice))
            {
                var validChoices = new[] { "auto", "any", "tool" };
                if (!validChoices.Contains(bedrockConfig.ToolChoice))
                {
                    errors.Add("ToolChoice must be one of: auto, any, tool");
                }

                // Validate ToolChoiceName requirement
                if (bedrockConfig.ToolChoice == "tool" && string.IsNullOrEmpty(bedrockConfig.ToolChoiceName))
                {
                    errors.Add("ToolChoiceName is required when ToolChoice is 'tool'");
                }
            }

            // Validate Guardrail configuration
            if (!string.IsNullOrEmpty(bedrockConfig.GuardrailIdentifier) && string.IsNullOrEmpty(bedrockConfig.GuardrailVersion))
            {
                errors.Add("GuardrailVersion is required when GuardrailIdentifier is specified");
            }

            // Validate GuardrailTrace
            if (!string.IsNullOrEmpty(bedrockConfig.GuardrailTrace))
            {
                var validTraceOptions = new[] { "enabled", "disabled" };
                if (!validTraceOptions.Contains(bedrockConfig.GuardrailTrace))
                {
                    errors.Add("GuardrailTrace must be either 'enabled' or 'disabled'");
                }
            }

            // Validate credentials pairing
            if (!string.IsNullOrEmpty(bedrockConfig.AccessKeyId) && string.IsNullOrEmpty(bedrockConfig.SecretAccessKey))
            {
                errors.Add("SecretAccessKey is required when AccessKeyId is specified");
            }

            if (!string.IsNullOrEmpty(bedrockConfig.SecretAccessKey) && string.IsNullOrEmpty(bedrockConfig.AccessKeyId))
            {
                errors.Add("AccessKeyId is required when SecretAccessKey is specified");
            }
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }
}
