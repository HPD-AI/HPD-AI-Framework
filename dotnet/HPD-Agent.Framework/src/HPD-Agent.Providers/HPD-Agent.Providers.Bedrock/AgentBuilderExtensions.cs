using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Bedrock;

/// <summary>
/// Extension methods for AgentBuilder to configure AWS Bedrock as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use AWS Bedrock as the AI provider.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="model">The Bedrock model ID (e.g., "anthropic.claude-3-5-sonnet-20241022-v2:0", "meta.llama3-70b-instruct-v1:0")</param>
    /// <param name="region">AWS region where Bedrock is hosted (e.g., "us-east-1", "us-west-2")</param>
    /// <param name="configure">Optional action to configure additional Bedrock-specific options</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// Region Resolution (in priority order):
    /// 1. Explicit region parameter
    /// 2. BedrockProviderConfig.Region (via configure action)
    /// 3. Environment variable: AWS_REGION or AWS_DEFAULT_REGION
    /// 4. AWS credentials file (~/.aws/config)
    /// </para>
    /// <para>
    /// Credential Resolution (AWS Default Credential Chain):
    /// 1. Environment variables: AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_SESSION_TOKEN
    /// 2. AWS credentials file (~/.aws/credentials)
    /// 3. IAM role (for EC2, ECS, Lambda, etc.)
    /// 4. Explicit credentials via configure action
    /// </para>
    /// <para>
    /// This method creates a <see cref="BedrockProviderConfig"/> that is:
    /// - Stored in <c>ProviderClientConfig.ConstructionOptions</c> as a structured JSON/YAML object
    /// - Applied during <c>BedrockProvider.CreateChatClientAsync()</c> via the registered deserializer
    /// </para>
    /// <para>
    /// For FFI/JSON configuration, you can use the same config structure directly:
    /// <code>
    /// {
    ///   "providerKey": "bedrock",
    ///   "modelName": "anthropic.claude-3-5-sonnet-20241022-v2:0",
    ///   "constructionOptions": { "region": "us-east-1" }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Option 1: With explicit region
    /// var agent = await new AgentBuilder()
    ///     .WithBedrock(
    ///         model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
    ///         region: "us-east-1")
    ///     .Build();
    ///
    /// // Option 2: With AWS credentials
    /// var agent = await new AgentBuilder()
    ///     .WithBedrock(
    ///         model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
    ///         region: "us-east-1",
    ///         configure: opts =>
    ///         {
    ///             opts.AccessKeyId = "YOUR_ACCESS_KEY";
    ///             opts.SecretAccessKey = "YOUR_SECRET_KEY";
    ///         })
    ///     .Build();
    ///
    /// // Option 3: With AWS profile
    /// var agent = await new AgentBuilder()
    ///     .WithBedrock(
    ///         model: "meta.llama3-70b-instruct-v1:0",
    ///         region: "us-west-2",
    ///         configure: opts =>
    ///         {
    ///             opts.ProfileName = "my-aws-profile";
    ///         })
    ///     .Build();
    ///
    /// // Option 4: Auto-resolve from environment variables
    /// // Set AWS_REGION, AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY
    /// var agent = await new AgentBuilder()
    ///     .WithBedrock(model: "anthropic.claude-3-5-sonnet-20241022-v2:0")
    ///     .Build();
    ///
    /// // Option 5: With custom endpoint (VPC endpoint)
    /// var agent = await new AgentBuilder()
    ///     .WithBedrock(
    ///         model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
    ///         region: "us-east-1",
    ///         configure: opts =>
    ///         {
    ///             opts.ServiceUrl = "https://vpce-xxx.bedrock-runtime.us-east-1.vpce.amazonaws.com";
    ///         })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithBedrock(
        this AgentBuilder builder,
        string model,
        string? region = null,
        Action<BedrockProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model ID is required for AWS Bedrock provider.", nameof(model));

        // Create provider config
        var providerConfig = new BedrockProviderConfig();

        // Set region if provided
        if (!string.IsNullOrWhiteSpace(region))
        {
            providerConfig.Region = region;
        }

        // Allow user to configure additional options
        configure?.Invoke(providerConfig);

        // Validate configuration
        ValidateProviderConfig(providerConfig, model, configure);

        // Build provider config
        var chatConfig = new ProviderClientConfig
        {
            ProviderKey = "bedrock",
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);

        // Store the typed config
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(BedrockProviderConfig config, string model, Action<BedrockProviderConfig>? configure)
    {
        if (!string.IsNullOrEmpty(config.AccessKeyId) && string.IsNullOrEmpty(config.SecretAccessKey))
        {
            throw new ArgumentException(
                "SecretAccessKey is required when AccessKeyId is specified.",
                nameof(configure));
        }

        if (!string.IsNullOrEmpty(config.SecretAccessKey) && string.IsNullOrEmpty(config.AccessKeyId))
        {
            throw new ArgumentException(
                "AccessKeyId is required when SecretAccessKey is specified.",
                nameof(configure));
        }

        if (config.RequestTimeoutMs is <= 0)
            throw new ArgumentException("RequestTimeoutMs must be greater than 0.", nameof(configure));

        if (config.ConnectTimeoutMs is <= 0)
            throw new ArgumentException("ConnectTimeoutMs must be greater than 0.", nameof(configure));

        if (config.MaxRetryAttempts is < 0)
            throw new ArgumentException("MaxRetryAttempts must be greater than or equal to 0.", nameof(configure));

        if (config.MaxStaleConnectionRetries is < 0)
            throw new ArgumentException("MaxStaleConnectionRetries must be greater than or equal to 0.", nameof(configure));

        if (config.RequestMinCompressionSizeBytes is < 0)
            throw new ArgumentException("RequestMinCompressionSizeBytes must be greater than or equal to 0.", nameof(configure));

        if (config.HttpClientCacheSize is <= 0)
            throw new ArgumentException("HttpClientCacheSize must be greater than 0.", nameof(configure));

        if (config.ProxyPort is <= 0 or > 65535)
            throw new ArgumentException("ProxyPort must be between 1 and 65535.", nameof(configure));

        if (config.MaxConnectionsPerServer is <= 0)
            throw new ArgumentException("MaxConnectionsPerServer must be greater than 0.", nameof(configure));

        if (config.BufferSize is <= 0)
            throw new ArgumentException("BufferSize must be greater than 0.", nameof(configure));

        if (config.ProgressUpdateIntervalMs is <= 0)
            throw new ArgumentException("ProgressUpdateIntervalMs must be greater than 0.", nameof(configure));
    }
}
